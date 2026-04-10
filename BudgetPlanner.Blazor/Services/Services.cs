// ═══════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.Blazor/Services/Services.cs  — COMPLETE REPLACEMENT
//
// ROOT CAUSE OF TaskListDto CS0246 ERRORS:
//   TaskListApiService was accidentally nested INSIDE ToastService.
//   The compiler saw it as ToastService.TaskListApiService which is not what
//   Program.cs registers (builder.Services.AddScoped<TaskListApiService>()).
//   Moving it outside ToastService fixes all ~20 "type not found" errors.
// ═══════════════════════════════════════════════════════════════════════════════
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;
using Microsoft.Extensions.Configuration;

namespace BudgetPlanner.Blazor.Services;

// ─── JWT AUTH STATE PROVIDER ──────────────────────────────────────────────────
public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private static string?  _accessToken;
    private static UserDto? _currentUser;

    private readonly HttpClient _authClient;

    public JwtAuthenticationStateProvider(IConfiguration config)
    {
        var apiBase = config["ApiBaseUrl"] ?? "https://localhost:7000";
        _authClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    }

    public static string?  AccessToken => _accessToken;
    public static UserDto? CurrentUser => _currentUser;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && !IsExpired(_accessToken))
            return Build(_accessToken);

        try
        {
            var r = await _authClient.PostAsync("api/auth/refresh", null);
            if (r.IsSuccessStatusCode)
            {
                var result = await r.Content.ReadFromJsonAsync<AuthResultDto>();
                if (result != null) { SetToken(result.AccessToken, result.User); return Build(result.AccessToken); }
            }
        }
        catch { }

        ClearToken();
        return Anonymous();
    }

    public void SetToken(string token, UserDto user)
    {
        _accessToken = token;
        _currentUser = user;
        NotifyAuthenticationStateChanged(Task.FromResult(Build(token)));
    }

    public void ClearToken()
    {
        _accessToken = null;
        _currentUser = null;
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous()));
    }

    private static AuthenticationState Build(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var id  = new ClaimsIdentity(jwt.Claims, "jwt");
            return new AuthenticationState(new ClaimsPrincipal(id));
        }
        catch { return Anonymous(); }
    }

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static bool IsExpired(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo < DateTime.UtcNow.AddSeconds(-30);
        }
        catch { return true; }
    }
}

// ─── AUTH MESSAGE HANDLER ─────────────────────────────────────────────────────
public class AuthorizationMessageHandler : DelegatingHandler
{
    private readonly IServiceProvider _sp;
    private bool _refreshing;

    public AuthorizationMessageHandler(IServiceProvider sp) => _sp = sp;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content != null)
            await request.Content.LoadIntoBufferAsync();

        if (!string.IsNullOrEmpty(JwtAuthenticationStateProvider.AccessToken))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", JwtAuthenticationStateProvider.AccessToken);

        var response = await base.SendAsync(request, ct);

        var isAuthEndpoint = request.RequestUri?.AbsolutePath
            .Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) ?? false;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && !isAuthEndpoint && !_refreshing)
        {
            _refreshing = true;
            try
            {
                var provider = (JwtAuthenticationStateProvider)_sp
                    .GetRequiredService<AuthenticationStateProvider>();
                await provider.GetAuthenticationStateAsync();

                if (!string.IsNullOrEmpty(JwtAuthenticationStateProvider.AccessToken))
                {
                    var retry = await CloneRequestAsync(request);
                    retry.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", JwtAuthenticationStateProvider.AccessToken);
                    response = await base.SendAsync(retry, ct);
                }
            }
            finally { _refreshing = false; }
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage src)
    {
        var clone = new HttpRequestMessage(src.Method, src.RequestUri);
        foreach (var h in src.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (src.Content != null)
        {
            var ms = new MemoryStream();
            await src.Content.CopyToAsync(ms);
            ms.Position   = 0;
            clone.Content = new StreamContent(ms);
            foreach (var h in src.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }
}

// ─── BASE API SERVICE ─────────────────────────────────────────────────────────
public abstract class ApiServiceBase
{
    protected readonly HttpClient Http;
    protected ApiServiceBase(HttpClient http) => Http = http;

    protected Task<T?> GetAsync<T>(string url) => Http.GetFromJsonAsync<T>(url);

    protected async Task<T?> PostAsync<T>(string url, object body)
    {
        var r = await Http.PostAsJsonAsync(url, body);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<T>();
    }

    protected async Task DeleteAsync(string url)
    {
        var r = await Http.DeleteAsync(url);
        r.EnsureSuccessStatusCode();
    }
}

// ─── AUTH SERVICE ─────────────────────────────────────────────────────────────
public class AuthService : ApiServiceBase
{
    private readonly JwtAuthenticationStateProvider _provider;

    public AuthService(HttpClient http, JwtAuthenticationStateProvider provider) : base(http)
        => _provider = provider;

    public async Task<AuthResultDto?> RegisterAsync(RegisterDto dto)
    {
        var r = await PostAsync<AuthResultDto>("api/auth/register", dto);
        if (r != null) _provider.SetToken(r.AccessToken, r.User);
        return r;
    }

    public async Task<AuthResultDto?> LoginAsync(LoginDto dto)
    {
        var r = await PostAsync<AuthResultDto>("api/auth/login", dto);
        if (r != null) _provider.SetToken(r.AccessToken, r.User);
        return r;
    }

    public async Task LogoutAsync()
    {
        try { await Http.PostAsync("api/auth/logout", null); } catch { }
        _provider.ClearToken();
    }

    public async Task<AuthResultDto?> AcceptInviteAsync(string token)
    {
        var response = await Http.PostAsJsonAsync("api/auth/accept-invite", new AcceptInviteDto(token));
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadFromJsonAsync<ApiError>();
            throw new Exception(err?.Message ?? "Kunde inte acceptera inbjudan.");
        }
        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        if (result != null) _provider.SetToken(result.AccessToken, result.User);
        return result;
    }

    public UserDto? CurrentUser => JwtAuthenticationStateProvider.CurrentUser;

    private record ApiError(string Message);
}

// ─── BUDGET SERVICE ───────────────────────────────────────────────────────────
public class BudgetService : ApiServiceBase
{
    public BudgetService(HttpClient http) : base(http) { }

    public Task<List<BudgetDto>?> GetMyBudgetsAsync() =>
        GetAsync<List<BudgetDto>>("api/budgets");

    public Task<BudgetDto?> CreateAsync(CreateBudgetDto dto) =>
        PostAsync<BudgetDto>("api/budgets", dto);

    public Task<List<Category>?> GetCategoriesAsync(int budgetId) =>
        GetAsync<List<Category>>($"api/budgets/{budgetId}/categories");

    public async Task InviteAsync(int budgetId, InviteMemberDto dto) =>
        await PostAsync<object>($"api/budgets/{budgetId}/invite", dto);

    public async Task UpdateAsync(int budgetId, UpdateBudgetDto dto)
    {
        var r = await Http.PutAsJsonAsync($"api/budgets/{budgetId}", dto);
        r.EnsureSuccessStatusCode();
    }
}

// ─── PROJECT SERVICE ──────────────────────────────────────────────────────────
public class ProjectService : ApiServiceBase
{
    public ProjectService(HttpClient http) : base(http) { }

    public Task<List<ProjectDto>?> GetAllAsync(int budgetId) =>
        GetAsync<List<ProjectDto>>($"api/budgets/{budgetId}/projects");

    public Task<ProjectDto?> GetByIdAsync(int budgetId, int projectId) =>
        GetAsync<ProjectDto>($"api/budgets/{budgetId}/projects/{projectId}");

    public Task<ProjectDto?> CreateAsync(int budgetId, CreateProjectDto dto) =>
        PostAsync<ProjectDto>($"api/budgets/{budgetId}/projects", dto);

    public async Task DeleteAsync(int budgetId, int id) =>
        await DeleteAsync($"api/budgets/{budgetId}/projects/{id}");
}

// ─── TRANSACTION SERVICE ──────────────────────────────────────────────────────
public class TransactionService : ApiServiceBase
{
    public TransactionService(HttpClient http) : base(http) { }

    public Task<TransactionDto?> CreateAsync(int budgetId, CreateTransactionDto dto) =>
        PostAsync<TransactionDto>($"api/budgets/{budgetId}/transactions", dto);

    public async Task<TransactionDto?> UpdateAsync(int budgetId, UpdateTransactionDto dto)
    {
        var r = await Http.PutAsJsonAsync($"api/budgets/{budgetId}/transactions/{dto.Id}", dto);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<TransactionDto>();
    }

    public async Task DeleteAsync(int budgetId, int id) =>
        await DeleteAsync($"api/budgets/{budgetId}/transactions/{id}");

    public async Task BatchDeleteAsync(int budgetId, List<int> ids)
    {
        var r = await Http.PostAsJsonAsync(
            $"api/budgets/{budgetId}/transactions/batch-delete",
            new BatchDeleteDto(ids));
        r.EnsureSuccessStatusCode();
    }
}

// ─── JOURNAL API SERVICE ──────────────────────────────────────────────────────
public class JournalApiService : ApiServiceBase
{
    public JournalApiService(HttpClient http) : base(http) { }

    // FIX: uses Http.GetAsync + manual status check — GetFromJsonAsync in .NET 8
    // throws on non-2xx instead of returning null.
    public async Task<(PagedResult<JournalEntryDto> Result, SummaryDto Summary)?> GetPagedAsync(
        int budgetId, JournalQuery query)
    {
        var url      = BuildUrl(budgetId, query);
        var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        var raw = await response.Content.ReadFromJsonAsync<JournalPageResponse>();
        return raw == null ? null : (raw.Result, raw.Summary);
    }

    public string GetPdfUrl(int budgetId, JournalQuery q) =>
        $"api/budgets/{budgetId}/transactions/export/pdf?{FilterParams(q)}";

    public string GetExcelUrl(int budgetId, JournalQuery q) =>
        $"api/budgets/{budgetId}/transactions/export/excel?{FilterParams(q)}";

    private static string BuildUrl(int budgetId, JournalQuery q)
    {
        var parts = new List<string>
        {
            $"page={q.Page}",
            $"pageSize={q.PageSize}",
            $"sortBy={Uri.EscapeDataString(q.SortBy ?? "Date")}",
            $"sortDir={Uri.EscapeDataString(q.SortDir ?? "desc")}"
        };
        foreach (var t in q.IncludeTypes)
            parts.Add($"includeTypes={t}");
        foreach (var s in q.ReceiptStatuses)
            parts.Add($"receiptStatuses={s}");
        parts.AddRange(FilterParts(q));
        return $"api/budgets/{budgetId}/journal?" + string.Join("&", parts);
    }

    private static string FilterParams(JournalQuery q) =>
        string.Join("&", FilterParts(q));

    private static List<string> FilterParts(JournalQuery q)
    {
        var p = new List<string>();
        if (q.FilterByStartDate && q.StartDate.HasValue)
            p.Add($"filterByStartDate=true&startDate={q.StartDate.Value:yyyy-MM-dd}");
        if (q.FilterByEndDate && q.EndDate.HasValue)
            p.Add($"filterByEndDate=true&endDate={q.EndDate.Value:yyyy-MM-dd}");
        if (q.FilterByDescription && !string.IsNullOrWhiteSpace(q.Description))
            p.Add($"filterByDescription=true&description={Uri.EscapeDataString(q.Description)}");
        if (q.FilterByCategory && q.CategoryId.HasValue)
            p.Add($"filterByCategory=true&categoryId={q.CategoryId.Value}");
        if (q.FilterByProject && q.ProjectId.HasValue)
            p.Add($"filterByProject=true&projectId={q.ProjectId.Value}");
        return p;
    }

    private class JournalPageResponse
    {
        public PagedResult<JournalEntryDto> Result  { get; set; } = new();
        public SummaryDto                   Summary { get; set; } = new();
    }
}

// ─── RECEIPT API SERVICE ──────────────────────────────────────────────────────
public class ReceiptApiService : ApiServiceBase
{
    public ReceiptApiService(HttpClient http) : base(http) { }

    public Task<PagedResult<ReceiptBatchDto>?> GetAllAsync(
        int budgetId, int page = 1, int pageSize = 25, ReceiptBatchStatus? status = null)
    {
        var url = $"api/budgets/{budgetId}/receipts?page={page}&pageSize={pageSize}";
        if (status.HasValue) url += $"&statuses={(int)status.Value}";
        return GetAsync<PagedResult<ReceiptBatchDto>>(url);
    }

    public Task<ReceiptBatchDto?> GetByIdAsync(int budgetId, int batchId) =>
        GetAsync<ReceiptBatchDto>($"api/budgets/{budgetId}/receipts/{batchId}");

    public Task<ReceiptBatchDto?> CreateAsync(int budgetId, CreateReceiptBatchDto dto) =>
        PostAsync<ReceiptBatchDto>($"api/budgets/{budgetId}/receipts", dto);

    public async Task UpdateAsync(int budgetId, int batchId, UpdateReceiptBatchDto dto)
    {
        var r = await Http.PutAsJsonAsync($"api/budgets/{budgetId}/receipts/{batchId}", dto);
        r.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int budgetId, int batchId) =>
        await DeleteAsync($"api/budgets/{budgetId}/receipts/{batchId}");

    public Task<ReceiptLineDto?> AddLineAsync(int budgetId, int batchId, CreateReceiptLineDto dto) =>
        PostAsync<ReceiptLineDto>($"api/budgets/{budgetId}/receipts/{batchId}/lines", dto);

    public async Task DeleteLineAsync(int budgetId, int batchId, int lineId) =>
        await DeleteAsync($"api/budgets/{budgetId}/receipts/{batchId}/lines/{lineId}");

    public async Task<ReceiptBatchDto?> UpdateStatusAsync(
        int budgetId, int batchId, ReceiptBatchStatus newStatus, string? rejectionReason = null)
    {
        var r = await Http.PatchAsJsonAsync(
            $"api/budgets/{budgetId}/receipts/{batchId}/status",
            new UpdateReceiptStatusDto(newStatus, rejectionReason));
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<ReceiptBatchDto>();
    }

    public Task<List<ReceiptBatchCategoryDto>?> GetCategoriesAsync(int budgetId) =>
        GetAsync<List<ReceiptBatchCategoryDto>>($"api/budgets/{budgetId}/receipt-categories");

    public string GetPdfUrl(int budgetId, int batchId) =>
        $"api/budgets/{budgetId}/receipts/{batchId}/export/pdf";
}

// ─── MILERSÄTTNING API SERVICE ────────────────────────────────────────────────
public class MilersattningApiService : ApiServiceBase
{
    public MilersattningApiService(HttpClient http) : base(http) { }

    public Task<List<MilersattningDto>?> GetAllAsync(int budgetId) =>
        GetAsync<List<MilersattningDto>>($"api/budgets/{budgetId}/milersattning");

    public Task<MilersattningDto?> CreateAsync(int budgetId, CreateMilersattningDto dto) =>
        PostAsync<MilersattningDto>($"api/budgets/{budgetId}/milersattning", dto);

    public async Task DeleteAsync(int budgetId, int id) =>
        await DeleteAsync($"api/budgets/{budgetId}/milersattning/{id}");

    public async Task<decimal> GetRateAsync(int budgetId)
    {
        var r = await GetAsync<RateWrapper>($"api/budgets/{budgetId}/milersattning/rate");
        return r?.Rate ?? 0.25m;
    }

    private class RateWrapper { public decimal Rate { get; set; } }
}

// ─── VAB API SERVICE ──────────────────────────────────────────────────────────
public class VabApiService : ApiServiceBase
{
    public VabApiService(HttpClient http) : base(http) { }

    public Task<List<VabDto>?> GetAllAsync(int budgetId) =>
        GetAsync<List<VabDto>>($"api/budgets/{budgetId}/vab");

    public Task<VabDto?> CreateAsync(int budgetId, CreateVabDto dto) =>
        PostAsync<VabDto>($"api/budgets/{budgetId}/vab", dto);

    public async Task DeleteAsync(int budgetId, int id) =>
        await DeleteAsync($"api/budgets/{budgetId}/vab/{id}");
}

// ─── IMPORT API SERVICE ───────────────────────────────────────────────────────
public class ImportApiService : ApiServiceBase
{
    public ImportApiService(HttpClient http) : base(http) { }

    public async Task<ImportSessionDto?> PreviewAsync(
        int budgetId, Stream stream, string fileName, string bankProfile)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        var r = await Http.PostAsync(
            $"api/budgets/{budgetId}/import/preview?bankProfile={bankProfile}", content);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<ImportSessionDto>();
    }

    public async Task<int> ConfirmAsync(int budgetId, ConfirmImportDto dto)
    {
        var r = await Http.PostAsJsonAsync($"api/budgets/{budgetId}/import/confirm", dto);
        r.EnsureSuccessStatusCode();
        var res = await r.Content.ReadFromJsonAsync<ImportResult>();
        return res?.Imported ?? 0;
    }

    private class ImportResult { public int Imported { get; set; } }
}

// ─── REPORTS API SERVICE ──────────────────────────────────────────────────────
public class ReportsApiService : ApiServiceBase
{
    public ReportsApiService(HttpClient http) : base(http) { }

    public Task<MonthlySummary?> GetMonthlySummaryAsync(int budgetId, int year) =>
        GetAsync<MonthlySummary>($"api/budgets/{budgetId}/reports/monthly-summary?year={year}");

    public Task<List<CategoryBreakdownItem>?> GetCategoryBreakdownAsync(
        int budgetId, DateTime? from, DateTime? to)
    {
        var url = $"api/budgets/{budgetId}/reports/category-breakdown";
        var sep = "?";
        if (from.HasValue) { url += $"{sep}from={from.Value:yyyy-MM-dd}"; sep = "&"; }
        if (to.HasValue)   { url += $"{sep}to={to.Value:yyyy-MM-dd}"; }
        return GetAsync<List<CategoryBreakdownItem>>(url);
    }
}

// ─── TOAST SERVICE ────────────────────────────────────────────────────────────
public class ToastService
{
    public List<ToastMessage> Toasts { get; } = new();
    public event Action? OnChange;

    public void Show(string message, string type = "info", int durationMs = 4000)
    {
        var id = Guid.NewGuid().ToString();
        Toasts.Add(new ToastMessage
        {
            Id      = id,
            Message = message,
            Type    = type,
            Icon    = type switch { "success" => "check_circle", "error" => "error", _ => "info" }
        });
        OnChange?.Invoke();
        _ = Task.Delay(durationMs).ContinueWith(_ => { Remove(id); });
    }

    public void Success(string m) => Show(m, "success");
    public void Error(string m)   => Show(m, "error");
    public void Info(string m)    => Show(m, "info");

    public void Remove(string id)
    {
        Toasts.RemoveAll(t => t.Id == id);
        OnChange?.Invoke();
    }
}

public class ToastMessage
{
    public string Id      { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type    { get; set; } = "info";
    public string Icon    { get; set; } = "info";
}

// ─── TASK LIST API SERVICE ────────────────────────────────────────────────────
// FIX: This class was previously nested INSIDE ToastService, which is why
// TaskListDto, CreateTaskItemDto etc. all produced CS0246.
// It is now a proper top-level class in the same namespace.
//
// Register in BudgetPlanner.Blazor/Program.cs:
//   builder.Services.AddScoped<TaskListApiService>();
public class TaskListApiService : ApiServiceBase
{
    public TaskListApiService(HttpClient http) : base(http) { }

    // ── Lists ─────────────────────────────────────────────────────────────────

    public async Task<List<TaskListDto>?> GetAllAsync(int budgetId, bool includeArchived = false)
    {
        var resp = await Http.GetAsync(
            $"api/budgets/{budgetId}/lists?includeArchived={includeArchived}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<List<TaskListDto>>();
    }

    public async Task<TaskListDto?> GetByIdAsync(int budgetId, int listId)
    {
        var resp = await Http.GetAsync($"api/budgets/{budgetId}/lists/{listId}");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskListDto>();
    }

    public async Task<TaskListDto?> CreateAsync(int budgetId, CreateTaskListDto dto)
    {
        var resp = await Http.PostAsJsonAsync($"api/budgets/{budgetId}/lists", dto);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskListDto>();
    }

    public async Task<TaskListDto?> UpdateAsync(int budgetId, int listId, UpdateTaskListDto dto)
    {
        var resp = await Http.PutAsJsonAsync($"api/budgets/{budgetId}/lists/{listId}", dto);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskListDto>();
    }

    public async Task<bool> ArchiveAsync(int budgetId, int listId)
    {
        var resp = await Http.PatchAsync(
            $"api/budgets/{budgetId}/lists/{listId}/archive",
            JsonContent.Create(new { }));
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(int budgetId, int listId)
    {
        var resp = await Http.DeleteAsync($"api/budgets/{budgetId}/lists/{listId}");
        return resp.IsSuccessStatusCode;
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    public async Task<TaskItemDto?> AddItemAsync(int budgetId, int listId, CreateTaskItemDto dto)
    {
        var resp = await Http.PostAsJsonAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items", dto);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskItemDto>();
    }

    public async Task<TaskItemDto?> CheckItemAsync(
        int budgetId, int listId, int itemId, bool isChecked)
    {
        var resp = await Http.PatchAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items/{itemId}/check",
            JsonContent.Create(new CheckTaskItemDto(isChecked)));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskItemDto>();
    }

    public async Task<TaskItemDto?> UpdateItemTextAsync(
        int budgetId, int listId, int itemId, string text)
    {
        var resp = await Http.PutAsJsonAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items/{itemId}",
            new UpdateTaskItemDto(text));
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskItemDto>();
    }

    public async Task<bool> DeleteItemAsync(int budgetId, int listId, int itemId)
    {
        var resp = await Http.DeleteAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items/{itemId}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderItemsAsync(int budgetId, int listId, List<int> orderedIds)
    {
        var resp = await Http.PutAsJsonAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items/reorder",
            new ReorderTaskItemDto(orderedIds));
        return resp.IsSuccessStatusCode;
    }

    public async Task<TaskItemDto?> LinkItemAsync(
        int budgetId, int listId, int itemId, LinkTaskItemDto dto)
    {
        var resp = await Http.PostAsJsonAsync(
            $"api/budgets/{budgetId}/lists/{listId}/items/{itemId}/link", dto);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<TaskItemDto>();
    }
}
