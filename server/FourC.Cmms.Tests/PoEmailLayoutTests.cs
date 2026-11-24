using System.Net.Http.Json;
using System.Text.Json;
using FourC.Cmms.Api.Features;
using Microsoft.Extensions.DependencyInjection;
using static FourC.Cmms.Tests.PurchaseRequestTests;

namespace FourC.Cmms.Tests;

/// <summary>The purchase order e-mail follows the order's print layout (0.23.0); label site; a request's work order due.</summary>
public class PoEmailLayoutTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Addr(string who) => $"{who}-{Guid.NewGuid():N}@cmms.test";
    private EmailSenderService Sender => fx.Factory!.Services.GetRequiredService<EmailSenderService>();

    private static async Task<Guid> SupplierAsync(HttpClient c, string email) =>
        Id(await Ok(await c.PostAsJsonAsync("/api/suppliers", new { code = $"S{Guid.NewGuid():N}"[..10], name = "Filters Ltd", email, taxNumber = "TX-77", isContractor = false })));

    /// <summary>An approved order of one line (2 × 100 at 10% off), sent to the supplier; returns the e-mail's HTML.</summary>
    private async Task<string> SendAsync(PurchaseRequestTests.Org o, string paymentTerms = "Net 60", string shippingMethod = "Courier")
    {
        var s = Addr("supplier");
        var po = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/purchase-orders", new
        {
            siteId = o.Site, supplierId = await SupplierAsync(o.Admin, s), shippingCost = 0, paymentTerms, shippingMethod, notes = "Gate 3",
            lines = new[] { new { description = "Filter element", quantity = 2, unitPrice = 100m, discountPercent = 10m } },
        })));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/approve", null));
        await Ok(await o.Admin.PostAsync($"/api/purchase-orders/{po}/send", null));
        await Sender.RunOnceAsync();
        return Assert.Single(fx.Emails.To(s)).Html;
    }

    private static Task<HttpResponseMessage> LayoutAsync(HttpClient c, object blocks, string? header = null, string? terms = null, string? termsAr = null, string? footer = null) =>
        c.PostAsJsonAsync("/api/print-layouts", new
        {
            paper = "purchase-order", name = "Our PO", isDefault = true, paperSize = "A4", showLogo = true, headerText = header, footerText = footer, terms, termsAr,
            blocks, labelFields = Array.Empty<string>(),
        });

    [SkippableFact]
    public async Task Po_email_without_layout_is_unchanged()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, email: true);
        var html = await SendAsync(o);
        foreach (var text in new[] { "Item", "Qty", "Unit price", "Total", "Order date", "Payment terms", "Net 60", "Filter element", "90.00", "180.00" })
            Assert.Contains(text, html);
        Assert.DoesNotContain("Discount", html);
        Assert.DoesNotContain("Courier", html);
        Assert.DoesNotContain("TX-77", html);
    }

    [SkippableFact]
    public async Task Po_email_follows_the_layout()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, email: true);
        var blocks = new object[]
        {
            new { key = "header", shown = true, fields = new[] { "orderDate" } },
            new { key = "supplier", shown = true, fields = new[] { "name", "taxNumber" } },
            new { key = "details", shown = true, fields = new[] { "shippingMethod", "currency" } },
            new { key = "lines", shown = true, fields = new[] { "lineNumber", "description", "quantity", "unitPrice", "lineTotal" } },
            new { key = "totals", shown = true, fields = new[] { "total" } },
            new { key = "notes", shown = true, fields = Array.Empty<string>() },
            new { key = "terms", shown = true, fields = Array.Empty<string>() },
        };
        await Ok(await LayoutAsync(o.Admin, blocks, header: "Ref <b>X</b>", terms: "Pay in 30 days", termsAr: "الدفع خلال 30 يومًا", footer: "Form PO-2"));
        var html = await SendAsync(o);
        Assert.Contains("Ref &lt;b&gt;X&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>X</b>", html);
        Assert.Contains("Pay in 30 days", html);
        Assert.Contains("Form PO-2", html);
        Assert.Contains("Courier", html);
        Assert.Contains("TX-77", html);
        Assert.Contains("Gate 3", html);
        Assert.DoesNotContain("Net 60", html);
        Assert.DoesNotContain("Subtotal", html);
        Assert.DoesNotContain("Discount", html);
        Assert.DoesNotContain("100.00", html); // discount hidden: the unit price is after discount
        Assert.Contains("90.00", html);
        Assert.Contains("180.00", html);

        await TestSettings.SetSettingsAsync(o.Admin, s => s["defaultLanguage"] = "ar");
        var ar = await SendAsync(o);
        Assert.Contains("الدفع خلال 30 يومًا", ar);
        Assert.DoesNotContain("Pay in 30 days", ar);
    }

    [SkippableFact]
    public async Task Po_email_with_minimal_layout_keeps_fixed_columns()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx, email: true);
        var blocks = new object[]
        {
            new { key = "header", shown = false, fields = Array.Empty<string>() },
            new { key = "supplier", shown = false, fields = Array.Empty<string>() },
            new { key = "details", shown = false, fields = Array.Empty<string>() },
            new { key = "lines", shown = true, fields = new[] { "description", "lineTotal" } },
            new { key = "totals", shown = true, fields = new[] { "total" } },
        };
        await Ok(await LayoutAsync(o.Admin, blocks));
        var html = await SendAsync(o);
        Assert.Contains("Filter element", html);
        Assert.Contains("180.00", html);
        Assert.DoesNotContain("Unit price", html);
        Assert.DoesNotContain("Qty", html);
        Assert.DoesNotContain("Order date", html);
    }

    [SkippableFact]
    public async Task Labels_carry_their_site()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var asset = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/assets", new { name = "Chiller", siteId = o.Site, status = "Active", criticality = "High", warrantyType = "None" })));
        var a = (await Ok(await o.Admin.PostAsJsonAsync("/api/labels", new { type = "asset", ids = new[] { asset } }))).EnumerateArray().Single();
        Assert.Equal("Cairo Plant", a.GetProperty("site").GetString());
        var p = (await Ok(await o.Admin.PostAsJsonAsync("/api/labels", new { type = "part", ids = new[] { o.Part } }))).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, p.GetProperty("site").ValueKind);
    }

    [SkippableFact]
    public async Task Request_detail_shows_its_work_orders_due()
    {
        Skip.IfNot(fx.Enabled, "Set CMMS_TEST_SQL to run integration tests.");
        var o = await OrgAsync(fx);
        var request = Id(await Ok(await o.Admin.PostAsJsonAsync("/api/work-requests", new { title = "Warm room", siteId = o.Site, priority = "Low", type = "Corrective" })));
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var approved = await Ok(await o.Admin.PostAsJsonAsync($"/api/work-requests/{request}/approve", new { dueAt = due }));
        var wo = await Ok(await o.Admin.GetAsync($"/api/work-orders/{approved.GetProperty("workOrderId").GetGuid()}"));
        var detail = await Ok(await o.Admin.GetAsync($"/api/work-requests/{request}"));
        var linked = detail.GetProperty("workOrder");
        Assert.Equal(wo.GetProperty("dueAt").GetDateTimeOffset(), linked.GetProperty("dueAt").GetDateTimeOffset());
        Assert.True(linked.TryGetProperty("dueDate", out _));
    }
}
