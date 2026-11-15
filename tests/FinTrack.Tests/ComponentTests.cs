using Bunit;
using FinTrack.Web.Components.Shared;
using FinTrack.Web.Domain;
using FinTrack.Web.Services;
using MudBlazor.Services;
using Xunit;

namespace FinTrack.Tests;

/// <summary>Blazor component tests with bUnit.</summary>
public class ComponentTests : TestContext
{
    public ComponentTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void MoneyText_ExpenseIsRedAndFormatted()
    {
        var cut = RenderComponent<MoneyText>(p => p
            .Add(x => x.Amount, -1500m)
            .Add(x => x.Colored, true));

        var span = cut.Find("span");
        Assert.Equal("-1 500 ₸", span.TextContent);
        Assert.Contains("money-expense", span.ClassName);
    }

    [Fact]
    public void MoneyText_NotColored_HasNoClass()
    {
        var cut = RenderComponent<MoneyText>(p => p.Add(x => x.Amount, 100m));
        Assert.True(string.IsNullOrEmpty(cut.Find("span").ClassName));
    }

    [Fact]
    public void BudgetProgress_ShowsExceededText()
    {
        var status = new BudgetStatus(new Budget { Limit = 10_000 }, new Category { Name = "Cafe", Color = "#E76F51" }, 12_000);
        var cut = RenderComponent<BudgetProgress>(p => p.Add(x => x.Status, status));

        Assert.Contains("Cafe", cut.Markup);
        Assert.Contains("Over by 2 000 ₸", cut.Find(".budget-remaining").TextContent);
    }

    [Fact]
    public void BudgetProgress_ShowsRemaining()
    {
        var status = new BudgetStatus(new Budget { Limit = 10_000 }, new Category { Name = "Cafe" }, 4_000);
        var cut = RenderComponent<BudgetProgress>(p => p.Add(x => x.Status, status));

        Assert.Contains("6 000 ₸ left", cut.Find(".budget-remaining").TextContent);
    }

    [Fact]
    public void EntityGrid_RendersTemplateForEachItem()
    {
        var cut = RenderComponent<EntityGrid<string>>(p => p
            .Add(x => x.Items, new[] { "a", "b", "c" })
            .Add(x => x.ItemTemplate, item => $"<div class='item'>{item}</div>"));

        Assert.Equal(3, cut.FindAll(".item").Count);
    }

    [Fact]
    public void EntityGrid_EmptyShowsEmptyText()
    {
        var cut = RenderComponent<EntityGrid<string>>(p => p
            .Add(x => x.Items, Array.Empty<string>())
            .Add(x => x.ItemTemplate, item => item)
            .Add(x => x.EmptyText, "No data"));

        Assert.Contains("No data", cut.Markup);
    }
}
