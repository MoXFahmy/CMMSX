using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Files;
using FourC.Cmms.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Settings › Import: check a workbook (nothing saved) or import it. Administrators only.</summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/import").WithTags("Import");

        // Checks and imports run in the background (0.26.0): the upload answers at once with the job, which the page follows.
        g.MapPost("/check", (HttpRequest req, CmmsDbContext db, ITenantContext ctx, IFileStorage storage, CancellationToken ct) => QueueAsync(req, db, ctx, storage, save: false, ct))
            .RequirePermission(Permissions.SettingsManage).DisableAntiforgery();

        g.MapPost("/run", (HttpRequest req, CmmsDbContext db, ITenantContext ctx, IFileStorage storage, CancellationToken ct) => QueueAsync(req, db, ctx, storage, save: true, ct))
            .RequirePermission(Permissions.SettingsManage).DisableAntiforgery();

        // The list leaves each job's result out (it can be large); it's read one job at a time.
        g.MapGet("/jobs", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            (await ImportJobs.Visible(db, ctx).OrderByDescending(j => j.CreatedAt).Take(100)
                .Select(j => new FourC.Cmms.Core.Imports.ImportJob
                {
                    Id = j.Id, TenantId = j.TenantId, Save = j.Save, Status = j.Status, FileName = j.FileName, UserId = j.UserId, UserName = j.UserName, CreatedAt = j.CreatedAt,
                    StartedAt = j.StartedAt, FinishedAt = j.FinishedAt, Sheet = j.Sheet, RowsDone = j.RowsDone, RowsTotal = j.RowsTotal, Error = j.Error,
                }).ToListAsync(ct)).Select(j => ImportJobs.Shape(j, 0, withResult: false)))
            .RequirePermission(Permissions.SettingsManage);

        g.MapGet("/jobs/{id:guid}", async (Guid id, CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            await ImportJobs.Visible(db, ctx).FirstOrDefaultAsync(j => j.Id == id, ct) is { } job
                ? Results.Ok(ImportJobs.Shape(job, await ImportJobs.WaitingBehindAsync(db, job, ct), withResult: true)) : Results.NotFound())
            .RequirePermission(Permissions.SettingsManage);

        // The rows a finished check or import found problems in, as they were in the file, with the reason beside each.
        g.MapGet("/jobs/{id:guid}/problems", async (Guid id, CmmsDbContext db, ITenantContext ctx, IFileStorage storage,
            Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken ct) =>
        {
            var job = await ImportJobs.Visible(db, ctx).FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job?.ResultJson is null) return Results.NotFound();
            if (job.StorageKey is null || await storage.OpenReadAsync(job.StorageKey, ct) is not { } stream)
                return Results.Problem(title: Translator.T(ImportJobs.FileGone), statusCode: 404);
            SpreadsheetBook book;
            await using (stream) book = SpreadsheetReader.Read(stream, job.FileName);
            var result = System.Text.Json.JsonSerializer.Deserialize<ImportResult>(job.ResultJson, json.Value.SerializerOptions)!;
            return Results.File(ImportWorkbook.Problems(book, result), Xlsx, "CMX-import-problems.xlsx");
        }).RequirePermission(Permissions.SettingsManage);

        g.MapGet("/template", async (CmmsDbContext db, CancellationToken ct) =>
            Results.File(await ImportWorkbook.TemplateAsync(db, ct), Xlsx, "CMX-import-template.xlsx"))
            .RequirePermission(Permissions.SettingsManage);

        g.MapGet("/current", async (CmmsDbContext db, ITenantContext ctx, CancellationToken ct) =>
            Results.File(await ImportWorkbook.CurrentAsync(db, ctx, ct), Xlsx, $"CMX-data-{DateTime.UtcNow:yyyy-MM-dd}.xlsx"))
            .RequirePermission(Permissions.SettingsManage);
    }

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static async Task<IResult> QueueAsync(HttpRequest req, CmmsDbContext db, ITenantContext ctx, IFileStorage storage, bool save, CancellationToken ct)
    {
        var id = await ImportJobs.QueueAsync(req, db, ctx, storage, save, ct);
        return Results.Accepted($"/api/import/jobs/{id}", new { id });
    }
}
