using FourC.Cmms.Core.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FourC.Cmms.Core.Data;

/// <summary>Used by `dotnet ef` tooling only.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<CmmsDbContext>
{
    public CmmsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CmmsDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__Cmms") ?? "Server=localhost;Database=Cmms;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, new FixedTenantContext(null));
}
