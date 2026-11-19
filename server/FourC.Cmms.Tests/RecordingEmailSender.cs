using System.Collections.Concurrent;
using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>Test double for the mail service: records every message, and rejects addresses a real server would.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public async Task SendAsync(EmailMessage m, CancellationToken ct)
    {
        if (!m.To.Contains('@') || m.To.Contains("fail", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"mailbox rejected: {m.To}");
        if (m.To.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            throw new TaskCanceledException("The mail server did not answer in time."); // what an HTTP/SMTP timeout looks like
        if (m.To.Contains("slow", StringComparison.OrdinalIgnoreCase)) await Task.Delay(100, ct);
        Sent.Enqueue(m);
    }

    /// <summary>Messages sent to one of <paramref name="addresses"/> (tests use unique addresses, so other tests' mail is ignored).</summary>
    public List<EmailMessage> To(params string[] addresses) => Sent.Where(m => addresses.Contains(m.To, StringComparer.OrdinalIgnoreCase)).ToList();
}
