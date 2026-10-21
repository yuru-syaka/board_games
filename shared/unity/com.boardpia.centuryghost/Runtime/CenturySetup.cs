using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.CenturyGhost
{
    /// <summary>初期状態の生成。</summary>
    public static class CenturySetup
    {
        /// <summary>山札をシャッフルし、先手をランダムに決める。</summary>
        public static CenturyState CreateRandom(Random rng)
        {
            var market = CardCatalog.MarketCardIds().ToList();
            var ghosts = Enumerable.Range(0, CardCatalog.GhostCount).ToList();
            Shuffle(market, rng);
            Shuffle(ghosts, rng);
            return Create(market, ghosts, rng.Next(2));
        }

        /// <summary>山札の並び(先頭から場に出る)と先手を指定して作る。</summary>
        public static CenturyState Create(IList<int> marketOrder, IList<int> ghostOrder, int firstPlayer)
        {
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            int m = Math.Min(CenturyLogic.MarketSize, marketOrder.Count);
            int g = Math.Min(CenturyLogic.GhostRowSize, ghostOrder.Count);
            var table = new Table(marketOrder.Take(m), Enumerable.Repeat(Spirits.Empty, m), ghostOrder.Take(g),
                CenturyLogic.InitialGold, CenturyLogic.InitialSilver);

            var players = new PlayerData[2];
            for (int p = 0; p < 2; p++)
            {
                int yellow = p == firstPlayer ? 3 : 4;
                players[p] = new PlayerData(new Spirits(yellow, 0, 0, 0), CardCatalog.StartingHands[p], new int[0], new int[0], 0, 0);
            }
            return new CenturyState(players, table, marketOrder.Skip(m), ghostOrder.Skip(g), firstPlayer, firstPlayer, 0, false,
                GameResult.Ongoing, EndReason.None, null);
        }

        /// <summary>テスト用: 任意の局面を作る。</summary>
        public static CenturyState CreateCustom(PlayerData p0, PlayerData p1, Table table, IEnumerable<int> marketDeck,
            IEnumerable<int> ghostDeck, int firstPlayer, int currentPlayer, int ply = 0, bool finalRound = false) =>
            new CenturyState(new[] { p0, p1 }, table, marketDeck ?? new int[0], ghostDeck ?? new int[0], firstPlayer, currentPlayer,
                ply, finalRound, GameResult.Ongoing, EndReason.None, null);

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
