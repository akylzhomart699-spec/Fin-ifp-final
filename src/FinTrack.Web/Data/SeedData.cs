using FinTrack.Web.Domain;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace FinTrack.Web.Data;

/// <summary>Demo data: accounts, categories, 3 months of transactions and budgets for the current month.</summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(AppDbContext db, DateTime? todayOverride = null)
    {
        if (await db.Accounts.AnyAsync()) return;

        var today = (todayOverride ?? DateTime.Today).Date;

        var kaspi = new Account { Name = "Kaspi Gold", InitialBalance = 150_000m };
        var cash = new Account { Name = "Cash", InitialBalance = 20_000m };
        db.Accounts.AddRange(kaspi, cash);

        Category Cat(string name, CategoryType type, string color, string icon, string keywords) =>
            new() { Name = name, Type = type, Color = color, Icon = icon, Keywords = keywords };

        var categories = new List<Category>
        {
            Cat("Groceries", CategoryType.Expense, "#2A9D8F", Icons.Material.Filled.ShoppingCart,
                "magnum, small, galmart, anvar, arzan, supermarket, grocery, анвар, арзан, супермаркет, продукты"),
            Cat("Cafes & Restaurants", CategoryType.Expense, "#E76F51", Icons.Material.Filled.LocalCafe,
                "starbucks, kfc, coffee, wolt, glovo, restaurant, cafe, burger, delivery, yandex.eda, кофе, ресторан, кафе"),
            Cat("Transport", CategoryType.Expense, "#264653", Icons.Material.Filled.DirectionsCar,
                "yandex go, onay, helios, taxi, fuel, gas station, bus, азс, такси, бензин, автобус"),
            Cat("Phone & Internet", CategoryType.Expense, "#8AB17D", Icons.Material.Filled.Wifi,
                "beeline, kcell, tele2, activ, kazakhtelecom, internet, mobile, интернет"),
            Cat("Entertainment", CategoryType.Expense, "#F4A261", Icons.Material.Filled.SportsEsports,
                "kinopark, chaplin, cinema, tickets, spotify, steam, netflix, кино"),
            Cat("Clothing", CategoryType.Expense, "#9C6ADE", Icons.Material.Filled.Checkroom,
                "zara, lc waikiki, adidas, h&m, clothing, одежда"),
            Cat("Health", CategoryType.Expense, "#D62828", Icons.Material.Filled.LocalHospital,
                "pharmacy, europharma, biosfera, clinic, dentist, аптека, биосфера, клиника, стоматолог"),
            Cat("Education", CategoryType.Expense, "#3A86FF", Icons.Material.Filled.School,
                "udemy, coursera, course, bookstore, meloman, курс, книжный, меломан"),
            Cat("Housing", CategoryType.Expense, "#6D597A", Icons.Material.Filled.Home,
                "rent, utilities, alseco, apartment, аренда, коммунал, алсеко, квартира"),
            Cat("Other", CategoryType.Expense, "#8D99AE", Icons.Material.Filled.MoreHoriz, ""),
            Cat("Salary", CategoryType.Income, "#2E7D5B", Icons.Material.Filled.Work,
                "salary, payroll, advance, зарплата, аванс"),
            Cat("Scholarship", CategoryType.Income, "#43AA8B", Icons.Material.Filled.AccountBalance,
                "scholarship, stipend, стипендия"),
            Cat("Transfers", CategoryType.Income, "#90BE6D", Icons.Material.Filled.SwapHoriz,
                "transfer, перевод"),
        };
        db.Categories.AddRange(categories);
        Category ByName(string n) => categories.First(c => c.Name == n);

        var templates = new (string Category, string[] Descriptions, int Min, int Max, int PerMonth)[]
        {
            ("Groceries", new[] { "Magnum Cash&Carry", "Small", "Galmart", "Anvar supermarket", "Arzan" }, 2_500, 18_000, 10),
            ("Cafes & Restaurants", new[] { "Starbucks", "KFC", "Coffee Boom", "Wolt delivery", "Glovo" }, 1_200, 7_000, 8),
            ("Transport", new[] { "Yandex Go", "Onay top-up", "Helios gas station", "Taxi" }, 300, 6_000, 8),
            ("Phone & Internet", new[] { "Beeline", "Kazakhtelecom" }, 3_000, 7_000, 2),
            ("Entertainment", new[] { "Kinopark", "Spotify", "Steam" }, 1_500, 8_000, 2),
            ("Clothing", new[] { "Zara", "LC Waikiki" }, 8_000, 30_000, 1),
            ("Health", new[] { "Europharma pharmacy", "Biosfera pharmacy" }, 1_000, 9_000, 1),
            ("Education", new[] { "Udemy course", "Meloman bookstore" }, 4_000, 12_000, 1),
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

            Add("Housing", "Apartment rent", 120_000m, 1);
            if (lastDay >= 5) Add("Salary", "Salary — Alem LLP", 280_000m, 5);
            if (lastDay >= 25) Add("Scholarship", "Scholarship", 52_000m, 25);
            if (lastDay >= 15) Add("Transfers", "Transfer from mom", 30_000m, 15);

            foreach (var t in templates)
            {
                // The current month has proportionally fewer transactions — it is not over yet
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
            new Budget { CategoryId = ByName("Groceries").Id, Year = today.Year, Month = today.Month, Limit = 90_000m },
            new Budget { CategoryId = ByName("Cafes & Restaurants").Id, Year = today.Year, Month = today.Month, Limit = 35_000m },
            new Budget { CategoryId = ByName("Transport").Id, Year = today.Year, Month = today.Month, Limit = 25_000m },
            new Budget { CategoryId = ByName("Entertainment").Id, Year = today.Year, Month = today.Month, Limit = 10_000m });

        await db.SaveChangesAsync();
    }
}
