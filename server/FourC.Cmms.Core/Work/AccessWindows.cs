namespace FourC.Cmms.Core.Work;

/// <summary>A time when the work may be done (a room is free, an area is open).</summary>
public sealed record AccessWindow(DateTimeOffset Start, DateTimeOffset End, string? Note);

public static class AccessWindows
{
    public const int Max = 10;
    public const int NoteLength = 200;

    /// <summary>What is wrong with a list of windows, or null when it is fine (none at all is fine).</summary>
    public static string? Problem(IReadOnlyList<AccessWindow>? list)
    {
        if (list is null || list.Count == 0) return null;
        if (list.Count > Max) return $"Give at most {Max} access windows.";
        if (list.Any(w => w.End <= w.Start)) return "An access window must end after it starts.";
        if (list.Any(w => (w.Note?.Trim().Length ?? 0) > NoteLength)) return $"An access window's note can be at most {NoteLength} characters.";
        return null;
    }

    /// <summary>The list as stored: notes trimmed (empty ones dropped), in time order.</summary>
    public static List<AccessWindow> Clean(IReadOnlyList<AccessWindow>? list) =>
        (list ?? []).Select(w => w with { Note = string.IsNullOrWhiteSpace(w.Note) ? null : w.Note.Trim() }).OrderBy(w => w.Start).ToList();

    /// <summary>True when there are no windows, or one of them overlaps <paramref name="day"/> in <paramref name="zone"/>.</summary>
    public static bool Covers(IReadOnlyList<AccessWindow>? list, DateOnly day, TimeZoneInfo zone)
    {
        if (list is null || list.Count == 0) return true;
        var start = Midnight(day, zone);
        var end = Midnight(day.AddDays(1), zone);
        return list.Any(w => w.Start < end && w.End > start);
    }

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
