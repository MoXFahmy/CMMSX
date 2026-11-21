using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;

namespace FourC.Cmms.Api.Features;

/// <summary>Settings › Import: check a workbook (nothing saved) or import it. Administrators only.</summary>
public static class ImportEndpoints
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Well inside the 60-second limit in front of the app, so an import never saves after the page has given up.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    public static void MapImportEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/import").WithTags("Import");

        g.MapPost("/check", async (HttpRequest req, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            Results.Ok(await ImportRun.RunAsync(db, ctx, await ReadAsync(req, ct), save: false, Budget, ct)))
            .RequirePermission(Permissions.SettingsManage).DisableAntiforgery();

        g.MapPost("/run", async (HttpRequest req, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            Results.Ok(await ImportRun.RunAsync(db, ctx, await ReadAsync(req, ct), save: true, Budget, ct)))
            .RequirePermission(Permissions.SettingsManage).DisableAntiforgery();
    }

    /// <summary>The uploaded workbook ("file"): .xlsx or .csv, up to 10 MB.</summary>
    internal static async Task<SpreadsheetBook> ReadAsync(HttpRequest req, CancellationToken ct)
    {
        if (!req.HasFormContentType) throw new DomainException("Choose a file of up to 10 MB.");
        var form = await req.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0 || file.Length > MaxBytes) throw new DomainException("Choose a file of up to 10 MB.");
        await using var stream = file.OpenReadStream();
        return SpreadsheetReader.Read(stream, file.FileName);
    }
}
