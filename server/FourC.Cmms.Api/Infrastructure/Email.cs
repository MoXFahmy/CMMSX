using Amazon;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FourC.Cmms.Api.Infrastructure;

/// <summary>One e-mail, ready for the mail service.</summary>
public sealed record EmailMessage(string To, string? ToName, string From, string FromName, string? ReplyTo, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage m, CancellationToken ct);
}

/// <summary>Server-wide e-mail settings (section "Email"). Organizations switch e-mail on in their own settings.</summary>
public sealed class EmailOptions
{
    /// <summary>"Ses" (Amazon SES, credentials from the environment/task role) or "Smtp"; empty = e-mail is not set up.</summary>
    public string? Provider { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "4C CMMS";
    /// <summary>SES region; defaults to the SDK's own region resolution.</summary>
    public string? Region { get; set; }
    public int PollSeconds { get; set; } = 15;
    /// <summary>Run the background sender (tests switch it off and send the queue themselves).</summary>
    public bool AutoSend { get; set; } = true;
    public SmtpOptions Smtp { get; set; } = new();

    public bool Available => Provider?.ToLowerInvariant() is "ses" or "smtp" && !string.IsNullOrWhiteSpace(From);

    public sealed class SmtpOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public string? User { get; set; }
        public string? Password { get; set; }
        public bool UseTls { get; set; } = true;
    }

    /// <summary>"Name" &lt;address&gt; with the name encoded for headers when it isn't plain ASCII.</summary>
    public static string Mailbox(string? name, string address) =>
        new MailboxAddress(name ?? "", address).ToString(FormatOptions.Default, encode: true);
}

/// <summary>Amazon SES (API v2). On AWS the ECS task role grants ses:SendEmail; no secret is stored.</summary>
public sealed class SesEmailSender : IEmailSender, IDisposable
{
    private readonly AmazonSimpleEmailServiceV2Client _client;

    public SesEmailSender(IOptions<EmailOptions> options) =>
        _client = options.Value.Region is { Length: > 0 } r ? new(RegionEndpoint.GetBySystemName(r)) : new();

    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        Content Utf8(string s) => new() { Data = s, Charset = "UTF-8" };
        await _client.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = EmailOptions.Mailbox(m.FromName, m.From),
            Destination = new Destination { ToAddresses = [EmailOptions.Mailbox(m.ToName, m.To)] },
            ReplyToAddresses = m.ReplyTo is null ? null : [m.ReplyTo],
            Content = new EmailContent { Simple = new Message { Subject = Utf8(m.Subject), Body = new Body { Html = Utf8(m.Html), Text = Utf8(m.Text) } } },
        }, ct);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>Any SMTP server: a local test mailbox (Mailpit), Microsoft 365, an SMTP relay...</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        var o = options.Value.Smtp;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(m.FromName, m.From));
        msg.To.Add(new MailboxAddress(m.ToName ?? "", m.To));
        if (m.ReplyTo is not null) msg.ReplyTo.Add(new MailboxAddress("", m.ReplyTo));
        msg.Subject = m.Subject;
        msg.Body = new BodyBuilder { HtmlBody = m.Html, TextBody = m.Text }.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host ?? "localhost", o.Port, o.UseTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.User)) await client.AuthenticateAsync(o.User, o.Password ?? "", ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>Makes user-typed text safe for e-mails.</summary>
public static class EmailText
{
    /// <summary>For subjects and display names: no line breaks (header injection), at most 200 characters.</summary>
    public static string OneLine(string? s)
    {
        var t = string.Join(' ', (s ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return t.Length > 200 ? t[..200] : t;
    }

    /// <summary>For HTML bodies.</summary>
    public static string Html(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
