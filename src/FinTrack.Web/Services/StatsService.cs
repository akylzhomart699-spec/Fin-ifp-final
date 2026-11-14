using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

public sealed class StatsService(IDbContextFactory<AppDbContext> factory) : IStatsService
{
    private const int TrendMonths = 6;

    public async Task<MonthSummary> GetMonthSummaryAsync(int year, int month, DateTime? today = null)
    {
        var monthStart = new DateTime(year, month, 1);
        var from = monthStart.AddMonths(-(TrendMonths - 1));
        var to = monthStart.AddMonths(1);

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from && t.Date < to)
            .Select(t => new { t.Date, t.Amount, t.Category!.Type, t.Category.Name, t.Category.Color })
            .ToListAsync();

        var current = rows.Where(r => r.Date >= monthStart).ToList();
        var income = current.Where(r => r.Type == CategoryType.Income).Sum(r => r.Amount);
        var expense = current.Where(r => r.Type == CategoryType.Expense).Sum(r => r.Amount);

        var byCategory = current
            .Where(r => r.Type == CategoryType.Expense)
            .GroupBy(r => new { r.Name, r.Color })
            .Select(g => new CategorySlice(g.Key.Name, g.Key.Color, g.Sum(r => r.Amount)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        var trend = Enumerable.Range(0, TrendMonths)
            .Select(i =>
            {
                var ms = from.AddMonths(i);
                var me = ms.AddMonths(1);
                var inMonth = rows.Where(r => r.Date >= ms && r.Date < me).ToList();
                return new MonthPoint(
                    $"{MonthNames.Short[ms.Month - 1]}",
                    inMonth.Where(r => r.Type == CategoryType.Income).Sum(r => r.Amount),
                    inMonth.Where(r => r.Type == CategoryType.Expense).Sum(r => r.Amount));
            })
            .ToList();

        var forecast = ForecastExpense(expense, monthStart, (today ?? DateTime.Today).Date);
        return new MonthSummary(income, expense, forecast, byCategory, trend);
    }

    public async Task<MonthSummary> GetYearSummaryAsync(int year, DateTime? today = null)
    {
        var from = new DateTime(year, 1, 1);
        var to = from.AddYears(1);

        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= from && t.Date < to)
            .Select(t => new { t.Date, t.Amount, t.Category!.Type, t.Category.Name, t.Category.Color })
            .ToListAsync();

        var income = rows.Where(r => r.Type == CategoryType.Income).Sum(r => r.Amount);
        var expense = rows.Where(r => r.Type == CategoryType.Expense).Sum(r => r.Amount);

        var byCategory = rows
            .Where(r => r.Type == CategoryType.Expense)
            .GroupBy(r => new { r.Name, r.Color })
            .Select(g => new CategorySlice(g.Key.Name, g.Key.Color, g.Sum(r => r.Amount)))
            .OrderByDescending(s => s.Amount)
            .ToList();

        var trend = Enumerable.Range(1, 12)
            .Select(m => new MonthPoint(
                MonthNames.Short[m - 1],
                rows.Where(r => r.Date.Month == m && r.Type == CategoryType.Income).Sum(r => r.Amount),
                rows.Where(r => r.Date.Month == m && r.Type == CategoryType.Expense).Sum(r => r.Amount)))
            .ToList();

        var forecast = ForecastYearExpense(expense, year, (today ?? DateTime.Today).Date);
        return new MonthSummary(income, expense, forecast, byCategory, trend);
    }

    /// <summary>Прогноз расходов на конец года при текущем темпе. Прошедший год — факт, будущий — 0.</summary>
    public static decimal ForecastYearExpense(decimal spentSoFar, int year, DateTime today)
    {
        if (today.Year < year) return 0;
        if (today.Year > year) return spentSoFar;

        var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        return Math.Round(spentSoFar / today.DayOfYear * daysInYear, 0);
    }

    /// <summary>
    /// Прогноз расходов на конец месяца: при текущем темпе трат.
    /// Для прошедшего месяца — фактические расходы, для будущего — 0.
    /// </summary>
    public static decimal ForecastExpense(decimal spentSoFar, DateTime monthStart, DateTime today)
    {
        var monthEnd = monthStart.AddMonths(1);
        if (today < monthStart) return 0;
        if (today >= monthEnd) return spentSoFar;

        var days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        return Math.Round(spentSoFar / today.Day * days, 0);
    }
}
