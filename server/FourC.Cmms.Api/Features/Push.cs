using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Notifications;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

public enum PushResult { Sent, Gone, Failed }

/// <summary>Delivers one push to one phone (Firebase in production, a recorder in tests).</summary>
public interface IPushTransport
{
    Task<PushResult> SendAsync(string token, string title, string body, string? link, CancellationToken ct);
}

/// <summary>
/// Firebase Cloud Messaging (HTTP v1) with the project's service account (config <c>Push:ServiceAccountJson</c>). The key is
/// only read from configuration and never logged or returned.
/// </summary>
public sealed class FcmPushTransport(IConfiguration config, IHttpClientFactory http, ILogger<FcmPushTransport> log) : IPushTransport
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    private sealed record Account(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri);

    private Account? Read()
    {
        var json = config["Push:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new Account(r.GetProperty("project_id").GetString()!, r.GetProperty("client_email").GetString()!, r.GetProperty("private_key").GetString()!,
                r.TryGetProperty("token_uri", out var t) ? t.GetString()! : "https://oauth2.googleapis.com/token");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            log.LogError("Push:ServiceAccountJson isn't a Firebase service account key.");
            return null;
        }
    }

    public async Task<PushResult> SendAsync(string token, string title, string body, string? link, CancellationToken ct)
    {
        var account = Read();
        if (account is null) return PushResult.Failed;
        var client = http.CreateClient("fcm");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{account.ProjectId}/messages:send")
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    token, notification = new { title, body }, data = new Dictionary<string, string> { ["link"] = link ?? "" },
                    android = new { priority = "high" },
                },
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(account, client, ct));
        using var res = await client.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return PushResult.Sent;
        var text = await res.Content.ReadAsStringAsync(ct);
        // The phone uninstalled the app or the token was replaced: forget it.
        if ((int)res.StatusCode == 404 || text.Contains("UNREGISTERED", StringComparison.Ordinal) || text.Contains("registration-token-not-registered", StringComparison.Ordinal))
            return PushResult.Gone;
        log.LogWarning("Firebase answered {Status} for a push.", (int)res.StatusCode);
        return PushResult.Failed;
    }

    /// <summary>An OAuth access token for Firebase Messaging, from a JWT signed with the service account's key (cached until near expiry).</summary>
    private async Task<string> AccessTokenAsync(Account a, HttpClient client, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;
            static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var head = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = a.ClientEmail, scope = "https://www.googleapis.com/auth/firebase.messaging", aud = a.TokenUri, iat = now, exp = now + 3600,
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(a.PrivateKey);
            var sig = B64(rsa.SignData(Encoding.ASCII.GetBytes($"{head}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var res = await client.PostAsync(a.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = $"{head}.{claims}.{sig}",
            }), ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            _token = doc.RootElement.GetProperty("access_token").GetString();
            _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 120);
            return _token!;
        }
        finally { _lock.Release(); }
    }
}

/// <summary>
/// Queues push notifications (0.11.0) with the change that causes them: job assigned, request approved. Only people with a
/// phone registered and who can see the record; never the person who acted; in their language. Nothing when push isn't set up.
/// </summary>
public sealed class PushOutbox(IConfiguration config, AccessService access)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["Push:ServiceAccountJson"]);

    /// <summary>A job given to these employees (as assigned or through a team).</summary>
    public async Task JobAssignedAsync(CmmsDbContext db, ITenantContext ctx, WorkOrder w, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        if (!Configured || employeeIds.Count == 0) return;
        var users = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id) && e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct);
        var path = w.LocationId is { } lid ? await db.Locations.AllSites().Where(l => l.Id == lid).Select(l => l.Path).FirstOrDefaultAsync(ct) : null;
        await QueueAsync(db, ctx, users, w.SiteId, path, () => (Translator.T($"New job: {w.Number}"), w.Title), $"/tech/job/{w.Id}", "WorkOrder", w.Id, ct);
    }

    /// <summary>A request approved: its requester hears when they raised it with their login.</summary>
    public async Task RequestApprovedAsync(CmmsDbContext db, ITenantContext ctx, WorkRequest r, string workOrderNumber, CancellationToken ct)
    {
        if (!Configured || r.CreatedById is not { } creator) return;
        var path = r.LocationId is { } lid ? await db.Locations.AllSites().Where(l => l.Id == lid).Select(l => l.Path).FirstOrDefaultAsync(ct) : null;
        await QueueAsync(db, ctx, [creator], r.SiteId, path, () => (Translator.T($"Request approved: {r.Number}"), Translator.T($"Work order {workOrderNumber}: {r.Title}")),
            $"/tech/requests", "WorkRequest", r.Id, ct);
    }

    private async Task QueueAsync(CmmsDbContext db, ITenantContext ctx, IReadOnlyCollection<Guid> userIds, Guid siteId, string? locationPath,
        Func<(string Title, string Body)> text, string link, string entityType, Guid entityId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var withPhone = await db.UserSessions.IgnoreQueryFilters()
            .Where(s => userIds.Contains(s.UserId) && s.TenantId == db.CurrentTenantId && s.PushToken != null && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => s.UserId).Distinct().ToListAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct);
        foreach (var userId in withPhone.Where(u => u != ctx.UserId))
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive).Select(u => new { u.Language }).FirstOrDefaultAsync(ct);
            if (user is null || !Access.Covers(await access.GetAsync(userId, ct), siteId, locationPath)) continue;
            (string Title, string Body) content;
            using (Translator.Use(Languages.Effective(user.Language, tenant.Settings.DefaultLanguage))) content = text();
            db.PushMessages.Add(new PushMessage
            {
                TenantId = tenant.Id, UserId = userId, Title = Cut(content.Title, 200), Body = Cut(content.Body, 1000), Link = link, EntityType = entityType, EntityId = entityId,
            });
        }
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Sends queued pushes to each person's registered phones and retries failures (like the e-mail sender).</summary>
public sealed class PushSenderService(IServiceScopeFactory scopes, IConfiguration config, ILogger<PushSenderService> log) : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!config.GetValue("Push:AutoSend", true) || string.IsNullOrWhiteSpace(config["Push:ServiceAccountJson"])) return;
        var interval = TimeSpan.FromSeconds(Math.Max(5, config.GetValue("Push:PollSeconds", 15)));
        while (!stop.IsCancellationRequested)
        {
            try { while (await RunOnceAsync(stop) == BatchSize) { } }
            catch (Exception ex) when (!stop.IsCancellationRequested) { log.LogError(ex, "Push sender run failed"); }
            try { await Task.Delay(interval, stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One batch across organizations; returns how many messages it handled.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CmmsDbContext>>();
        var transport = scope.ServiceProvider.GetRequiredService<IPushTransport>();
        await using var db = new CmmsDbContext(options, new FixedTenantContext(null));
        var at = DateTimeOffset.UtcNow;
        var claim = at + ClaimFor;
        var all = db.PushMessages.IgnoreQueryFilters();
        var ids = await all.Where(p => p.Status == EmailStatus.Pending && p.NextAttemptAt <= at && (p.LockedUntil == null || p.LockedUntil < at))
            .OrderBy(p => p.NextAttemptAt).Take(BatchSize).Select(p => p.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        await all.Where(p => ids.Contains(p.Id) && (p.LockedUntil == null || p.LockedUntil < at)).ExecuteUpdateAsync(s => s.SetProperty(p => p.LockedUntil, claim), ct);
        var rows = await all.Where(p => ids.Contains(p.Id) && p.LockedUntil == claim).ToListAsync(ct);
        foreach (var row in rows)
        {
            var sessions = await db.UserSessions.IgnoreQueryFilters()
                .Where(s => s.UserId == row.UserId && s.TenantId == row.TenantId && s.PushToken != null && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > at)).ToListAsync(ct);
            var sent = 0; var failed = false;
            foreach (var s in sessions)
            {
                PushResult r;
                try { r = await transport.SendAsync(s.PushToken!, row.Title, row.Body, row.Link, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Push failed"); r = PushResult.Failed; }
                if (r == PushResult.Sent) sent++;
                else if (r == PushResult.Gone) s.PushToken = null;
                else failed = true;
            }
            row.Attempts++;
            row.LockedUntil = null;
            if (sent > 0 || !failed) { row.Status = EmailStatus.Sent; row.SentAt = DateTimeOffset.UtcNow; row.LastError = sent == 0 ? "No phone to send to." : null; }
            else if (row.Attempts >= MaxAttempts) { row.Status = EmailStatus.Failed; row.LastError = "Firebase didn't take the message."; }
            else row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, row.Attempts));
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
