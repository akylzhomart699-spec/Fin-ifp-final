using System.Globalization;
using FinTrack.Web.Domain;
using MudBlazor;

namespace FinTrack.Web.Services;

/// <summary>Результат операции: успех или понятное пользователю сообщение об ошибке.</summary>
public sealed record Result(bool Ok, string? Error = null, int Count = 0)
{
    public static Result Success(int count = 0) => new(true, null, count);
    public static Result Fail(string error) => new(false, error);
}

/// <summary>Форматирование и разбор денежных сумм в тенге (не зависит от культуры ОС).</summary>
public static class Money
{
    private static readonly NumberFormatInfo Nfi = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ",",
    };

    public static string Format(decimal amount, bool showPlus = false)
    {
        var text = Math.Abs(amount).ToString("#,0.##", Nfi) + " ₸";
        if (amount < 0) return "-" + text;
        return showPlus && amount > 0 ? "+" + text : text;
    }

    /// <summary>Разбирает "-12 500,50 ₸", "1,234.56", "350000" и т. п.</summary>
    public static bool TryParse(string? raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = new string(raw.Replace('−', '-')
            .Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray());
        if (cleaned.Length == 0) return false;

        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            // Последний разделитель — десятичный, остальные — разделители тысяч
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", "").Replace(',', '.')
                : cleaned.Replace(",", "");
        }
        else
        {
            cleaned = cleaned.Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }
}

public static class MonthNames
{
    public static readonly string[] Full =
        ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

    public static readonly string[] Short =
        ["янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];
}

/// <summary>Готовые цвета и иконки для категорий.</summary>
public static class CategoryPresets
{
    public static readonly string[] Colors =
    [
        "#2A9D8F", "#E76F51", "#264653", "#8AB17D", "#F4A261", "#9C6ADE",
        "#D62828", "#3A86FF", "#6D597A", "#8D99AE", "#2E7D5B", "#43AA8B", "#90BE6D"
    ];

    public static readonly (string Name, string Icon)[] IconList =
    [
        ("Корзина", Icons.Material.Filled.ShoppingCart),
        ("Кофе", Icons.Material.Filled.LocalCafe),
        ("Машина", Icons.Material.Filled.DirectionsCar),
        ("Wi-Fi", Icons.Material.Filled.Wifi),
        ("Игры", Icons.Material.Filled.SportsEsports),
        ("Одежда", Icons.Material.Filled.Checkroom),
        ("Здоровье", Icons.Material.Filled.LocalHospital),
        ("Учёба", Icons.Material.Filled.School),
        ("Дом", Icons.Material.Filled.Home),
        ("Работа", Icons.Material.Filled.Work),
        ("Банк", Icons.Material.Filled.AccountBalance),
        ("Перевод", Icons.Material.Filled.SwapHoriz),
        ("Подарок", Icons.Material.Filled.CardGiftcard),
        ("Путешествия", Icons.Material.Filled.Flight),
        ("Другое", Icons.Material.Filled.MoreHoriz),
    ];

    public static string IconName(string icon) =>
        IconList.FirstOrDefault(i => i.Icon == icon).Name ?? "Иконка";

    public static string TypeName(CategoryType type) => type == CategoryType.Income ? "Доход" : "Расход";
}

public static class DialogDefaults
{
    public static DialogOptions Form => new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true,
    };
}
