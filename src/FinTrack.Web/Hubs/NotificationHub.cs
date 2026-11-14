using FinTrack.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace FinTrack.Web.Hubs;

/// <summary>SignalR-хаб: рассылает уведомления о бюджете и сигнал «данные изменились» всем открытым клиентам.</summary>
public sealed class NotificationHub : Hub
{
}

public static class HubEvents
{
    public const string BudgetAlert = "BudgetAlert";
    public const string DataChanged = "DataChanged";
}

public sealed class SignalRNotificationService(
    IHubContext<NotificationHub> hub,
    ILogger<SignalRNotificationService> logger) : INotificationService
{
    public Task BudgetAlertAsync(string message, bool exceeded) =>
        SendAsync(HubEvents.BudgetAlert, message, exceeded);

    public Task DataChangedAsync() => SendAsync(HubEvents.DataChanged);

    private async Task SendAsync(string method, params object?[] args)
    {
        try
        {
            await hub.Clients.All.SendCoreAsync(method, args);
        }
        catch (Exception ex)
        {
            // Уведомление — не критичная часть: ошибка не должна ломать сохранение данных
            logger.LogWarning(ex, "Не удалось отправить SignalR-событие {Method}", method);
        }
    }
}
