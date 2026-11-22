using System.Text.Json.Nodes;
using FourC.Cmms.Core.Ai;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>How hard Claude thinks: light for reports, deeper for diagnoses.</summary>
public enum AiEffort { Low, Medium }

/// <summary>
/// One question to Claude. <see cref="Instructions"/> (stable) and <see cref="SharedContext"/> (the organization's lists) come first
/// and are cached; <see cref="Prompt"/> is this call's own material. The answer follows <see cref="Schema"/> (JSON schema).
/// </summary>
public sealed record AiAsk(AiFeature Feature, string Instructions, string SharedContext, string Prompt, JsonObject Schema, AiEffort Effort, int MaxTokens);

/// <summary>Claude's answer: the JSON (null when it refused), the model that answered and the tokens it used.</summary>
public sealed record AiReply(bool Refused, string? Json, string Model, int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens);

/// <summary>Claude couldn't be reached or didn't answer (time-out, network, API error). Not counted against the allowance.</summary>
public sealed class AiUnavailableException(string reason, Exception? inner = null) : Exception(reason, inner);

/// <summary>The one way the server talks to Claude; tests replace it with a fake and never call Anthropic.</summary>
public interface IAiClient
{
    /// <summary>The server has a key.</summary>
    bool Configured { get; }
    Task<AiReply> AskAsync(AiAsk ask, CancellationToken ct);
}

/// <summary>Stands in until a real client is registered: AI isn't set up.</summary>
public sealed class NoAiClient : IAiClient
{
    public bool Configured => false;
    public Task<AiReply> AskAsync(AiAsk ask, CancellationToken ct) => throw new AiUnavailableException("AI isn't set up on this server.");
}
