using System.Text;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Api.Features;

/// <summary>An e-mail's subject and bodies (HTML and plain text).</summary>
public sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>Facts about a work order that e-mails mention.</summary>
/// <summary>A word in both languages, read in the language of the e-mail being written (0.21.0: the organization's names for priorities).</summary>
public sealed record LocalWords(string En, string Ar)
{
    public override string ToString() => Translator.CurrentLanguage == "ar" ? Ar : En;
}

/// <remarks>0.28.0: the title and the place in both languages too (each recipient reads their own).</remarks>
public sealed record WorkOrderFacts(string Number, LocalWords Title, LocalWords Priority, DateOnly? DueDate, LocalWords? Place, string? Description, string? CompletionNotes);

/// <summary>Facts about a purchase order for internal e-mails.</summary>
public sealed record PoFacts(string Number, string? Supplier, string Total, string? RequestedBy);
/// <summary>A purchase request in an e-mail (0.16.0).</summary>
public sealed record PrFacts(string Number, string? Site, string? RequestedBy, string Lines, string? Estimate);

/// <summary>A contract that needs attention (0.17.0).</summary>
public sealed record ContractFacts(string Number, string Title, string Company, string? Site, DateOnly End, DateOnly Deadline, bool AutoRenew);

/// <summary>A purchase order as the supplier sees it.</summary>
public sealed record PoDocument(string Number, string OrderDate, string? ExpectedDate, string? DeliverTo, string? PaymentTerms, string? QuoteRef, string? Contact,
    IReadOnlyList<PoDocumentLine> Lines, IReadOnlyList<(string Label, string Value)> Totals);

public sealed record PoDocumentLine(int Number, string Description, string Quantity, string UnitPrice, string Total);

/// <summary>
/// A purchase order e-mail shaped by the order's print layout (0.23.0): the facts, supplier lines, line columns and totals the
/// layout shows, and its texts already chosen for the e-mail's language. Labels are English, translated when rendered.
/// </summary>
public sealed record PoEmailLayout(IReadOnlyList<(string Label, string? Value)> Facts, IReadOnlyList<string> SupplierLines, IReadOnlyList<string> Columns,
    IReadOnlyList<PoEmailLine> Lines, IReadOnlyList<(string Label, string Value)> Totals, string? Notes, string? Header, string? Footer, string? Terms);

/// <summary>A line of a laid-out order e-mail, every column as text (unit price is after discount when the discount column is hidden).</summary>
public sealed record PoEmailLine(int Number, string? PartNumber, string Description, string? SupplierPartNumber, string Quantity, string Unit,
    string UnitPrice, string Discount, string Total);

/// <summary>Facts about a work request that e-mails mention.</summary>
public sealed record RequestFacts(string Number, string Title, LocalWords Priority, LocalWords? Place);

/// <summary>
/// The text of every notification. One short layout: heading, a few facts, an optional paragraph, a button to the
/// record (when the server knows its own address) and a footer naming the organization. All user text is encoded.
/// </summary>
public static class EmailTemplates
{
    /// <summary>A background check or import finished (0.26.0).</summary>
    public static EmailContent ImportFinished(string org, string fileName, bool import, bool ok, IReadOnlyList<(string Label, string? Value)> lines, string? link) => Layout(org,
        ok ? (import ? $"Import finished: {fileName}" : $"Check finished: {fileName}") : (import ? $"Import failed: {fileName}" : $"Check failed: {fileName}"),
        ok ? (import ? "Your import has finished" : "Your check has finished") : (import ? "Your import didn't finish" : "Your check didn't finish"),
        lines, null, link, "Open the import");

    public static EmailContent Assigned(string org, WorkOrderFacts w, string? link, bool pm) => Layout(org,
        pm ? $"PM work {w.Number}: {w.Title}" : $"Work order {w.Number}: {w.Title}",
        pm ? "Preventive maintenance has been assigned to you" : "A work order has been assigned to you",
        WorkOrderLines(w), w.Description, link, "Open the work order");

    public static EmailContent WorkOrderCompleted(string org, WorkOrderFacts w, string? link) => Layout(org,
        $"Completed: {w.Number} {w.Title}", "Work you raised has been completed",
        WorkOrderLines(w), w.CompletionNotes is { } n ? $"Work done: {n}" : null, link, "Open the work order");

    public static EmailContent RequestSubmitted(string org, RequestFacts r, string? requester, string? link) => Layout(org,
        $"New request {r.Number}: {r.Title}", "A new work request is waiting for review",
        [("Request", $"{r.Number} · {r.Title}"), ("Priority", r.Priority.ToString()), ("Where", r.Place?.ToString()), ("From", requester)], null, link, "Review the request");

    public static EmailContent RequestApproved(string org, RequestFacts r, string workOrderNumber, string? link) => Layout(org,
        $"Your request {r.Number} was approved", "Your work request was approved",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place?.ToString())],
        "The work has been planned. You'll hear from us again when it's done.", link, "View your request");

    public static EmailContent RequestRejected(string org, RequestFacts r, string reason, string? link) => Layout(org,
        $"Your request {r.Number} was not approved", "Your work request was not approved",
        [("Request", $"{r.Number} · {r.Title}"), ("Where", r.Place?.ToString())], $"Reason: {reason}", link, "View your request");

    public static EmailContent RequestCompleted(string org, RequestFacts r, string workOrderNumber, string? notes, string? link, bool askAnswer = false) => Layout(org,
        $"Done: your request {r.Number}", askAnswer ? "The work you asked for has been done. Please accept or refuse it." : "The work you asked for has been done",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place?.ToString()), ("Work done", notes)],
        null, link, askAnswer ? "Accept or refuse the work" : "View your request");

    public static EmailContent WorkOrderOverdue(string org, WorkOrderFacts w, string? link) => Layout(org,
        $"Overdue: work order {w.Number}", "This work is past its due time",
        [("Work order", $"{w.Number} · {w.Title}"), ("Priority", w.Priority.ToString()), ("Due", w.DueDate is { } d ? Translator.Date(d) : null), ("Where", w.Place?.ToString())],
        null, link, "Open the work order");

    public static EmailContent WorkOrderCancelled(string org, WorkOrderFacts w, LocalWords? reason, string? note, string? link, bool forRequester = false) => Layout(org,
        $"Cancelled: work order {w.Number}", forRequester ? "The work you asked for was cancelled" : "This work was cancelled",
        [("Work order", $"{w.Number} · {w.Title}"), ("Where", w.Place?.ToString()), ("Reason", reason?.ToString()), ("Note", note)], null, link,
        forRequester ? "View your request" : "Open the work order");

    public static EmailContent RequestRefused(string org, RequestFacts r, string workOrderNumber, string? byName, string? reason, string? link) => Layout(org,
        $"Refused: work order {workOrderNumber}", "The requester refused the finished work. The work order was reopened.",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place?.ToString()), ("Refused by", byName), ("Reason", reason)],
        null, link, "Open the work order");

    public static EmailContent RequestComment(string org, RequestFacts r, string? author, string body, string? link) => Layout(org,
        $"New comment on request {r.Number}", "A new comment on the request",
        [("Request", $"{r.Number} · {r.Title}"), ("Where", r.Place?.ToString()), ("From", author), ("Comment", body)], null, link, "View the request");

    public static EmailContent PoSubmitted(string org, PoFacts po, string? link) => Layout(org,
        $"Approve purchase order {po.Number} ({po.Total})", "A purchase order is waiting for your approval",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total), ("Requested by", po.RequestedBy)], null, link, "Review the order");

    public static EmailContent PoApprovalWaiting(string org, PoFacts po, string level, string? link) => Layout(org,
        $"Sign purchase order {po.Number} ({po.Total})", "A purchase order is waiting for your signature",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total), ("Requested by", po.RequestedBy), ("Level", level)], null, link, "Review the order");

    public static EmailContent PoApproved(string org, PoFacts po, string? link) => Layout(org,
        $"Purchase order {po.Number} approved", "Your purchase order was approved",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total)], "You can now send it to the supplier.", link, "Open the order");

    public static EmailContent PoRejected(string org, PoFacts po, string reason, string? link) => Layout(org,
        $"Purchase order {po.Number} returned", "Your purchase order was returned for changes",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total)], $"Reason: {reason}", link, "Open the order");

    // ---------------- Purchase requests (0.16.0) ----------------

    private static IEnumerable<(string, string?)> PrLines(PrFacts r) =>
        [("Request", r.Number), ("Site", r.Site), ("Requested by", r.RequestedBy), ("Items", r.Lines), ("Estimate", r.Estimate)];

    /// <summary>The last day to stop an automatic renewal, or the end of a contract that doesn't renew, is near (0.17.0).</summary>
    public static EmailContent ContractAlert(string org, ContractFacts c, string? link) => Layout(org,
        c.AutoRenew ? $"Contract {c.Number} · {c.Title}: last day to stop the renewal is {Translator.Date(c.Deadline)}"
                    : $"Contract {c.Number} · {c.Title} ends on {Translator.Date(c.End)}",
        c.AutoRenew ? "This contract renews itself unless it is stopped" : "This contract is about to end",
        [("Contract", $"{c.Number} · {c.Title}"), ("Company", c.Company), ("Site", c.Site), ("Ends", Translator.Date(c.End)),
         ("Last day to stop the renewal", c.AutoRenew ? Translator.Date(c.Deadline) : null)],
        null, link, "Open the contract");

    public static EmailContent PrSubmitted(string org, PrFacts r, string? link) => Layout(org,
        $"Approve purchase request {r.Number}", "A purchase request is waiting for approval", PrLines(r), null, link, "Review the request");

    public static EmailContent PrApproved(string org, PrFacts r, string? link) => Layout(org,
        $"Purchase request {r.Number} approved", "Your purchase request was approved", PrLines(r), "Purchasing will order it.", link, "Open the request");

    public static EmailContent PrRejected(string org, PrFacts r, string reason, string? link) => Layout(org,
        $"Purchase request {r.Number} returned", "Your purchase request was returned", PrLines(r), $"Reason: {reason}", link, "Open the request");

    public static EmailContent PrOrdered(string org, PrFacts r, IReadOnlyList<string> orders, string? link) => Layout(org,
        $"Purchase request {r.Number} ordered", "Items you asked for were ordered", [.. PrLines(r), ("Orders", string.Join(", ", orders))], null, link, "Open the request");

    public static EmailContent PrReceived(string org, PrFacts r, IReadOnlyList<string> lines, string? link) => Layout(org,
        $"Items arrived for purchase request {r.Number}", "Items you asked for have arrived", PrLines(r), string.Join("\n", lines), link, "Open the request");

    public static EmailContent PartsReceived(string org, WorkOrderFacts w, string poNumber, IReadOnlyList<string> lines, string? link) => Layout(org,
        $"Parts arrived for {w.Number} {w.Title}", "Parts ordered for your job have arrived",
        [.. WorkOrderLines(w), ("Order", poNumber)], string.Join("\n", lines), link, "Open the work order");

    /// <summary>The order itself, for a supplier with no account: every line and the totals in the e-mail, no link.</summary>
    public static EmailContent PurchaseOrder(string org, PoDocument po, PoEmailLayout? layout = null)
    {
        if (layout is not null) return LaidOutPurchaseOrder(org, po.Number, layout);
        var table = new StringBuilder("<table role=\"presentation\" style=\"width:100%;border-collapse:collapse;margin:8px 0 16px;font-size:14px\">");
        var (start, end) = Translator.CurrentLanguage == "ar" ? ("right", "left") : ("left", "right");
        table.Append($"<tr style=\"background:#e9edf1\"><th align=\"{start}\" style=\"padding:6px\">#</th><th align=\"{start}\" style=\"padding:6px\">{EmailText.Html(Translator.T("Item"))}</th>"
            + $"<th align=\"{end}\" style=\"padding:6px\">{EmailText.Html(Translator.T("Qty"))}</th><th align=\"{end}\" style=\"padding:6px\">{EmailText.Html(Translator.T("Unit price"))}</th><th align=\"{end}\" style=\"padding:6px\">{EmailText.Html(Translator.T("Total"))}</th></tr>");
        var text = new StringBuilder();
        foreach (var l in po.Lines)
        {
            table.Append($"<tr><td style=\"padding:6px;border-top:1px solid #d3dae1\">{l.Number}</td><td style=\"padding:6px;border-top:1px solid #d3dae1\">{EmailText.Html(l.Description)}</td>"
                + $"<td align=\"{end}\" style=\"padding:6px;border-top:1px solid #d3dae1\">{EmailText.Html(l.Quantity)}</td><td align=\"{end}\" style=\"padding:6px;border-top:1px solid #d3dae1\">{EmailText.Html(l.UnitPrice)}</td>"
                + $"<td align=\"{end}\" style=\"padding:6px;border-top:1px solid #d3dae1\">{EmailText.Html(l.Total)}</td></tr>");
            text.AppendLine($"{l.Number}. {l.Description} — {l.Quantity} × {l.UnitPrice} = {l.Total}");
        }
        foreach (var (label, value) in po.Totals)
        {
            table.Append($"<tr><td colspan=\"4\" align=\"{end}\" style=\"padding:6px\">{EmailText.Html(label)}</td><td align=\"{end}\" style=\"padding:6px\">{EmailText.Html(value)}</td></tr>");
            text.AppendLine($"{label}: {value}");
        }
        table.Append("</table>");
        return Layout(org, $"Purchase order {po.Number} from {org}", $"Purchase order {po.Number}",
            [("Order date", po.OrderDate), ("Deliver by", po.ExpectedDate), ("Deliver to", po.DeliverTo), ("Payment terms", po.PaymentTerms),
             ("Your quote", po.QuoteRef), ("Contact", po.Contact)],
            "Please confirm the order and delivery date by replying to this e-mail.", null, "", table.ToString(), text.ToString());
    }

    /// <summary>The column headings of a laid-out order, by column key.</summary>
    private static readonly Dictionary<string, string> PoColumnTitles = new()
    {
        ["lineNumber"] = "#", ["partNumber"] = "Part no.", ["description"] = "Item", ["supplierPartNumber"] = "Your part no.", ["quantity"] = "Qty",
        ["unit"] = "Unit", ["unitPrice"] = "Unit price", ["discount"] = "Discount", ["lineTotal"] = "Total",
    };

    private static string Lines(string text) => EmailText.Html(text).Replace("\r\n", "<br>").Replace("\n", "<br>");

    private static EmailContent LaidOutPurchaseOrder(string org, string number, PoEmailLayout l)
    {
        var (start, end) = Translator.CurrentLanguage == "ar" ? ("right", "left") : ("left", "right");
        static bool Numeric(string c) => c is "quantity" or "unitPrice" or "discount" or "lineTotal";
        string Cell(PoEmailLine x, string c) => c switch
        {
            "lineNumber" => x.Number.ToString(), "partNumber" => x.PartNumber ?? "", "description" => x.Description, "supplierPartNumber" => x.SupplierPartNumber ?? "",
            "quantity" => x.Quantity, "unit" => x.Unit, "unitPrice" => x.UnitPrice, "discount" => x.Discount, _ => x.Total,
        };
        var html = new StringBuilder();
        var text = new StringBuilder();
        if (l.Header is { Length: > 0 } header) { html.Append($"<p style=\"margin:0 0 16px\">{Lines(header)}</p>"); text.AppendLine(header).AppendLine(); }
        if (l.SupplierLines.Count > 0)
        {
            html.Append($"<p style=\"margin:0 0 16px\"><strong>{EmailText.Html(Translator.T("Supplier"))}</strong><br>{string.Join("<br>", l.SupplierLines.Select(EmailText.Html))}</p>");
            text.AppendLine(Translator.T("Supplier")).AppendLine(string.Join("\n", l.SupplierLines)).AppendLine();
        }
        html.Append("<table role=\"presentation\" style=\"width:100%;border-collapse:collapse;margin:8px 0 16px;font-size:14px\"><tr style=\"background:#e9edf1\">");
        foreach (var c in l.Columns)
            html.Append($"<th align=\"{(Numeric(c) ? end : start)}\" style=\"padding:6px\">{EmailText.Html(Translator.T(PoColumnTitles[c]))}</th>");
        html.Append("</tr>");
        foreach (var x in l.Lines)
        {
            html.Append("<tr>");
            foreach (var c in l.Columns)
                html.Append($"<td align=\"{(Numeric(c) ? end : start)}\" style=\"padding:6px;border-top:1px solid #d3dae1\">{EmailText.Html(Cell(x, c))}</td>");
            html.Append("</tr>");
            text.AppendLine(string.Join(" · ", l.Columns.Select(c => Cell(x, c)).Where(v => v.Length > 0)));
        }
        foreach (var (label, value) in l.Totals)
        {
            html.Append($"<tr><td colspan=\"{Math.Max(1, l.Columns.Count - 1)}\" align=\"{end}\" style=\"padding:6px\">{EmailText.Html(label)}</td><td align=\"{end}\" style=\"padding:6px\">{EmailText.Html(value)}</td></tr>");
            text.AppendLine($"{label}: {value}");
        }
        html.Append("</table>");
        if (l.Notes is { Length: > 0 } notes)
        {
            html.Append($"<p style=\"margin:0 0 16px\"><strong>{EmailText.Html(Translator.T("Notes"))}</strong><br>{Lines(notes)}</p>");
            text.AppendLine().AppendLine($"{Translator.T("Notes")}: {notes}");
        }
        if (l.Terms is { Length: > 0 } terms)
        {
            html.Append($"<p style=\"margin:0 0 16px\"><strong>{EmailText.Html(Translator.T("Terms"))}</strong><br>{Lines(terms)}</p>");
            text.AppendLine().AppendLine(Translator.T("Terms")).AppendLine(terms);
        }
        if (l.Footer is { Length: > 0 } footer) { html.Append($"<p style=\"margin:16px 0 0;font-size:12px;color:#56636f\">{Lines(footer)}</p>"); text.AppendLine().AppendLine(footer); }
        return Layout(org, $"Purchase order {number} from {org}", $"Purchase order {number}", l.Facts,
            "Please confirm the order and delivery date by replying to this e-mail.", null, "", html.ToString(), text.ToString());
    }

    private static (string, string?)[] WorkOrderLines(WorkOrderFacts w) =>
        [("Work order", $"{w.Number} · {w.Title}"), ("Priority", w.Priority.ToString()), ("Due", w.DueDate is { } d ? Translator.Date(d) : null), ("Where", w.Place?.ToString())];

    /// <summary>Builds both bodies. Facts with no value are left out.</summary>
    public static EmailContent Layout(string org, string subject, string heading, IEnumerable<(string Label, string? Value)> facts, string? paragraph,
        string? link, string linkText, string? extraHtml = null, string? extraText = null)
    {
        // In the language set for this recipient (EmailOutbox): fixed words translated, people's own text left as typed.
        subject = Translator.T(subject); heading = Translator.T(heading); linkText = Translator.T(linkText);
        paragraph = paragraph is null ? null : Translator.T(paragraph);
        var rtl = Translator.CurrentLanguage == "ar";
        var rows = facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => (Label: Translator.T(f.Label), Value: TranslateFixed(f.Label, f.Value))).ToList();
        var footer = $"{org} · {Translator.T("Change your e-mail settings in CMX.")}";
        var h = new StringBuilder();
        h.Append(rtl
            ? "<!doctype html><html dir=\"rtl\" lang=\"ar\"><body dir=\"rtl\" style=\"margin:0;padding:24px;background:#f4f6f8;font-family:Segoe UI,Tahoma,Arial,sans-serif;color:#17212b;text-align:right\">"
            : "<!doctype html><html><body style=\"margin:0;padding:24px;background:#f4f6f8;font-family:Segoe UI,Arial,sans-serif;color:#17212b\">");
        h.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;margin:0 auto;background:#fff;border-radius:8px;border:1px solid #d3dae1\"><tr><td style=\"padding:24px\">");
        h.Append($"<h1 style=\"font-size:20px;margin:0 0 16px\">{EmailText.Html(heading)}</h1>");
        if (rows.Count > 0)
        {
            h.Append("<table role=\"presentation\" style=\"border-collapse:collapse;margin-bottom:16px\">");
            foreach (var (label, value) in rows)
                h.Append($"<tr><td style=\"padding:{(rtl ? "4px 0 4px 16px" : "4px 16px 4px 0")};color:#56636f;vertical-align:top\">{EmailText.Html(label)}</td><td style=\"padding:4px 0\">{EmailText.Html(value)}</td></tr>");
            h.Append("</table>");
        }
        if (paragraph is { Length: > 0 }) h.Append($"<p style=\"margin:0 0 16px;white-space:pre-wrap\">{EmailText.Html(paragraph)}</p>");
        if (extraHtml is not null) h.Append(extraHtml);
        if (link is not null)
            h.Append($"<p style=\"margin:8px 0 0\"><a href=\"{EmailText.Html(link)}\" style=\"display:inline-block;background:#0b6b88;color:#fff;text-decoration:none;padding:10px 16px;border-radius:6px\">{EmailText.Html(linkText)}</a></p>");
        h.Append($"</td></tr></table><p style=\"max-width:600px;margin:12px auto 0;font-size:12px;color:#56636f\">{EmailText.Html(footer)}</p></body></html>");

        var t = new StringBuilder();
        t.AppendLine(heading).AppendLine();
        foreach (var (label, value) in rows) t.AppendLine($"{label}: {value}");
        if (paragraph is { Length: > 0 }) t.AppendLine().AppendLine(paragraph);
        if (extraText is not null) t.AppendLine().AppendLine(extraText);
        if (link is not null) t.AppendLine().AppendLine($"{linkText}: {link}");
        t.AppendLine().AppendLine(footer);
        return new EmailContent(EmailText.OneLine(subject), h.ToString(), t.ToString());
    }

    /// <summary>Values are shown as given: a priority already reads in the e-mail's language (<see cref="LocalWords"/>).</summary>
    private static string? TranslateFixed(string label, string? value) => value;
}
