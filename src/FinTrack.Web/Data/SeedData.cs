using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace FinTrack.Web.Data;

/// <summary>Демо-данные: счета, категории, операции за 3 месяца и бюджеты на текущий месяц.</summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db, DateTime? todayOverride = null)
    {
        if (await db.Accounts.AnyAsync()) return;

        var today = (todayOverride ?? DateTime.Today).Date;

        var kaspi = new Account { Name = "Kaspi Gold", InitialBalance = 150_000m };
        var cash = new Account { Name = "Наличные", InitialBalance = 20_000m };
        db.Accounts.AddRange(kaspi, cash);

        Category Cat(string name, CategoryType type, string color, string icon, string keywords) =>
            new() { Name = name, Type = type, Color = color, Icon = icon, Keywords = keywords };

        var categories = new List<Category>
        {
            Cat("Продукты", CategoryType.Expense, "#2A9D8F", Icons.Material.Filled.ShoppingCart,
                "magnum, small, galmart, анвар, арзан, супермаркет, продукты"),
            Cat("Кафе и рестораны", CategoryType.Expense, "#E76F51", Icons.Material.Filled.LocalCafe,
                "starbucks, kfc, coffee, кофе, wolt, glovo, ресторан, кафе, burger"),
            Cat("Транспорт", CategoryType.Expense, "#264653", Icons.Material.Filled.DirectionsCar,
                "yandex go, onay, азс, helios, такси, бензин, автобус"),
            Cat("Связь и интернет", CategoryType.Expense, "#8AB17D", Icons.Material.Filled.Wifi,
                "beeline, kcell, tele2, activ, kazakhtelecom, интернет"),
            Cat("Развлечения", CategoryType.Expense, "#F4A261", Icons.Material.Filled.SportsEsports,
                "kinopark, chaplin, кино, spotify, steam, netflix"),
            Cat("Одежда", CategoryType.Expense, "#9C6ADE", Icons.Material.Filled.Checkroom,
                "zara, lc waikiki, adidas, h&m, одежда"),
            Cat("Здоровье", CategoryType.Expense, "#D62828", Icons.Material.Filled.LocalHospital,
                "аптека, europharma, биосфера, клиника, стоматолог"),
            Cat("Образование", CategoryType.Expense, "#3A86FF", Icons.Material.Filled.School,
                "udemy, coursera, курс, книжный, меломан"),
            Cat("Жильё", CategoryType.Expense, "#6D597A", Icons.Material.Filled.Home,
                "аренда, коммунал, алсеко, квартира"),
            Cat("Прочее", CategoryType.Expense, "#8D99AE", Icons.Material.Filled.MoreHoriz, ""),
            Cat("Зарплата", CategoryType.Income, "#2E7D5B", Icons.Material.Filled.Work,
                "зарплата, salary, аванс"),
            Cat("Стипендия", CategoryType.Income, "#43AA8B", Icons.Material.Filled.AccountBalance,
                "стипендия"),
            Cat("Переводы", CategoryType.Income, "#90BE6D", Icons.Material.Filled.SwapHoriz,
                "перевод, transfer"),
        };
        db.Categories.AddRange(categories);
        Category ByName(string n) => categories.First(c => c.Name == n);

        var templates = new (string Category, string[] Descriptions, int Min, int Max, int PerMonth)[]
        {
            ("Продукты", new[] { "Magnum Cash&Carry", "Small", "Galmart", "Анвар супермаркет", "Арзан" }, 2_500, 18_000, 10),
            ("Кафе и рестораны", new[] { "Starbucks", "KFC", "Coffee Boom", "Wolt доставка", "Glovo" }, 1_200, 7_000, 8),
            ("Транспорт", new[] { "Yandex Go", "Onay пополнение", "АЗС Helios", "Такси" }, 300, 6_000, 8),
            ("Связь и интернет", new[] { "Beeline", "Kazakhtelecom" }, 3_000, 7_000, 2),
            ("Развлечения", new[] { "Kinopark", "Spotify", "Steam" }, 1_500, 8_000, 2),
            ("Одежда", new[] { "Zara", "LC Waikiki" }, 8_000, 30_000, 1),
            ("Здоровье", new[] { "Аптека Europharma", "Аптека Биосфера" }, 1_000, 9_000, 1),
            ("Образование", new[] { "Udemy курс", "Книжный Меломан" }, 4_000, 12_000, 1),
        };

        var rnd = new Random(42);
        var transactions = new List<Transaction>();
        var currentMonth = new DateTime(today.Year, today.Month, 1);

        for (var offset = -2; offset <= 0; offset++)
        {
            var monthStart = currentMonth.AddMonths(offset);
            var lastDay = offset == 0 ? today.Day : DateTime.DaysInMonth(monthStart.Year, monthStart.Month);

            void Add(string category, string description, decimal amount, int day, Account? account = null) =>
                transactions.Add(new Transaction
                {
                    Amount = amount,
                    Description = description,
                    Date = monthStart.AddDays(Math.Min(day, lastDay) - 1),
                    AccountId = (account ?? kaspi).Id,
                    CategoryId = ByName(category).Id,
                });

            Add("Жильё", "Аренда квартиры", 120_000m, 1);
            if (lastDay >= 5) Add("Зарплата", "Зарплата ТОО «Алем»", 280_000m, 5);
            if (lastDay >= 25) Add("Стипендия", "Стипендия", 52_000m, 25);
            if (lastDay >= 15) Add("Переводы", "Перевод от мамы", 30_000m, 15);

            foreach (var t in templates)
            {
                // В текущем месяце операций пропорционально меньше — месяц ещё не закончился
                var count = Math.Max(1, t.PerMonth * lastDay / DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
                for (var i = 0; i < count; i++)
                {
                    var amount = Math.Round((decimal)rnd.Next(t.Min, t.Max) / 10m) * 10m;
                    var account = rnd.NextDouble() < 0.8 ? kaspi : cash;
                    Add(t.Category, t.Descriptions[rnd.Next(t.Descriptions.Length)], amount, rnd.Next(1, lastDay + 1), account);
                }
            }
        }
        db.Transactions.AddRange(transactions);

        db.Budgets.AddRange(
            new Budget { CategoryId = ByName("Продукты").Id, Year = today.Year, Month = today.Month, Limit = 90_000m },
            new Budget { CategoryId = ByName("Кафе и рестораны").Id, Year = today.Year, Month = today.Month, Limit = 35_000m },
            new Budget { CategoryId = ByName("Транспорт").Id, Year = today.Year, Month = today.Month, Limit = 25_000m },
            new Budget { CategoryId = ByName("Развлечения").Id, Year = today.Year, Month = today.Month, Limit = 10_000m });

        await db.SaveChangesAsync();
    }
}
