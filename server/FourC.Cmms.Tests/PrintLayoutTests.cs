using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>Print layouts: saving, checking, and which one a record prints with (0.23.0).</summary>
public class PrintLayoutTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private static object Layout(string paper = "job-card", string name = "Card", Guid? siteId = null, string? kindKey = null, bool isDefault = false,
        string? paperSize = "A4", object? blocks = null, string? header = null, string? headerAr = null, string? footer = null, string? footerAr = null,
        string? terms = null, string? termsAr = null, string? labelSize = null, string[]? labelFields = null) => new
        {
            paper, name, isDefault, siteId, kindKey, paperSize, showLogo = true, headerText = header, headerTextAr = headerAr, footerText = footer, footerTextAr = footerAr,
            terms, termsAr, blocks = blocks ?? Array.Empty<object>(), labelSize, labelFields = labelFields ?? Array.Empty<string>(),
        };

    private static Task<HttpResponseMessage> Save(HttpClient c, object body) => c.PostAsJsonAsync("/api/print-layouts", body);

    private static async Task<Guid> SaveOk(HttpClient c, object body) => Id(await Ok(await Save(c, body)));

    private static async Task Refused(HttpClient c, object body, string message, Guid? id = null)
    {
        var r = id is null ? await Save(c, body) : await c.PutAsJsonAsync($"/api/print-layouts/{id}", body);
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{(int)r.StatusCode}: {text}");
        Assert.Contains(message, text);
    }

    private static async Task<Guid> ForAsync(HttpClient c, string paper, Guid id, string? labelKind = null) =>
        Id(await Ok(await c.GetAsync($"/api/print-layouts/for?paper={paper}&id={id}{(labelKind is null ? "" : $"&labelKind={labelKind}")}")));

    [SkippableFact]
    public async Task Layout_round_trip()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var blocks = new object[]
        {
            new { key = "header", shown = true, fields = new[] { "orgName", "title" } },
            new { key = "details", shown = true, fields = new[] { "status", "asset" } },
            new { key = "parts", shown = false, fields = Array.Empty<string>() },
        };
        var id = await SaveOk(o.Admin, Layout(name: "Short card", siteId: o.Site, kindKey: "Corrective", paperSize: "A5", blocks: blocks,
            header: "Form QF-12", headerAr: "نموذج QF-12", footer: "Rev 3", footerAr: "إصدار 3", terms: "Wear gloves", termsAr: "ارتدِ القفازات"));
        var list = (await Ok(await o.Admin.GetAsync("/api/print-layouts?paper=job-card"))).EnumerateArray().ToArray();
        var l = Assert.Single(list);
        Assert.Equal(id, Id(l));
        Assert.Equal("Short card", l.GetProperty("name").GetString());
        Assert.Equal(o.Site, l.GetProperty("siteId").GetGuid());
        Assert.Equal("Cairo Plant", l.GetProperty("siteName").GetString());
        Assert.Equal("Corrective", l.GetProperty("kindKey").GetString());
        Assert.Equal("A5", l.GetProperty("paperSize").GetString());
        Assert.Equal("نموذج QF-12", l.GetProperty("headerTextAr").GetString());
        Assert.Equal("ارتدِ القفازات", l.GetProperty("termsAr").GetString());
        var parts = l.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("key").GetString() == "parts");
        Assert.False(parts.GetProperty("shown").GetBoolean());
        Assert.Equal(["status", "asset"], l.GetProperty("blocks")[1].GetProperty("fields").EnumerateArray().Select(x => x.GetString()));

        var one = await Ok(await o.Admin.GetAsync($"/api/print-layouts/{id}"));
        Assert.Equal("Rev 3", one.GetProperty("footerText").GetString());
        await Ok(await o.Admin.PutAsJsonAsync($"/api/print-layouts/{id}", Layout(name: "Card A5", siteId: o.Site, kindKey: "Corrective", paperSize: "A5", blocks: blocks)));
        Assert.Equal("Card A5", (await Ok(await o.Admin.GetAsync($"/api/print-layouts/{id}"))).GetProperty("name").GetString());
        Assert.Empty((await Ok(await o.Admin.GetAsync("/api/print-layouts?paper=purchase-order"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await o.Admin.DeleteAsync($"/api/print-layouts/{id}")).StatusCode);
        Assert.Empty((await Ok(await o.Admin.GetAsync("/api/print-layouts?paper=job-card"))).EnumerateArray());

        var built = await Ok(await o.Admin.GetAsync("/api/print-layouts/built-in?paper=purchase-order"));
        Assert.Equal(Guid.Empty, Id(built));
        Assert.False(built.GetProperty("showLogo").GetBoolean());
    }

    [SkippableFact]
    public async Task For_endpoint_applies_precedence()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var wo = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Leak", siteId = o.Site, type = "Corrective", priority = "Medium", tasks = Array.Empty<string>() })));
        var both = await SaveOk(o.Admin, Layout(name: "Both", siteId: o.Site, kindKey: "Corrective"));
        var site = await SaveOk(o.Admin, Layout(name: "Site", siteId: o.Site));
        var kind = await SaveOk(o.Admin, Layout(name: "Kind", kindKey: "Corrective"));
        var def = await SaveOk(o.Admin, Layout(name: "Default", isDefault: true));
        await SaveOk(o.Admin, Layout(name: "By hand"));
        await SaveOk(o.Admin, Layout(name: "Inspection", siteId: o.Site, kindKey: "Inspection"));
        foreach (var expected in new[] { both, site, kind, def })
        {
            Assert.Equal(expected, await ForAsync(o.Admin, "job-card", wo));
            await Ok(await o.Admin.DeleteAsync($"/api/print-layouts/{expected}"));
        }
        Assert.Equal(Guid.Empty, await ForAsync(o.Admin, "job-card", wo));

        // Purchase orders by order type, requests by request type.
        var types = (await Ok(await o.Admin.GetAsync("/api/order-types"))).EnumerateArray().ToList();
        var services = Id(types.Single(t => t.GetProperty("code").GetString() == "SERVICES"));
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = o.Supplier, shippingCost = 0, orderTypeId = services,
            lines = new[] { new { description = "Crane hire", quantity = 1, unitPrice = 100m, discountPercent = 0 } },
        })));
        var poLayout = await SaveOk(o.Admin, Layout(paper: "purchase-order", name: "Services PO", kindKey: services.ToString()));
        Assert.Equal(poLayout, await ForAsync(o.Admin, "purchase-order", po));

        var rtype = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/request-types", new { code = "AC", name = "AC", isActive = true, autoApprove = false, recipientIds = Array.Empty<Guid>() })));
        var request = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Warm room", siteId = o.Site, priority = "Low", type = "Corrective", requestTypeId = rtype })));
        var rLayout = await SaveOk(o.Admin, Layout(paper: "request", name: "AC receipt", kindKey: rtype.ToString()));
        Assert.Equal(rLayout, await ForAsync(o.Admin, "request", request));

        var asset = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Chiller", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var labelLayout = await SaveOk(o.Admin, Layout(paper: "labels", name: "Asset stickers", kindKey: "asset", paperSize: null, labelSize: "a4-2x7", labelFields: ["number", "name", "site"]));
        Assert.Equal(labelLayout, await ForAsync(o.Admin, "labels", asset, "asset"));
        Assert.Equal(Guid.Empty, await ForAsync(o.Admin, "labels", o.Part, "part"));
    }

    [SkippableFact]
    public async Task Layout_validation()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var a = o.Admin;
        await Refused(a, Layout(paper: "invoice"), "Choose a paper from the list.");
        await Refused(a, Layout(name: " "), "Give the layout a name.");
        await Refused(a, Layout(name: new string('n', 61)), "Keep the name to 60 characters.");
        await SaveOk(a, Layout(name: "Card"));
        await Refused(a, Layout(name: "card"), "Another layout of this paper has this name.");
        await Refused(a, Layout(name: "Default", isDefault: true, siteId: o.Site), "The default layout applies when no other does, so it can't have a site or type.");
        await SaveOk(a, Layout(name: "Plant", siteId: o.Site, kindKey: "Corrective"));
        await Refused(a, Layout(name: "Plant 2", siteId: o.Site, kindKey: "corrective"), "Another layout already has this site and type.");
        await SaveOk(a, Layout(name: "Second by hand"));
        var other = await OrgAsync(fx);
        await Refused(a, Layout(name: "Elsewhere", siteId: other.Site), "Choose a site you can see.");
        await Refused(a, Layout(name: "Bad type", kindKey: "Repair"), "Choose a type from the list.");
        await Refused(a, Layout(name: "Bad PO type", paper: "purchase-order", kindKey: Guid.NewGuid().ToString()), "Choose a type from the list.");
        await Refused(a, Layout(name: "A3", paperSize: "A3"), "Choose a paper size from the list.");
        await Refused(a, Layout(name: "Big", paper: "labels", paperSize: null, labelSize: "big"), "Choose a label size from the list.");
        await Refused(a, Layout(name: "Barcode", paper: "labels", paperSize: null, labelSize: "a4-3x8", labelFields: ["barcode"]), "Choose fields from the list.");
        await Refused(a, Layout(name: "Blocks", blocks: new[] { new { key = "map", shown = true, fields = Array.Empty<string>() } }), "Choose blocks from the list.");
        await Refused(a, Layout(name: "Fields", blocks: new[] { new { key = "details", shown = true, fields = new[] { "colour" } } }), "Choose fields from the list.");
        await Refused(a, Layout(name: "No description", paper: "purchase-order", blocks: new[] { new { key = "lines", shown = true, fields = new[] { "quantity", "lineTotal" } } }),
            "This field always prints.");
        await Refused(a, Layout(name: "Custom", blocks: new[] { new { key = "asset-fields", shown = true, fields = new[] { "power" } } }), "Choose an asset field from the list.");
        await Ok(await a.PostAsJsonAsync("/api/custom-fields", new { entityType = "Asset", key = "power", label = "Power (kW)", dataType = "Number", isRequired = false, sortOrder = 1, showInList = false }));
        await SaveOk(a, Layout(name: "Custom", blocks: new[] { new { key = "asset-fields", shown = true, fields = new[] { "power" } } }));
        await Refused(a, Layout(name: "Long header", header: new string('h', 501)), "Keep the header to 500 characters.");
        await Refused(a, Layout(name: "Long footer", footerAr: new string('ف', 501)), "Keep the footer to 500 characters.");
        await Refused(a, Layout(name: "Long terms", terms: new string('t', 4001)), "Keep the terms to 4,000 characters.");

        // 20 layouts a paper.
        for (var i = 0; i < 20; i++) await SaveOk(a, Layout(paper: "request", name: $"R{i}"));
        await Refused(a, Layout(paper: "request", name: "R20"), "A paper can have up to 20 layouts.");

        // A new default takes over from the old one.
        var d1 = await SaveOk(a, Layout(name: "D1", isDefault: true));
        var d2 = await SaveOk(a, Layout(name: "D2", isDefault: true));
        Assert.False((await Ok(await a.GetAsync($"/api/print-layouts/{d1}"))).GetProperty("isDefault").GetBoolean());
        Assert.True((await Ok(await a.GetAsync($"/api/print-layouts/{d2}"))).GetProperty("isDefault").GetBoolean());
    }

    [SkippableFact]
    public async Task Texts_at_their_limits_save_and_one_more_is_refused()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var id = await SaveOk(o.Admin, Layout(name: "Full", header: new string('h', 500), headerAr: new string('ه', 500), footer: new string('f', 500),
            footerAr: new string('ف', 500), terms: new string('t', 4000), termsAr: new string('ش', 4000)));
        Assert.Equal(4000, (await Ok(await o.Admin.GetAsync($"/api/print-layouts/{id}"))).GetProperty("termsAr").GetString()!.Length);
        await Refused(o.Admin, Layout(name: "Full", terms: new string('t', 4001)), "Keep the terms to 4,000 characters.", id);
        await Refused(o.Admin, Layout(name: "Full", headerAr: new string('ه', 501)), "Keep the header to 500 characters.", id);
    }

    [SkippableFact]
    public async Task Layouts_are_per_organization()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var other = await OrgAsync(fx);
        var theirs = await SaveOk(other.Admin, Layout(name: "Theirs"));
        Assert.Equal(HttpStatusCode.NotFound, (await o.Admin.GetAsync($"/api/print-layouts/{theirs}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Admin.PutAsJsonAsync($"/api/print-layouts/{theirs}", Layout(name: "Mine now"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Admin.DeleteAsync($"/api/print-layouts/{theirs}")).StatusCode);
        var theirWo = Id(await Ok(await other.Admin.PostAsJsonAsync("/api/work-orders", new { title = "Leak", siteId = other.Site, type = "Corrective", priority = "Medium", tasks = Array.Empty<string>() })));
        Assert.Equal(HttpStatusCode.NotFound, (await o.Admin.GetAsync($"/api/print-layouts/for?paper=job-card&id={theirWo}")).StatusCode);
        Assert.Empty((await Ok(await o.Admin.GetAsync("/api/print-layouts?paper=job-card"))).EnumerateArray());
    }

    [SkippableFact]
    public async Task Writing_needs_manage_settings()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var (tech, _, _) = await UserAsync(fx, o.Admin, "Technician");
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(tech, Layout(name: "Mine"))).StatusCode);
        await Ok(await tech.GetAsync("/api/print-layouts?paper=job-card"));
        await Ok(await tech.GetAsync("/api/print-layouts/built-in?paper=job-card"));
    }
}
