using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.People;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Tests;

/// <summary>Every column holding a copy of a person's name is known to the anonymiser (0.25.0); no database needed.</summary>
public class NameColumnTests
{
    private static readonly string[] Endings = ["ByName", "UserName", "By", "RequesterName", "PersonName"];

    [Fact]
    public void Every_name_column_is_listed()
    {
        using var db = new CmmsDbContext(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none").Options, new FixedTenantContext(null));
        var listed = NameColumns.All.Select(c => (c.Entity, c.Property)).Concat(NameColumns.NotPeople).ToHashSet();
        var missing = db.Model.GetEntityTypes().Where(t => !t.IsOwned())
            .SelectMany(t => t.GetProperties().Where(p => p.ClrType == typeof(string) && p.PropertyInfo is not null && Endings.Any(e => p.Name.EndsWith(e, StringComparison.Ordinal)))
                .Select(p => (t.ClrType, p.Name)))
            .Where(c => !listed.Contains(c))
            .Select(c => $"{c.ClrType.Name}.{c.Name} holds a person's name but isn't in NameColumns").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Listed_columns_exist()
    {
        using var db = new CmmsDbContext(new DbContextOptionsBuilder<CmmsDbContext>().UseSqlServer("Server=none;Database=none").Options, new FixedTenantContext(null));
        Assert.All(NameColumns.All, c =>
        {
            var type = db.Model.FindEntityType(c.Entity);
            Assert.NotNull(type);
            Assert.NotNull(type!.FindProperty(c.Property));
            Assert.NotNull(type.FindProperty("TenantId"));
            if (c.IdProperty is { } id) Assert.NotNull(type.FindProperty(id));
        });
    }
}
