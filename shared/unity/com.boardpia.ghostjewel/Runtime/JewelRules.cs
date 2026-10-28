using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostJewel
{
    /// <summary>
    /// ゴースト・ジュエルのルール。
    /// 手番では「ちがう色の宝石を 3 個」「同じ色を 2 個」「カードを予約(＋ゴールド)」「カードを買う」のどれか 1 つ。
    /// 手元の宝石は 10 個まで(超えたら返す)。手番の終わりに、条件を満たした館の主が 1 人訪れる(3 点)。
    /// だれかが 15 点に届いたら、その周の最後の人まで続けて終局。点が多い人の勝ち(同点ならカードが少ない人)。
    /// </summary>
    public sealed class JewelRules : IGameRules<JewelState, JewelMove>
    {
        public IReadOnlyList<JewelMove> GetLegalMoves(JewelState s)
        {
            var moves = new List<JewelMove>();
            if (s.Result.IsOver || s.Phase == JewelPhase.Over) return moves;
            var me = s.Current;

            if (s.Phase == JewelPhase.Return)
            {
                for (int g = 0; g < Gem.All; g++)
                    if (me.Gems[g] > 0) moves.Add(JewelMove.Return(g));
                return moves;
            }

            // ちがう色を 1 個ずつ(取れる色の数が 3 未満なら、その数だけ)
            int availableMask = 0;
            for (int c = 0; c < Gem.Colors; c++)
                if (s.Bank[c] > 0) availableMask |= 1 << c;
            int take = Math.Min(3, JewelMove.CountBits(availableMask));
            if (take > 0)
            {
                for (int mask = 1; mask < 1 << Gem.Colors; mask++)
                    if ((mask & ~availableMask) == 0 && JewelMove.CountBits(mask) == take) moves.Add(JewelMove.TakeGems(mask));
            }
            // 同じ色を 2 個
            for (int c = 0; c < Gem.Colors; c++)
                if (s.Bank[c] >= JewelLogic.TakeTwoMinimum) moves.Add(JewelMove.TakeTwo(c));

            // 予約
            if (me.Reserved.Count < JewelLogic.MaxReserved)
            {
                for (int t = 0; t < JewelLogic.Tiers; t++)
                {
                    foreach (int card in s.Market[t])
                        if (card >= 0) moves.Add(JewelMove.Reserve(card));
                    if (s.Decks[t].Count > 0) moves.Add(JewelMove.ReserveDeck(t));
                }
            }

            // 買う(場と、自分の予約)
            for (int t = 0; t < JewelLogic.Tiers; t++)
                foreach (int card in s.Market[t])
                    if (card >= 0 && CanAfford(me, card)) moves.Add(JewelMove.Buy(card));
            foreach (int card in me.Reserved)
                if (card >= 0 && CanAfford(me, card)) moves.Add(JewelMove.Buy(card));

            if (moves.Count == 0) moves.Add(JewelMove.Pass());
            return moves;
        }

        public bool IsLegal(JewelState s, JewelMove move)
        {
            foreach (var m in GetLegalMoves(s))
                if (m.Equals(move)) return true;
            return false;
        }

        /// <summary>割引とゴールドを使って、そのカードを買えるか。</summary>
        public static bool CanAfford(JewelPlayer p, int card) => Shortfall(p, card) <= p.Gems[Gem.Gold];

        /// <summary>割引と色の宝石で足りないぶん(ゴールドで払う必要がある数)。</summary>
        public static int Shortfall(JewelPlayer p, int card)
        {
            var cost = JewelCards.Get(card).Cost;
            int missing = 0;
            for (int c = 0; c < Gem.Colors; c++)
                missing += Math.Max(0, cost[c] - p.Bonus[c] - p.Gems[c]);
            return missing;
        }

        /// <summary>そのカードを買うときに払う宝石(色の宝石を先に使い、足りないぶんをゴールドで)。</summary>
        public static int[] Payment(JewelPlayer p, int card)
        {
            var cost = JewelCards.Get(card).Cost;
            var pay = new int[Gem.All];
            for (int c = 0; c < Gem.Colors; c++)
            {
                int need = Math.Max(0, cost[c] - p.Bonus[c]);
                pay[c] = Math.Min(need, p.Gems[c]);
                pay[Gem.Gold] += need - pay[c];
            }
            return pay;
        }

        public JewelState ApplyMove(JewelState state, JewelMove move)
        {
            if (!IsLegal(state, move)) throw new InvalidOperationException($"非合法の手: {move}");
            var s = state.Clone();
            var me = s.Current;
            int p = s.CurrentPlayer;
            s.Ply++;
            if (move.Kind != MoveKind.Pass && move.Kind != MoveKind.Return) s.PassStreak = 0;

            switch (move.Kind)
            {
                case MoveKind.TakeGems:
                {
                    var got = new int[Gem.All];
                    for (int c = 0; c < Gem.Colors; c++)
                    {
                        if ((move.A & (1 << c)) == 0) continue;
                        s.Bank[c]--;
                        me.Gems[c]++;
                        got[c] = 1;
                    }
                    s.AddEvent(EventKind.TookGems, p, gems: got);
                    break;
                }

                case MoveKind.TakeTwo:
                {
                    var got = new int[Gem.All];
                    s.Bank[move.A] -= 2;
                    me.Gems[move.A] += 2;
                    got[move.A] = 2;
                    s.AddEvent(EventKind.TookGems, p, gems: got);
                    break;
                }

                case MoveKind.Reserve:
                {
                    s.FindInMarket(move.A, out int tier, out int slot);
                    me.Reserved.Add(move.A);
                    me.ReservedFromDeck.Add(false);
                    int gold = TakeGold(s, me);
                    s.AddEvent(EventKind.Reserved, p, move.A, tier, slot, amount: gold);
                    Refill(s, tier, slot);
                    break;
                }

                case MoveKind.ReserveDeck:
                {
                    var deck = s.Decks[move.A];
                    int card = deck[deck.Count - 1];
                    deck.RemoveAt(deck.Count - 1);
                    me.Reserved.Add(card);
                    me.ReservedFromDeck.Add(true);
                    int gold = TakeGold(s, me);
                    s.AddEvent(EventKind.Reserved, p, card, move.A, -1, amount: gold);
                    break;
                }

                case MoveKind.Buy:
                {
                    var pay = Payment(me, move.A);
                    for (int g = 0; g < Gem.All; g++)
                    {
                        me.Gems[g] -= pay[g];
                        s.Bank[g] += pay[g];
                    }
                    var info = JewelCards.Get(move.A);
                    me.Bonus[info.Bonus]++;
                    me.Points += info.Points;
                    me.Bought.Add(move.A);
                    int index = me.Reserved.IndexOf(move.A);
                    if (index >= 0)
                    {
                        me.Reserved.RemoveAt(index);
                        me.ReservedFromDeck.RemoveAt(index);
                        s.AddEvent(EventKind.Bought, p, move.A, info.Tier, -1, gems: pay);
                    }
                    else
                    {
                        s.FindInMarket(move.A, out int tier, out int slot);
                        s.AddEvent(EventKind.Bought, p, move.A, tier, slot, gems: pay);
                        Refill(s, tier, slot);
                    }
                    break;
                }

                case MoveKind.Return:
                    me.Gems[move.A]--;
                    s.Bank[move.A]++;
                    s.AddEvent(EventKind.Returned, p, gem: move.A);
                    break;

                case MoveKind.Pass:
                    s.PassStreak++;
                    s.AddEvent(EventKind.Passed, p);
                    break;
            }

            // 宝石が多すぎるなら、同じ人が返し続ける
            if (me.GemTotal > JewelLogic.MaxGems)
            {
                s.Phase = JewelPhase.Return;
                return s;
            }
            EndTurn(s);
            return s;
        }

        static int TakeGold(JewelState s, JewelPlayer me)
        {
            if (s.Bank[Gem.Gold] <= 0) return 0;
            s.Bank[Gem.Gold]--;
            me.Gems[Gem.Gold]++;
            return 1;
        }

        static void Refill(JewelState s, int tier, int slot)
        {
            var deck = s.Decks[tier];
            int card = -1;
            if (deck.Count > 0)
            {
                card = deck[deck.Count - 1];
                deck.RemoveAt(deck.Count - 1);
            }
            s.Market[tier][slot] = card;
            s.AddEvent(EventKind.Refilled, -1, card, tier, slot);
        }

        /// <summary>館の主の訪問 → 目標の点の確認 → 次の人へ(最終ラウンドが一巡したら終局)。</summary>
        static void EndTurn(JewelState s)
        {
            var me = s.Current;
            int p = s.CurrentPlayer;
            s.Phase = JewelPhase.Main;

            // 条件を満たした館の主のうち、番号のいちばん小さい 1 人が訪れる
            int visitor = s.Nobles.Find(n => Qualifies(me, n));
            if (s.Nobles.Contains(visitor) && Qualifies(me, visitor))
            {
                s.Nobles.Remove(visitor);
                me.Nobles.Add(visitor);
                me.Points += JewelCards.GetNoble(visitor).Points;
                s.AddEvent(EventKind.NobleVisit, p, noble: visitor);
            }

            if (!s.FinalRound && me.Points >= JewelLogic.WinPoints)
            {
                s.FinalRound = true;
                s.AddEvent(EventKind.FinalRound, p, amount: me.Points);
            }

            int next = (p + 1) % s.PlayerCount;
            bool roundOver = next == s.StartPlayer;
            bool stuck = s.PassStreak >= s.PlayerCount;
            if ((s.FinalRound && roundOver) || stuck || s.Ply >= JewelLogic.MaxPlies)
            {
                Finish(s);
                return;
            }
            s.CurrentPlayer = next;
        }

        public static bool Qualifies(JewelPlayer p, int noble)
        {
            var need = JewelCards.GetNoble(noble).Need;
            for (int c = 0; c < Gem.Colors; c++)
                if (p.Bonus[c] < need[c]) return false;
            return true;
        }

        static void Finish(JewelState s)
        {
            s.Phase = JewelPhase.Over;
            var order = Ranking(s);
            int best = order[0];
            bool tie = order.Count > 1 && Compare(s, order[0], order[1]) == 0;
            s.Result = tie ? GameResult.Draw : GameResult.WinFor(best);
            s.AddEvent(EventKind.GameEnd, tie ? -1 : best);
        }

        /// <summary>順位の並び(点が多い順。同点ならカードが少ない順)。</summary>
        public static List<int> Ranking(JewelState s)
        {
            var order = new List<int>();
            for (int i = 0; i < s.PlayerCount; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = Compare(s, a, b);
                return c != 0 ? c : a.CompareTo(b);
            });
            return order;
        }

        /// <summary>a のほうが上位なら負。同順位なら 0。</summary>
        static int Compare(JewelState s, int a, int b)
        {
            var pa = s.Players[a];
            var pb = s.Players[b];
            if (pa.Points != pb.Points) return pb.Points.CompareTo(pa.Points);
            return pa.Bought.Count.CompareTo(pb.Bought.Count);
        }

        public GameResult GetResult(JewelState state) => state.Result;
    }

    /// <summary>対局の始まりの局面を作る。</summary>
    public static class JewelSetup
    {
        public static JewelState CreateRandom(Random rng, int playerCount)
        {
            if (!JewelLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            var s = new JewelState(playerCount);
            int each = JewelLogic.GemsPerColor(playerCount);
            for (int c = 0; c < Gem.Colors; c++) s.Bank[c] = each;
            s.Bank[Gem.Gold] = JewelLogic.GoldCount;

            for (int t = 0; t < JewelLogic.Tiers; t++)
            {
                var ids = JewelCards.TierIds(t);
                Shuffle(ids, rng);
                s.Decks[t].AddRange(ids);
                for (int slot = 0; slot < JewelLogic.MarketSize; slot++)
                {
                    var deck = s.Decks[t];
                    s.Market[t][slot] = deck[deck.Count - 1];
                    deck.RemoveAt(deck.Count - 1);
                }
            }

            var nobles = new List<int>();
            for (int i = 0; i < JewelCards.Nobles.Count; i++) nobles.Add(i);
            Shuffle(nobles, rng);
            for (int i = 0; i < JewelLogic.NobleCount(playerCount); i++) s.Nobles.Add(nobles[i]);
            s.Nobles.Sort();

            s.StartPlayer = rng.Next(playerCount);
            s.CurrentPlayer = s.StartPlayer;
            s.Phase = JewelPhase.Main;
            s.AddEvent(EventKind.GameStart, s.StartPlayer);
            return s;
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
