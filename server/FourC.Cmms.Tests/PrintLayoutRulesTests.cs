using FourC.Cmms.Core.Settings;

namespace FourC.Cmms.Tests;

/// <summary>The print layout catalogue and which layout a record prints with (0.23.0). No database.</summary>
public class PrintLayoutRulesTests
{
    private static readonly Guid S = Guid.NewGuid(), T = Guid.NewGuid();

    private static PrintLayout L(string name, Guid? site = null, string? kind = null, bool isDefault = false) =>
        new() { Id = Guid.NewGuid(), Paper = PrintPapers.JobCard, Name = name, SiteId = site, KindKey = kind, IsDefault = isDefault };

    [Fact]
    public void Pick_follows_site_and_kind_precedence()
    {
        var both = L("both", S, "Corrective");
        var site = L("site", S);
        var kind = L("kind", kind: "Corrective");
        var def = L("default", isDefault: true);
        var byHand = L("by hand");
        var all = new List<PrintLayout> { byHand, def, kind, site, both };
        Assert.Same(both, PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard));
        all.Remove(both);
        Assert.Same(site, PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard));
        all.Remove(site);
        Assert.Same(kind, PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard));
        all.Remove(kind);
        Assert.Same(def, PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard));
        all.Remove(def);
        var built = PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard);
        Assert.Equal(Guid.Empty, built.Id);
        Assert.Equal("Built-in", built.Name);
    }

    [Fact]
    public void Pick_skips_layouts_for_other_sites_and_kinds()
    {
        var all = new List<PrintLayout> { L("other site", T, "Corrective"), L("other kind", S, "Inspection"), L("other site only", T),
            new() { Id = Guid.NewGuid(), Paper = PrintPapers.PurchaseOrder, Name = "other paper", IsDefault = true } };
        Assert.Equal(Guid.Empty, PrintLayoutRules.Pick(all, S, "Corrective", PrintPapers.JobCard).Id);
        // Work type names compare without case.
        var kind = L("kind", kind: "corrective");
        Assert.Same(kind, PrintLayoutRules.Pick([.. all, kind], S, "Corrective", PrintPapers.JobCard));
        // A record with no kind (a PO without an order type) never matches a kind layout.
        Assert.Equal(Guid.Empty, PrintLayoutRules.Pick([kind], S, null, PrintPapers.JobCard).Id);
    }

    [Fact]
    public void Built_in_layouts_match_todays_prints()
    {
        var job = PrintPapers.BuiltIn(PrintPapers.JobCard);
        Assert.Equal(["header", "details", "description", "technician-notes", "warranty", "asset-fields", "checklist", "contractors", "labour", "parts",
            "completion", "signatures", "terms"], job.Blocks.Select(b => b.Key));
        Assert.Equal(["asset-fields", "terms"], job.Blocks.Where(b => !b.Shown).Select(b => b.Key));
        var details = job.Blocks.Single(b => b.Key == "details").Fields;
        Assert.DoesNotContain("team", details);
        Assert.DoesNotContain("costCentre", details);
        Assert.Contains("serialNumber", details);
        Assert.True(job.ShowLogo);
        Assert.Equal("A4", job.PaperSize);

        var po = PrintPapers.BuiltIn(PrintPapers.PurchaseOrder);
        Assert.Equal(["header", "supplier", "details", "lines", "totals", "notes", "approvals", "terms"], po.Blocks.Select(b => b.Key));
        Assert.Equal(["terms"], po.Blocks.Where(b => !b.Shown).Select(b => b.Key));
        Assert.False(po.ShowLogo);
        Assert.DoesNotContain("orderType", po.Blocks.Single(b => b.Key == "details").Fields);
        Assert.Contains("discount", po.Blocks.Single(b => b.Key == "lines").Fields);

        var request = PrintPapers.BuiltIn(PrintPapers.Request);
        Assert.Equal(["header", "details", "description", "access-windows", "progress", "terms"], request.Blocks.Select(b => b.Key));
        Assert.Equal(["terms"], request.Blocks.Where(b => !b.Shown).Select(b => b.Key));

        var labels = PrintPapers.BuiltIn(PrintPapers.Labels);
        Assert.Equal(["number", "name", "detail"], labels.LabelFields);
        Assert.Equal("a4-3x8", labels.LabelSize);
        Assert.Empty(labels.Blocks);

        Assert.Equal(["purchase-order:lines.description", "purchase-order:lines.lineTotal", "purchase-order:totals.total"],
            PrintPapers.All.SelectMany(p => PrintPapers.FixedFields(p).Select(f => $"{p}:{f}")).Order());
    }

    /// <summary>The catalogue: paper/block: fields. The web keeps the same list (web/src/print/layouts.test.ts pins it to this text).</summary>
    internal static readonly string[] Catalogue =
    [
        "job-card/header: orgName,site,title,printedDate",
        "job-card/details: status,priority,workType,trade,team,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy,costCentre",
        "job-card/description: ",
        "job-card/technician-notes: ",
        "job-card/warranty: ",
        "job-card/asset-fields: ",
        "job-card/checklist: ",
        "job-card/contractors: ",
        "job-card/labour: ",
        "job-card/parts: ",
        "job-card/completion: completionNotes,causeNotes,preventionNotes,symptom,cause,remedy,assetWasDown,downtimeHours",
        "job-card/signatures: ",
        "job-card/terms: ",
        "purchase-order/header: orgName,site,deliveryAddress,title,orderDate,expectedDate",
        "purchase-order/supplier: name,contact,address,email,phone,taxNumber",
        "purchase-order/details: paymentTerms,quoteRef,shippingMethod,requestedBy,currency,orderType",
        "purchase-order/lines: lineNumber,partNumber,description,supplierPartNumber,quantity,unit,unitPrice,discount,lineTotal",
        "purchase-order/totals: subtotal,tax,shipping,total",
        "purchase-order/notes: ",
        "purchase-order/approvals: ",
        "purchase-order/terms: ",
        "request/header: orgName,site,title,printedDate",
        "request/details: status,submitted,requester,phone,requestType,priority,workType,site,location,asset,requiredBy,symptom",
        "request/description: ",
        "request/access-windows: ",
        "request/progress: ",
        "request/terms: ",
        "labels/fields: number,name,detail,site",
    ];

    [Fact]
    public void Catalogue_matches_the_web_list()
    {
        var lines = PrintPapers.All.Where(p => p != PrintPapers.Labels)
            .SelectMany(p => PrintPapers.Blocks(p).Select(b => $"{p}/{b.Key}: {string.Join(",", b.Fields)}"))
            .Append($"labels/fields: {string.Join(",", PrintPapers.LabelFields)}");
        Assert.Equal(Catalogue, lines);
        Assert.Equal(["A4", "A5", "Letter"], PrintPapers.PaperSizes);
        Assert.Equal(["a4-3x8", "a4-3x7", "a4-2x7", "roll-50x25", "roll-100x50"], PrintPapers.LabelSizes);
    }
}
