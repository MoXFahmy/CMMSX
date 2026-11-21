namespace FourC.Cmms.Tests;

/// <summary>Mapping rules for 4CSQL multi-asset PMs and PM step results (RoundImport.cs is linked into this project).</summary>
public class RoundImportTests
{
    [Fact]
    public void A_round_on_one_site_keeps_its_pm_number()
    {
        var split = RoundImport.SplitBySite("CH-FD-D-F1", [("1110", "MEGS"), ("1120", "MEGS")]);
        var only = Assert.Single(split);
        Assert.Equal(("MEGS", "CH-FD-D-F1"), (only.SiteCode, only.Code));
        Assert.Equal(["1110", "1120"], only.AssetNos);
    }

    [Fact]
    public void A_round_spanning_sites_is_split_per_site_in_first_appearance_order()
    {
        var split = RoundImport.SplitBySite("PM-1", [("A1", "MGM"), ("B1", "MEGS"), ("A2", "mgm")]);
        Assert.Equal(["PM-1-MGM", "PM-1-MEGS"], split.Select(s => s.Code));
        Assert.Equal(["A1", "A2"], split[0].AssetNos);
        Assert.Equal(["B1"], split[1].AssetNos);
    }

    [Fact]
    public void Covered_assets_come_from_the_work_orders_steps_in_round_order()
    {
        Assert.Equal(["1110", "1120"], RoundImport.CoveredAssets([("1120", 2), ("1110", 1), (" 1110 ", 1)], ["9999"]));
        Assert.Equal(["9999", "8888"], RoundImport.CoveredAssets([], ["9999", "8888"]));
        Assert.Equal(["9999"], RoundImport.CoveredAssets([("", 1)], ["9999"]));
    }

    [Fact]
    public void Findings_and_remarks_are_joined()
    {
        Assert.Equal("leak", RoundImport.JoinRemarks(" leak ", ""));
        Assert.Equal("a · b", RoundImport.JoinRemarks("a", "b"));
        Assert.Equal("b", RoundImport.JoinRemarks(null, "b"));
        Assert.Null(RoundImport.JoinRemarks("  ", null));
    }

    [Fact]
    public void Pm_parts_rows_are_cleaned_and_merged()
    {
        (string?, string?, double?)[] rows = [(" T1 ", "P1", 1), ("t1", "p1 ", 2), ("T1", "", 1), ("T2", "P2", 0), (null, "P3", 1)];
        Assert.Equal([("T1", "P1", 3m)], PmPartsRules.Lines(rows));
    }

    [Fact]
    public void Pm_parts_rows_added_together_are_not_counted_as_skipped()
    {
        (string?, string?, double?)[] rows = [(" T1 ", "P1", 1), ("t1", "p1 ", 2), ("T1", "", 1), ("T2", "P2", 0), (null, "P3", 1)];
        Assert.Equal(3, PmPartsRules.Unusable(rows)); // no part, no quantity, no task; the repeated T1/P1 row was added, not skipped
    }
}
