using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.DAL.Models;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.API.Services;

// ─── CURRENT USER ACCESSOR ────────────────────────────────────────────────────
public class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;
    public HttpCurrentUserAccessor(IHttpContextAccessor http) => _http = http;

    public string? UserId =>
        _http.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? _http.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

    public string? UserEmail =>
        _http.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? _http.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
}

// ─── TOKEN SERVICE ────────────────────────────────────────────────────────────
public class TokenService
{
    private readonly IConfiguration _cfg;
    private readonly BudgetDbContext _db;

    public TokenService(IConfiguration cfg, BudgetDbContext db) { _cfg = cfg; _db = db; }

    public string GenerateAccessToken(ApplicationUser user, List<BudgetMembershipDto> memberships)
    {
        var jwtSection = _cfg.GetSection("Jwt");
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(int.Parse(jwtSection["AccessTokenExpiryMinutes"] ?? "15"));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new("firstName",                   user.FirstName),
            new("lastName",                    user.LastName)
        };
        foreach (var m in memberships)
            claims.Add(new Claim("budget", $"{m.BudgetId}:{m.Role}"));

        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(jwtSection["Issuer"], jwtSection["Audience"], claims, expires: expiry, signingCredentials: creds));
    }

    public async Task<(string rawToken, RefreshToken entity)> GenerateRefreshTokenAsync(string userId)
    {
        var raw    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash   = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        var days   = int.Parse(_cfg["Jwt:RefreshTokenExpiryDays"] ?? "30");
        var entity = new RefreshToken { UserId = userId, TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddDays(days) };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();
        return (raw, entity);
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken)
    {
        var hash  = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        return token?.IsActive == true ? token : null;
    }

    public async Task RevokeRefreshTokenAsync(int tokenId)
    {
        var t = await _db.RefreshTokens.FindAsync(tokenId);
        if (t != null) { t.RevokedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    }

    public async Task RevokeAllRefreshTokensAsync(string userId)
    {
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}

// ─── BUDGET AUTHORIZATION ─────────────────────────────────────────────────────
public class BudgetAuthorizationService
{
    private readonly BudgetDbContext _db;
    public BudgetAuthorizationService(BudgetDbContext db) => _db = db;

    public async Task<BudgetMembership?> GetMembershipAsync(int budgetId, string userId) =>
        await _db.BudgetMemberships
            .FirstOrDefaultAsync(m => m.BudgetId == budgetId && m.UserId == userId && m.AcceptedAt != null);

    /// <summary>
    /// Returns true when the user's role is at least <paramref name="minimumRole"/>.
    ///
    /// Role hierarchy (highest → lowest):
    ///   Owner (4) > Editor (3) > Auditor (2) > Viewer (1)
    ///
    ///   • Owner   — full control: create / edit / delete / approve / audit / view
    ///   • Editor  — create / edit / delete transactions; view everything
    ///   • Auditor — read-only access to audit log AND journal; cannot edit
    ///   • Viewer  — read-only access to journal only; cannot see audit log
    ///
    /// PHASE 4 FIX: replaced ad-hoc pattern-matching with numeric rank comparison.
    /// Previously Auditor was excluded from Viewer-level satisfaction, meaning
    /// Auditors could reach the audit endpoint but not the journal.
    /// </summary>
    public async Task<bool> HasRoleAsync(int budgetId, string userId, BudgetMemberRole minimumRole)
    {
        var m = await GetMembershipAsync(budgetId, userId);
        if (m == null) return false;

        static int Rank(BudgetMemberRole r) => r switch
        {
            BudgetMemberRole.Viewer  => 1,
            BudgetMemberRole.Auditor => 2,
            BudgetMemberRole.Editor  => 3,
            BudgetMemberRole.Owner   => 4,
            _                        => 0
        };

        return Rank(m.Role) >= Rank(minimumRole);
    }
}

// ─── EMAIL SERVICE ────────────────────────────────────────────────────────────
public class EmailOptions
{
    public string SmtpHost    { get; set; } = string.Empty;
    public int    SmtpPort    { get; set; } = 587;
    public string SmtpUser    { get; set; } = string.Empty;
    public string SmtpPass    { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName    { get; set; } = "BudgetPlanner";
}

public interface IEmailService
{
    Task SendInviteAsync(string toEmail, string budgetName, string inviteToken, string baseUrl);
    Task SendReceiptStatusChangedAsync(string toEmail, string batchLabel, string newStatus, string? reason = null);
    Task SendReceiptSubmittedAsync(string ownerEmail, string submitterEmail, string batchLabel);
}

public class MailKitEmailService : IEmailService
{
    private readonly EmailOptions _opts;
    private readonly ILogger<MailKitEmailService> _log;

    public MailKitEmailService(EmailOptions opts, ILogger<MailKitEmailService> log)
    { _opts = opts; _log = log; }

    public Task SendInviteAsync(string toEmail, string budgetName, string inviteToken, string baseUrl) =>
        SendAsync(toEmail,
            $"Inbjudan till budgeten \"{budgetName}\"",
            $"<p>Du har bjudits in till <strong>{budgetName}</strong>.</p>" +
            $"<p><a href=\"{baseUrl}/accept-invite?token={inviteToken}\" " +
            $"style=\"display:inline-block;padding:10px 20px;background:#1565C0;color:#fff;border-radius:6px;text-decoration:none\">" +
            $"Acceptera inbjudan</a></p>");

    public Task SendReceiptStatusChangedAsync(string toEmail, string batchLabel, string newStatus, string? reason = null)
    {
        var (subj, verb) = newStatus switch
        {
            "Approved"   => ($"Utläggsunderlag godkänt: \"{batchLabel}\"",  "godkändes"),
            "Rejected"   => ($"Utläggsunderlag avslaget: \"{batchLabel}\"", "avslogs"),
            "Reimbursed" => ($"Utlägg utbetalt: \"{batchLabel}\"",          "markerades som utbetalt"),
            _            => ($"Status uppdaterad: \"{batchLabel}\"",        "uppdaterades")
        };
        var body = $"<p>Ditt underlag <strong>\"{batchLabel}\"</strong> {verb}.</p>"
                 + (reason != null ? $"<p><strong>Orsak:</strong> {reason}</p>" : "");
        return SendAsync(toEmail, subj, body);
    }

    public Task SendReceiptSubmittedAsync(string ownerEmail, string submitterEmail, string batchLabel) =>
        SendAsync(ownerEmail,
            $"Nytt utläggsunderlag att granska: \"{batchLabel}\"",
            $"<p>{submitterEmail} har skickat in <strong>\"{batchLabel}\"</strong> för granskning.</p>");

    private async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_opts.SmtpHost))
        {
            _log.LogWarning("SMTP inte konfigurerat. Hoppar över e-post till {To}: {Subject}", toEmail, subject);
            return;
        }
        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
            msg.To.Add(MailboxAddress.Parse(toEmail));
            msg.Subject = subject;
            msg.Body = new TextPart("html")
            {
                Text = "<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#37474F;max-width:560px;margin:0 auto;padding:24px\">"
                     + "<div style=\"border-bottom:2px solid #1565C0;padding-bottom:12px;margin-bottom:20px\"><strong style=\"color:#1565C0;font-size:18px\">BudgetPlanner</strong></div>"
                     + htmlBody + "</body></html>"
            };
            using var client = new SmtpClient();
            await client.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_opts.SmtpUser, _opts.SmtpPass);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex) { _log.LogError(ex, "Misslyckades skicka e-post till {To}", toEmail); }
    }
}
