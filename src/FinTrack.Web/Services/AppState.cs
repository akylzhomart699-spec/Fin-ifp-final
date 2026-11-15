namespace FinTrack.Web.Services;

/// <summary>
/// Per-tab UI state (scoped): the selected month and theme.
/// Components subscribe to OnChange and re-render when it changes.
/// </summary>
public sealed class AppState
{
    public int Year { get; private set; } = DateTime.Today.Year;
    public int Month { get; private set; } = DateTime.Today.Month;
    public bool IsDarkMode { get; private set; }

    public DateTime MonthStart => new(Year, Month, 1);

    public event Action? OnChange;

    public void SetMonth(int year, int month)
    {
        if (year == Year && month == Month) return;
        Year = year;
        Month = month;
        Notify();
    }

    public void ShiftMonth(int delta)
    {
        var d = MonthStart.AddMonths(delta);
        SetMonth(d.Year, d.Month);
    }

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        Notify();
    }

    /// <summary>Called when data has changed (including from another device via SignalR).</summary>
    public void NotifyDataChanged() => Notify();

    private void Notify() => OnChange?.Invoke();
}
