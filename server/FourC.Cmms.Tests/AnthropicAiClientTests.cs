using System.Text.Json;
using System.Text.Json.Nodes;
using FourC.Cmms.Api.Features.Ai;
using FourC.Cmms.Core.Ai;
using Microsoft.Extensions.Options;

namespace FourC.Cmms.Tests;

/// <summary>The real Claude client (0.15.0): configured only with a key; the request it builds (never sent in tests).</summary>
public class AnthropicAiClientTests
{
    private static AiAsk Ask() => new(AiFeature.Report, "Fixed instructions", "SYX — Leaking", "Work order WO-1 notes",
        AiText.Obj(("workDone", AiText.Str())), AiEffort.Low, 4000);

    [Fact]
    public void Not_configured_without_key()
    {
        Assert.False(new AnthropicAiClient(Options.Create(new AiOptions())).Configured);
        Assert.False(new AnthropicAiClient(Options.Create(new AiOptions { ApiKey = "  " })).Configured);
        Assert.True(new AnthropicAiClient(Options.Create(new AiOptions { ApiKey = "x" })).Configured);
    }

    [Fact]
    public void Request_caches_shared_context_and_uses_settings()
    {
        var p = AnthropicAiClient.BuildRequest(Ask(), new AiOptions { Model = "claude-opus-5-5" });
        // The wire shape, as the API receives it.
        var wire = JsonNode.Parse(JsonSerializer.Serialize(p.RawBodyData))!.AsObject();
        Assert.Equal("claude-opus-5-5", wire["model"]!.GetValue<string>());
        Assert.Equal(4000, wire["max_tokens"]!.GetValue<int>());
        var system = wire["system"]!.AsArray();
        Assert.Equal(2, system.Count);
        Assert.Equal("Fixed instructions", system[0]!["text"]!.GetValue<string>());
        Assert.Null(system[0]!["cache_control"]);
        Assert.Equal("SYX — Leaking", system[1]!["text"]!.GetValue<string>());
        Assert.Equal("ephemeral", system[1]!["cache_control"]!["type"]!.GetValue<string>());
        var message = Assert.Single(wire["messages"]!.AsArray())!;
        Assert.Equal("user", message["role"]!.GetValue<string>());
        Assert.Contains("Work order WO-1 notes", message["content"]!.ToJsonString());
        Assert.Equal("json_schema", wire["output_config"]!["format"]!["type"]!.GetValue<string>());
        Assert.Equal("low", wire["output_config"]!["effort"]!.GetValue<string>());
        Assert.Equal("default", wire["fallbacks"]!.GetValue<string>());
        Assert.Null(wire["thinking"]);
        Assert.Contains("server-side-fallback-2026-07-01", p.Betas!.Select(b => b.Raw()));
    }

    /// <summary>
    /// CloudFront gives the server 60 seconds (OriginReadTimeout); the AI call must end well before, after the history and lock
    /// queries, so the person gets "didn't answer, try again" instead of a bare 504 and no counted answer is lost.
    /// </summary>
    [Fact]
    public void Time_limit_fits_under_the_CloudFront_origin_timeout()
    {
        Assert.InRange(new AiOptions().TimeoutSeconds, 1, 45);
    }
}
