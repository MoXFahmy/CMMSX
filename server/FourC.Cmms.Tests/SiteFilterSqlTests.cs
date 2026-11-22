using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace FourC.Cmms.Tests;

/// <summary>The site filter costs nothing when there is no limit, and limits by site and branch when there is (SQL only, no database).</summary>
public class SiteFilterSqlTests(ITestOutputHelper output)
{
    private sealed class Ctx(IReadOnlyList<Guid>? sites, IReadOnlyList<string>? branches) : ITenantContext
    {
        public Guid? TenantId { get; } = Guid.NewGuid();
        public Guid? UserId => null;
        public string? UserName => null;
        public IReadOnlyList<Guid>? SiteIds => sites;
        public IReadOnlyList<string>? BranchPaths => branches;
    }

    private sealed class MutableCtx : ITenantContext
    {
        public IReadOnlyList<Guid>? Sites { get; set; }
        public Guid? TenantId { get; } = Guid.NewGuid();
        public Guid? UserId => null;
        public string? UserName => null;
        public IReadOnlyList<Guid>? SiteIds => Sites;
    }

    [SkippableFact]
    public async Task A_context_set_up_before_the_limit_refuses_to_run()
    {
        var server = Environment.GetEnvironmentVariable("CMMS_TEST_SQL");
        Skip.If(string.IsNullOrWhiteSpace(server), "Set CMMS_TEST_SQL to run integration tests.");
        var ctx = new MutableCtx();
        await using var db = new CmmsDbContext(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer(server).Options, ctx);
        _ = db.Model; // set up while the request had no limit
        ctx.Sites = [Guid.NewGuid()];
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Sites.ToListAsync());
        Assert.Contains("site scope", ex.Message);
    }

    private static CmmsDbContext Db(ITenantContext ctx) =>
        new(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none;TrustServerCertificate=True").Options, ctx);

    [Fact]
    public void No_limit_adds_nothing_to_the_sql()
    {
        using var db = Db(new Ctx(null, null));
        var labour = db.WorkOrderLabor.Where(l => l.Hours > 1).ToQueryString();
        var orders = db.WorkOrders.Where(w => w.Title != "").ToQueryString();
        output.WriteLine(labour); output.WriteLine(orders);
        Assert.DoesNotContain("[WorkOrders]", labour);
        Assert.DoesNotContain("[Locations]", orders);
        Assert.DoesNotContain("OPENJSON", orders);
    }

    [Fact]
    public void A_limit_filters_by_site_branch_and_parent()
    {
        using var db = Db(new Ctx([Guid.NewGuid()], ["/a/"]));
        var labour = db.WorkOrderLabor.Where(l => l.Hours > 1).ToQueryString();
        var orders = db.WorkOrders.Where(w => w.Title != "").ToQueryString();
        output.WriteLine(labour); output.WriteLine(orders);
        Assert.Contains("[WorkOrders]", labour);
        Assert.Contains("[SiteId]", orders);
        Assert.Contains("[Path]", orders);
    }
}
