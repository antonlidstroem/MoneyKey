using System.Globalization;
using BudgetPlanner.Blazor.Services;
using BudgetPlanner.Blazor.State;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<BudgetPlanner.Blazor.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("sv-SE");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("sv-SE");

var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7000";

// ── Authentication state ───────────────────────────────────────────────────────
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());

// ── HTTP message handler (must be registered before named clients) ─────────────
builder.Services.AddTransient<AuthorizationMessageHandler>();

// ── Named HttpClient: authenticated API calls ──────────────────────────────────
// This is what ALL scoped services (AuthService, TransactionService, etc.) receive
// when HttpClient is constructor-injected via DI.
builder.Services.AddHttpClient("BudgetAPI", client =>
{
    client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
})
.AddHttpMessageHandler<AuthorizationMessageHandler>();

// Register the default HttpClient as the "BudgetAPI" named client so that
// constructor-injected HttpClient resolves correctly throughout the app.
//builder.Services.AddScoped(sp =>
//    sp.GetRequiredService<IHttpClientFactory>().CreateClient("BudgetAPI"));

// ── Named HttpClient: unauthenticated public endpoints ────────────────────────
// Used by PublicListPage via IHttpClientFactory.CreateClient("PublicClient").
// Must NOT have AuthorizationMessageHandler attached.
builder.Services.AddHttpClient("PublicClient", client =>
{
    client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
});

builder.Services.AddAuthorizationCore();

// ── Application services ───────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<MilersattningApiService>();
builder.Services.AddScoped<VabApiService>();
builder.Services.AddScoped<ImportApiService>();
builder.Services.AddScoped<ReportsApiService>();
builder.Services.AddScoped<JournalApiService>();
builder.Services.AddScoped<ReceiptApiService>();
builder.Services.AddScoped<TaskListApiService>();
builder.Services.AddSingleton<ToastService>();

// ── State ──────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<BudgetState>();
builder.Services.AddScoped<JournalFilterState>();
builder.Services.AddScoped<SignalRService>();

await builder.Build().RunAsync();
