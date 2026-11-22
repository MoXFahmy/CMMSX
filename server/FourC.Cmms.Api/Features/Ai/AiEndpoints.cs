namespace FourC.Cmms.Api.Features.Ai;

/// <summary>The AI assistant's endpoints (0.15.0).</summary>
public static class AiEndpoints
{
    public static void MapAiEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ai").WithTags("AI assistant");
        // Whether AI can be used here this month; every signed-in person sees it (pages show or hide the AI buttons).
        g.MapGet("/status", (AiService ai, CancellationToken ct) => ai.StatusAsync(ct)).RequireAuthorization();
    }
}
