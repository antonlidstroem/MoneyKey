using Microsoft.AspNetCore.SignalR.Client;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Blazor.State;

// ═══════════════════════════════════════════════════════════════════════════════
// BUDGET STATE
// ═══════════════════════════════════════════════════════════════════════════════
public class BudgetState
{
    public int    ActiveBudgetId   { get; private set; }
    public string ActiveBudgetName { get; private set; } = string.Empty;
    public BudgetMemberRole MyRole { get; private set; }
    public List<BudgetDto>  MyBudgets { get; private set; } = new();

    public event Action? StateChanged;

    public bool CanEdit  => MyRole is BudgetMemberRole.Editor or BudgetMemberRole.Owner;
    public bool IsOwner  => MyRole == BudgetMemberRole.Owner;

    public void SetBudgets(List<BudgetDto> budgets)
    {
        MyBudgets = budgets;
        if (budgets.Any() && ActiveBudgetId == 0) SetActiveBudget(budgets.First());
        StateChanged?.Invoke();
    }

    public void SetActiveBudget(BudgetDto b)
    {
        ActiveBudgetId   = b.Id;
        ActiveBudgetName = b.Name;
        MyRole           = b.MyRole;
        StateChanged?.Invoke();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// JOURNAL FILTER STATE
// ═══════════════════════════════════════════════════════════════════════════════
public class JournalFilterState
{
    public JournalQuery Query { get; private set; } = new()
        { PageSize = 50, SortBy = "Date", SortDir = "desc" };

    public bool IsFilterPanelOpen { get; set; } = false;

    public event Action? FilterChanged;

    public bool IsTypeIncluded(JournalEntryType t) =>
        !Query.IncludeTypes.Any() || Query.IncludeTypes.Contains(t);

    public void ToggleType(JournalEntryType t)
    {
        if (!Query.IncludeTypes.Any())
            Query.IncludeTypes = Enum.GetValues<JournalEntryType>().Where(x => x != t).ToList();
        else if (Query.IncludeTypes.Contains(t))
        {
            Query.IncludeTypes.Remove(t);
            if (!Query.IncludeTypes.Any()) Query.IncludeTypes = new();
        }
        else
        {
            Query.IncludeTypes.Add(t);
            if (Query.IncludeTypes.Count == Enum.GetValues<JournalEntryType>().Length)
                Query.IncludeTypes = new();
        }
        Query.Page = 1; FilterChanged?.Invoke();
    }

    public void SetOnlyType(JournalEntryType t)
    {
        Query.IncludeTypes = new List<JournalEntryType> { t };
        Query.Page = 1; FilterChanged?.Invoke();
    }

    public void ShowAllTypes()
    {
        Query.IncludeTypes = new(); Query.Page = 1; FilterChanged?.Invoke();
    }

    public void Update(Action<JournalQuery> modify)
    {
        modify(Query); Query.Page = 1; FilterChanged?.Invoke();
    }

    public void SetPage(int page) { Query.Page = page; FilterChanged?.Invoke(); }

    public void SetSort(string column)
    {
        if (Query.SortBy == column)
            Query.SortDir = Query.SortDir == "asc" ? "desc" : "asc";
        else { Query.SortBy = column; Query.SortDir = "asc"; }
        Query.Page = 1; FilterChanged?.Invoke();
    }

    public void ResetFilters()
    {
        Query = new JournalQuery { PageSize = Query.PageSize, SortBy = "Date", SortDir = "desc" };
        FilterChanged?.Invoke();
    }

    public bool HasActiveFilters =>
        Query.IncludeTypes.Any()  || Query.FilterByStartDate || Query.FilterByEndDate ||
        Query.FilterByDescription || Query.FilterByCategory  || Query.FilterByProject ||
        Query.FilterByAmount      || Query.ReceiptStatuses.Any();
}

// ═══════════════════════════════════════════════════════════════════════════════
// SIGNALR SERVICE
// ═══════════════════════════════════════════════════════════════════════════════
public class SignalRService : IAsyncDisposable
{
    private HubConnection? _hub;
    private int _budgetId;

    public event Func<BudgetHubEvent, Task>? OnBudgetEvent;

    public async Task ConnectAsync(string apiBase, string accessToken, int budgetId)
    {
        if (_hub != null) { await _hub.DisposeAsync(); _hub = null; }
        _budgetId = budgetId;
        _hub = new HubConnectionBuilder()
            .WithUrl($"{apiBase}/hubs/budget?access_token={accessToken}")
            .WithAutomaticReconnect()
            .Build();
        _hub.On<BudgetHubEvent>("BudgetEvent", async e =>
        {
            if (OnBudgetEvent != null) await OnBudgetEvent(e);
        });
        await _hub.StartAsync();
        await _hub.InvokeAsync("JoinBudget", budgetId);
    }

    public async Task SwitchBudgetAsync(int newId)
    {
        if (_hub?.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync("LeaveBudget", _budgetId);
            _budgetId = newId;
            await _hub.InvokeAsync("JoinBudget", newId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null) await _hub.DisposeAsync();
    }
}

public class BudgetHubEvent
{
    public string EventType    { get; set; } = string.Empty;
    public int BudgetId        { get; set; }
    public int? EntityId       { get; set; }
    public string? UpdatedByEmail { get; set; }
    public DateTime Timestamp  { get; set; }
}
