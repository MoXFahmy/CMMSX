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

        g.MapGet("/template", async (CmmsDbContext db, CancellationToken ct) =>
            Results.File(await ImportWorkbook.TemplateAsync(db, ct), Xlsx, "CMX-import-template.xlsx"))
            .RequirePermission(Permissions.SettingsManage);

        g.MapGet("/current", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            Results.File(await ImportWorkbook.CurrentAsync(db, ctx, ct), Xlsx, $"CMX-data-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"))
            .RequirePermission(Permissions.SettingsManage);

        // The rows a check found problems in, as they were in the file, with the reason beside each.
        g.MapPost("/problems", async (HttpRequest req, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        {
            var book = await ReadAsync(req, ct);
            var result = await ImportRun.RunAsync(db, ctx, book, save: false, Budget, ct);
            return Results.File(ImportWorkbook.Problems(book, result), Xlsx, "CMX-import-problems.xlsx");
        }).RequirePermission(Permissions.SettingsManage).DisableAntiforgery();
    }

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

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
