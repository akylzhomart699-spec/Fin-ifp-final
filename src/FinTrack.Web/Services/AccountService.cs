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

        // SQLite не умеет агрегировать decimal на сервере, поэтому суммируем в памяти
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
        if (name.Length == 0) return Result.Fail("Введите название счёта.");
        if (name.Length > 100) return Result.Fail("Название не длиннее 100 символов.");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Accounts.AnyAsync(a => a.Name == name && a.Id != input.Id))
            return Result.Fail("Счёт с таким названием уже есть.");

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
            return Result.Fail("Должен остаться хотя бы один счёт.");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account is null) return Result.Fail("Счёт не найден.");

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
        await notifications.DataChangedAsync();
        return Result.Success();
    }
}
