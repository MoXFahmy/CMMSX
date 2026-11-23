using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Identity;

namespace FourC.Cmms.Api.Features.Ai;

/// <summary>The AI assistant's endpoints (0.15.0).</summary>
public static class AiEndpoints
{
    /// <summary>A problem answer with a translated title.</summary>
    public static IResult Problem(int status, string message) => Results.Problem(title: Translator.T(message), statusCode: status);

    /// <summary>Runs an AI endpoint, turning an <see cref="AiProblem"/> (off, no key, allowance used up, no answer) into its answer.</summary>
    public static async Task<IResult> Guard(Func<Task<IResult>> work)
    {
        try { return await work(); }
        catch (AiProblem p) { return Problem(p.Status, p.Message); }
    }

    public static void MapAiEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ai").WithTags("AI assistant");
        // Whether AI can be used here this month; every signed-in person sees it (pages show or hide the AI buttons).
        g.MapGet("/status", (AiService ai, CancellationToken ct) => ai.StatusAsync(ct)).RequireAuthorization();

        // The completion report from the technician's notes: suggestions only, nothing is saved.
        g.MapPost("/report", (AiReportInput input, Core.Data.CmmsDbContext db, AiService ai, CancellationToken ct) =>
            Guard(() => AiReport.HandleAsync(input, db, ai, ct))).RequirePermission(Permissions.WorkOrdersExecute);
    }
}
