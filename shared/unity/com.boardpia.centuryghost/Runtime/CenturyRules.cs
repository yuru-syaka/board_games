using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.CenturyGhost
{
    public sealed class CenturyRules : IGameRules<CenturyState, CenturyMove>
    {
        public IReadOnlyList<CenturyMove> GetLegalMoves(CenturyState state) =>
            state.Result.IsOver ? new List<CenturyMove>() : CenturyLogic.GenerateMoves(state.Current, state.Table);

        public bool IsLegal(CenturyState state, CenturyMove move) =>
            !state.Result.IsOver && CenturyLogic.IsLegal(state.Current, state.Table, move);

        public CenturyState ApplyMove(CenturyState state, CenturyMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move {move}");

            int me = state.CurrentPlayer;
            var (player, table) = CenturyLogic.Resolve(state.Current, state.Table, move);

            // 山札から右端に補充する
            var market = table.Market.ToList();
            var marketSpirits = table.MarketSpirits.ToList();
            var marketDeck = state.MarketDeck.ToList();
            while (market.Count < CenturyLogic.MarketSize && marketDeck.Count > 0)
            {
                market.Add(marketDeck[0]);
                marketSpirits.Add(Spirits.Empty);
                marketDeck.RemoveAt(0);
            }
            var ghostRow = table.GhostRow.ToList();
            var ghostDeck = state.GhostDeck.ToList();
            while (ghostRow.Count < CenturyLogic.GhostRowSize && ghostDeck.Count > 0)
            {
                ghostRow.Add(ghostDeck[0]);
                ghostDeck.RemoveAt(0);
            }
            table = new Table(market, marketSpirits, ghostRow, table.GoldLeft, table.SilverLeft);

            var players = state.Players.ToArray();
            players[me] = player;
            bool finalRound = state.FinalRound || player.Ghosts.Count >= CenturyLogic.GhostsToEnd;
            int next = 1 - me;
            int ply = state.Ply + 1;

            var result = GameResult.Ongoing;
            var reason = EndReason.None;
            if (finalRound && next == state.FirstPlayer)
            {
                result = Judge(players);
                reason = EndReason.GhostsCollected;
            }
            else if (ply >= CenturyLogic.MaxPlies)
            {
                result = Judge(players);
                reason = EndReason.TurnLimit;
            }

            return new CenturyState(players, table, marketDeck, ghostDeck, state.FirstPlayer, next, ply, finalRound, result, reason,
                move);
        }

        public GameResult GetResult(CenturyState state) => state.Result;

        static GameResult Judge(IReadOnlyList<PlayerData> players)
        {
            int s0 = players[0].Score, s1 = players[1].Score;
            if (s0 == s1) return GameResult.Draw;
            return GameResult.WinFor(s0 > s1 ? 0 : 1);
        }
    }
}
