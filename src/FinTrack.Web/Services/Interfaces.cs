using FinTrack.Web.Domain;

namespace FinTrack.Web.Services;

public sealed record AccountBalance(Account Account, decimal Balance);

public sealed record TransactionFilter(
    DateTime? From = null,
    DateTime? To = null,
    Guid? AccountId = null,
    Guid? CategoryId = null,
    string? Search = null);

public sealed record BudgetStatus(Budget Budget, Category Category, decimal Spent)
{
    public double Percent => Budget.Limit <= 0 ? 0 : (double)(Spent / Budget.Limit * 100m);
    public decimal Remaining => Budget.Limit - Spent;
    public bool IsWarning => Percent >= 80 && Percent < 100;
    public bool IsExceeded => Percent >= 100;
}

public sealed record CategorySlice(string Name, string Color, decimal Amount);

public sealed record MonthPoint(string Label, decimal Income, decimal Expense);

public sealed record MonthSummary(
    decimal Income,
    decimal Expense,
    decimal Forecast,
    IReadOnlyList<CategorySlice> ByCategory,
    IReadOnlyList<MonthPoint> Trend)
{
    public decimal Net => Income - Expense;
}

public sealed record CategorySuggestion(Guid CategoryId, float Confidence, string Method);

public static class SuggestionMethods
{
    public const string Ml = "ML.NET";
    public const string Keywords = "keywords";
    public const string Default = "default";
}

public interface INotificationService
{
    Task BudgetAlertAsync(string message, bool exceeded);
    Task DataChangedAsync();
}

public interface IAccountService
{
    Task<List<Account>> GetAllAsync();
    Task<List<AccountBalance>> GetWithBalancesAsync();
    Task<Result> SaveAsync(Account account);
    Task<Result> DeleteAsync(Guid id);
}

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync(CategoryType? type = null);
    Task<Result> SaveAsync(Category category);
    Task<Result> DeleteAsync(Guid id);
}

public interface ITransactionService
{
    Task<List<Transaction>> GetAsync(TransactionFilter filter);
    Task<Result> SaveAsync(Transaction transaction);
    Task<Result> AddRangeAsync(IReadOnlyList<Transaction> transactions);
    Task<Result> DeleteAsync(Guid id);
}

public interface IBudgetService
{
    Task<List<BudgetStatus>> GetStatusAsync(int year, int month);
    Task<BudgetStatus?> GetCategoryStatusAsync(Guid categoryId, int year, int month);
    Task<Result> SaveAsync(Guid categoryId, int year, int month, decimal limit);
    Task<Result> DeleteAsync(Guid budgetId);
    Task<Result> CopyFromPreviousMonthAsync(int year, int month);
}

public interface IStatsService
{
    Task<MonthSummary> GetMonthSummaryAsync(int year, int month, DateTime? today = null);

    /// <summary>Calendar-year totals: sums, categories and all 12 months.</summary>
    Task<MonthSummary> GetYearSummaryAsync(int year, DateTime? today = null);
}

public interface ICategorizationService
{
    bool IsModelReady { get; }
    DateTime? TrainedAt { get; }
    int TrainingSamples { get; }
    int LabelsCount { get; }

    Task TrainAsync(CancellationToken ct = default);
    Task<CategorySuggestion?> SuggestAsync(string description, CategoryType? type, CancellationToken ct = default);
    CategorySuggestion? Suggest(string description, IReadOnlyList<Category> candidates);
}

public interface ICsvImportService
{
    /// <summary>Parses a statement file: CSV or Kaspi PDF (detected by extension).</summary>
    Task<ImportParseResult> ParseAsync(Stream stream, string fileName, CancellationToken ct = default);
    Task PrepareAsync(List<ImportRow> rows, CancellationToken ct = default);
    Task<Result> CommitAsync(IReadOnlyCollection<ImportRow> rows, Guid accountId);
}
