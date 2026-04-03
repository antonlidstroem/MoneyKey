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

public class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private static string? _accessToken;
    private static UserDto? _currentUser;

    private readonly HttpClient _authClient;

    public JwtAuthenticationStateProvider(IConfiguration config)
    {
        var apiBase = config["ApiBaseUrl"] ?? "https://localhost:7000";
        _authClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    }

    public static string? AccessToken  => _accessToken;
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
        _accessToken = token; _currentUser = user;
        NotifyAuthenticationStateChanged(Task.FromResult(Build(token)));
    }

    public void ClearToken()
    {
        _accessToken = null; _currentUser = null;
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous()));
    }

    private static AuthenticationState Build(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
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

// FIX: Buffer request content before first send; clone HttpRequestMessage for
//      the 401-retry to avoid "stream already consumed" failures.
public class AuthorizationMessageHandler : DelegatingHandler
{
    private readonly IServiceProvider _serviceProvider;
    private bool _refreshing;

    public AuthorizationMessageHandler(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Buffer the content so it can survive a second send after 401.
        if (request.Content != null)
            await request.Content.LoadIntoBufferAsync();

        if (!string.IsNullOrEmpty(JwtAuthenticationStateProvider.AccessToken))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", JwtAuthenticationStateProvider.AccessToken);

        var response = await base.SendAsync(request, ct);

        var isAuthRequest = request.RequestUri?.AbsolutePath
                                   .Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) ?? false;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && !isAuthRequest && !_refreshing)
        {
            _refreshing = true;
            try
            {
                var authProvider = (JwtAuthenticationStateProvider)_serviceProvider
                    .GetRequiredService<AuthenticationStateProvider>();

                await authProvider.GetAuthenticationStateAsync();

                if (!string.IsNullOrEmpty(JwtAuthenticationStateProvider.AccessToken))
                {
                    // Clone the original request so we start with a clean message.
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

    /// <summary>
    /// Creates a new HttpRequestMessage that is a shallow copy of <paramref name="src"/>,
    /// re-using the already-buffered content so it can be sent again.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage src)
    {
        var clone = new HttpRequestMessage(src.Method, src.RequestUri);

        foreach (var h in src.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

        if (src.Content != null)
        {
            var ms = new MemoryStream();
            await src.Content.CopyToAsync(ms);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);

            foreach (var h in src.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        return clone;
    }
}

public abstract class ApiServiceBase
{
    protected readonly HttpClient Http;
    protected ApiServiceBase(HttpClient http) => Http = http;

    protected Task<T?> GetAsync<T>(string url) =>
        Http.GetFromJsonAsync<T>(url);

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

    /// <summary>
    /// Claims an invite token. On success the returned JWT already contains
    /// the newly joined budget so BudgetState will update on the next navigation.
    /// </summary>
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

public class BudgetService : ApiServiceBase
{
    public BudgetService(HttpClient http) : base(http) { }
    public Task<List<BudgetDto>?> GetMyBudgetsAsync() => GetAsync<List<BudgetDto>>("api/budgets");
    public Task<BudgetDto?> CreateAsync(CreateBudgetDto dto) => PostAsync<BudgetDto>("api/budgets", dto);
    public Task<List<Category>?> GetCategoriesAsync(int budgetId) =>
        GetAsync<List<Category>>($"api/budgets/{budgetId}/categories");
    public async Task InviteAsync(int budgetId, InviteMemberDto dto) =>
        await PostAsync<object>($"api/budgets/{budgetId}/invite", dto);
}

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
        var r = await Http.PostAsJsonAsync($"api/budgets/{budgetId}/transactions/batch-delete", new BatchDeleteDto(ids));
        r.EnsureSuccessStatusCode();
    }
}

public class JournalApiService : ApiServiceBase
{
    public JournalApiService(HttpClient http) : base(http) { }

    public async Task<(PagedResult<JournalEntryDto> Result, SummaryDto Summary)?> GetPagedAsync(
        int budgetId, JournalQuery query)
    {
        var raw = await GetAsync<JournalPageResponse>(BuildUrl(budgetId, query));
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
            $"page={q.Page}", $"pageSize={q.PageSize}",
            $"sortBy={q.SortBy ?? "Date"}", $"sortDir={q.SortDir ?? "desc"}"
        };
        foreach (var t in q.IncludeTypes)    parts.Add($"includeTypes={t}");
        foreach (var s in q.ReceiptStatuses) parts.Add($"receiptStatuses={s}");
        parts.AddRange(FilterParts(q));
        return $"api/budgets/{budgetId}/journal?" + string.Join("&", parts);
    }

    private static string FilterParams(JournalQuery q) => string.Join("&", FilterParts(q));

    private static List<string> FilterParts(JournalQuery q)
    {
        var p = new List<string>();
        if (q.FilterByStartDate   && q.StartDate.HasValue)         p.Add($"filterByStartDate=true&startDate={q.StartDate:yyyy-MM-dd}");
        if (q.FilterByEndDate     && q.EndDate.HasValue)           p.Add($"filterByEndDate=true&endDate={q.EndDate:yyyy-MM-dd}");
        if (q.FilterByDescription && !string.IsNullOrWhiteSpace(q.Description)) p.Add($"filterByDescription=true&description={Uri.EscapeDataString(q.Description)}");
        if (q.FilterByCategory    && q.CategoryId.HasValue)        p.Add($"filterByCategory=true&categoryId={q.CategoryId}");
        if (q.FilterByProject     && q.ProjectId.HasValue)         p.Add($"filterByProject=true&projectId={q.ProjectId}");
        return p;
    }

    private class JournalPageResponse
    {
        public PagedResult<JournalEntryDto> Result { get; set; } = new();
        public SummaryDto Summary { get; set; } = new();
    }
}

public class ReceiptApiService : ApiServiceBase
{
    public ReceiptApiService(HttpClient http) : base(http) { }

    public Task<PagedResult<ReceiptBatchDto>?> GetAllAsync(int budgetId, int page = 1, int pageSize = 50) =>
        GetAsync<PagedResult<ReceiptBatchDto>>($"api/budgets/{budgetId}/receipts?page={page}&pageSize={pageSize}");

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

public class ImportApiService : ApiServiceBase
{
    public ImportApiService(HttpClient http) : base(http) { }

    public async Task<ImportSessionDto?> PreviewAsync(int budgetId, Stream stream, string fileName, string bankProfile)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(stream), "file", fileName);
        var r = await Http.PostAsync($"api/budgets/{budgetId}/import/preview?bankProfile={bankProfile}", content);
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

public class ReportsApiService : ApiServiceBase
{
    public ReportsApiService(HttpClient http) : base(http) { }
    public Task<MonthlySummary?> GetMonthlySummaryAsync(int budgetId, int year) =>
        GetAsync<MonthlySummary>($"api/budgets/{budgetId}/reports/monthly-summary?year={year}");
    public Task<List<CategoryBreakdownItem>?> GetCategoryBreakdownAsync(int budgetId, DateTime? from, DateTime? to)
    {
        var url = $"api/budgets/{budgetId}/reports/category-breakdown";
        if (from.HasValue) url += $"?from={from:yyyy-MM-dd}";
        if (to.HasValue)   url += (from.HasValue ? "&" : "?") + $"to={to:yyyy-MM-dd}";
        return GetAsync<List<CategoryBreakdownItem>>(url);
    }
}

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

    public void Remove(string id) { Toasts.RemoveAll(t => t.Id == id); OnChange?.Invoke(); }
}

public class ToastMessage
{
    public string Id      { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type    { get; set; } = "info";
    public string Icon    { get; set; } = "info";
}
