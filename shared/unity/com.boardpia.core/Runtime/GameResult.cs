namespace BoardGames.Core
{
    public enum GameStatus
    {
        Ongoing,
        Win,
        Draw,
    }

    /// <summary>対局結果。Winner は Status == Win のときだけ有効(それ以外は -1)。</summary>
    public readonly struct GameResult
    {
        public readonly GameStatus Status;
        public readonly int Winner;

        GameResult(GameStatus status, int winner)
        {
            Status = status;
            Winner = winner;
        }

        public static GameResult Ongoing => new GameResult(GameStatus.Ongoing, -1);
        public static GameResult Draw => new GameResult(GameStatus.Draw, -1);
        public static GameResult WinFor(int player) => new GameResult(GameStatus.Win, player);

        public bool IsOver => Status != GameStatus.Ongoing;

        public override string ToString() => Status == GameStatus.Win ? $"Win(P{Winner})" : Status.ToString();
    }
}
