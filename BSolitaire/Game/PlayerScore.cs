using System.Text.Json.Serialization;

namespace BSolitaire.Game;

/// <summary>
/// One player's running record, in the spirit of classic solitaire's scoreboard. A plain value
/// the game owns and the host persists — nothing here knows about storage.
///
/// <see cref="Muted"/> and <see cref="ShowStats"/> are preferences rather than scores, but they
/// belong to the same player and ride along on the same write: a second localStorage key for
/// two bools would be more machinery than the facts deserve.
/// </summary>
internal sealed class PlayerScore
{
    public string Nickname { get; set; } = "";

    /// <summary>Deals that reached an end — won, stuck, or proved lost.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    public bool Muted { get; set; }

    public bool ShowStats { get; set; }

    /// <summary>How many cards a turn of the stock brings over. Takes effect on the next
    /// deal, never partway through one.</summary>
    public DrawMode DrawMode { get; set; }

    /// <summary>Deals with a time against them: won, lost, and walked away from after at
    /// least one move. Its own count rather than <see cref="Games"/>, because every record
    /// saved before this existed has games in it and no time for any of them.</summary>
    public int TimedGames { get; set; }

    /// <summary>Seconds over <see cref="TimedGames"/>. A sum and a count rather than a list
    /// of times: an average needs no more than this, and a player a thousand games in should
    /// not be carrying a thousand numbers around to get one.</summary>
    public long TotalGameSeconds { get; set; }

    public int TimedWins { get; set; }

    public long TotalWinSeconds { get; set; }

    /// <summary>Average seconds per timed deal, or null before there is one to average.</summary>
    [JsonIgnore]
    public long? AverageGameSeconds => TimedGames > 0 ? TotalGameSeconds / TimedGames : null;

    /// <summary>Average seconds per win, or null before the first one.</summary>
    [JsonIgnore]
    public long? AverageWinSeconds => TimedWins > 0 ? TotalWinSeconds / TimedWins : null;

    /// <summary>The line drawn on the board. A player with no nickname yet is still a player.</summary>
    [JsonIgnore]
    public string Summary =>
        $"{(string.IsNullOrWhiteSpace(Nickname) ? "Player" : Nickname)}   Games: {Games}   Wins: {Wins}";

    /// <summary>
    /// Takes on everything a stored record holds. The game keeps one score object for its
    /// lifetime and hands the same reference to the host and the board, so a loaded record is
    /// copied in rather than swapped for. Field by field, and deliberately next to the fields:
    /// this is what a new property has to be added to, and the last one was not.
    /// </summary>
    public void CopyFrom(PlayerScore other)
    {
        Nickname = other.Nickname;
        Games = other.Games;
        Wins = other.Wins;
        Muted = other.Muted;
        ShowStats = other.ShowStats;
        DrawMode = other.DrawMode;
        TimedGames = other.TimedGames;
        TotalGameSeconds = other.TotalGameSeconds;
        TimedWins = other.TimedWins;
        TotalWinSeconds = other.TotalWinSeconds;
    }
}
