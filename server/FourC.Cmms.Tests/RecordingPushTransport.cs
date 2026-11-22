using System.Collections.Concurrent;
using FourC.Cmms.Api.Features;

namespace FourC.Cmms.Tests;

/// <summary>Test double for Firebase: records every push; tokens in <see cref="Gone"/> answer "no longer registered".</summary>
public sealed class RecordingPushTransport : IPushTransport
{
    public ConcurrentQueue<(string Token, string Title, string Body, string? Link)> Sent { get; } = new();
    public ConcurrentDictionary<string, bool> Gone { get; } = new();

    public Task<PushResult> SendAsync(string token, string title, string body, string? link, CancellationToken ct)
    {
        if (Gone.ContainsKey(token)) return Task.FromResult(PushResult.Gone);
        Sent.Enqueue((token, title, body, link));
        return Task.FromResult(PushResult.Sent);
    }

    public List<(string Token, string Title, string Body, string? Link)> To(string token) => Sent.Where(s => s.Token == token).ToList();
}
