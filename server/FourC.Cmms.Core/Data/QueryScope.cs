using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Data;

public static class QueryScope
{
    /// <summary>
    /// Looks past the signed-in person's site and branch limits (the organization filter stays). Only for numbering, uniqueness
    /// checks and names of things already linked to a record the person can see.
    /// </summary>
    public static IQueryable<T> AllSites<T>(this IQueryable<T> q) where T : class => q.IgnoreQueryFilters([CmmsDbContext.SiteFilter]);
}
