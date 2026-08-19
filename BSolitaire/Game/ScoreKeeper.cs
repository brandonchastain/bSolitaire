namespace BSolitaire.Game;

/// <summary>
/// Watches the board finish and keeps the player's running record. Counting deals is not one
/// of solitaire's rules — the board plays exactly the same whether or not anyone is keeping
/// score — so it sits outside the game and listens instead.
/// </summary>
public sealed class ScoreKeeper
{
    private readonly Board board;

    private int recordedVersion = -1;

    public ScoreKeeper(Board board) => this.board = board;

    /// <summary>The local player's running record. The host loads it before the first frame
    /// and saves it whenever <see cref="Changed"/> fires — this only counts.</summary>
    public PlayerScore Score { get; } = new();

    /// <summary>Raised when <see cref="Score"/> has changed and is worth persisting.</summary>
    public event Action? Changed;

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname)
    {
        Score.Nickname = nickname.Trim();
        Changed?.Invoke();
    }

    /// <summary>Silences the board, or lets it speak again. It rides along with the record
    /// because that is what the host already persists.</summary>
    public void ToggleMute()
    {
        Score.Muted = !Score.Muted;
        Changed?.Invoke();
    }

    /// <summary>
    /// Counts a deal once it is over — won, stuck, or proved lost — and once only, reporting
    /// whether anything was counted. Keyed to the board's version rather than to a flag this
    /// class clears: a finished board reports the same state on every frame until it is dealt
    /// again, and dealing again does not always come through the game's reset — the banner's
    /// button resets the board itself. A deal is over at one version and the next deal starts
    /// at another, so the version is the one thing that is true however the game was started.
    /// </summary>
    public bool Update()
    {
        if (board.State == GameState.Playing || recordedVersion == board.Version)
        {
            return false;
        }

        recordedVersion = board.Version;
        Score.Games++;

        if (board.State == GameState.Won)
        {
            Score.Wins++;
        }

        Changed?.Invoke();
        return true;
    }
}
