# FinTrack — personal finance tracking on Blazor

FinTrack is a cross-platform web application built with **C# / .NET 10 / Blazor**. It lets you manage accounts, income and expenses, set monthly budgets, review analytics, and import bank statements from CSV. Transaction categories are assigned automatically by an **ML.NET** model.

## Features

- **Accounts**: Kaspi, cash, and similar account types; balances are calculated automatically.
- **Categories** with colors, icons, and keywords.
- **Transactions**: add, edit, delete, search, filter by period, account, and category; virtualized table.
- **Budgets**: monthly limits by category, warnings at 80% and 100%, and copying from the previous month.
- **Overview**: monthly balance, income and expenses, projected spending by month-end, donut chart by category, and a 6-month trend chart.
- **CSV import**: automatic delimiter detection, Russian and English headers, preview, duplicate detection, and row-level error reporting.
- **ML.NET**: multiclass text classification using `FeaturizeText` + `SdcaMaximumEntropy`. The model learns from your transactions and continues training after import. If it is uncertain, the category is selected using keywords.
- **SignalR**: budget overrun notifications; data updates immediately across all open tabs and devices.
- **Responsive interface**: MudBlazor, dark theme, sidebar on desktop, and bottom navigation on mobile.

## Running

Requires the **.NET 10 SDK** (https://dotnet.microsoft.com/download).

```bash
cd src/FinTrack.Web
dotnet run
```

Open http://localhost:5080. The `fintrack.db` database is created automatically and populated with demo data for 3 months.

**To open it from a phone** (computer and phone on the same Wi-Fi network):

```bash
dotnet run --launch-profile "lan (for phone)"
```

Then open `http://<computer IP>:5080` on the phone. You can find the IP using `ipconfig` (Windows) or `ifconfig` (macOS/Linux). If it does not open, allow port 5080 through the firewall.

**Reset demo data:** stop the application and delete `src/FinTrack.Web/fintrack.db`.

## Tests

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"   # with coverage
```

- **Unit** (xUnit): amount formatting and parsing, forecast logic, budget status, validation, CSV parsing, and keyword matching.
- **Integration**: services against a real in-memory SQLite database — balance checks, budget overrun notifications, category deletion restrictions, budget copying, import with duplicates, and ML model training.
- **Component** (bUnit): `MoneyText`, `BudgetProgress`, and generic `EntityGrid<TItem>`.

## Architecture

```
src/FinTrack.Web/
├── Domain/        entities: Account, Category, Transaction, Budget
├── Data/          AppDbContext (EF Core + SQLite), SeedData
├── Services/      business logic behind interfaces: accounts, categories,
│                  transactions, budgets, statistics, CSV import, ML.NET, AppState
├── Hubs/          SignalR hub and notification service
└── Components/
    ├── Layout/    MainLayout (theme, SignalR client, ErrorBoundary), navigation
    ├── Pages/     Overview, Transactions, Budgets, Accounts, Categories, Import
    ├── Shared/    reusable components: MoneyText, StatCard, BudgetProgress,
    │              TransactionList, MonthSelector, EmptyState, EntityGrid<TItem>
    └── Dialogs/   forms: transaction, account, category, budget limit
tests/FinTrack.Tests/   xUnit + bUnit
```

**Data flow:** page → service interface (DI) → `IDbContextFactory<AppDbContext>` → SQLite. After data changes, the service emits a `DataChanged` event via `INotificationService` (SignalR). `MainLayout` listens for it and calls `AppState.NotifyDataChanged()`, while pages subscribed to `AppState.OnChange` reload their data.

**Key design decisions:**

- **SOLID.** Pages depend only on interfaces such as `ITransactionService`. Implementations are registered in `Program.cs`, making them easy to replace in tests (`FakeNotifications`).
- **`IDbContextFactory`.** In Blazor Server, components live for a long time and `DbContext` is not thread-safe. That is why a new short-lived context is created for each operation.
- **`decimal` for money**, not `double`, to avoid rounding errors.
- **Error handling:** services return a `Result` with a clear message instead of throwing exceptions. `ErrorBoundary` catches component failures. If SignalR is unavailable, the app continues to work without live notifications. If the ML model is not trained, category matching falls back to keywords.
- **Performance:** `AsNoTracking` for reads, an index by date, `Virtualize` in tables, and background model training.
- **Components:** `[Parameter]`, `EventCallback<T>`, templated `EntityGrid<TItem>` with `RenderFragment<TItem>`, state managed via the scoped `AppState` service, and `CascadingParameter` in dialogs.

## CSV import format

```csv
Date;Description;Amount
01.11.2026;Magnum Cash&Carry;-14 250,00
05.11.2026;Salary;280 000
```

Expenses are written with a minus sign. Supported date formats: `dd.MM.yyyy`, `yyyy-MM-dd`, `dd/MM/yyyy`. A sample file is available on the import page.

## Roadmap

- Authentication and roles via ASP.NET Core Identity (multi-user mode).
- .NET MAUI Blazor Hybrid — a native app for Windows, macOS, Android, and iOS using the same components.
- Offline mode (PWA + IndexedDB) with synchronization.
- PostgreSQL + Docker, GitHub Actions CI, and Playwright E2E tests.
- PDF report export and Kazakh language support in the interface.
 