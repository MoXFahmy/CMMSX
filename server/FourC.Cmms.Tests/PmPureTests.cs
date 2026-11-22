using FourC.Cmms.Core.Pm;

namespace FourC.Cmms.Tests;

/// <summary>0.12.0 rules that need no database.</summary>
public class PmPureTests
{
    [Fact]
    public void Round_steps_per_asset_template()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var covered = new[] { new RoundAsset(a, 1, Guid.Empty, null, null, true), new RoundAsset(b, 2, Guid.Empty, null, null, true) };
        var steps = PmRound.Steps(covered, id => id == b ? ["Special", "Extra"] : ["Routine"]);
        Assert.Equal([new RoundStep(a, 1, "Routine"), new RoundStep(b, 2, "Special"), new RoundStep(b, 3, "Extra")], steps);
    }
}
