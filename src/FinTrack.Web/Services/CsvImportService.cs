using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

/// <summary>Строка выписки после разбора. Amount со знаком: минус — расход.</summary>
public sealed class ImportRow
{
    public int Line { get; init; }
    public DateTime? Date { get; init; }
    public string Description { get; init; } = "";
    public decimal Amount { get; init; }
    public string? Error { get; init; }

    public bool Include { get; set; }
    public bool IsDuplicate { get; set; }
    public Guid? CategoryId { get; set; }
    public float? Confidence { get; set; }
    public string? Method { get; set; }

    public CategoryType Type => Amount < 0 ? CategoryType.Expense : CategoryType.Income;
}

public sealed record ImportParseResult(List<ImportRow> Rows, string? FatalError)
{
    public static ImportParseResult Fatal(string error) => new(new List<ImportRow>(), error);
}

/// <summary>Импорт банковской выписки (CSV или PDF Kaspi): разбор, автокатегории, поиск дубликатов, сохранение.</summary>
public sealed class CsvImportService(
    IDbContextFactory<AppDbContext> factory,
    ICategorizationService categorizer,
    ITransactionService transactions,
    ILogger<CsvImportService> logger) : ICsvImportService
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public const int MaxRows = 5000;

    private static readonly string[] DateHeaders = ["дата", "date", "дата операции", "дата транзакции"];
    private static readonly string[] DescriptionHeaders = ["описание", "description", "детали", "назначение", "details", "merchant", "получатель"];
    private static readonly string[] AmountHeaders = ["сумма", "amount", "sum"];
    private static readonly string[] DateFormats =
        ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "dd/MM/yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss"];

    public async Task<ImportParseResult> ParseAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        if (Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Поток из браузера нельзя перематывать, а PdfPig нужен произвольный доступ — копируем в память
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return KaspiPdfParser.Parse(buffer.ToArray());
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);
        return ParseText(text);
    }

    /// <summary>Разбирает CSV-текст. Разделитель (',' или ';') определяется автоматически.</summary>
    public static ImportParseResult ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ImportParseResult.Fatal("Файл пустой.");

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
        };

        try
        {
            using var csv = new CsvReader(new StringReader(text), config);
            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null)
                return ImportParseResult.Fatal("Не найдена строка с заголовками колонок.");

            var headers = csv.HeaderRecord.Select(h => h.Trim().ToLowerInvariant()).ToArray();
            var dateIdx = FindColumn(headers, DateHeaders);
            var descIdx = FindColumn(headers, DescriptionHeaders);
            var amountIdx = FindColumn(headers, AmountHeaders);
            if (dateIdx < 0 || descIdx < 0 || amountIdx < 0)
                return ImportParseResult.Fatal(
                    $"Нужны колонки «Дата», «Описание», «Сумма». В файле: {string.Join(", ", csv.HeaderRecord)}.");

            var rows = new List<ImportRow>();
            while (csv.Read() && rows.Count < MaxRows)
            {
                var rawDate = csv.GetField(dateIdx);
                var description = (csv.GetField(descIdx) ?? "").Trim();
                var rawAmount = csv.GetField(amountIdx);

                string? error = null;
                DateTime? date = TryParseDate(rawDate, out var d) ? d : null;
                if (date is null) error = $"Не удалось распознать дату «{rawDate}».";
                if (!Money.TryParse(rawAmount, out var amount) || amount == 0)
                    error ??= $"Не удалось распознать сумму «{rawAmount}».";
                if (description.Length == 0) error ??= "Пустое описание.";
                if (description.Length > TransactionValidator.MaxDescription)
                    description = description[..TransactionValidator.MaxDescription];

                rows.Add(new ImportRow
                {
                    Line = csv.Parser.Row,
                    Date = date,
                    Description = description,
                    Amount = amount,
                    Error = error,
                    Include = error is null,
                });
            }

            return rows.Count == 0
                ? ImportParseResult.Fatal("В файле нет строк с операциями.")
                : new ImportParseResult(rows, null);
        }
        catch (Exception ex) when (ex is CsvHelperException or FormatException)
        {
            return ImportParseResult.Fatal($"Файл повреждён или не является CSV: {ex.Message}");
        }
    }

    public async Task PrepareAsync(List<ImportRow> rows, CancellationToken ct = default)
    {
        var valid = rows.Where(r => r.Error is null && r.Date is not null).ToList();
        if (valid.Count == 0) return;

        await using var db = await factory.CreateDbContextAsync(ct);
        var categories = await db.Categories.AsNoTracking().ToListAsync(ct);
        var expenseCats = categories.Where(c => c.Type == CategoryType.Expense).ToList();
        var incomeCats = categories.Where(c => c.Type == CategoryType.Income).ToList();

        var min = valid.Min(r => r.Date!.Value).Date;
        var max = valid.Max(r => r.Date!.Value).Date.AddDays(1);
        var existing = (await db.Transactions.AsNoTracking()
                .Where(t => t.Date >= min && t.Date < max)
                .Select(t => new { t.Date, t.Amount, t.Description })
                .ToListAsync(ct))
            .Select(t => DuplicateKey(t.Date, t.Amount, t.Description))
            .ToHashSet();

        foreach (var row in valid)
        {
            row.IsDuplicate = existing.Contains(DuplicateKey(row.Date!.Value, Math.Abs(row.Amount), row.Description));
            if (row.IsDuplicate) row.Include = false;

            var candidates = row.Type == CategoryType.Expense ? expenseCats : incomeCats;
            var suggestion = categorizer.Suggest(row.Description, candidates);
            if (suggestion is not null)
            {
                row.CategoryId = suggestion.CategoryId;
                row.Confidence = suggestion.Confidence;
                row.Method = suggestion.Method;
            }
            else
            {
                var fallback = candidates.FirstOrDefault(c => c.Name == "Прочее") ?? candidates.FirstOrDefault();
                row.CategoryId = fallback?.Id;
                row.Method = fallback is null ? null : SuggestionMethods.Default;
            }
        }
    }

    public async Task<Result> CommitAsync(IReadOnlyCollection<ImportRow> rows, Guid accountId)
    {
        if (accountId == Guid.Empty) return Result.Fail("Выберите счёт.");

        var items = rows
            .Where(r => r.Include && r.Error is null && r.Date is not null && r.CategoryId is not null)
            .Select(r => new Transaction
            {
                Amount = Math.Abs(r.Amount),
                Date = r.Date!.Value.Date,
                Description = r.Description,
                AccountId = accountId,
                CategoryId = r.CategoryId!.Value,
                Source = TransactionSource.Import,
                MlConfidence = r.Method == SuggestionMethods.Ml ? r.Confidence : null,
            })
            .ToList();

        if (items.Count == 0) return Result.Fail("Не выбрано ни одной строки для импорта.");

        var result = await transactions.AddRangeAsync(items);
        if (result.Ok)
        {
            // Дообучаем модель на новых данных в фоне
            _ = Task.Run(async () =>
            {
                try { await categorizer.TrainAsync(); }
                catch (Exception ex) { logger.LogWarning(ex, "Дообучение после импорта не удалось"); }
            });
        }
        return result;
    }

    internal static bool TryParseDate(string? raw, out DateTime date) =>
        DateTime.TryParseExact(raw?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static int FindColumn(string[] headers, string[] candidates)
    {
        for (var i = 0; i < headers.Length; i++)
            if (candidates.Contains(headers[i])) return i;
        for (var i = 0; i < headers.Length; i++)
            if (candidates.Any(c => headers[i].Contains(c))) return i;
        return -1;
    }

    private static string DuplicateKey(DateTime date, decimal amount, string description) =>
        $"{date:yyyy-MM-dd}|{amount.ToString("0.00", CultureInfo.InvariantCulture)}|{description.Trim().ToLowerInvariant()}";
}
