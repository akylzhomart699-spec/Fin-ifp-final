using FinTrack.Web.Components;
using FinTrack.Web.Data;
using FinTrack.Web.Hubs;
using FinTrack.Web.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// UI
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
});

// Data: a context factory is safe for Blazor Server, where a single tab lives for a long time
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=fintrack.db";
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(connectionString));

// Real-time
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificationService, SignalRNotificationService>();

// Business logic (Dependency Inversion: pages depend on interfaces)
builder.Services.AddSingleton<ICategorizationService, CategorizationService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<ICsvImportService, CsvImportService>();
builder.Services.AddScoped<AppState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // HTTPS is terminated by the host (Render) in front of the app, so no UseHttpsRedirection —
    // otherwise it causes an infinite redirect behind the proxy
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHub<NotificationHub>("/hubs/notifications");

// Create the database, seed demo data and train the model for the first time
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await SeedData.EnsureSeededAsync(db);
    await scope.ServiceProvider.GetRequiredService<ICategorizationService>().TrainAsync();
}

app.Run();

public partial class Program { }
