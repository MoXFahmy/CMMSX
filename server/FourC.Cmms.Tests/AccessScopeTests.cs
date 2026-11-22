using FourC.Cmms.Api.Infrastructure;

namespace FourC.Cmms.Tests;

/// <summary>Working out a request's scope from a person's access and the site switch (no database).</summary>
public class AccessScopeTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    [Fact]
    public void Narrow_keeps_everything_without_a_header()
    {
        var all = Access.Narrow(new UserAccess(null, null), null);
        Assert.Null(all.SiteIds); Assert.Null(all.BranchPaths); Assert.Null(all.NarrowedTo);
        var limited = Access.Narrow(new UserAccess([A, B], null), null);
        Assert.Equal([A, B], limited.SiteIds!);
        Assert.Null(limited.NarrowedTo);
    }

    [Fact]
    public void Narrow_to_a_site_in_scope()
    {
        var everyone = Access.Narrow(new UserAccess(null, null), B);
        Assert.Equal([B], everyone.SiteIds!); Assert.Equal(B, everyone.NarrowedTo);
        var limited = Access.Narrow(new UserAccess([A, B], null), A);
        Assert.Equal([A], limited.SiteIds!); Assert.Equal(A, limited.NarrowedTo);
    }

    [Fact]
    public void Narrow_ignores_a_site_outside_scope()
    {
        var s = Access.Narrow(new UserAccess([A], null), B);
        Assert.Equal([A], s.SiteIds!); Assert.Null(s.NarrowedTo);
    }

    [Fact]
    public void Narrow_with_branches_keeps_only_that_sites_branches()
    {
        var access = new UserAccess([A, B], [new AccessBranch(A, "/a1/"), new AccessBranch(B, "/b1/"), new AccessBranch(A, "/a2/")]);
        var all = Access.Narrow(access, null);
        Assert.Equal(["/a1/", "/b1/", "/a2/"], all.BranchPaths!);
        var onlyA = Access.Narrow(access, A);
        Assert.Equal([A], onlyA.SiteIds!); Assert.Equal(["/a1/", "/a2/"], onlyA.BranchPaths!);
        // Branches that are all gone mean nothing visible, never everything.
        var gone = Access.Narrow(new UserAccess([], []), null);
        Assert.Empty(gone.SiteIds!); Assert.Empty(gone.BranchPaths!);
    }

    [Fact]
    public void Covers_checks_site_and_branch_path()
    {
        Assert.True(Access.Covers(new UserAccess(null, null), A, null));
        Assert.True(Access.Covers(new UserAccess([A], null), A, null));
        Assert.False(Access.Covers(new UserAccess([A], null), B, "/x/"));
        var branch = new UserAccess([A], [new AccessBranch(A, "/a1/")]);
        Assert.True(Access.Covers(branch, A, "/a1/f1/"));
        Assert.False(Access.Covers(branch, A, "/a2/"));
        Assert.False(Access.Covers(branch, A, null)); // site-level records are outside every branch
    }
}
