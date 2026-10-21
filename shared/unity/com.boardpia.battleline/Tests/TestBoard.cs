using System.Collections.Generic;
using System.Linq;

namespace BoardGames.BattleLine.Tests
{
    /// <summary>テスト用の局面づくり。カードは必ずどこか 1 か所にあるように移動させる(枚数が保存される)。</summary>
    static class TestBoard
    {
        public const int R = 0, O = 1, Y = 2, G = 3, B = 4, P = 5;

        public static int T(int color, int value) => Card.Troop(color, value);

        /// <summary>山札を Id 順に並べ、P0 先手で配った局面。</summary>
        public static BattleLineState Fresh(int first = 0)
        {
            var troops = Enumerable.Range(0, Card.TroopCount).ToList();
            var tactics = Enumerable.Range(Card.TroopCount, Card.TacticsCount).ToList();
            return BattleLineSetup.Create(troops, tactics, first);
        }

        /// <summary>手札も山札もフラッグも空にして、全カードを山札に戻した局面。</summary>
        public static BattleLineState Empty(int current = 0)
        {
            var s = Fresh(current);
            for (int p = 0; p < 2; p++)
            {
                s.Deck(DeckKind.Troop).AddRange(s.Hand(p));
                s.Hand(p).Clear();
            }
            return s;
        }

        public static void Take(BattleLineState s, int card)
        {
            for (int p = 0; p < 2; p++) s.Hand(p).Remove(card);
            s.Deck(DeckKind.Troop).Remove(card);
            s.Deck(DeckKind.Tactics).Remove(card);
            s.DiscardPile.Remove(card);
            foreach (var f in s.Flags) s.FlagAt(f.Index).Remove(card);
        }

        public static void Put(BattleLineState s, int player, int flag, params int[] cards)
        {
            foreach (int c in cards)
            {
                Take(s, c);
                s.FlagAt(flag).AddCard(player, c);
            }
            UpdateSeq(s, flag);
        }

        public static void Env(BattleLineState s, int player, int flag, int card)
        {
            Take(s, card);
            s.FlagAt(flag).AddEnvironment(card, player);
            UpdateSeq(s, flag);
        }

        public static void Hand(BattleLineState s, int player, params int[] cards)
        {
            foreach (int c in s.Hand(player).ToList())
                s.Deck(Card.DeckOf(c)).Add(c);
            s.Hand(player).Clear();
            foreach (int c in cards)
            {
                Take(s, c);
                s.Hand(player).Add(c);
            }
        }

        public static void Discard(BattleLineState s, params int[] cards)
        {
            foreach (int c in cards)
            {
                Take(s, c);
                s.DiscardPile.Add(c);
            }
        }

        public static void Claim(BattleLineState s, int player, params int[] flags)
        {
            foreach (int f in flags) s.FlagAt(f).SetClaimer(player);
        }

        /// <summary>置いた順に完成順を振る(Put を呼んだ順 = 完成した順)。</summary>
        static void UpdateSeq(BattleLineState s, int flag)
        {
            var f = s.FlagAt(flag);
            for (int p = 0; p < 2; p++)
            {
                if (!f.IsComplete(p)) f.SetCompletedSeq(p, -1);
                else if (f.CompletedSeq(p) < 0) f.SetCompletedSeq(p, ++s.Seq);
            }
        }

        public static int CountAll(BattleLineState s)
        {
            var all = new List<int>();
            all.AddRange(s.HandOf(0));
            all.AddRange(s.HandOf(1));
            all.AddRange(s.TroopDeck);
            all.AddRange(s.TacticsDeck);
            all.AddRange(s.Discard);
            foreach (var f in s.Flags)
            {
                all.AddRange(f.CardsOf(0));
                all.AddRange(f.CardsOf(1));
                all.AddRange(f.Environment);
            }
            return all.Count == all.Distinct().Count() ? all.Count : -1;
        }
    }
}
