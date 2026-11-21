using System.Text.Json.Serialization;
using FourC.Cmms.Api.Features;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddHttpContextAccessor();
services.AddMemoryCache();
services.AddScoped<ITenantContext, HttpTenantContext>();
services.AddDbContext<CmmsDbContext>(o => o.UseSqlServer(DatabaseConnection.Build(builder.Configuration),
    sql => sql.EnableRetryOnFailure(3).UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)));

services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 8;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<CmmsDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsFactory>()
    .AddDefaultTokenProviders();

// Sign-in cookies are encrypted with keys kept in the database, so sessions survive restarts and redeployments.
services.AddDataProtection().SetApplicationName("FourC.Cmms").PersistKeysToDbContext<CmmsDbContext>();

services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "cmms.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = Sessions.DefaultLifetime;
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    // The security-stamp check (Identity's default), then the device session (signed-out devices stop working).
    o.Events.OnValidatePrincipal = async ctx =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(ctx);
        if (ctx.Principal is not null) await Sessions.ValidateAsync(ctx);
    };
});
services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
services.AddScoped<IAuthorizationHandler, PermissionHandler>();
services.AddSingleton<PermissionService>();
services.AddAuthorization();

switch (builder.Configuration["Storage:Provider"]?.ToLowerInvariant())
{
    case "azureblob": services.AddSingleton<IFileStorage, AzureBlobFileStorage>(); break;
    case "s3": services.AddSingleton<IFileStorage, S3FileStorage>(); break;
    default: services.AddSingleton<IFileStorage, LocalFileStorage>(); break;
}

// Behind a proxy that reports the original scheme in its own header (CloudFront: CloudFront-Forwarded-Proto),
// read it so HTTPS requests are recognised and sign-in cookies are marked Secure.
if (builder.Configuration["ForwardedHeaders:ProtoHeaderName"] is { Length: > 0 } protoHeader)
    services.Configure<ForwardedHeadersOptions>(o => o.ForwardedProtoHeaderName = protoHeader);
services.AddHostedService<PmSchedulerService>();

// E-mail: queued in the database (outbox) and sent by a background job through SES or SMTP. Without a provider and a
// From address, e-mail is unavailable: nothing is queued and Settings says so.
services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
var emailOptions = builder.Configuration.GetSection("Email").Get<EmailOptions>() ?? new();
if (emailOptions.Available)
{
    if (string.Equals(emailOptions.Provider, "ses", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<IEmailSender, SesEmailSender>();
    else services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
services.AddSingleton<EmailOutbox>();
services.AddSingleton<EmailSenderService>();
services.AddHostedService(sp => sp.GetRequiredService<EmailSenderService>());
services.AddExceptionHandler<ApiExceptionHandler>();
services.AddProblemDetails();
services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CmmsDbContext>();
    await db.Database.MigrateAsync();
    if (await FourC.Cmms.Core.Services.DatabaseSettings.EnsureReadCommittedSnapshotAsync(db))
        app.Logger.LogInformation("Turned on READ_COMMITTED_SNAPSHOT: reads no longer wait for other people's unsaved work.");
    await FourC.Cmms.Core.Services.DueBackfill.RunAsync(db);
}

// The app says which language it shows (Accept-Language); messages come back in it. Only the wording changes: numbers
// and dates the server formats keep the invariant culture.
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture("en");
    o.SupportedCultures = [System.Globalization.CultureInfo.InvariantCulture, new System.Globalization.CultureInfo("en")];
    o.SupportedUICultures = [new System.Globalization.CultureInfo("en"), new System.Globalization.CultureInfo("ar")];
    o.FallBackToParentUICultures = true;
});
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/api/docs");

var api = app.MapGroup("/api");
var version = app.Configuration["App:Version"] is { Length: > 0 } v ? v : "dev";
// The release number (VERSION file, stamped into the assembly at build time); "version" stays the commit for deploy checks.
var release = (typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0-dev").Split('+')[0];
api.MapGet("/health", () => Results.Ok(new { status = "ok", version, release })).AllowAnonymous();
api.MapAuthEndpoints();
api.MapSiteEndpoints();
api.MapLocationEndpoints();
api.MapAssetEndpoints();
api.MapAssetPartEndpoints();
api.MapAssetCategoryEndpoints();
api.MapCustomFieldEndpoints();
api.MapAttachmentEndpoints();
api.MapAuditEndpoints();
api.MapAdminEndpoints();
api.MapPeopleEndpoints();
api.MapImportEndpoints();
api.MapWorkRequestEndpoints();
api.MapWorkOrderEndpoints();
api.MapDowntimeEndpoints();
api.MapPlanningEndpoints();
api.MapPlanningBoardEndpoints();
api.MapPmEndpoints();
api.MapInventoryEndpoints();
api.MapPurchasingEndpoints();
api.MapReportEndpoints();
api.MapDashboardEndpoints();
api.MapAlertEndpoints();
api.MapEmailEndpoints();
api.MapViewEndpoints();
api.MapDeviceEndpoints();
api.MapTechEndpoints();
api.MapTechActions();
api.MapLabelEndpoints();

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
