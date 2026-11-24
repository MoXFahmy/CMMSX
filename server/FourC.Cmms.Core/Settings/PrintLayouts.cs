using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Settings;

/// <summary>One block of a printed paper: shown or not, and the fields it shows (0.23.0).</summary>
public sealed class PrintBlock
{
    public required string Key { get; set; }
    public bool Shown { get; set; } = true;
    public List<string> Fields { get; set; } = [];
}

/// <summary>
/// An organization's layout for a printed paper (0.23.0): which blocks and fields print, in what order, the texts in English and
/// Arabic, and the paper. Chosen for a record by site and kind (<see cref="PrintLayoutRules"/>); with none, the built-in layout.
/// </summary>
public class PrintLayout : TenantEntity
{
    public required string Paper { get; set; }
    public required string Name { get; set; }
    /// <summary>Applies when no layout with a site or kind matches. Has no site and no kind.</summary>
    public bool IsDefault { get; set; }
    public Guid? SiteId { get; set; }
    /// <summary>The record kind it is for: a work type name (job card), an order type id (purchase order), a request type id (request), or asset/location/part (labels).</summary>
    public string? KindKey { get; set; }
    public string? PaperSize { get; set; }
    public bool ShowLogo { get; set; } = true;
    public string? HeaderText { get; set; }
    public string? HeaderTextAr { get; set; }
    public string? FooterText { get; set; }
    public string? FooterTextAr { get; set; }
    public string? Terms { get; set; }
    public string? TermsAr { get; set; }
    public List<PrintBlock> Blocks { get; set; } = [];
    public string? LabelSize { get; set; }
    public List<string> LabelFields { get; set; } = [];
}

/// <summary>A block of a paper: its fields, and what today's print (the built-in layout) shows.</summary>
public sealed record PaperBlock(string Key, IReadOnlyList<string> Fields, IReadOnlyList<string> BuiltInFields, bool BuiltInShown);

/// <summary>
/// The printed papers, their blocks and fields (0.23.0). The built-in layout of each is today's print. The web keeps the same
/// catalogue (web/src/print/layouts.ts); tests on both sides pin it to one list.
/// </summary>
public static class PrintPapers
{
    public const string JobCard = "job-card", PurchaseOrder = "purchase-order", Request = "request", Labels = "labels";
    public const int MaxLayouts = 20, MaxName = 60, MaxText = 500, MaxTerms = 4000;
    public const string AssetFieldsBlock = "asset-fields";

    public static readonly IReadOnlyList<string> All = [JobCard, PurchaseOrder, Request, Labels];
    public static readonly IReadOnlyList<string> PaperSizes = ["A4", "A5", "Letter"];
    public static readonly IReadOnlyList<string> LabelSizes = ["a4-3x8", "a4-3x7", "a4-2x7", "roll-50x25", "roll-100x50"];
    public static readonly IReadOnlyList<string> LabelFields = ["number", "name", "detail", "site"];
    public static readonly IReadOnlyList<string> BuiltInLabelFields = ["number", "name", "detail"];
    public static readonly IReadOnlyList<string> LabelKinds = ["asset", "location", "part"];

    private static PaperBlock B(string key, string fields = "", string? builtIn = null, bool shown = true)
    {
        var all = fields.Length == 0 ? [] : fields.Split(',');
        return new(key, all, builtIn is null ? all : builtIn.Split(',', StringSplitOptions.RemoveEmptyEntries), shown);
    }

    private static readonly Dictionary<string, IReadOnlyList<PaperBlock>> Catalogue = new()
    {
        [JobCard] =
        [
            B("header", "orgName,site,title,printedDate"),
            B("details", "status,priority,workType,trade,team,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy,costCentre",
                "status,priority,workType,trade,site,location,asset,serialNumber,plannedStart,due,assignees,requestedBy"),
            B("description"), B("technician-notes"), B("warranty"),
            // Its fields are the asset's custom field keys, chosen by the administrator.
            B(AssetFieldsBlock, shown: false),
            B("checklist"), B("contractors"), B("labour"), B("parts"),
            B("completion", "completionNotes,causeNotes,preventionNotes,symptom,cause,remedy,assetWasDown,downtimeHours"),
            B("signatures"), B("terms", shown: false),
        ],
        [PurchaseOrder] =
        [
            B("header", "orgName,site,deliveryAddress,title,orderDate,expectedDate"),
            B("supplier", "name,contact,address,email,phone,taxNumber"),
            B("details", "paymentTerms,quoteRef,shippingMethod,requestedBy,currency,orderType", "paymentTerms,quoteRef,shippingMethod,requestedBy,currency"),
            B("lines", "lineNumber,partNumber,description,supplierPartNumber,quantity,unit,unitPrice,discount,lineTotal"),
            B("totals", "subtotal,tax,shipping,total"),
            B("notes"), B("approvals"), B("terms", shown: false),
        ],
        [Request] =
        [
            B("header", "orgName,site,title,printedDate"),
            B("details", "status,submitted,requester,phone,requestType,priority,workType,site,location,asset,requiredBy,symptom"),
            B("description"), B("access-windows"), B("progress"), B("terms", shown: false),
        ],
        [Labels] = [],
    };

    private static readonly Dictionary<string, IReadOnlySet<string>> Fixed = new()
    {
        [JobCard] = new HashSet<string>(),
        [PurchaseOrder] = new HashSet<string> { "lines.description", "lines.lineTotal", "totals.total" },
        [Request] = new HashSet<string>(),
        [Labels] = new HashSet<string>(),
    };

    public static bool IsPaper(string? paper) => paper is not null && Catalogue.ContainsKey(paper);
    public static IReadOnlyList<PaperBlock> Blocks(string paper) => Catalogue[paper];
    /// <summary>Fields ("block.field") that always print.</summary>
    public static IReadOnlySet<string> FixedFields(string paper) => Fixed[paper];

    /// <summary>Today's print of a paper, as a layout (Id empty).</summary>
    public static PrintLayout BuiltIn(string paper) => new()
    {
        Id = Guid.Empty, Paper = paper, Name = "Built-in",
        PaperSize = paper == Labels ? null : "A4",
        ShowLogo = paper != PurchaseOrder,
        Blocks = [.. Catalogue[paper].Select(b => new PrintBlock { Key = b.Key, Shown = b.BuiltInShown, Fields = [.. b.BuiltInFields] })],
        LabelSize = paper == Labels ? "a4-3x8" : null,
        LabelFields = paper == Labels ? [.. BuiltInLabelFields] : [],
    };
}

/// <summary>Which layout a record prints with: the most specific that matches, the same order as the approval levels.</summary>
public static class PrintLayoutRules
{
    public static PrintLayout Pick(IEnumerable<PrintLayout> layouts, Guid? siteId, string? kindKey, string paper)
    {
        var mine = layouts.Where(l => l.Paper == paper && !l.IsDeleted).ToList();
        bool Kind(PrintLayout l) => kindKey is not null && l.KindKey is not null && string.Equals(l.KindKey, kindKey, StringComparison.OrdinalIgnoreCase);
        Func<PrintLayout, bool>[] tiers =
        [
            l => siteId is not null && l.SiteId == siteId && Kind(l),
            l => siteId is not null && l.SiteId == siteId && l.KindKey is null,
            l => l.SiteId is null && Kind(l),
            l => l.IsDefault && l.SiteId is null && l.KindKey is null,
        ];
        foreach (var tier in tiers)
            if (mine.Where(tier).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault() is { } hit) return hit;
        return PrintPapers.BuiltIn(paper);
    }
}
