using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>
/// Claude through the official Anthropic SDK (0.15.0). The fixed instructions and the organization's lists are cached; the answer
/// follows a JSON schema; a policy decline is re-served server-side by Anthropic's default fallback; one retry at most and a time
/// limit per call. Opus 5.5 always thinks, so no thinking setting is sent; effort sets how deep.
/// </summary>
public sealed class AnthropicAiClient(IOptions<AiOptions> options) : IAiClient
{
    private readonly Lazy<AnthropicClient> _client = new(() => new AnthropicClient
    {
        ApiKey = options.Value.ApiKey ?? "",
        MaxRetries = 1,
        Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)),
    });

    public bool Configured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    /// <summary>The request for <paramref name="ask"/> (kept separate so its shape can be checked without calling Claude).</summary>
    public static MessageCreateParams BuildRequest(AiAsk ask, AiOptions o) => new()
    {
        Model = o.Model,
        MaxTokens = ask.MaxTokens,
        System = new List<BetaTextBlockParam>
        {
            new() { Text = ask.Instructions },
            new() { Text = ask.SharedContext, CacheControl = new BetaCacheControlEphemeral() },
        },
        Messages = [new BetaUserMessageParam { Content = ask.Prompt }],
        OutputConfig = new BetaOutputConfig
        {
            Effort = ask.Effort == AiEffort.Low ? Effort.Low : Effort.Medium,
            Format = new BetaJsonOutputFormat { Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ask.Schema.ToJsonString())! },
        },
        Fallbacks = new Default(),
        Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
    };

    public async Task<AiReply> AskAsync(AiAsk ask, CancellationToken ct)
    {
        if (!Configured) throw new AiUnavailableException("AI isn't set up on this server.");
        BetaMessage m;
        try { m = await _client.Value.Beta.Messages.Create(BuildRequest(ask, options.Value), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not AiUnavailableException) { throw new AiUnavailableException("Claude didn't answer.", e); }

        var refused = m.StopReason is { } sr && sr.Raw() == "refusal";
        var json = refused ? null : m.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).FirstOrDefault(t => t is not null);
        var u = m.Usage;
        return new AiReply(refused, json, m.Model.Raw(), (int)u.InputTokens, (int)u.OutputTokens, (int)(u.CacheReadInputTokens ?? 0), (int)(u.CacheCreationInputTokens ?? 0));
    }
}
