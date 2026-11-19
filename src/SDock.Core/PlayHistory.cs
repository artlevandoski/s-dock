using System.Globalization;

namespace SDock.Core;

/// <summary>How much a game has been played: when last, for how long in total, and how many times.</summary>
public sealed record PlayStats(DateTime LastPlayedUtc, long TotalSeconds, int Sessions)
{
    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);

    /// <summary>"Played 12 h · last played Tuesday" (relative to <paramref name="nowLocal"/>).</summary>
    public string Describe(DateTime nowLocal) => $"Played {DescribeTime(Total)} · last played {DescribeDay(LastPlayedUtc.ToLocalTime(), nowLocal)}";

    /// <summary>"under a minute", "25 min", "1 h 20 min", "12 h".</summary>
    public static string DescribeTime(TimeSpan t) => t.TotalMinutes switch
    {
        < 1 => "under a minute",
        < 60 => $"{(int)t.TotalMinutes} min",
        < 600 => t.Minutes == 0 ? $"{(int)t.TotalHours} h" : $"{(int)t.TotalHours} h {t.Minutes} min",
        _ => $"{(int)Math.Round(t.TotalHours)} h",
    };

    /// <summary>"today", "yesterday", "Tuesday" (this past week), "12 Nov", or "12 Nov 2025" in another year.</summary>
    public static string DescribeDay(DateTime whenLocal, DateTime nowLocal)
    {
        var days = (nowLocal.Date - whenLocal.Date).Days;
        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 7 => whenLocal.ToString("dddd", CultureInfo.CurrentCulture),
            _ when whenLocal.Year == nowLocal.Year => whenLocal.ToString("d MMM", CultureInfo.CurrentCulture),
            _ => whenLocal.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
        };
    }
}

public sealed partial class LibraryStore
{
    /// <summary>A session shorter than this isn't counted (a game that fails to start isn't "played").</summary>
    public static TimeSpan MinimumSession { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>A longer session is counted as this long (e.g. the PC slept with the game open).</summary>
    public static readonly TimeSpan MaximumSession = TimeSpan.FromHours(24);

    /// <summary>Adds one play session that ended at <paramref name="endedUtc"/>. Returns false if it was too short to count.</summary>
    public bool RecordPlay(string editionId, TimeSpan length, DateTime endedUtc)
    {
        if (length < MinimumSession) return false;
        var seconds = (long)Math.Min(length.TotalSeconds, MaximumSession.TotalSeconds);
        Exec("""
             INSERT INTO play_history(edition_id, last_played, total_seconds, sessions) VALUES ($e, $t, $s, 1)
             ON CONFLICT(edition_id) DO UPDATE SET
                 last_played = excluded.last_played,
                 total_seconds = total_seconds + excluded.total_seconds,
                 sessions = sessions + 1
             """, ("$e", editionId), ("$t", endedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)), ("$s", seconds));
        return true;
    }

    public Dictionary<string, PlayStats> AllPlayStats()
    {
        var result = new Dictionary<string, PlayStats>();
        using var cmd = Cmd("SELECT edition_id, last_played, total_seconds, sessions FROM play_history");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                result[r.GetString(0)] = new PlayStats(when.ToUniversalTime(), r.GetInt64(2), r.GetInt32(3));
        return result;
    }

    /// <summary>Forgets all play history (Settings → Library → Clear Play History).</summary>
    public void ClearPlayHistory() => Exec("DELETE FROM play_history");
}
