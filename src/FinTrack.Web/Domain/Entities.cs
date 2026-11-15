using System.ComponentModel.DataAnnotations.Schema;

namespace FinTrack.Web.Domain;

/// <summary>Category type: expense or income.</summary>
public enum CategoryType
{
    Expense = 0,
    Income = 1
}

/// <summary>Where the transaction came from.</summary>
public enum TransactionSource
{
    Manual = 0,
    Import = 1
}

/// <summary>Base entity: Id and audit dates.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Account: card, cash, deposit.</summary>
public class Account : Entity
{
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "KZT";
    public decimal InitialBalance { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
}

/// <summary>Transaction category with a color, icon and keywords for auto-categorization.</summary>
public class Category : Entity
{
    public string Name { get; set; } = "";
    public CategoryType Type { get; set; } = CategoryType.Expense;
    public string Color { get; set; } = "#4CAF50";
    public string Icon { get; set; } = "";

    /// <summary>Comma-separated keywords (e.g. "magnum, small").</summary>
    public string Keywords { get; set; } = "";

    [NotMapped]
    public IEnumerable<string> KeywordList =>
        Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Transaction. The amount is always positive; the direction is defined by the category type.</summary>
public class Transaction : Entity
{
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string Description { get; set; } = "";
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public TransactionSource Source { get; set; } = TransactionSource.Manual;

    /// <summary>ML model confidence, if the category was suggested by ML.NET.</summary>
    public float? MlConfidence { get; set; }

    /// <summary>Signed amount: income +, expense −.</summary>
    [NotMapped]
    public decimal SignedAmount => Category?.Type == CategoryType.Income ? Amount : -Amount;
}

/// <summary>Monthly spending limit for a category.</summary>
public class Budget : Entity
{
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Limit { get; set; }
}
