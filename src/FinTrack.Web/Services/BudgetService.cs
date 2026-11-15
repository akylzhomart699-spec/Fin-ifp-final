using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

public sealed class BudgetService(IDbContextFactory<AppDbContext> factory, INotificationService notifications)
    : IBudgetService
{
    public async Task<List<BudgetStatus>> GetStatusAsync(int year, int month)
    {
        await using var db = await factory.CreateDbContextAsync();
        var list = await db.Budgets.AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.Year == year && b.Month == month)
            .ToListAsync();

        var spent = await SpentByCategoryAsync(db, year, month);
        return list
            .Where(b => b.Category is not null)
            .Select(b => new BudgetStatus(b, b.Category!, spent.GetValueOrDefault(b.CategoryId)))
            .OrderByDescending(s => s.Percent)
            .ToList();
    }

    public async Task<BudgetStatus?> GetCategoryStatusAsync(Guid categoryId, int year, int month)
    {
        await using var db = await factory.CreateDbContextAsync();
        var budget = await db.Budgets.AsNoTracking()
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.CategoryId == categoryId && b.Year == year && b.Month == month);
        if (budget?.Category is null) return null;

        var spent = await SpentByCategoryAsync(db, year, month, categoryId);
        return new BudgetStatus(budget, budget.Category, spent.GetValueOrDefault(categoryId));
    }

    public async Task<Result> SaveAsync(Guid categoryId, int year, int month, decimal limit)
    {
        if (limit <= 0) return Result.Fail("The limit must be greater than zero.");
        if (month is < 1 or > 12) return Result.Fail("Invalid month.");

        await using var db = await factory.CreateDbContextAsync();
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null) return Result.Fail("Category not found.");
        if (category.Type != CategoryType.Expense) return Result.Fail("A limit can only be set for an expense category.");

        var existing = await db.Budgets.FirstOrDefaultAsync(b =>
            b.CategoryId == categoryId && b.Year == year && b.Month == month);
        if (existing is null)
            db.Budgets.Add(new Budget { CategoryId = categoryId, Year = year, Month = month, Limit = limit });
        else
            existing.Limit = limit;

        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid budgetId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == budgetId);
        if (budget is null) return Result.Fail("Limit not found.");

        db.Budgets.Remove(budget);
        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    public async Task<Result> CopyFromPreviousMonthAsync(int year, int month)
    {
        var prev = new DateTime(year, month, 1).AddMonths(-1);
        await using var db = await factory.CreateDbContextAsync();

        var previous = await db.Budgets.AsNoTracking()
            .Where(b => b.Year == prev.Year && b.Month == prev.Month).ToListAsync();
        if (previous.Count == 0) return Result.Fail("There were no limits last month.");

        var existing = await db.Budgets.Where(b => b.Year == year && b.Month == month)
            .Select(b => b.CategoryId).ToListAsync();

        var toAdd = previous.Where(p => !existing.Contains(p.CategoryId))
            .Select(p => new Budget { CategoryId = p.CategoryId, Year = year, Month = month, Limit = p.Limit })
            .ToList();
        if (toAdd.Count == 0) return Result.Fail("All of last month's limits already exist.");

        db.Budgets.AddRange(toAdd);
        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success(toAdd.Count);
    }

    private static async Task<Dictionary<Guid, decimal>> SpentByCategoryAsync(
        AppDbContext db, int year, int month, Guid? categoryId = null)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var query = db.Transactions.AsNoTracking()
            .Where(t => t.Date >= start && t.Date < end && t.Category!.Type == CategoryType.Expense);
        if (categoryId is Guid id) query = query.Where(t => t.CategoryId == id);

        var rows = await query.Select(t => new { t.CategoryId, t.Amount }).ToListAsync();
        return rows.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
    }
}
