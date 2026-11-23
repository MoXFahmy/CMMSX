using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>
/// The <c>/export</c> endpoint of a list: the rows the list itself would return for the same filters and sort (up to
/// <see cref="MaxRows"/>), as .xlsx (default) or CSV. The query carries repeated <c>c</c> (property key) and <c>t</c>
/// (column title) values in screen order; with no <c>c</c>, every non-id property is exported under its key.
/// </summary>
public static class ListExport
{
    public const int MaxRows = 10_000;
    private const int MaxColumns = 100;
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public sealed record ExportFile(byte[] Bytes, string ContentType, string FileName, bool Truncated);

    public static async Task<IResult> FileAsync<T>(HttpContext http, CmmsDbContext db, string list, Func<int, int, Task<PagedResult<T>>> page, CancellationToken ct)
        where T : notnull
    {
        var settings = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == db.CurrentTenantId, ct)).Settings;
        var zone = settings.TimeZone;
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(zone ?? "UTC", out var found) ? found : TimeZoneInfo.Utc;
        ExportFile file;
        try { file = Build(http.Request.Query, list, await page(1, MaxRows), tz, settings); }
        catch (ArgumentException ex) { throw new DomainException(ex.Message); }
        if (file.Truncated) http.Response.Headers["X-Export-Truncated"] = "true";
        return Results.File(file.Bytes, file.ContentType, file.FileName);
    }

    public static ExportFile Build<T>(IQueryCollection query, string list, PagedResult<T> page, TimeZoneInfo tz, FourC.Cmms.Core.Tenancy.TenantSettings? settings = null) where T : notnull
    {
        var items = page.Items.Cast<object>().ToList();
        var rowType = typeof(T) != typeof(object) ? typeof(T) : items.FirstOrDefault()?.GetType();
        var keys = query["c"].Select(k => (k ?? "").Trim()).Where(k => k.Length > 0).ToList();
        var titles = query["t"].Select(x => (x ?? "").Trim()).ToList();
        IReadOnlyList<ExportColumn> cols = keys.Count == 0
            ? rowType is null ? [] : TableExport.DefaultColumns(rowType)
            : keys.Select((k, i) => new ExportColumn(k, titles.Count == keys.Count && titles[i].Length > 0 ? titles[i][..Math.Min(120, titles[i].Length)] : k)).ToList();
        if (cols.Count > MaxColumns) throw new ArgumentException($"Choose at most {MaxColumns} columns.");
        if (rowType is not null) TableExport.Check(rowType, cols);
        var rows = TableExport.Rows(items, cols, tz, settings);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        var truncated = page.Total > items.Count;
        return string.Equals(query["format"], "csv", StringComparison.OrdinalIgnoreCase)
            ? new ExportFile(TableExport.Csv(cols, rows), "text/csv", $"{list}-{stamp}.csv", truncated)
            : new ExportFile(TableExport.Xlsx(SheetName(list), cols, rows), XlsxType, $"{list}-{stamp}.xlsx", truncated);
    }

    /// <summary>"work-orders" → "Work orders".</summary>
    private static string SheetName(string list) => list.Length == 0 ? "Sheet1" : char.ToUpperInvariant(list[0]) + list[1..].Replace('-', ' ');
}
