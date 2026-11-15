using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FinTrack.Web.Services;

/// <summary>
/// Разбор PDF-выписки Kaspi Gold.
/// 1) PdfPig достаёт из PDF слова с координатами.
/// 2) Слова с одинаковой высотой (Y) собираются в строки, слева направо.
/// 3) Строки операций распознаются регулярным выражением:
///    "14.11.26 - 4 309,00 ₸ Покупка YANDEX.EDA".
/// </summary>
public static partial class KaspiPdfParser
{
    /// <summary>Дата · знак · сумма · ₸ · тип операции · детали.</summary>
    [GeneratedRegex(@"^(\d{2}\.\d{2}\.\d{2})\s+([+-])\s*(\d[\d\s]*,\d{2})\s*₸\s+(\S+)\s+(.+)$")]
    private static partial Regex OperationLine();

    private const double SameLineTolerance = 2.0;

    public static ImportParseResult Parse(byte[] pdfBytes)
    {
        List<string> lines;
        try
        {
            lines = ExtractLines(pdfBytes);
        }
        catch (Exception ex)
        {
            return ImportParseResult.Fatal($"Не удалось прочитать PDF: {ex.Message}");
        }

        return ParseLines(lines);
    }

    /// <summary>Распознаёт операции в уже извлечённых строках текста (отдельно — чтобы легко тестировать).</summary>
    public static ImportParseResult ParseLines(IEnumerable<string> lines)
    {
        var rows = new List<ImportRow>();
        var lineNo = 0;

        foreach (var raw in lines)
        {
            lineNo++;
            var match = OperationLine().Match(raw.Trim());
            if (!match.Success) continue;

            var dateText = match.Groups[1].Value;
            var sign = match.Groups[2].Value;
            var amountText = match.Groups[3].Value;
            var operation = match.Groups[4].Value;
            var details = match.Groups[5].Value.Trim();

            string? error = null;
            DateTime? date = DateTime.TryParseExact(dateText, "dd.MM.yy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : null;
            if (date is null) error = $"Не удалось распознать дату «{dateText}».";

            if (!Money.TryParse(sign + amountText, out var amount) || amount == 0)
                error ??= $"Не удалось распознать сумму «{sign}{amountText}».";

            // Для покупок достаточно названия магазина, для остального добавляем тип: «Перевод Азамат Б.»
            var description = operation == "Покупка" ? details : $"{operation} {details}";
            if (description.Length > TransactionValidator.MaxDescription)
                description = description[..TransactionValidator.MaxDescription];

            rows.Add(new ImportRow
            {
                Line = lineNo,
                Date = date,
                Description = description,
                Amount = amount,
                Error = error,
                Include = error is null,
            });

            if (rows.Count >= CsvImportService.MaxRows) break;
        }

        return rows.Count == 0
            ? ImportParseResult.Fatal("В PDF не найдено операций. Поддерживается выписка Kaspi Gold (текстовый PDF, не скан).")
            : new ImportParseResult(rows, null);
    }

    private static List<string> ExtractLines(byte[] pdfBytes)
    {
        var result = new List<string>();
        using var document = PdfDocument.Open(pdfBytes);

        foreach (var page in document.GetPages())
        {
            // Группируем слова по вертикали: одинаковый Bottom (с допуском) — одна строка
            var groups = new List<(double Y, List<Word> Words)>();
            foreach (var word in page.GetWords())
            {
                var y = word.BoundingBox.Bottom;
                var index = groups.FindIndex(g => Math.Abs(g.Y - y) < SameLineTolerance);
                if (index < 0) groups.Add((y, new List<Word> { word }));
                else groups[index].Words.Add(word);
            }

            // В PDF ось Y направлена вверх: сверху вниз = по убыванию Y
            foreach (var group in groups.OrderByDescending(g => g.Y))
                result.Add(string.Join(' ', group.Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
        }

        return result;
    }
}
