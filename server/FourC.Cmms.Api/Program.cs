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
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
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
    await scope.ServiceProvider.GetRequiredService<CmmsDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/api/docs");

var api = app.MapGroup("/api");
var version = app.Configuration["App:Version"] is { Length: > 0 } v ? v : "dev";
api.MapGet("/health", () => Results.Ok(new { status = "ok", version })).AllowAnonymous();
api.MapAuthEndpoints();
api.MapSiteEndpoints();
api.MapLocationEndpoints();
api.MapAssetEndpoints();
api.MapAssetCategoryEndpoints();
api.MapCustomFieldEndpoints();
api.MapAttachmentEndpoints();
api.MapAuditEndpoints();
api.MapAdminEndpoints();
api.MapPeopleEndpoints();
api.MapWorkRequestEndpoints();
api.MapWorkOrderEndpoints();
api.MapPmEndpoints();
api.MapInventoryEndpoints();
api.MapPurchasingEndpoints();
api.MapReportEndpoints();
api.MapDashboardEndpoints();

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
