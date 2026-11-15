using System.Globalization;
using FinTrack.Web.Domain;
using MudBlazor;

namespace FinTrack.Web.Services;

/// <summary>Operation result: success or a user-friendly error message.</summary>
public sealed record Result(bool Ok, string? Error = null, int Count = 0)
{
    public static Result Success(int count = 0) => new(true, null, count);
    public static Result Fail(string error) => new(false, error);
}

/// <summary>Formatting and parsing of tenge amounts (independent of the OS culture).</summary>
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

    /// <summary>Parses "-12 500,50 ₸", "1,234.56", "350000", etc.</summary>
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
            // The last separator is the decimal one; the others are thousands separators
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
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static readonly string[] Short =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}

/// <summary>Preset colors and icons for categories.</summary>
public static class CategoryPresets
{
    public static readonly string[] Colors =
    [
        "#2A9D8F", "#E76F51", "#264653", "#8AB17D", "#F4A261", "#9C6ADE",
        "#D62828", "#3A86FF", "#6D597A", "#8D99AE", "#2E7D5B", "#43AA8B", "#90BE6D"
    ];

    public static readonly (string Name, string Icon)[] IconList =
    [
        ("Cart", Icons.Material.Filled.ShoppingCart),
        ("Coffee", Icons.Material.Filled.LocalCafe),
        ("Car", Icons.Material.Filled.DirectionsCar),
        ("Wi-Fi", Icons.Material.Filled.Wifi),
        ("Games", Icons.Material.Filled.SportsEsports),
        ("Clothing", Icons.Material.Filled.Checkroom),
        ("Health", Icons.Material.Filled.LocalHospital),
        ("Education", Icons.Material.Filled.School),
        ("Home", Icons.Material.Filled.Home),
        ("Work", Icons.Material.Filled.Work),
        ("Bank", Icons.Material.Filled.AccountBalance),
        ("Transfer", Icons.Material.Filled.SwapHoriz),
        ("Gift", Icons.Material.Filled.CardGiftcard),
        ("Travel", Icons.Material.Filled.Flight),
        ("Other", Icons.Material.Filled.MoreHoriz),
    ];

    public static string IconName(string icon) =>
        IconList.FirstOrDefault(i => i.Icon == icon).Name ?? "Icon";

    public static string TypeName(CategoryType type) => type == CategoryType.Income ? "Income" : "Expense";
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
