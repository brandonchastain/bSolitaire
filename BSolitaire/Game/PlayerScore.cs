using System.Text.Json.Serialization;

namespace BSolitaire.Game;

/// <summary>
/// One player's running record, in the spirit of classic solitaire's scoreboard: who is
/// playing, how many deals they have finished, and how many of those they won. It is a
/// plain value the game owns and the host persists — nothing here knows about storage.
/// </summary>
public sealed class PlayerScore
{
    /// <summary>Shown on the board. Empty until the player has been asked for one.</summary>
    public string Nickname { get; set; } = "";

    /// <summary>Deals that reached an end — won, stuck, or proved lost.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>The line drawn on the board. A player with no nickname yet is still a player.</summary>
    [JsonIgnore]
    public string Summary =>
        $"{(string.IsNullOrWhiteSpace(Nickname) ? "Player" : Nickname)}   Games: {Games}   Wins: {Wins}";
}
