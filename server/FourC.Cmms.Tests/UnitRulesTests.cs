using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Inventory;

namespace FourC.Cmms.Tests;

/// <summary>Serial units' pure rules (0.24.0). No database.</summary>
public class UnitRulesTests
{
    private static Asset A(AssetStatus status, Guid? parent = null) => new() { AssetNumber = "U-1", Name = "Pump", Status = status, ParentAssetId = parent };

    [Fact]
    public void State_follows_status_and_parent()
    {
        Assert.Equal(UnitState.InStore, UnitRules.StateOf(A(AssetStatus.InStorage)));
        Assert.Equal(UnitState.Fitted, UnitRules.StateOf(A(AssetStatus.Active, Guid.NewGuid())));
        Assert.Equal(UnitState.AtRepair, UnitRules.StateOf(A(AssetStatus.UnderRepair)));
        Assert.Equal(UnitState.Scrapped, UnitRules.StateOf(A(AssetStatus.Disposed)));
    }

    [Fact]
    public void Hours_units_and_end_of_life()
    {
        Assert.True(UnitRules.IsHoursUnit("Hours"));
        Assert.True(UnitRules.IsHoursUnit(" h "));
        Assert.True(UnitRules.IsHoursUnit("ساعة"));
        Assert.False(UnitRules.IsHoursUnit("km"));
        Assert.False(UnitRules.IsHoursUnit(null));
        Assert.Equal(new DateOnly(2027, 11, 24), UnitRules.EndOfLife(new DateOnly(2026, 11, 24), 12));
        Assert.Null(UnitRules.EndOfLife(new DateOnly(2026, 11, 24), null));
    }

    [Fact]
    public void Serials_are_trimmed_with_blanks_dropped()
    {
        Assert.Equal(["SN1", "sn2"], UnitRules.CleanSerials([" SN1 ", "", "sn2", null, "  "]));
    }
}
