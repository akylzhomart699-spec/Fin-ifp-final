using System.Globalization;
using FinTrack.Web.Domain;
using FinTrack.Web.Services;
using Xunit;

namespace FinTrack.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("12500", "12 500 ₸")]
    [InlineData("-1500.5", "-1 500,5 ₸")]
    [InlineData("0", "0 ₸")]
    [InlineData("1234567.89", "1 234 567,89 ₸")]
    public void Format_UsesSpacesAndTengeSign(string amount, string expected) =>
        Assert.Equal(expected, Money.Format(decimal.Parse(amount, CultureInfo.InvariantCulture)));

    [Fact]
    public void Format_ShowPlus_AddsPlusForPositive() =>
        Assert.Equal("+100 ₸", Money.Format(100m, showPlus: true));

    [Theory]
    [InlineData("-12 500,50 ₸", "-12500.50")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("350000", "350000")]
    [InlineData("−2 000", "-2000")]
    public void TryParse_UnderstandsBankFormats(string raw, string expected)
    {
        Assert.True(Money.TryParse(raw, out var value));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsGarbage(string? raw) => Assert.False(Money.TryParse(raw, out _));
}

public class ForecastTests
{
    [Fact]
    public void CurrentMonth_ExtrapolatesByDays()
    {
        // 10 000 ₸ за 10 дней 30-дневного месяца → 30 000 ₸
        var forecast = StatsService.ForecastExpense(10_000m, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10));
        Assert.Equal(30_000m, forecast);
    }

    [Fact]
    public void PastMonth_ReturnsActualSpending() =>
        Assert.Equal(5_000m, StatsService.ForecastExpense(5_000m, new DateTime(2026, 9, 1), new DateTime(2026, 11, 14)));

    [Fact]
    public void CurrentYear_ExtrapolatesByDayOfYear()
    {
        // 73 000 ₸ за первые 73 дня 2026 года (365 дней) → 365 000 ₸
        var forecast = StatsService.ForecastYearExpense(73_000m, 2026, new DateTime(2026, 3, 14));
        Assert.Equal(365_000m, forecast);
    }

    [Fact]
    public void PastYear_ReturnsActualSpending() =>
        Assert.Equal(1_000m, StatsService.ForecastYearExpense(1_000m, 2025, new DateTime(2026, 11, 14)));

    [Fact]
    public void FutureMonth_ReturnsZero() =>
        Assert.Equal(0m, StatsService.ForecastExpense(0m, new DateTime(2026, 12, 1), new DateTime(2026, 11, 14)));
}

public class BudgetStatusTests
{
    private static BudgetStatus Make(decimal limit, decimal spent) =>
        new(new Budget { Limit = limit }, new Category { Name = "Продукты" }, spent);

    [Fact]
    public void UnderLimit_IsNeitherWarningNorExceeded()
    {
        var s = Make(100_000, 50_000);
        Assert.Equal(50, s.Percent, 3);
        Assert.False(s.IsWarning);
        Assert.False(s.IsExceeded);
        Assert.Equal(50_000m, s.Remaining);
    }

    [Fact]
    public void Over80Percent_IsWarning() => Assert.True(Make(100_000, 85_000).IsWarning);

    [Fact]
    public void OverLimit_IsExceeded()
    {
        var s = Make(100_000, 120_000);
        Assert.True(s.IsExceeded);
        Assert.Equal(-20_000m, s.Remaining);
    }
}

public class TransactionValidatorTests
{
    private static Transaction Valid() => new()
    {
        Amount = 1000,
        Description = "Magnum",
        AccountId = Guid.NewGuid(),
        CategoryId = Guid.NewGuid(),
        Date = DateTime.Today,
    };

    [Fact]
    public void ValidTransaction_HasNoErrors() => Assert.Empty(TransactionValidator.Validate(Valid()));

    [Fact]
    public void ZeroAmount_IsRejected()
    {
        var t = Valid();
        t.Amount = 0;
        Assert.Contains(TransactionValidator.Validate(t), e => e.Contains("Сумма"));
    }

    [Fact]
    public void EmptyDescriptionAndCategory_AreRejected()
    {
        var t = Valid();
        t.Description = " ";
        t.CategoryId = Guid.Empty;
        var errors = TransactionValidator.Validate(t);
        Assert.Equal(2, errors.Count);
    }
}

public class CsvParsingTests
{
    [Fact]
    public void ParsesSemicolonCsvWithRussianHeaders()
    {
        const string csv = "Дата;Описание;Сумма\n01.11.2026;Magnum Cash&Carry;-12 500,50\n02.11.2026;Зарплата;350000\n";
        var result = CsvImportService.ParseText(csv);

        Assert.Null(result.FatalError);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(-12500.50m, result.Rows[0].Amount);
        Assert.Equal(CategoryType.Expense, result.Rows[0].Type);
        Assert.Equal(new DateTime(2026, 11, 1), result.Rows[0].Date);
        Assert.Equal(CategoryType.Income, result.Rows[1].Type);
    }

    [Fact]
    public void ParsesCommaCsvWithEnglishHeaders()
    {
        const string csv = "date,description,amount\n2026-11-03,Starbucks,-2400\n";
        var result = CsvImportService.ParseText(csv);

        Assert.Null(result.FatalError);
        Assert.Single(result.Rows);
        Assert.Equal("Starbucks", result.Rows[0].Description);
    }

    [Fact]
    public void BadRow_GetsErrorAndIsNotIncluded()
    {
        const string csv = "Дата;Описание;Сумма\nвчера;Kaspi;abc\n";
        var row = Assert.Single(CsvImportService.ParseText(csv).Rows);

        Assert.NotNull(row.Error);
        Assert.False(row.Include);
    }

    [Fact]
    public void MissingColumns_IsFatal()
    {
        var result = CsvImportService.ParseText("foo;bar\n1;2\n");
        Assert.NotNull(result.FatalError);
    }

    [Fact]
    public void EmptyFile_IsFatal() => Assert.NotNull(CsvImportService.ParseText("").FatalError);
}

public class KeywordMatchTests
{
    private static readonly List<Category> Categories =
    [
        new Category { Name = "Продукты", Keywords = "magnum, small" },
        new Category { Name = "Транспорт", Keywords = "yandex go, такси" },
        new Category { Name = "Кафе", Keywords = "coffee" },
    ];

    [Theory]
    [InlineData("MAGNUM Алматы", "Продукты")]
    [InlineData("Оплата Yandex Go", "Транспорт")]
    [InlineData("Coffee Boom", "Кафе")]
    public void FindsCategoryByKeyword(string text, string expected) =>
        Assert.Equal(expected, CategorizationService.KeywordMatch(text, Categories)?.Name);

    [Fact]
    public void UnknownText_ReturnsNull() =>
        Assert.Null(CategorizationService.KeywordMatch("что-то непонятное", Categories));
}

public class KaspiPdfParserTests
{
    [Fact]
    public void ParsesKaspiOperationLines()
    {
        var lines = new[]
        {
            "ВЫПИСКА по Kaspi Gold",
            "Дата Сумма Операция Детали",
            "14.11.26 - 4 309,00 ₸ Покупка YANDEX.EDA",
            "Курсовая разница",
            "12.11.26 + 60 000,00 ₸ Пополнение В Kaspi Терминале",
            "12.11.26 - 2 000,00 ₸ Перевод Азамат Б.",
        };

        var result = KaspiPdfParser.ParseLines(lines);

        Assert.Null(result.FatalError);
        Assert.Equal(3, result.Rows.Count);

        Assert.Equal(new DateTime(2026, 11, 14), result.Rows[0].Date);
        Assert.Equal(-4309m, result.Rows[0].Amount);
        Assert.Equal("YANDEX.EDA", result.Rows[0].Description);

        Assert.Equal(60000m, result.Rows[1].Amount);
        Assert.Equal(CategoryType.Income, result.Rows[1].Type);
        Assert.Equal("Пополнение В Kaspi Терминале", result.Rows[1].Description);

        Assert.Equal("Перевод Азамат Б.", result.Rows[2].Description);
    }

    [Fact]
    public void NoOperations_IsFatal() =>
        Assert.NotNull(KaspiPdfParser.ParseLines(new[] { "СПРАВКА об остатке на счете" }).FatalError);
}
