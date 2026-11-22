using System.Text;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Api.Features;

/// <summary>An e-mail's subject and bodies (HTML and plain text).</summary>
public sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>Facts about a work order that e-mails mention.</summary>
public sealed record WorkOrderFacts(string Number, string Title, string Priority, DateOnly? DueDate, string? Place, string? Description, string? CompletionNotes);

/// <summary>Facts about a purchase order for internal e-mails.</summary>
public sealed record PoFacts(string Number, string? Supplier, string Total, string? RequestedBy);

/// <summary>A purchase order as the supplier sees it.</summary>
public sealed record PoDocument(string Number, string OrderDate, string? ExpectedDate, string? DeliverTo, string? PaymentTerms, string? QuoteRef, string? Contact,
    IReadOnlyList<PoDocumentLine> Lines, IReadOnlyList<(string Label, string Value)> Totals);

public sealed record PoDocumentLine(int Number, string Description, string Quantity, string UnitPrice, string Total);

/// <summary>Facts about a work request that e-mails mention.</summary>
public sealed record RequestFacts(string Number, string Title, string Priority, string? Place);

/// <summary>
/// The text of every notification. One short layout: heading, a few facts, an optional paragraph, a button to the
/// record (when the server knows its own address) and a footer naming the organization. All user text is encoded.
/// </summary>
public static class EmailTemplates
{
    public static EmailContent Assigned(string org, WorkOrderFacts w, string? link, bool pm) => Layout(org,
        pm ? $"PM work {w.Number}: {w.Title}" : $"Work order {w.Number}: {w.Title}",
        pm ? "Preventive maintenance has been assigned to you" : "A work order has been assigned to you",
        WorkOrderLines(w), w.Description, link, "Open the work order");

    public static EmailContent WorkOrderCompleted(string org, WorkOrderFacts w, string? link) => Layout(org,
        $"Completed: {w.Number} {w.Title}", "Work you raised has been completed",
        WorkOrderLines(w), w.CompletionNotes is { } n ? $"Work done: {n}" : null, link, "Open the work order");

    public static EmailContent RequestSubmitted(string org, RequestFacts r, string? requester, string? link) => Layout(org,
        $"New request {r.Number}: {r.Title}", "A new work request is waiting for review",
        [("Request", $"{r.Number} · {r.Title}"), ("Priority", r.Priority), ("Where", r.Place), ("From", requester)], null, link, "Review the request");

    public static EmailContent RequestApproved(string org, RequestFacts r, string workOrderNumber, string? link) => Layout(org,
        $"Your request {r.Number} was approved", "Your work request was approved",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place)],
        "The work has been planned. You'll hear from us again when it's done.", link, "View your request");

    public static EmailContent RequestRejected(string org, RequestFacts r, string reason, string? link) => Layout(org,
        $"Your request {r.Number} was not approved", "Your work request was not approved",
        [("Request", $"{r.Number} · {r.Title}"), ("Where", r.Place)], $"Reason: {reason}", link, "View your request");

    public static EmailContent RequestCompleted(string org, RequestFacts r, string workOrderNumber, string? notes, string? link, bool askAnswer = false) => Layout(org,
        $"Done: your request {r.Number}", askAnswer ? "The work you asked for has been done. Please accept or refuse it." : "The work you asked for has been done",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place), ("Work done", notes)],
        null, link, askAnswer ? "Accept or refuse the work" : "View your request");

    public static EmailContent WorkOrderOverdue(string org, WorkOrderFacts w, string? link) => Layout(org,
        $"Overdue: work order {w.Number}", "This work is past its due time",
        [("Work order", $"{w.Number} · {w.Title}"), ("Priority", w.Priority), ("Due", w.DueDate is { } d ? Translator.Date(d) : null), ("Where", w.Place)],
        null, link, "Open the work order");

    public static EmailContent WorkOrderCancelled(string org, WorkOrderFacts w, string? reason, string? note, string? link, bool forRequester = false) => Layout(org,
        $"Cancelled: work order {w.Number}", forRequester ? "The work you asked for was cancelled" : "This work was cancelled",
        [("Work order", $"{w.Number} · {w.Title}"), ("Where", w.Place), ("Reason", reason), ("Note", note)], null, link,
        forRequester ? "View your request" : "Open the work order");

    public static EmailContent RequestRefused(string org, RequestFacts r, string workOrderNumber, string? byName, string? reason, string? link) => Layout(org,
        $"Refused: work order {workOrderNumber}", "The requester refused the finished work. The work order was reopened.",
        [("Request", $"{r.Number} · {r.Title}"), ("Work order", workOrderNumber), ("Where", r.Place), ("Refused by", byName), ("Reason", reason)],
        null, link, "Open the work order");

    public static EmailContent RequestComment(string org, RequestFacts r, string? author, string body, string? link) => Layout(org,
        $"New comment on request {r.Number}", "A new comment on the request",
        [("Request", $"{r.Number} · {r.Title}"), ("Where", r.Place), ("From", author), ("Comment", body)], null, link, "View the request");

    public static EmailContent PoSubmitted(string org, PoFacts po, string? link) => Layout(org,
        $"Approve purchase order {po.Number} ({po.Total})", "A purchase order is waiting for your approval",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total), ("Requested by", po.RequestedBy)], null, link, "Review the order");

    public static EmailContent PoApproved(string org, PoFacts po, string? link) => Layout(org,
        $"Purchase order {po.Number} approved", "Your purchase order was approved",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total)], "You can now send it to the supplier.", link, "Open the order");

    public static EmailContent PoRejected(string org, PoFacts po, string reason, string? link) => Layout(org,
        $"Purchase order {po.Number} returned", "Your purchase order was returned for changes",
        [("Order", po.Number), ("Supplier", po.Supplier), ("Total", po.Total)], $"Reason: {reason}", link, "Open the order");

    public static EmailContent PartsReceived(string org, WorkOrderFacts w, string poNumber, IReadOnlyList<string> lines, string? link) => Layout(org,
        $"Parts arrived for {w.Number} {w.Title}", "Parts ordered for your job have arrived",
        [.. WorkOrderLines(w), ("Order", poNumber)], string.Join("\n", lines), link, "Open the work order");

    /// <summary>The order itself, for a supplier with no account: every line and the totals in the e-mail, no link.</summary>
    public static EmailContent PurchaseOrder(string org, PoDocument po)
    {
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

    private static (string, string?)[] WorkOrderLines(WorkOrderFacts w) =>
        [("Work order", $"{w.Number} · {w.Title}"), ("Priority", w.Priority), ("Due", w.DueDate is { } d ? Translator.Date(d) : null), ("Where", w.Place)];

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

    /// <summary>Fixed values are translated (a priority such as "High"); names, numbers and text people typed are not.</summary>
    private static string? TranslateFixed(string label, string? value) => label == "Priority" ? Translator.T(value) : value;
}
