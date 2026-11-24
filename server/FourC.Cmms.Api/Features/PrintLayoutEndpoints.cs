using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Identity;
using FourC.Cmms.Core.Settings;
using FourC.Cmms.Core.Work;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Api.Features;

/// <summary>Print layouts (0.23.0): the organization's versions of the job card, purchase order, request receipt and labels.</summary>
public static class PrintLayoutEndpoints
{
    public sealed record PrintLayoutInput(string? Paper, string? Name, bool IsDefault, Guid? SiteId, string? KindKey, string? PaperSize, bool ShowLogo,
        string? HeaderText, string? HeaderTextAr, string? FooterText, string? FooterTextAr, string? Terms, string? TermsAr,
        List<PrintBlock>? Blocks, string? LabelSize, List<string>? LabelFields);

    public static void MapPrintLayoutEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/print-layouts").WithTags("Print layouts").RequireAuthorization();

        g.MapGet("/", async (string? paper, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!PrintPapers.IsPaper(paper)) throw new DomainException("Choose a paper from the list.");
            var rows = await db.PrintLayouts.AsNoTracking().Where(l => l.Paper == paper).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).ToListAsync(ct);
            return await ViewsAsync(db, rows, ct);
        });

        g.MapGet("/built-in", async (string? paper, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!PrintPapers.IsPaper(paper)) throw new DomainException("Choose a paper from the list.");
            return (await ViewsAsync(db, [PrintPapers.BuiltIn(paper!)], ct))[0];
        });

        // The layout a record prints with: the record is read through the usual filters, so one the person can't see answers 404.
        g.MapGet("/for", async (string? paper, Guid id, string? labelKind, CmmsDbContext db, CancellationToken ct) =>
        {
            if (!PrintPapers.IsPaper(paper)) throw new DomainException("Choose a paper from the list.");
            (Guid? Site, string? Kind)? record = paper switch
            {
                PrintPapers.JobCard => await db.WorkOrders.Where(w => w.Id == id).Select(w => new { w.SiteId, w.Type }).FirstOrDefaultAsync(ct) is { } w
                    ? (w.SiteId, w.Type.ToString()) : null,
                PrintPapers.PurchaseOrder => await db.PurchaseOrders.Where(p => p.Id == id).Select(p => new { p.SiteId, p.OrderTypeId }).FirstOrDefaultAsync(ct) is { } p
                    ? (p.SiteId, p.OrderTypeId?.ToString()) : null,
                PrintPapers.Request => await db.WorkRequests.Where(r => r.Id == id).Select(r => new { r.SiteId, r.RequestTypeId }).FirstOrDefaultAsync(ct) is { } r
                    ? (r.SiteId, r.RequestTypeId?.ToString()) : null,
                _ => labelKind switch
                {
                    "asset" => await db.Assets.Where(a => a.Id == id).Select(a => (Guid?)a.SiteId).FirstOrDefaultAsync(ct) is { } s ? (s, "asset") : null,
                    "location" => await db.Locations.Where(l => l.Id == id).Select(l => (Guid?)l.SiteId).FirstOrDefaultAsync(ct) is { } s2 ? (s2, "location") : null,
                    "part" => await db.Parts.AnyAsync(x => x.Id == id, ct) ? ((Guid?)null, "part") : null,
                    _ => throw new DomainException("Choose a type from the list."),
                },
            };
            if (record is null) return Results.NotFound();
            var layouts = await db.PrintLayouts.AsNoTracking().Where(l => l.Paper == paper).ToListAsync(ct);
            return Results.Ok((await ViewsAsync(db, [PrintLayoutRules.Pick(layouts, record.Value.Site, record.Value.Kind, paper!)], ct))[0]);
        });

        g.MapGet("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
            await db.PrintLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct) is { } l ? Results.Ok((await ViewsAsync(db, [l], ct))[0]) : Results.NotFound());

        g.MapPost("/", async (PrintLayoutInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var v = await ValidateAsync(db, input, null, ct);
            if (!v.IsValid) return v.Problem();
            var layout = new PrintLayout { Paper = input.Paper!, Name = input.Name!.Trim() };
            Apply(layout, input);
            await ClearOtherDefaultsAsync(db, layout, ct);
            db.PrintLayouts.Add(layout);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { layout.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPut("/{id:guid}", async (Guid id, PrintLayoutInput input, CmmsDbContext db, CancellationToken ct) =>
        {
            var layout = await db.PrintLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (layout is null) return Results.NotFound();
            // A layout stays on its paper.
            input = input with { Paper = layout.Paper };
            var v = await ValidateAsync(db, input, id, ct);
            if (!v.IsValid) return v.Problem();
            layout.Name = input.Name!.Trim();
            Apply(layout, input);
            await ClearOtherDefaultsAsync(db, layout, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { layout.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapDelete("/{id:guid}", async (Guid id, CmmsDbContext db, CancellationToken ct) =>
        {
            var layout = await db.PrintLayouts.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (layout is null) return Results.NotFound();
            db.PrintLayouts.Remove(layout);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static void Apply(PrintLayout l, PrintLayoutInput i)
    {
        var labels = l.Paper == PrintPapers.Labels;
        l.IsDefault = i.IsDefault;
        l.SiteId = i.SiteId;
        l.KindKey = NormalKind(l.Paper, i.KindKey);
        l.PaperSize = labels ? null : i.PaperSize ?? "A4";
        l.ShowLogo = i.ShowLogo;
        l.HeaderText = i.HeaderText.Clean(); l.HeaderTextAr = i.HeaderTextAr.Clean();
        l.FooterText = i.FooterText.Clean(); l.FooterTextAr = i.FooterTextAr.Clean();
        l.Terms = i.Terms.Clean(); l.TermsAr = i.TermsAr.Clean();
        l.Blocks = labels ? [] : CompleteBlocks(l.Paper, i.Blocks ?? []);
        l.LabelSize = labels ? i.LabelSize ?? "a4-3x8" : null;
        l.LabelFields = labels ? (i.LabelFields is { Count: > 0 } f ? [.. PrintPapers.LabelFields.Where(f.Contains)] : [.. PrintPapers.BuiltInLabelFields]) : [];
    }

    /// <summary>
    /// The stored block list: no blocks means today's print; blocks left out are added at the end, hidden (or shown with their
    /// fixed fields when they carry any, since those always print).
    /// </summary>
    private static List<PrintBlock> CompleteBlocks(string paper, List<PrintBlock> given)
    {
        if (given.Count == 0) return PrintPapers.BuiltIn(paper).Blocks;
        var fixedFields = PrintPapers.FixedFields(paper);
        var list = given.Select(b => new PrintBlock { Key = b.Key, Shown = b.Shown, Fields = [.. b.Fields.Distinct()] }).ToList();
        foreach (var b in PrintPapers.Blocks(paper).Where(b => list.All(x => x.Key != b.Key)))
        {
            var mustShow = fixedFields.Any(f => f.StartsWith(b.Key + ".", StringComparison.Ordinal));
            list.Add(new PrintBlock { Key = b.Key, Shown = mustShow, Fields = [.. b.BuiltInFields] });
        }
        return list;
    }

    private static string? NormalKind(string paper, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return null;
        kind = kind.Trim();
        return paper == PrintPapers.JobCard && Enum.TryParse<WorkType>(kind, true, out var t) ? t.ToString()
            : paper is PrintPapers.PurchaseOrder or PrintPapers.Request && Guid.TryParse(kind, out var g) ? g.ToString() : kind.ToLowerInvariant();
    }

    private static async Task ClearOtherDefaultsAsync(CmmsDbContext db, PrintLayout layout, CancellationToken ct)
    {
        if (!layout.IsDefault) return;
        foreach (var other in await db.PrintLayouts.Where(l => l.Paper == layout.Paper && l.IsDefault && l.Id != layout.Id).ToListAsync(ct))
            other.IsDefault = false;
    }

    private static async Task<Validator> ValidateAsync(CmmsDbContext db, PrintLayoutInput i, Guid? id, CancellationToken ct)
    {
        var v = new Validator();
        if (!PrintPapers.IsPaper(i.Paper)) { v.Add("paper", "Choose a paper from the list."); return v; }
        var paper = i.Paper!;
        var name = i.Name?.Trim() ?? "";
        v.When(name.Length == 0, "name", "Give the layout a name.")
         .When(name.Length > PrintPapers.MaxName, "name", $"Keep the name to {PrintPapers.MaxName} characters.");
        if (name.Length > 0 && await db.PrintLayouts.AnyAsync(l => l.Paper == paper && l.Id != id && l.Name.ToLower() == name.ToLower(), ct))
            v.Add("name", "Another layout of this paper has this name.");
        if (id is null && await db.PrintLayouts.CountAsync(l => l.Paper == paper, ct) >= PrintPapers.MaxLayouts)
            v.Add("name", $"A paper can have up to {PrintPapers.MaxLayouts} layouts.");

        var kind = NormalKind(paper, i.KindKey);
        v.When(i.IsDefault && (i.SiteId is not null || kind is not null), "isDefault", "The default layout applies when no other does, so it can't have a site or type.");
        if (i.SiteId is { } site && !await db.Sites.AnyAsync(s => s.Id == site, ct)) v.Add("siteId", "Choose a site you can see.");
        if (kind is not null)
        {
            var known = paper switch
            {
                PrintPapers.JobCard => Enum.TryParse<WorkType>(kind, true, out var t) && Enum.IsDefined(t) && !int.TryParse(kind, out _),
                PrintPapers.PurchaseOrder => Guid.TryParse(kind, out var ot) && await db.OrderTypes.AnyAsync(x => x.Id == ot, ct),
                PrintPapers.Request => Guid.TryParse(kind, out var rt) && await db.RequestTypes.AnyAsync(x => x.Id == rt, ct),
                _ => PrintPapers.LabelKinds.Contains(kind),
            };
            v.When(!known, "kindKey", "Choose a type from the list.");
        }
        if ((i.SiteId is not null || kind is not null)
            && (await db.PrintLayouts.Where(l => l.Paper == paper && l.Id != id && l.SiteId == i.SiteId && l.KindKey != null == (kind != null)).Select(l => l.KindKey).ToListAsync(ct))
                .Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)))
            v.Add("siteId", "Another layout already has this site and type.");

        if (paper == PrintPapers.Labels)
        {
            v.When(i.LabelSize is not null && !PrintPapers.LabelSizes.Contains(i.LabelSize), "labelSize", "Choose a label size from the list.");
            v.When(i.LabelFields?.Any(f => !PrintPapers.LabelFields.Contains(f)) == true, "labelFields", "Choose fields from the list.");
        }
        else
        {
            v.When(i.PaperSize is not null && !PrintPapers.PaperSizes.Contains(i.PaperSize), "paperSize", "Choose a paper size from the list.");
            await ValidateBlocksAsync(db, v, paper, i.Blocks ?? [], ct);
        }
        v.When((i.HeaderText?.Trim().Length ?? 0) > PrintPapers.MaxText || (i.HeaderTextAr?.Trim().Length ?? 0) > PrintPapers.MaxText,
            "headerText", $"Keep the header to {PrintPapers.MaxText} characters.");
        v.When((i.FooterText?.Trim().Length ?? 0) > PrintPapers.MaxText || (i.FooterTextAr?.Trim().Length ?? 0) > PrintPapers.MaxText,
            "footerText", $"Keep the footer to {PrintPapers.MaxText} characters.");
        v.When((i.Terms?.Trim().Length ?? 0) > PrintPapers.MaxTerms || (i.TermsAr?.Trim().Length ?? 0) > PrintPapers.MaxTerms,
            "terms", "Keep the terms to 4,000 characters.");
        return v;
    }

    private static async Task ValidateBlocksAsync(CmmsDbContext db, Validator v, string paper, List<PrintBlock> blocks, CancellationToken ct)
    {
        var catalogue = PrintPapers.Blocks(paper).ToDictionary(b => b.Key);
        if (blocks.Any(b => b?.Key is null || !catalogue.ContainsKey(b.Key)) || blocks.Select(b => b.Key).Distinct().Count() != blocks.Count)
        {
            v.Add("blocks", "Choose blocks from the list.");
            return;
        }
        foreach (var b in blocks)
        {
            if (b.Key == PrintPapers.AssetFieldsBlock)
            {
                var keys = b.Fields ?? [];
                if (keys.Count > 0)
                {
                    var known = await db.CustomFields.Where(f => f.EntityType == "Asset").Select(f => f.Key).ToListAsync(ct);
                    if (keys.Any(k => !known.Contains(k))) v.Add("blocks", "Choose an asset field from the list.");
                }
                continue;
            }
            if ((b.Fields ?? []).Any(f => !catalogue[b.Key].Fields.Contains(f))) { v.Add("blocks", "Choose fields from the list."); return; }
        }
        var fixedFields = PrintPapers.FixedFields(paper);
        foreach (var f in fixedFields)
        {
            var parts = f.Split('.');
            var block = blocks.FirstOrDefault(b => b.Key == parts[0]);
            if (block is not null && (!block.Shown || !(block.Fields ?? []).Contains(parts[1]))) { v.Add("blocks", "This field always prints."); return; }
        }
    }

    /// <summary>Layouts as the web reads them, with the site's and the kind's names.</summary>
    private static async Task<List<object>> ViewsAsync(CmmsDbContext db, List<PrintLayout> rows, CancellationToken ct)
    {
        var siteIds = rows.Where(r => r.SiteId is not null).Select(r => r.SiteId!.Value).Distinct().ToList();
        var sites = await db.Sites.AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var typeIds = rows.Select(r => Guid.TryParse(r.KindKey, out var g) ? g : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var orderTypes = await db.OrderTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var requestTypes = await db.RequestTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        string? KindName(PrintLayout l) => l.KindKey is null ? null
            : Guid.TryParse(l.KindKey, out var g) ? (l.Paper == PrintPapers.PurchaseOrder ? orderTypes.GetValueOrDefault(g) : requestTypes.GetValueOrDefault(g))
            : l.KindKey;
        return rows.Select(l => (object)new
        {
            l.Id, l.Paper, l.Name, l.IsDefault, l.SiteId, SiteName = l.SiteId is { } s ? sites.GetValueOrDefault(s) : null, l.KindKey, KindName = KindName(l),
            l.PaperSize, l.ShowLogo, l.HeaderText, l.HeaderTextAr, l.FooterText, l.FooterTextAr, l.Terms, l.TermsAr, l.Blocks, l.LabelSize, l.LabelFields,
        }).ToList();
    }
}
