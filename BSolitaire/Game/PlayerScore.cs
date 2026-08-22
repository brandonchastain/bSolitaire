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
    }
}
