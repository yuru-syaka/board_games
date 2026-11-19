using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostGold
{
    /// <summary>
    /// ゴースト・ゴールドのルール(docs/games/ghostgold.md)。
    /// 手は「探検中の人が、席の番号の順に 1 人ずつ『進む・帰る』を決める」だけ。最後の 1 人が決めたら、
    /// 公開・帰る人の精算・次の 1 枚・夜の終わり・次の夜の準備までを、その手の中でまとめて進める。
    /// </summary>
    public sealed class GoldRules : IGameRules<GoldState, GoldMove>
    {
        static readonly GoldMove[] Both = { GoldMove.Go, GoldMove.Return };

        public IReadOnlyList<GoldMove> GetLegalMoves(GoldState state) =>
            state.Result.IsOver || state.CurrentPlayer < 0 ? Array.Empty<GoldMove>() : Both;

        public bool IsLegal(GoldState state, GoldMove move) =>
            !state.Result.IsOver && state.Phase == GoldPhase.Decide && state.CurrentPlayer >= 0 && move.IsValid;

        public GameResult GetResult(GoldState state) => state.Result;

        public GoldState ApplyMove(GoldState state, GoldMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move: {move}");
            var s = state.Clone();
            int p = s.CurrentPlayer;
            s.Players[p].Choice = move.Choice;
            s.Players[p].Decided = true;
            s.AddEvent(EventKind.Decided, p);
            s.Ply++;

            s.CurrentPlayer = NextToDecide(s);
            if (s.CurrentPlayer < 0) Resolve(s);
            return s;
        }

        // ---------- 対局の始まり ----------

        /// <summary>1 夜目を始める(山札を作り、最初の 1 枚をめくる)。</summary>
        public static void Begin(GoldState s)
        {
            s.Night = 1;
            StartNight(s);
        }

        // ---------- 決定の公開 ----------

        static int NextToDecide(GoldState s)
        {
            for (int i = 0; i < s.PlayerCount; i++)
                if (s.Players[i].Exploring && !s.Players[i].Decided) return i;
            return -1;
        }

        static void Resolve(GoldState s)
        {
            var returners = new List<int>();
            for (int i = 0; i < s.PlayerCount; i++)
            {
                var pl = s.Players[i];
                if (!pl.Exploring) continue;
                s.AddEvent(EventKind.Chose, i, amount: (int)pl.Choice);
                if (pl.Choice == Choice.Return) returners.Add(i);
            }
            foreach (var pl in s.Players)
            {
                pl.Choice = Choice.None;
                pl.Decided = false;
            }

            ReturnHome(s, returners);
            if (s.ExploringCount == 0) EndNight(s, NightEndReason.AllReturned);
            else Reveal(s);
        }

        /// <summary>returners が帰る: 通路の余りを等分し、1 人だけなら秘宝ももらい、手持ちと分け前を金庫に入れる。</summary>
        static void ReturnHome(GoldState s, List<int> returners)
        {
            if (returners.Count == 0) return;
            int share = s.Leftover / returners.Count;
            s.Leftover -= share * returners.Count;
            foreach (int p in returners)
            {
                var pl = s.Players[p];
                int bank = pl.Carry + share;
                pl.Safe += bank;
                s.AddEvent(EventKind.Returned, p, amount: bank, amount2: share);
                pl.Carry = 0;
                pl.Exploring = false;
            }
            if (returners.Count == 1)
            {
                int p = returners[0];
                for (int n = 1; n <= GoldLogic.RelicCount; n++)
                {
                    if (s.Relics[n - 1] != RelicStatus.OnPath) continue;
                    s.Relics[n - 1] = RelicStatus.Taken;
                    s.Players[p].Relics.Add(GoldCards.RelicCard(n));
                    s.AddEvent(EventKind.RelicTaken, p, GoldCards.RelicCard(n));
                }
            }
        }

        // ---------- めくる ----------

        static void Reveal(GoldState s)
        {
            if (s.Deck.Count == 0)
            {
                s.AddEvent(EventKind.DeckEmpty);
                var all = Enumerable.Range(0, s.PlayerCount).Where(i => s.Players[i].Exploring).ToList();
                ReturnHome(s, all);
                EndNight(s, NightEndReason.DeckEmpty);
                return;
            }

            int card = s.Deck[s.Deck.Count - 1];
            s.Deck.RemoveAt(s.Deck.Count - 1);
            s.Path.Add(card);

            switch (GoldCards.KindOf(card))
            {
                case CardKind.Treasure:
                {
                    int n = s.ExploringCount;
                    int gold = GoldCards.TreasureOf(card);
                    int share = gold / n, rest = gold % n;
                    foreach (var pl in s.Players)
                        if (pl.Exploring) pl.Carry += share;
                    s.Leftover += rest;
                    s.AddEvent(EventKind.Reveal, card: card, amount: share, amount2: rest);
                    break;
                }
                case CardKind.Relic:
                    s.Relics[GoldCards.RelicNight(card) - 1] = RelicStatus.OnPath;
                    s.AddEvent(EventKind.Reveal, card: card);
                    break;
                default:
                {
                    int kind = GoldCards.HazardOf(card);
                    s.AddEvent(EventKind.Reveal, card: card, amount: kind);
                    if (s.HazardOnPath(kind) >= 2)
                    {
                        Scare(s, card, kind);
                        return;
                    }
                    break;
                }
            }

            s.Phase = GoldPhase.Decide;
            s.CurrentPlayer = NextToDecide(s);
        }

        /// <summary>同じ怪異が 2 枚そろった: 探検中の人は手持ちを失う(お守りがあれば半分を金庫へ)。その怪異を 1 枚取り除く。</summary>
        static void Scare(GoldState s, int card, int kind)
        {
            s.AddEvent(EventKind.Scared, card: card, amount: kind);
            for (int i = 0; i < s.PlayerCount; i++)
            {
                var pl = s.Players[i];
                if (!pl.Exploring) continue;
                int lost = pl.Carry;
                if (pl.HasAmulet && pl.Carry >= 2)
                {
                    int saved = pl.Carry / 2;
                    pl.Safe += saved;
                    pl.HasAmulet = false;
                    lost -= saved;
                    s.AddEvent(EventKind.Amulet, i, amount: saved);
                }
                if (lost > 0) s.AddEvent(EventKind.Lost, i, amount: lost);
                pl.Carry = 0;
                pl.Exploring = false;
            }
            s.HazardRemoved[kind]++;
            s.AddEvent(EventKind.HazardRemoved, card: card, amount: kind);
            EndNight(s, NightEndReason.Scared);
        }

        // ---------- 夜の始まりと終わり ----------

        static void EndNight(GoldState s, int reason)
        {
            for (int n = 1; n <= GoldLogic.RelicCount; n++)
            {
                if (s.Relics[n - 1] != RelicStatus.OnPath) continue;
                s.Relics[n - 1] = RelicStatus.Lost;
                s.AddEvent(EventKind.RelicLost, card: GoldCards.RelicCard(n));
            }
            s.Leftover = 0; // 通路の余りは、だれも持ち帰らなければなくなる
            s.AddEvent(EventKind.NightEnd, amount: s.Night, amount2: reason);
            s.CurrentPlayer = -1;

            if (s.Night >= GoldLogic.Nights)
            {
                Finish(s);
                return;
            }
            s.Night++;
            StartNight(s);
        }

        static void StartNight(GoldState s)
        {
            s.Path.Clear();
            s.Leftover = 0;
            if (s.Relics[s.Night - 1] == RelicStatus.NotYet) s.Relics[s.Night - 1] = RelicStatus.InDeck;

            s.Deck.Clear();
            for (int c = 0; c < GoldLogic.TreasureCount; c++) s.Deck.Add(c);
            for (int k = 0; k < GoldLogic.HazardKinds; k++)
                for (int copy = 0; copy < GoldLogic.HazardCopies - s.HazardRemoved[k]; copy++)
                    s.Deck.Add(GoldCards.HazardCard(k, copy));
            for (int n = 1; n <= GoldLogic.RelicCount; n++)
                if (s.Relics[n - 1] == RelicStatus.InDeck) s.Deck.Add(GoldCards.RelicCard(n));
            Shuffle(s.Deck, new Random(unchecked(s.Seed * 1000003 + s.Night * 7919 + 13)));

            foreach (var pl in s.Players)
            {
                pl.Exploring = true;
                pl.Carry = 0;
                pl.Choice = Choice.None;
                pl.Decided = false;
            }
            s.Phase = GoldPhase.Decide;
            s.AddEvent(EventKind.NightStart, amount: s.Night, amount2: s.Deck.Count);
            Reveal(s);
        }

        static void Finish(GoldState s)
        {
            s.Phase = GoldPhase.Over;
            s.CurrentPlayer = -1;
            var ranks = Ranking(s);
            var first = Enumerable.Range(0, s.PlayerCount).Where(i => ranks[i] == 0).ToList();
            s.Result = first.Count == 1 ? GameResult.WinFor(first[0]) : GameResult.Draw;
            s.AddEvent(EventKind.GameEnd, s.Result.Winner);
        }

        /// <summary>席ごとの順位(0 = 1 位)。点数の多い順、同じなら秘宝の数の多い順。それも同じなら同じ順位。</summary>
        public static int[] Ranking(GoldState s)
        {
            var ranks = new int[s.PlayerCount];
            for (int i = 0; i < s.PlayerCount; i++)
                ranks[i] = Enumerable.Range(0, s.PlayerCount).Count(j => Better(s.Players[j], s.Players[i]));
            return ranks;
        }

        static bool Better(GoldPlayer a, GoldPlayer b) =>
            a.Score > b.Score || (a.Score == b.Score && a.Relics.Count > b.Relics.Count);

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>対局の準備。</summary>
    public static class GoldSetup
    {
        /// <summary>種 seed で、playerCount 人の対局を始める(1 夜目の最初の 1 枚までめくった局面)。</summary>
        public static GoldState Create(int playerCount, int seed)
        {
            if (!GoldLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            var s = new GoldState(playerCount) { Seed = seed };
            GoldRules.Begin(s);
            return s;
        }

        public static GoldState CreateRandom(Random rng, int playerCount) => Create(playerCount, (rng ?? new Random()).Next());
    }
}
