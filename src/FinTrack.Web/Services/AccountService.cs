using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Web.Services;

public sealed class AccountService(IDbContextFactory<AppDbContext> factory, INotificationService notifications)
    : IAccountService
{
    public async Task<List<Account>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();
    }

    public async Task<List<AccountBalance>> GetWithBalancesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();

        // SQLite can't aggregate decimal on the server, so we sum in memory
        var moves = await db.Transactions.AsNoTracking()
            .Select(t => new { t.AccountId, t.Amount, t.Category!.Type })
            .ToListAsync();

        var byAccount = moves
            .GroupBy(m => m.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Type == CategoryType.Income ? m.Amount : -m.Amount));

        return accounts
            .Select(a => new AccountBalance(a, a.InitialBalance + byAccount.GetValueOrDefault(a.Id)))
            .ToList();
    }

    public async Task<Result> SaveAsync(Account input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return Result.Fail("Enter an account name.");
        if (name.Length > 100) return Result.Fail("The name must be 100 characters or fewer.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.Name == name && a.Id != input.Id))
            return Result.Fail("An account with this name already exists.");

        var existing = await db.Accounts.FirstOrDefaultAsync(a => a.Id == input.Id);
        if (existing is null)
        {
            db.Accounts.Add(new Account { Id = input.Id, Name = name, InitialBalance = input.InitialBalance });
        }
        else
        {
            existing.Name = name;
            existing.InitialBalance = input.InitialBalance;
        }

        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Accounts.CountAsync() <= 1)
            return Result.Fail("At least one account must remain.");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return Result.Fail("Account not found.");

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }
}
