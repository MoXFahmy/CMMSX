using FourC.Cmms.Api.Features;

namespace FourC.Cmms.Tests;

public class EmailBacklogTests
{
    [Fact]
    public async Task A_backlog_is_sent_in_batches_with_a_pause_between_them()
    {
        var runs = new Queue<int>([50, 50, 12]);
        var log = new List<string>();
        await EmailSenderService.SendBacklogAsync(() => { var n = runs.Dequeue(); log.Add($"batch {n}"); return Task.FromResult(n); }, 50,
            () => { log.Add("pause"); return Task.CompletedTask; });
        Assert.Equal(["batch 50", "pause", "batch 50", "pause", "batch 12"], log);
    }

    [Fact]
    public async Task No_pause_when_there_is_no_backlog()
    {
        var pauses = 0;
        await EmailSenderService.SendBacklogAsync(() => Task.FromResult(3), 50, () => { pauses++; return Task.CompletedTask; });
        Assert.Equal(0, pauses);
    }
}
