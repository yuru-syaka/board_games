using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.CenturyGhost
{
    public enum EndReason
    {
        None,
        /// <summary>どちらかが規定数のおばけを呼び、ラウンドが終わった。</summary>
        GhostsCollected,
        /// <summary>手数の上限に達した。</summary>
        TurnLimit,
    }

    /// <summary>完全情報の対局状態(不変)。山札の並びを含むので CPU や画面には直接渡さない。</summary>
    public sealed class CenturyState
    {
        public IReadOnlyList<PlayerData> Players { get; }
        public Table Table { get; }
        public IReadOnlyList<int> MarketDeck { get; }
        public IReadOnlyList<int> GhostDeck { get; }
        public int FirstPlayer { get; }
        public int CurrentPlayer { get; }
        public int Ply { get; }
        /// <summary>終了条件を満たし、このラウンドで終わる。</summary>
        public bool FinalRound { get; }
        public GameResult Result { get; }
        public EndReason EndReason { get; }
        public CenturyMove LastMove { get; }

        public CenturyState(IEnumerable<PlayerData> players, Table table, IEnumerable<int> marketDeck, IEnumerable<int> ghostDeck,
            int firstPlayer, int currentPlayer, int ply, bool finalRound, GameResult result, EndReason endReason, CenturyMove lastMove)
        {
            Players = players.ToArray();
            if (Players.Count != 2) throw new ArgumentException("Two players required");
            Table = table;
            MarketDeck = marketDeck.ToArray();
            GhostDeck = ghostDeck.ToArray();
            FirstPlayer = firstPlayer;
            CurrentPlayer = currentPlayer;
            Ply = ply;
            FinalRound = finalRound;
            Result = result;
            EndReason = endReason;
            LastMove = lastMove;
        }

        public PlayerData Current => Players[CurrentPlayer];
    }
}
