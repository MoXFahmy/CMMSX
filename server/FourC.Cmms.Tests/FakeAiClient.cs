using System.Collections.Concurrent;
using FourC.Cmms.Api.Features.Ai;

namespace FourC.Cmms.Tests;

/// <summary>Test double for Claude: records every question; <see cref="Responder"/> decides the answer (tests never call Claude).</summary>
public sealed class FakeAiClient : IAiClient
{
    public bool Configured { get; set; } = true;
    public Func<AiAsk, Task<AiReply>> Responder { get; set; } = Default;
    public ConcurrentQueue<AiAsk> Asked { get; } = new();
    public List<AiAsk> Asks => Asked.ToList();

    public static Task<AiReply> Default(AiAsk ask) => Task.FromResult(Answer("{}"));

    /// <summary>An answered reply with some token use.</summary>
    public static AiReply Answer(string json) => new(false, json, "claude-opus-5-5", 1200, 300, 0, 0);

    public void Reset() { Responder = Default; Asked.Clear(); Configured = true; }

    public Task<AiReply> AskAsync(AiAsk ask, CancellationToken ct)
    {
        Asked.Enqueue(ask);
        return Responder(ask);
    }
}
