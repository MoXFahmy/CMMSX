using FourC.Cmms.Core.Identity;

namespace FourC.Cmms.Api.Infrastructure;

public sealed record HomeTileInput(string? Label, string? LabelAr, string? Color, string? To);
public sealed record HomeCardInput(string? Type, List<HomeTileInput>? Tiles);

/// <summary>Role home pages and hidden menu items (0.22.0): checking what an administrator sends, and what a person with several roles gets.</summary>
public static class RoleHomes
{
    public static Validator ValidateHome(Validator v, List<HomeCardInput>? cards)
    {
        if (cards is null) return v;
        if (cards.Count > HomeCardTypes.MaxCards) return Fail(v, $"A home page can have at most {HomeCardTypes.MaxCards} cards.");
        if (cards.Any(c => c is null || !HomeCardTypes.All.Contains(c.Type ?? ""))) return Fail(v, "Choose a card from the list.");
        if (cards.Select(c => c.Type).Distinct().Count() != cards.Count) return Fail(v, "Each card can be on a home page once.");
        if (cards.Any(c => c.Type != HomeCardTypes.Shortcuts && c.Tiles is { Count: > 0 })) return Fail(v, "Only the shortcuts card has tiles.");
        var tiles = cards.Where(c => c.Type == HomeCardTypes.Shortcuts).SelectMany(c => c.Tiles ?? []).ToList();
        if (tiles.Count > HomeCardTypes.MaxTiles) return Fail(v, $"A shortcuts card can have at most {HomeCardTypes.MaxTiles} tiles.");
        if (tiles.Any(t => t is null || string.IsNullOrWhiteSpace(t.Label))) return Fail(v, "Give every tile a label.");
        if (tiles.Any(t => t.Label!.Trim().Length > HomeCardTypes.MaxLabel || (t.LabelAr?.Trim().Length ?? 0) > HomeCardTypes.MaxLabel))
            return Fail(v, $"Keep tile labels to {HomeCardTypes.MaxLabel} characters.");
        if (tiles.Any(t => !HomeCardTypes.Colors.Contains(t.Color ?? ""))) return Fail(v, "Choose a colour from the list.");
        if (tiles.Any(t => !IsSafeLink(t.To))) return Fail(v, "A tile link must be a CMX address starting with /.");
        return v;
    }

    private static Validator Fail(Validator v, string message) { v.Add("home", message); return v; }

    public static Validator ValidateModules(Validator v, List<string>? keys) =>
        v.When(keys?.Any(k => !MenuModules.IsValid(k)) == true, "hiddenModules", "Choose menu items from the list.");

    /// <summary>The cards to store; no cards means no home (null).</summary>
    public static List<HomeCard>? CleanHome(List<HomeCardInput>? cards)
    {
        if (cards is null || cards.Count == 0) return null;
        return cards.Select(c => new HomeCard
        {
            Type = c.Type!,
            Tiles = c.Type == HomeCardTypes.Shortcuts
                ? (c.Tiles ?? []).Select(t => new HomeTile { Label = t.Label!.Trim(), LabelAr = t.LabelAr.Clean(), Color = t.Color!, To = t.To!.Trim() }).ToList()
                : null,
        }).ToList();
    }

    public static List<string> CleanModules(List<string> keys) => MenuModules.All.Select(m => m.Key).Where(keys.Contains).ToList();

    /// <summary>A CMX address: starts with one "/", no backslash, no scheme, at most 300 characters.</summary>
    public static bool IsSafeLink(string? to)
    {
        var s = to?.Trim();
        if (string.IsNullOrEmpty(s) || s.Length > HomeCardTypes.MaxLink || s[0] != '/') return false;
        if (s.Length > 1 && (s[1] == '/' || s[1] == '\\')) return false;
        if (s.Contains('\\')) return false;
        var end = s.IndexOfAny(['?', '#']);
        var path = end < 0 ? s : s[..end];
        return !path.Contains(':') && !s.Any(char.IsControl);
    }

    /// <summary>
    /// The cards of all of a person's roles, roles in name order (ignoring case), without repeats: a card type already taken is
    /// skipped, except shortcuts, whose tiles stay unless their link was already shown.
    /// </summary>
    public static List<HomeCard> Merge(IEnumerable<Role> roles)
    {
        var cards = new List<HomeCard>();
        var types = new HashSet<string>();
        var links = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var card in role.HomeLayout ?? [])
            {
                if (card.Type == HomeCardTypes.Shortcuts)
                {
                    var tiles = (card.Tiles ?? []).Where(t => links.Add(t.To.Trim())).ToList();
                    if (tiles.Count > 0) cards.Add(new HomeCard { Type = card.Type, Tiles = tiles });
                }
                else if (types.Add(card.Type)) cards.Add(new HomeCard { Type = card.Type });
            }
        }
        return cards;
    }

    /// <summary>A menu item is hidden when every one of the person's roles that gives its permission hides it.</summary>
    public static List<string> Hidden(IEnumerable<Role> roles)
    {
        var list = roles.ToList();
        return MenuModules.All.Where(m =>
        {
            var able = list.Where(r => r.Permissions.Intersect(m.AnyOf).Any()).ToList();
            return able.Count > 0 && able.All(r => r.HiddenModules.Contains(m.Key));
        }).Select(m => m.Key).ToList();
    }
}
