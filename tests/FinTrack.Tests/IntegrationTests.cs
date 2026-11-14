using FinTrack.Web.Data;
using FinTrack.Web.Domain;
using FinTrack.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinTrack.Tests;

/// <summary>Интеграционные тесты сервисов на настоящей SQLite-базе в памяти.</summary>
public class ServiceIntegrationTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeNotifications _notifications = new();

    private BudgetService Budgets => new(_db.Factory, _notifications);
    private TransactionService Transactions => new(_db.Factory, Budgets, _notifications, NullLogger<TransactionService>.Instance);
    private AccountService Accounts => new(_db.Factory, _notifications);

    private async Task<(Account Account, Category Food, Category Salary)> SeedBasicsAsync()
    {
        await using var ctx = _db.CreateContext();
        var account = new Account { Name = "Kaspi", InitialBalance = 10_000 };
        var food = new Category { Name = "Продукты", Type = CategoryType.Expense, Keywords = "magnum" };
        var salary = new Category { Name = "Зарплата", Type = CategoryType.Income };
        ctx.AddRange(account, food, salary);
        await ctx.SaveChangesAsync();
        return (account, food, salary);
    }

    [Fact]
    public async Task Balance_IsInitialPlusIncomeMinusExpense()
    {
        var (account, food, salary) = await SeedBasicsAsync();
        await Transactions.SaveAsync(new Transaction { Amount = 3_000, Description = "Magnum", AccountId = account.Id, CategoryId = food.Id });
        await Transactions.SaveAsync(new Transaction { Amount = 50_000, Description = "Зарплата", AccountId = account.Id, CategoryId = salary.Id });

        var balance = Assert.Single(await Accounts.GetWithBalancesAsync()).Balance;
        Assert.Equal(10_000m - 3_000m + 50_000m, balance);
    }

    [Fact]
    public async Task ExceedingBudget_SendsAlertAndDataChanged()
    {
        var (account, food, _) = await SeedBasicsAsync();
        var today = DateTime.Today;
        await Budgets.SaveAsync(food.Id, today.Year, today.Month, 5_000);

        var result = await Transactions.SaveAsync(new Transaction
        {
            Amount = 6_000, Description = "Magnum", AccountId = account.Id, CategoryId = food.Id, Date = today
        });

        Assert.True(result.Ok);
        var alert = Assert.Single(_notifications.Alerts);
        Assert.True(alert.Exceeded);
        Assert.Contains("Продукты", alert.Message);
        Assert.True(_notifications.DataChangedCount >= 1);
    }

    [Fact]
    public async Task InvalidTransaction_IsNotSaved()
    {
        var (account, food, _) = await SeedBasicsAsync();
        var result = await Transactions.SaveAsync(new Transaction { Amount = -5, Description = "", AccountId = account.Id, CategoryId = food.Id });

        Assert.False(result.Ok);
        await using var ctx = _db.CreateContext();
        Assert.Equal(0, await ctx.Transactions.CountAsync());
    }

    [Fact]
    public async Task CategoryWithTransactions_CannotBeDeleted()
    {
        var (account, food, _) = await SeedBasicsAsync();
        await Transactions.SaveAsync(new Transaction { Amount = 100, Description = "Magnum", AccountId = account.Id, CategoryId = food.Id });

        var categorizer = new CategorizationService(_db.Factory, NullLogger<CategorizationService>.Instance);
        var categories = new CategoryService(_db.Factory, categorizer, _notifications, NullLogger<CategoryService>.Instance);
        var result = await categories.DeleteAsync(food.Id);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task CopyBudgets_FromPreviousMonth()
    {
        var (_, food, _) = await SeedBasicsAsync();
        await Budgets.SaveAsync(food.Id, 2026, 10, 40_000);

        var result = await Budgets.CopyFromPreviousMonthAsync(2026, 11);

        Assert.True(result.Ok);
        var status = Assert.Single(await Budgets.GetStatusAsync(2026, 11));
        Assert.Equal(40_000m, status.Budget.Limit);
    }

    [Fact]
    public async Task Import_SkipsDuplicatesAndSavesRest()
    {
        var (account, food, _) = await SeedBasicsAsync();
        await Transactions.SaveAsync(new Transaction
        {
            Amount = 1_000, Description = "Magnum", AccountId = account.Id, CategoryId = food.Id, Date = new DateTime(2026, 11, 1)
        });

        var categorizer = new CategorizationService(_db.Factory, NullLogger<CategorizationService>.Instance);
        var importer = new CsvImportService(_db.Factory, categorizer, Transactions, NullLogger<CsvImportService>.Instance);
        var parsed = CsvImportService.ParseText("Дата;Описание;Сумма\n01.11.2026;Magnum;-1000\n02.11.2026;Magnum Small;-2000\n");
        await importer.PrepareAsync(parsed.Rows);

        Assert.True(parsed.Rows[0].IsDuplicate);
        Assert.False(parsed.Rows[0].Include);
        Assert.Equal(food.Id, parsed.Rows[1].CategoryId);

        var result = await importer.CommitAsync(parsed.Rows, account.Id);
        Assert.True(result.Ok);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task MlModel_LearnsFromSeedData()
    {
        await using (var ctx = _db.CreateContext())
            await SeedData.EnsureSeededAsync(ctx);

        var categorizer = new CategorizationService(_db.Factory, NullLogger<CategorizationService>.Instance);
        await categorizer.TrainAsync();
        Assert.True(categorizer.IsModelReady);

        var suggestion = await categorizer.SuggestAsync("Magnum Cash&Carry Астана", CategoryType.Expense);
        Assert.NotNull(suggestion);

        await using var check = _db.CreateContext();
        var category = await check.Categories.FirstAsync(c => c.Id == suggestion!.CategoryId);
        Assert.Equal("Продукты", category.Name);
    }

    public void Dispose() => _db.Dispose();
}
