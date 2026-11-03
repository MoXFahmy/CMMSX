using FourC.Cmms.Core.Common;
using FourC.Cmms.Core.Data;

namespace FourC.Cmms.Core.Services;

/// <summary>Human-readable, per-tenant numbers (WO-00042). Skips past numbers that already exist (manual entry, imports).</summary>
public static class Numbering
{
    public static async Task<string> NextAsync(CmmsDbContext db, string sequence, string? prefix, int padding,
        Func<string, Task<bool>> exists, Func<string, Task<List<string>>> existingWithPrefix, CancellationToken ct = default)
    {
        prefix ??= "";
        var format = new string('0', Math.Clamp(padding, 1, 12));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var number = prefix + (await db.NextNumberAsync(sequence, ct)).ToString(format);
            if (!await exists(number)) return number;
            var max = (await existingWithPrefix(prefix)).Select(n => long.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
            await db.SetSequenceAtLeastAsync(sequence, max + 1, ct);
        }
        throw new DomainException($"Could not generate a unique number for {sequence}. Check the numbering settings.");
    }
}
