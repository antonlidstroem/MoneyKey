using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BudgetPlanner.Blazor.Services;
using BudgetPlanner.Blazor.State;
using Microsoft.AspNetCore.Components.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<BudgetPlanner.Blazor.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

CultureInfo.DefaultThreadCurrentCulture   = new CultureInfo("sv-SE");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("sv-SE");

var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7000";

builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());

// FIX: Single registration — removed duplicate AddTransient<AuthorizationMessageHandler>().
builder.Services.AddTransient<AuthorizationMessageHandler>();

builder.Services.AddHttpClient("BudgetAPI", client =>
{
    client.BaseAddress = new Uri(apiBase);
})
.AddHttpMessageHandler<AuthorizationMessageHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("BudgetAPI"));

builder.Services.AddAuthorizationCore();

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
builder.Services.AddSingleton<ToastService>();

builder.Services.AddScoped<BudgetState>();
builder.Services.AddScoped<JournalFilterState>();
builder.Services.AddScoped<SignalRService>();

await builder.Build().RunAsync();
