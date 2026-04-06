using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BudgetPlanner.API.Hubs;
using BudgetPlanner.API.Services;
using BudgetPlanner.Core.Services;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.DAL.Models;
using BudgetPlanner.DAL.Repositories;

var builder = WebApplication.CreateBuilder(args);
var cfg  = builder.Configuration;
var svcs = builder.Services;

// ── Fail-fast: validate critical secrets before any service is registered ────
var jwtSecret = cfg["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be configured and at least 32 characters. " +
        "Use 'dotnet user-secrets' in development or environment variables in production. " +
        "Example: dotnet user-secrets set \"Jwt:Secret\" \"$(openssl rand -base64 48)\"");

var connStr = cfg.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connStr))
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection must be configured. " +
        "Use 'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"'");

// ── Infrastructure ────────────────────────────────────────────────────────────
svcs.AddHttpContextAccessor();
svcs.AddMemoryCache();  // required by ImportService session store

// FIX: Register against the DAL interface — the one AuditInterceptor is injected with.
svcs.AddScoped<BudgetPlanner.DAL.Data.ICurrentUserAccessor, HttpCurrentUserAccessor>();
svcs.AddScoped<AuditInterceptor>();

svcs.AddDbContext<BudgetDbContext>((sp, opt) =>
    opt.UseSqlServer(connStr,
            sql => sql.MigrationsAssembly("BudgetPlanner.DAL"))
        .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

svcs.AddIdentity<ApplicationUser, IdentityRole>(opt =>
{
    opt.Password.RequiredLength         = 8;
    opt.Password.RequireNonAlphanumeric = false;
    opt.User.RequireUniqueEmail         = true;
    opt.SignIn.RequireConfirmedEmail    = false;
})
.AddEntityFrameworkStores<BudgetDbContext>()
.AddDefaultTokenProviders();

// ── JWT ────────────────────────────────────────────────────────────────────────
var jwtSection = cfg.GetSection("Jwt");

svcs.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer              = jwtSection["Issuer"],
        ValidAudience            = jwtSection["Audience"],
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew                = TimeSpan.Zero
    };
    opt.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});

svcs.AddAuthorization();

var origins = cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
svcs.AddCors(opt => opt.AddPolicy("BlazorPolicy", p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

svcs.AddRateLimiter(opt =>
    opt.AddPolicy("AuthPolicy", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1), PermitLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0
            })));

svcs.AddSignalR();

// ── Repositories ───────────────────────────────────────────────────────────────
svcs.AddScoped<ITransactionRepository,   TransactionRepository>();
svcs.AddScoped<IBudgetRepository,        BudgetRepository>();
svcs.AddScoped<ICategoryRepository,      CategoryRepository>();
svcs.AddScoped<IProjectRepository,       ProjectRepository>();
svcs.AddScoped<IMilersattningRepository, MilersattningRepository>();
svcs.AddScoped<IVabRepository,           VabRepository>();
svcs.AddScoped<IKonteringRepository,     KonteringRepository>();
svcs.AddScoped<IReceiptRepository,       ReceiptRepository>();
svcs.AddScoped<IAuditRepository,         AuditRepository>();
svcs.AddScoped<IAppSettingRepository,    AppSettingRepository>();

// ── Services ───────────────────────────────────────────────────────────────────
svcs.AddScoped<TokenService>();
svcs.AddScoped<BudgetAuthorizationService>();
svcs.AddScoped<MilersattningService>();
svcs.AddScoped<VabService>();
svcs.AddScoped<BudgetCalculationService>();
svcs.AddScoped<ExportService>();
svcs.AddScoped<ImportService>();
svcs.AddScoped<ReceiptService>();
svcs.AddScoped<JournalQueryService>();
svcs.AddScoped<IReceiptAttachmentService, NoOpReceiptAttachmentService>();

// ── Email ─────────────────────────────────────────────────────────────────────
var emailOpts = cfg.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
svcs.AddSingleton(emailOpts);
svcs.AddScoped<IEmailService, MailKitEmailService>();

svcs.AddControllers();
svcs.AddEndpointsApiExplorer();
svcs.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BudgetPlanner API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Description = "Ange: Bearer {din-jwt-token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
        Array.Empty<string>()
    }});
});

var app = builder.Build();

// ── Migrate + seed ─────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<BudgetDbContext>().Database.Migrate();
}

// Create first-run admin only when the database is empty (no users).
// Credentials come from AdminSetup config — never from source code.
await DbInitializer.InitializeAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "BudgetPlanner API v1"));
app.UseHttpsRedirection();
app.UseCors("BlazorPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BudgetHub>("/hubs/budget").RequireAuthorization();
app.Run();
