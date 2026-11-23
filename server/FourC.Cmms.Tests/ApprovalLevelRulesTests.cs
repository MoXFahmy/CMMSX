using FourC.Cmms.Core.Purchasing;

namespace FourC.Cmms.Tests;

/// <summary>0.16.0: which approval levels an order needs.</summary>
public class ApprovalLevelRulesTests
{
    private static readonly Guid SiteA = Guid.NewGuid(), SiteB = Guid.NewGuid(), Capital = Guid.NewGuid(), Parts = Guid.NewGuid();

    private static ApprovalLevel L(string name, decimal min, Guid? site = null, Guid? type = null) =>
        new() { Name = name, MinAmount = min, SiteId = site, OrderTypeId = type, ApproverIds = [Guid.NewGuid()] };

    private static List<string> Names(IEnumerable<ApprovalLevel> levels) => levels.Select(l => l.Name).ToList();

    private static readonly ApprovalLevel[] Org = [L("L100k", 100_000), L("L0", 0), L("L10k", 10_000)];

    [Fact]
    public void Levels_add_up_lowest_first()
    {
        Assert.Equal(["L0", "L10k"], Names(ApprovalLevelRules.Required(Org, SiteA, null, 50_000)));
        Assert.Equal(["L0", "L10k", "L100k"], Names(ApprovalLevelRules.Required(Org, SiteA, null, 200_000)));
        Assert.Equal(["L0"], Names(ApprovalLevelRules.Required(Org, SiteA, null, 0)));
    }

    [Fact]
    public void Exactly_at_the_minimum_in_base_currency_needs_the_level()
    {
        Assert.Contains("L10k", Names(ApprovalLevelRules.Required(Org, SiteA, null, 10_000.00m)));
        Assert.DoesNotContain("L10k", Names(ApprovalLevelRules.Required(Org, SiteA, null, 9_999.99m)));
    }

    [Fact]
    public void Below_every_minimum_needs_none()
    {
        Assert.Empty(ApprovalLevelRules.Required([L("L5k", 5_000)], SiteA, null, 4_999.99m));
        Assert.Empty(ApprovalLevelRules.Required([], SiteA, null, 1_000_000));
    }

    [Fact]
    public void Site_set_replaces_the_organization_set()
    {
        ApprovalLevel[] all = [L("Org0", 0), L("Org10k", 10_000), L("A0", 0, site: SiteA)];
        Assert.Equal(["A0"], Names(ApprovalLevelRules.Required(all, SiteA, null, 50_000)));
        Assert.Equal(["Org0", "Org10k"], Names(ApprovalLevelRules.Required(all, SiteB, null, 50_000)));
    }

    [Fact]
    public void Type_levels_replace_all_types_levels_within_the_set()
    {
        ApprovalLevel[] all = [L("All0", 0), L("Cap0", 0, type: Capital), L("Cap50k", 50_000, type: Capital)];
        Assert.Equal(["Cap0", "Cap50k"], Names(ApprovalLevelRules.Required(all, SiteA, Capital, 60_000)));
        Assert.Equal(["All0"], Names(ApprovalLevelRules.Required(all, SiteA, Parts, 60_000)));
        Assert.Equal(["All0"], Names(ApprovalLevelRules.Required(all, SiteA, null, 60_000)));
    }

    [Fact]
    public void A_site_type_level_keeps_the_organization_levels_for_other_types()
    {
        ApprovalLevel[] all = [L("Org0", 0), L("Org10k", 10_000), L("ACap50k", 50_000, site: SiteA, type: Capital)];
        Assert.Equal(["Org0", "Org10k"], Names(ApprovalLevelRules.Required(all, SiteA, Parts, 20_000)));
        Assert.Equal(["ACap50k"], Names(ApprovalLevelRules.Required(all, SiteA, Capital, 60_000)));
        Assert.Empty(ApprovalLevelRules.Required(all, SiteA, Capital, 20_000));
    }

    [Fact]
    public void Most_specific_levels_win()
    {
        ApprovalLevel[] all = [L("Org0", 0), L("OrgCap0", 0, type: Capital), L("A0", 0, site: SiteA)];
        Assert.Equal(["A0"], Names(ApprovalLevelRules.Required(all, SiteA, Capital, 100)));     // the site's all-types beat the organization's Capital
        Assert.Equal(["OrgCap0"], Names(ApprovalLevelRules.Required(all, SiteB, Capital, 100))); // no site levels: the organization's Capital
        Assert.Equal(["Org0"], Names(ApprovalLevelRules.Required(all, SiteB, Parts, 100)));
    }
}
