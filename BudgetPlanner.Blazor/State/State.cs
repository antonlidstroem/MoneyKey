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
    public int    ActiveBudgetId   { get; private set; }
    public string ActiveBudgetName { get; private set; } = string.Empty;
    public BudgetMemberRole MyRole { get; private set; }
    public List<BudgetDto>  MyBudgets { get; private set; } = new();

    public event Action? StateChanged;

    public bool CanEdit => MyRole is BudgetMemberRole.Editor or BudgetMemberRole.Owner;
    public bool IsOwner => MyRole == BudgetMemberRole.Owner;

    public void SetBudgets(List<BudgetDto> budgets)
    {
        MyBudgets = budgets;

        // FIX: Always refresh the active budget from the new list so that
        // re-initialisation (e.g. after token refresh) picks up changes.
        if (budgets.Any())
        {
            var current = budgets.FirstOrDefault(b => b.Id == ActiveBudgetId);
            if (current != null)
                SetActiveBudget(current);          // refresh role/name in case it changed
            else
                SetActiveBudget(budgets.First());  // first run or budget removed
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

    /// <summary>
    /// Updates the name of the active budget in local state after a successful
    /// PUT /api/budgets/{id} call (SettingsPage fix).
    /// </summary>
    public void UpdateActiveBudgetName(string newName)
    {
        ActiveBudgetName = newName;
        var b = MyBudgets.FirstOrDefault(x => x.Id == ActiveBudgetId);
        if (b != null)
        {
            // Replace the immutable record with an updated copy.
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
// SIGNALR SERVICE  — FIX: refresh token before reconnect attempts
// ═══════════════════════════════════════════════════════════════════════════════
public class SignalRService : IAsyncDisposable
{
    private HubConnection? _hub;
    private int    _budgetId;
    private string _apiBase  = string.Empty;

    // FIX: store AuthenticationStateProvider so we can refresh the token
    // on reconnect without taking a circular DI dependency at construction time.
    private AuthenticationStateProvider? _authProvider;

    public event Func<BudgetHubEvent, Task>? OnBudgetEvent;

    /// <param name="authProvider">
    /// Pass the scoped <see cref="AuthenticationStateProvider"/> from the
    /// calling component so token refresh works on reconnect.
    /// </param>
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

        _hub = BuildConnection(apiBase, accessToken);
        RegisterHandlers();
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

    // ── Private helpers ────────────────────────────────────────────────────────

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

        // FIX: on reconnect, rebuild the connection with a fresh token so the
        // hub does not reject us with 401 after the 15-minute access token expiry.
        _hub.Reconnecting += _ => Task.CompletedTask;

        _hub.Reconnected += async _ =>
        {
            // Re-join the budget group after reconnect.
            if (_hub?.State == HubConnectionState.Connected)
                await _hub.InvokeAsync("JoinBudget", _budgetId);
        };

        _hub.Closed += async ex =>
        {
            // Attempt a full reconnect with a fresh token after unexpected close.
            if (ex != null) await ReconnectWithFreshTokenAsync();
        };
    }

    internal async Task ReconnectWithFreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_apiBase)) return;

        // Refresh access token via the AuthStateProvider.
        string? freshToken = null;
        if (_authProvider != null)
        {
            try
            {
                await _authProvider.GetAuthenticationStateAsync();
                freshToken = Services.JwtAuthenticationStateProvider.AccessToken;
            }
            catch { /* ignore refresh errors — we'll retry on next Closed event */ }
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
        catch { /* swallow — reconnect will be retried on next Closed */ }
    }

    // ── Retry policy that refreshes the token on each reconnect attempt ────────
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
            // After 4 attempts give up — the Closed handler will try a full rebuild.
            if (ctx.PreviousRetryCount >= _delays.Length) return null;
            // Fire-and-forget token refresh before the next attempt.
            _ = _svc.ReconnectWithFreshTokenAsync();
            return _delays[ctx.PreviousRetryCount];
        }
    }
}

public class BudgetHubEvent
{
    public string  EventType      { get; set; } = string.Empty;
    public int     BudgetId       { get; set; }
    public int?    EntityId       { get; set; }
    public string? UpdatedByEmail { get; set; }
    public DateTime Timestamp     { get; set; }
}
