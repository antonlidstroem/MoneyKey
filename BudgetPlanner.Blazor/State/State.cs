using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Blazor.State;

// ═══════════════════════════════════════════════════════════════════════════════
// BUDGET STATE
// ═══════════════════════════════════════════════════════════════════════════════
public class BudgetState
{
    public int              ActiveBudgetId   { get; private set; }
    public string           ActiveBudgetName { get; private set; } = string.Empty;
    public BudgetMemberRole MyRole           { get; private set; }
    public List<BudgetDto>  MyBudgets        { get; private set; } = new();

    public event Action? StateChanged;

    public bool CanEdit => MyRole is BudgetMemberRole.Editor or BudgetMemberRole.Owner;
    public bool IsOwner => MyRole == BudgetMemberRole.Owner;

    public void SetBudgets(List<BudgetDto> budgets)
    {
        MyBudgets = budgets;

        if (budgets.Any())
        {
            // If current budget is still in the list keep it selected (refreshes name/role).
            var current = budgets.FirstOrDefault(b => b.Id == ActiveBudgetId);
            SetActiveBudget(current ?? budgets.First());
        }
        else
        {
            StateChanged?.Invoke();
        }
    }

    public void SetActiveBudget(BudgetDto b)
    {
        ActiveBudgetId   = b.Id;
        ActiveBudgetName = b.Name;
        MyRole           = b.MyRole;
        StateChanged?.Invoke();
    }

    public void UpdateActiveBudgetName(string newName)
    {
        ActiveBudgetName = newName;
        var b = MyBudgets.FirstOrDefault(x => x.Id == ActiveBudgetId);
        if (b != null)
        {
            var idx = MyBudgets.IndexOf(b);
            MyBudgets[idx] = new BudgetDto(b.Id, newName, b.Description, b.IsActive, b.CreatedAt, b.MyRole);
        }
        StateChanged?.Invoke();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// JOURNAL FILTER STATE
// ═══════════════════════════════════════════════════════════════════════════════
public class JournalFilterState
{
    public JournalQuery Query           { get; private set; } = DefaultQuery();
    public bool         IsFilterPanelOpen { get; set; }       = false;

    public event Action? FilterChanged;

    private static JournalQuery DefaultQuery() => new()
    {
        PageSize = 50,
        SortBy   = "Date",
        SortDir  = "desc"
    };

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
        Query.Page = 1;
        FilterChanged?.Invoke();
    }

    public void SetOnlyType(JournalEntryType t)
    {
        Query.IncludeTypes = new List<JournalEntryType> { t };
        Query.Page = 1;
        FilterChanged?.Invoke();
    }

    public void ShowAllTypes()
    {
        Query.IncludeTypes = new();
        Query.Page = 1;
        FilterChanged?.Invoke();
    }

    // Generic update — caller mutates the query, we reset page and fire.
    public void Update(Action<JournalQuery> modify)
    {
        modify(Query);
        Query.Page = 1;
        FilterChanged?.Invoke();
    }

    public void SetPage(int page)
    {
        Query.Page = page;
        // NOTE: SetPage does NOT fire FilterChanged — callers call LoadAsync directly.
        // This avoids a double-load when pagination buttons are pressed.
    }

    public void SetSort(string column)
    {
        if (Query.SortBy == column)
            Query.SortDir = Query.SortDir == "asc" ? "desc" : "asc";
        else
        {
            Query.SortBy  = column;
            Query.SortDir = "asc";
        }
        Query.Page = 1;
        FilterChanged?.Invoke();
    }

    public void ResetFilters()
    {
        var size  = Query.PageSize; // preserve page size preference
        Query     = DefaultQuery();
        Query.PageSize = size;
        FilterChanged?.Invoke();
    }

    public bool HasActiveFilters =>
        Query.IncludeTypes.Any()   ||
        Query.FilterByStartDate    ||
        Query.FilterByEndDate      ||
        Query.FilterByDescription  ||
        Query.FilterByCategory     ||
        Query.FilterByProject      ||
        Query.FilterByAmount       ||
        Query.ReceiptStatuses.Any();
}

// ═══════════════════════════════════════════════════════════════════════════════
// SIGNALR SERVICE
// ═══════════════════════════════════════════════════════════════════════════════
public class SignalRService : IAsyncDisposable
{
    private HubConnection? _hub;
    private int    _budgetId;
    private string _apiBase = string.Empty;
    private AuthenticationStateProvider? _authProvider;

    public event Func<BudgetHubEvent, Task>? OnBudgetEvent;

    public async Task ConnectAsync(
        string apiBase,
        string accessToken,
        int    budgetId,
        AuthenticationStateProvider? authProvider = null)
    {
        _apiBase      = apiBase;
        _authProvider = authProvider;

        if (_hub != null) { await _hub.DisposeAsync(); _hub = null; }

        _budgetId = budgetId;
        _hub      = BuildConnection(apiBase, accessToken);
        RegisterHandlers();

        await _hub.StartAsync();
        await _hub.InvokeAsync("JoinBudget", budgetId);
    }

    public async Task SwitchBudgetAsync(int newBudgetId)
    {
        if (_hub?.State == HubConnectionState.Connected)
        {
            await _hub.InvokeAsync("LeaveBudget", _budgetId);
            _budgetId = newBudgetId;
            await _hub.InvokeAsync("JoinBudget", newBudgetId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null) await _hub.DisposeAsync();
    }

    private HubConnection BuildConnection(string apiBase, string token) =>
        new HubConnectionBuilder()
            .WithUrl($"{apiBase}/hubs/budget?access_token={token}")
            .WithAutomaticReconnect(new TokenRefreshRetryPolicy(this))
            .Build();

    private void RegisterHandlers()
    {
        if (_hub == null) return;

        _hub.On<BudgetHubEvent>("BudgetEvent", async e =>
        {
            if (OnBudgetEvent != null) await OnBudgetEvent(e);
        });

        _hub.Reconnected += async _ =>
        {
            if (_hub?.State == HubConnectionState.Connected)
                await _hub.InvokeAsync("JoinBudget", _budgetId);
        };

        _hub.Closed += async ex =>
        {
            if (ex != null) await ReconnectWithFreshTokenAsync();
        };
    }

    internal async Task ReconnectWithFreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_apiBase)) return;

        string? freshToken = null;
        if (_authProvider != null)
        {
            try
            {
                await _authProvider.GetAuthenticationStateAsync();
                freshToken = Services.JwtAuthenticationStateProvider.AccessToken;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(freshToken)) return;

        try
        {
            if (_hub != null) { await _hub.DisposeAsync(); _hub = null; }
            _hub = BuildConnection(_apiBase, freshToken);
            RegisterHandlers();
            await _hub.StartAsync();
            await _hub.InvokeAsync("JoinBudget", _budgetId);
        }
        catch { }
    }

    private sealed class TokenRefreshRetryPolicy : IRetryPolicy
    {
        private readonly SignalRService _svc;
        private static readonly TimeSpan[] _delays =
        {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        public TokenRefreshRetryPolicy(SignalRService svc) => _svc = svc;

        public TimeSpan? NextRetryDelay(RetryContext ctx)
        {
            if (ctx.PreviousRetryCount >= _delays.Length) return null;
            _ = _svc.ReconnectWithFreshTokenAsync();
            return _delays[ctx.PreviousRetryCount];
        }
    }
}

// ─── SIGNALR EVENT DTO ────────────────────────────────────────────────────────
public class BudgetHubEvent
{
    public string   EventType      { get; set; } = string.Empty;
    public int      BudgetId       { get; set; }
    public int?     EntityId       { get; set; }
    public string?  UpdatedByEmail { get; set; }
    public DateTime Timestamp      { get; set; }
}
