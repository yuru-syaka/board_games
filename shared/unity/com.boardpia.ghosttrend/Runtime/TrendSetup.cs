using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    public static class TrendSetup
    {
        /// <summary>山札をシャッフルし、最初の出品者をランダムに決めて 1 ラウンド目を配る。</summary>
        public static TrendState CreateRandom(Random rng, int playerCount)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var deck = TrendCards.AllCards().ToList();
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return Create(deck, playerCount, rng.Next(playerCount));
        }

        /// <summary>山札の並び(先頭から配る)と最初の出品者を指定して始める。</summary>
        public static TrendState Create(IEnumerable<int> deck, int playerCount, int firstPlayer)
        {
            if (!TrendLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (firstPlayer < 0 || firstPlayer >= playerCount) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            var w = TrendWork.Empty(playerCount, deck);
            w.StartRound(firstPlayer);
            return w.ToState(null);
        }

        /// <summary>
        /// テスト用: 手札・所持金などを直接決めた局面(配らずに round のラウンド途中から始める)。
        /// counts を省略すると 0 枚、values を省略すると価値なし。
        /// </summary>
        public static TrendState CreateCustom(int[][] hands, int[] money = null, int current = 0, int round = 0,
            int[] counts = null, int[][] values = null, int[][] bought = null, IEnumerable<int> deck = null)
        {
            int n = hands.Length;
            var w = TrendWork.Empty(n, deck ?? Enumerable.Empty<int>());
            for (int p = 0; p < n; p++)
            {
                w.Hands[p].AddRange(hands[p]);
                if (bought != null) w.Bought[p].AddRange(bought[p]);
            }
            if (money != null) w.Money = money.ToArray();
            w.Round = round;
            if (counts != null) w.Counts = counts.ToArray();
            if (values != null) w.Values = values.Select(v => v.ToArray()).ToArray();
            w.Phase = TrendPhase.Play;
            w.Current = current;
            w.Result = GameResult.Ongoing;
            return w.ToState(null);
        }
    }
}
