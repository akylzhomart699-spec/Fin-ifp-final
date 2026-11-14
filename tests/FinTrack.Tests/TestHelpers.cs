using FinTrack.Web.Data;
using FinTrack.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinTrack.Tests;

/// <summary>SQLite в памяти: настоящая реляционная БД без файлов, создаётся на каждый тест.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public IDbContextFactory<AppDbContext> Factory { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        Factory = new SimpleFactory(options);

        using var db = Factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateContext() => Factory.CreateDbContext();

    public void Dispose() => _connection.Dispose();

    private sealed class SimpleFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}

/// <summary>Подменяет SignalR: запоминает, какие уведомления были бы отправлены.</summary>
public sealed class FakeNotifications : INotificationService
{
    public List<(string Message, bool Exceeded)> Alerts { get; } = new();
    public int DataChangedCount { get; private set; }

    public Task BudgetAlertAsync(string message, bool exceeded)
    {
        Alerts.Add((message, exceeded));
        return Task.CompletedTask;
    }

    public Task DataChangedAsync()
    {
        DataChangedCount++;
        return Task.CompletedTask;
    }
}
