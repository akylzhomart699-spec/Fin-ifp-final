using FinTrack.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace FinTrack.Web.Hubs;

/// <summary>SignalR hub: broadcasts budget alerts and a "data changed" signal to all open clients.</summary>
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
            // Notifications are non-critical: a failure must not break saving data
            logger.LogWarning(ex, "Failed to send SignalR event {Method}", method);
        }
    }
}
