using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FinTrack.Web.Services;

/// <summary>
/// Kaspi Gold PDF statement parser.
/// 1) PdfPig extracts words with coordinates from the PDF.
/// 2) Words at the same height (Y) are joined into lines, left to right.
/// 3) Transaction lines are recognized with a regular expression:
///    "14.11.26 - 4 309,00 ₸ Покупка YANDEX.EDA".
/// </summary>
public static partial class KaspiPdfParser
{
    /// <summary>Date · sign · amount · ₸ · operation type · details.</summary>
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
            return ImportParseResult.Fatal($"Could not read the PDF: {ex.Message}");
        }

        return ParseLines(lines);
    }

    /// <summary>Recognizes transactions in already extracted text lines (kept separate for easy testing).</summary>
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
            if (date is null) error = $"Could not parse date \"{dateText}\".";

            if (!Money.TryParse(sign + amountText, out var amount) || amount == 0)
                error ??= $"Could not parse amount \"{sign}{amountText}\".";

            // For purchases ("Покупка") the merchant name is enough; otherwise prepend the type, e.g. "Перевод Азамат Б."
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
            ? ImportParseResult.Fatal("No transactions found in the PDF. Kaspi Gold statements are supported (text PDF, not a scan).")
            : new ImportParseResult(rows, null);
    }

    private static List<string> ExtractLines(byte[] pdfBytes)
    {
        var result = new List<string>();
        using var document = PdfDocument.Open(pdfBytes);

        foreach (var page in document.GetPages())
        {
            // Group words vertically: the same Bottom (within tolerance) means one line
            var groups = new List<(double Y, List<Word> Words)>();
            foreach (var word in page.GetWords())
            {
                var y = word.BoundingBox.Bottom;
                var index = groups.FindIndex(g => Math.Abs(g.Y - y) < SameLineTolerance);
                if (index < 0) groups.Add((y, new List<Word> { word }));
                else groups[index].Words.Add(word);
            }

            // In PDF the Y axis points up: top to bottom = descending Y
            foreach (var group in groups.OrderByDescending(g => g.Y))
                result.Add(string.Join(' ', group.Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
        }

        return result;
    }
}
