using System.Collections.Generic;
using System.Linq;

namespace BoardGames.CenturyGhost
{
    /// <summary>
    /// あるプレイヤー視点の局面(不変)。山札は枚数だけで、並び順は含まない。
    /// CPU と画面にはこれだけを渡す。
    /// </summary>
    public sealed class CenturyPlayerView
    {
        public int Viewer { get; }
        public IReadOnlyList<PlayerData> Players { get; }
        public Table Table { get; }
        public int MarketDeckCount { get; }
        public int GhostDeckCount { get; }
        public int FirstPlayer { get; }
        public int CurrentPlayer { get; }
        public int Ply { get; }
        public bool FinalRound { get; }
        public CenturyMove LastMove { get; }

        public CenturyPlayerView(int viewer, IEnumerable<PlayerData> players, Table table, int marketDeckCount, int ghostDeckCount,
            int firstPlayer, int currentPlayer, int ply, bool finalRound, CenturyMove lastMove)
        {
            Viewer = viewer;
            Players = players.ToArray();
            Table = table;
            MarketDeckCount = marketDeckCount;
            GhostDeckCount = ghostDeckCount;
            FirstPlayer = firstPlayer;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            FinalRound = finalRound;
            LastMove = lastMove;
        }

        public static CenturyPlayerView Create(CenturyState s, int viewer) =>
            new CenturyPlayerView(viewer, s.Players, s.Table, s.MarketDeck.Count, s.GhostDeck.Count, s.FirstPlayer, s.CurrentPlayer,
                s.Ply, s.FinalRound, s.LastMove);

        public PlayerData Me => Players[Viewer];
        public PlayerData Opponent => Players[1 - Viewer];

        /// <summary>手番のプレイヤーの合法手。</summary>
        public List<CenturyMove> GetLegalMoves() => CenturyLogic.GenerateMoves(Players[CurrentPlayer], Table);

        public bool IsLegal(CenturyMove move) => CenturyLogic.IsLegal(Players[CurrentPlayer], Table, move);
    }
}
