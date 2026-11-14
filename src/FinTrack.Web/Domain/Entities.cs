using System.ComponentModel.DataAnnotations.Schema;

namespace FinTrack.Web.Domain;

/// <summary>Тип категории: расход или доход.</summary>
public enum CategoryType
{
    Expense = 0,
    Income = 1
}

/// <summary>Откуда появилась операция.</summary>
public enum TransactionSource
{
    Manual = 0,
    Import = 1
}

/// <summary>Базовая сущность: Id и служебные даты.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Счёт: карта, наличные, депозит.</summary>
public class Account : Entity
{
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "KZT";
    public decimal InitialBalance { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
}

/// <summary>Категория операций с цветом, иконкой и ключевыми словами для автокатегоризации.</summary>
public class Category : Entity
{
    public string Name { get; set; } = "";
    public CategoryType Type { get; set; } = CategoryType.Expense;
    public string Color { get; set; } = "#4CAF50";
    public string Icon { get; set; } = "";

    /// <summary>Ключевые слова через запятую (например: "magnum, small").</summary>
    public string Keywords { get; set; } = "";

    [NotMapped]
    public IEnumerable<string> KeywordList =>
        Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Операция. Сумма всегда положительная, направление определяется типом категории.</summary>
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

    /// <summary>Уверенность ML-модели, если категорию предложил ML.NET.</summary>
    public float? MlConfidence { get; set; }

    /// <summary>Сумма со знаком: доход +, расход −.</summary>
    [NotMapped]
    public decimal SignedAmount => Category?.Type == CategoryType.Income ? Amount : -Amount;
}

/// <summary>Месячный лимит расходов по категории.</summary>
public class Budget : Entity
{
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Limit { get; set; }
}
