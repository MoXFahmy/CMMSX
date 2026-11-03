using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Data;
using FourC.Cmms.Core.Locations;
using Microsoft.EntityFrameworkCore;

namespace FourC.Cmms.Core.Services;

/// <summary>Generates a realistic demo hierarchy (sites, buildings, areas, assets with sub-assets).</summary>
public static class SampleData
{
    public static async Task<int> SeedAsync(CmmsDbContext db, int assetsPerArea = 6, int seed = 42, CancellationToken ct = default)
    {
        var rnd = new Random(seed);
        var cats = await db.AssetCategories.ToDictionaryAsync(c => c.Code, ct);
        Guid? Cat(string code) => cats.TryGetValue(code, out var c) ? c.Id : null;

        var sites = new[]
        {
            ("CAI", "Cairo Production Plant", "Cairo", "Egypt", 30.0444, 31.2357),
            ("ALX", "Alexandria Distribution Center", "Alexandria", "Egypt", 31.2001, 29.9187),
            ("DXB", "Dubai Head Office", "Dubai", "United Arab Emirates", 25.2048, 55.2708),
        };
        var models = new (string Cat, string Name, string Mfr, string Model)[]
        {
            ("HVAC", "Air Handling Unit", "Carrier", "39HQ"), ("HVAC", "Chiller", "Trane", "CGAM-120"), ("HVAC", "Split AC Unit", "Daikin", "FTXM50"),
            ("ELEC", "Main Distribution Board", "Schneider Electric", "Prisma P"), ("ELEC", "Diesel Generator", "Caterpillar", "C18"), ("ELEC", "UPS", "APC", "Galaxy VS"),
            ("MECH", "Air Compressor", "Atlas Copco", "GA 37"), ("MECH", "Centrifugal Pump", "Grundfos", "NB 65-200"), ("MECH", "Conveyor", "Interroll", "RM 8310"),
            ("PLMB", "Water Heater", "Ariston", "Pro1 Eco"), ("FIRE", "Fire Pump", "Pentair", "Aurora 481"), ("FIRE", "Fire Alarm Panel", "Honeywell", "Notifier NFS2"),
            ("PROD", "Filling Machine", "Krones", "Modulfill"), ("PROD", "Packaging Machine", "Bosch", "SVE 2520"), ("PROD", "Injection Molding Machine", "Engel", "victory 200"),
            ("VEH", "Forklift", "Toyota", "8FBE20"), ("IT", "Server Rack", "Dell", "PowerEdge R760"),
        };
        var statuses = new[] { AssetStatus.Active, AssetStatus.Active, AssetStatus.Active, AssetStatus.Active, AssetStatus.UnderRepair, AssetStatus.InStorage, AssetStatus.Inactive };
        var count = 0;
        var seq = await db.Assets.CountAsync(ct);
        foreach (var (code, name, city, country, lat, lng) in sites)
        {
            if (await db.Sites.AnyAsync(s => s.Code == code, ct)) continue;
            var site = new Site { Code = code, Name = name, City = city, Country = country, Latitude = lat, Longitude = lng, TimeZone = country == "Egypt" ? "Africa/Cairo" : "Asia/Dubai" };
            db.Sites.Add(site);
            foreach (var b in new[] { "Main Building", "Utilities Building" })
            {
                var bcode = $"{code}-{(b.StartsWith("Main") ? "MB" : "UB")}";
                var building = new Location { SiteId = site.Id, Code = bcode, Name = b, Type = LocationType.Building };
                await LocationTree.AssignPathAsync(db, building, ct);
                db.Locations.Add(building);
                var areas = b.StartsWith("Main")
                    ? new[] { ("PL1", "Production Line 1", LocationType.ProductionLine), ("PL2", "Production Line 2", LocationType.ProductionLine), ("WH", "Warehouse", LocationType.Area), ("OFF", "Offices - Floor 1", LocationType.Floor) }
                    : new[] { ("PWR", "Power Room", LocationType.Room), ("CMP", "Compressor Room", LocationType.Room), ("RF", "Roof", LocationType.Area) };
                foreach (var (acode, aname, atype) in areas)
                {
                    var area = new Location { SiteId = site.Id, ParentId = building.Id, Code = $"{bcode}-{acode}", Name = aname, Type = atype, Criticality = atype == LocationType.ProductionLine ? Criticality.High : Criticality.Medium };
                    await LocationTree.AssignPathAsync(db, area, ct);
                    db.Locations.Add(area);
                    for (var i = 0; i < assetsPerArea; i++)
                    {
                        var m = models[rnd.Next(models.Length)];
                        var asset = NewAsset(++seq, m, site, area, rnd, statuses, Cat(m.Cat));
                        db.Assets.Add(asset); count++;
                        if (m.Cat == "PROD" || m.Name == "Air Handling Unit")
                        {
                            foreach (var part in new[] { "Drive Motor", "Gearbox", "Control Panel" })
                            {
                                var child = NewAsset(++seq, (m.Cat, $"{part} - {m.Name}", m.Mfr, m.Model + "-" + part[..3].ToUpperInvariant()), site, area, rnd, statuses, Cat(m.Cat));
                                child.ParentAssetId = asset.Id; child.Status = AssetStatus.Active;
                                db.Assets.Add(child); count++;
                            }
                        }
                    }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await db.SetSequenceAtLeastAsync("Asset", seq + 1, ct);
        return count;
    }

    private static Asset NewAsset(int n, (string Cat, string Name, string Mfr, string Model) m, Site site, Location loc, Random rnd, AssetStatus[] statuses, Guid? catId)
    {
        var commissioned = DateOnly.FromDateTime(DateTime.Today.AddDays(-rnd.Next(200, 4000)));
        var price = Math.Round((decimal)(rnd.NextDouble() * 90000 + 2000), 0);
        return new Asset
        {
            AssetNumber = $"AST-{n:00000}", Name = m.Name, CategoryId = catId, SiteId = site.Id, LocationId = loc.Id,
            Status = statuses[rnd.Next(statuses.Length)], Criticality = (Criticality)rnd.Next(1, 5),
            Manufacturer = m.Mfr, Model = m.Model, SerialNumber = $"SN{rnd.Next(100000, 999999)}", Barcode = $"AST{n:00000}",
            CommissionedOn = commissioned, PurchasePrice = price, CurrentValue = Math.Round(price * (decimal)(0.3 + rnd.NextDouble() * 0.6), 0),
            ExpectedLifeYears = rnd.Next(8, 25), DowntimeCostPerHour = rnd.Next(1, 20) * 50,
            WarrantyType = rnd.Next(3) == 0 ? WarrantyType.ServiceContract : WarrantyType.Warranty,
            WarrantyExpiresOn = commissioned.AddYears(rnd.Next(1, 6)),
        };
    }
}
