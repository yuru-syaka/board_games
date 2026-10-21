using System;
using System.Collections.Generic;

namespace BoardGames.BattleLine
{
    public static class BattleLineSetup
    {
        /// <summary>シャッフルして 7 枚ずつ配り、先手をランダムに決める。</summary>
        public static BattleLineState CreateRandom(Random rng)
        {
            var troops = new List<int>(Card.TroopCount);
            for (int i = 0; i < Card.TroopCount; i++) troops.Add(i);
            var tactics = new List<int>(Card.TacticsCount);
            for (int i = Card.TroopCount; i < Card.Total; i++) tactics.Add(i);
            Shuffle(troops, rng);
            Shuffle(tactics, rng);
            return Create(troops, tactics, rng.Next(2));
        }

        /// <summary>
        /// 山札の並び(末尾が一番上)を指定して作る。部隊山札の上から P0, P1, P0, … の順に 7 枚ずつ配る。
        /// </summary>
        public static BattleLineState Create(IReadOnlyList<int> troopDeck, IReadOnlyList<int> tacticsDeck, int firstPlayer)
        {
            if (troopDeck.Count != Card.TroopCount || tacticsDeck.Count != Card.TacticsCount)
                throw new ArgumentException("Deck size mismatch");
            var seen = new bool[Card.Total];
            foreach (int c in troopDeck)
            {
                if (!Card.IsTroop(c) || seen[c]) throw new ArgumentException("Invalid troop deck");
                seen[c] = true;
            }
            foreach (int c in tacticsDeck)
            {
                if (!Card.IsTactic(c) || seen[c]) throw new ArgumentException("Invalid tactics deck");
                seen[c] = true;
            }
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));

            var s = new BattleLineState();
            s.Deck(DeckKind.Troop).AddRange(troopDeck);
            s.Deck(DeckKind.Tactics).AddRange(tacticsDeck);
            var deck = s.Deck(DeckKind.Troop);
            for (int i = 0; i < BattleLineState.HandSize * BattleLineState.Players; i++)
            {
                s.Hand(i % 2).Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
            s.CurrentPlayer = firstPlayer;
            s.Phase = TurnPhase.Play;
            return s;
        }

        static void Shuffle(List<int> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
