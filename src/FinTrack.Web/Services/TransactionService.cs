using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

/// <summary>Validates a transaction before saving. A pure function — easy to test.</summary>
public static class TransactionValidator
{
    public const int MaxDescription = 300;

    public static List<string> Validate(Transaction t)
    {
        var errors = new List<string>();
        if (t.Amount <= 0) errors.Add("Amount must be greater than zero.");
        if (t.Amount > 1_000_000_000m) errors.Add("Amount is too large.");
        if (string.IsNullOrWhiteSpace(t.Description)) errors.Add("Add a description.");
        else if (t.Description.Length > MaxDescription) errors.Add($"Description must be {MaxDescription} characters or fewer.");
        if (t.AccountId == Guid.Empty) errors.Add("Select an account.");
        if (t.CategoryId == Guid.Empty) errors.Add("Select a category.");
        if (t.Date.Year < 2000 || t.Date > DateTime.Today.AddYears(1)) errors.Add("Invalid date.");
        return errors;
    }
}

public sealed class TransactionService(
    IDbContextFactory<AppDbContext> factory,
    IBudgetService budgets,
    INotificationService notifications,
    ILogger<TransactionService> logger) : ITransactionService
{
    public async Task<List<Transaction>> GetAsync(TransactionFilter filter)
    {
        await using var db = await factory.CreateDbContextAsync();
        IQueryable<Transaction> query = db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.Account);

        if (filter.From is DateTime start) query = query.Where(t => t.Date >= start);
        if (filter.To is DateTime end) query = query.Where(t => t.Date < end);
        if (filter.AccountId is Guid accountId) query = query.Where(t => t.AccountId == accountId);
        if (filter.CategoryId is Guid categoryId) query = query.Where(t => t.CategoryId == categoryId);

        var list = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        // In-memory search: SQLite LIKE isn't case-insensitive for Cyrillic
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            list = list.Where(t =>
                    t.Description.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    (t.Category?.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return list;
    }

    public async Task<Result> SaveAsync(Transaction input)
    {
        var errors = TransactionValidator.Validate(input);
        if (errors.Count > 0) return Result.Fail(string.Join(" ", errors));

        await using var db = await factory.CreateDbContextAsync();
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CategoryId);
        if (category is null) return Result.Fail("Category not found.");
        if (!await db.Accounts.AnyAsync(a => a.Id == input.AccountId)) return Result.Fail("Account not found.");

        var existing = await db.Transactions.FirstOrDefaultAsync(t => t.Id == input.Id);
        if (existing is null)
            db.Transactions.Add(CopyTo(input, new Transaction { Id = input.Id }));
        else
            CopyTo(input, existing);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Error saving transaction");
            return Result.Fail("Could not save the transaction. Please try again.");
        }

        if (category.Type == CategoryType.Expense)
            await CheckBudgetAsync(category, input.Date.Year, input.Date.Month);

        await notifications.DataChangedAsync();
        return Result.Success(1);
    }

    public async Task<Result> AddRangeAsync(IReadOnlyList<Transaction> items)
    {
        if (items.Count == 0) return Result.Fail("No transactions to save.");

        for (var i = 0; i < items.Count; i++)
        {
            var errors = TransactionValidator.Validate(items[i]);
            if (errors.Count > 0) return Result.Fail($"Transaction {i + 1}: {string.Join(" ", errors)}");
        }

        await using var db = await factory.CreateDbContextAsync();
        db.Transactions.AddRange(items.Select(t => CopyTo(t, new Transaction { Id = t.Id })));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Batch save error");
            return Result.Fail("Could not save the transactions: check the account and categories.");
        }

        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var touched = items
            .Where(t => categories.TryGetValue(t.CategoryId, out var c) && c.Type == CategoryType.Expense)
            .Select(t => (t.CategoryId, t.Date.Year, t.Date.Month))
            .Distinct();

        foreach (var (categoryId, year, month) in touched)
            await CheckBudgetAsync(categories[categoryId], year, month);

        await notifications.DataChangedAsync();
        return Result.Success(items.Count);
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entity = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null) return Result.Fail("Transaction not found.");

        db.Transactions.Remove(entity);
        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    private async Task CheckBudgetAsync(Category category, int year, int month)
    {
        var status = await budgets.GetCategoryStatusAsync(category.Id, year, month);
        if (status is null) return;

        if (status.IsExceeded)
            await notifications.BudgetAlertAsync(
                $"Budget \"{category.Name}\" exceeded: {Money.Format(status.Spent)} of {Money.Format(status.Budget.Limit)}",
                exceeded: true);
        else if (status.IsWarning)
            await notifications.BudgetAlertAsync(
                $"Budget \"{category.Name}\" is {status.Percent:0}% spent",
                exceeded: false);
    }

    private static Transaction CopyTo(Transaction from, Transaction to)
    {
        to.Amount = Math.Round(from.Amount, 2);
        to.Date = from.Date.Date;
        to.Description = from.Description.Trim();
        to.AccountId = from.AccountId;
        to.CategoryId = from.CategoryId;
        to.Source = from.Source;
        to.MlConfidence = from.MlConfidence;
        return to;
    }
}
