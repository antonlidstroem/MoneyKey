// ═══════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.API / Program.cs
// Add these DI registrations in the service registration block:
// ═══════════════════════════════════════════════════════════════════════════════

builder.Services.AddScoped<TaskListService>();
builder.Services.AddScoped<ITaskListRepository, TaskListRepository>();
builder.Services.AddScoped<ITaskItemRepository, TaskItemRepository>();

// ═══════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.Blazor / Program.cs
// ═══════════════════════════════════════════════════════════════════════════════

// 1. Authenticated service (uses the existing AuthorizationMessageHandler)
builder.Services.AddScoped<TaskListApiService>();

// 2. BUG 6 FIX — unauthenticated named client for the public share page.
//    This client must NOT have AuthorizationMessageHandler attached.
//    It is used only by PublicListPage via IHttpClientFactory.CreateClient("PublicClient").
builder.Services.AddHttpClient("PublicClient", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7000/");
});
// Note: do NOT call .AddHttpMessageHandler<AuthorizationMessageHandler>() here.

// ═══════════════════════════════════════════════════════════════════════════════
// index.html — add before </body>
// ═══════════════════════════════════════════════════════════════════════════════

/*
<script src="https://cdnjs.cloudflare.com/ajax/libs/qrcodejs/1.0.0/qrcode.min.js"
        integrity="sha512-CNgIRecGo7nphbeZ04Sc13ka07paqdeTu0WR1IM4kNcpmBAUSHSE7ApupMutZSmvZ8XKkXoV7XMoGdI1NeHMeQ=="
        crossorigin="anonymous" referrerpolicy="no-referrer"></script>
<script src="js/qrcode.js"></script>
*/

// ═══════════════════════════════════════════════════════════════════════════════
// EF Core migration — run from solution root
// ═══════════════════════════════════════════════════════════════════════════════

/*
dotnet ef migrations add AddTaskLists \
    --project BudgetPlanner.DAL \
    --startup-project BudgetPlanner.API

dotnet ef database update \
    --project BudgetPlanner.DAL \
    --startup-project BudgetPlanner.API
*/

// ═══════════════════════════════════════════════════════════════════════════════
// lists.css — link in index.html (or import in site.css)
// ═══════════════════════════════════════════════════════════════════════════════

/*
In wwwroot/index.html, add inside <head>:
    <link href="css/lists.css" rel="stylesheet" />

OR at the bottom of wwwroot/css/site.css add:
    @import url('lists.css');
*/
