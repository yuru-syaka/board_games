using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardGames.CenturyGhost
{
    public enum MoveKind
    {
        /// <summary>術を使う(Target = カード Id)。</summary>
        Play,
        /// <summary>術を覚える(Target = 市場の位置。左端 = 0)。</summary>
        Acquire,
        /// <summary>おばけを呼ぶ(Target = おばけの列の位置。左端 = 0)。</summary>
        Claim,
        /// <summary>休む。</summary>
        Rest,
    }

    /// <summary>1 手(不変)。</summary>
    public sealed class CenturyMove : IEquatable<CenturyMove>
    {
        static readonly int[] NoUpgrades = new int[0];

        public MoveKind Kind { get; }
        public int Target { get; }
        /// <summary>交換の回数(交換以外は 0)。</summary>
        public int Times { get; }
        /// <summary>強化で上げる魂のランクを順に並べたもの(強化以外は空)。</summary>
        public IReadOnlyList<int> Upgrades { get; }

        CenturyMove(MoveKind kind, int target, int times, IReadOnlyList<int> upgrades)
        {
            Kind = kind;
            Target = target;
            Times = times;
            Upgrades = upgrades ?? NoUpgrades;
        }

        public static CenturyMove Play(int cardId) => new CenturyMove(MoveKind.Play, cardId, 0, null);
        public static CenturyMove Trade(int cardId, int times) => new CenturyMove(MoveKind.Play, cardId, times, null);
        public static CenturyMove Upgrade(int cardId, params int[] ranks) =>
            new CenturyMove(MoveKind.Play, cardId, 0, (int[])ranks.Clone());
        public static CenturyMove Acquire(int index) => new CenturyMove(MoveKind.Acquire, index, 0, null);
        public static CenturyMove Claim(int index) => new CenturyMove(MoveKind.Claim, index, 0, null);
        public static CenturyMove Rest() => new CenturyMove(MoveKind.Rest, 0, 0, null);

        /// <summary>通信・テスト用に部品から作る。</summary>
        public static CenturyMove FromParts(MoveKind kind, int target, int times, IEnumerable<int> upgrades) =>
            new CenturyMove(kind, target, times, upgrades?.ToArray());

        public bool Equals(CenturyMove o) =>
            o != null && Kind == o.Kind && Target == o.Target && Times == o.Times && Upgrades.SequenceEqual(o.Upgrades);

        public override bool Equals(object obj) => Equals(obj as CenturyMove);

        public override int GetHashCode()
        {
            int h = ((int)Kind * 397) ^ (Target * 31) ^ Times;
            foreach (int r in Upgrades) h = h * 7 + r;
            return h;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case MoveKind.Play:
                    if (Times > 0) return $"Play#{Target}x{Times}";
                    if (Upgrades.Count > 0) return $"Play#{Target}[{string.Join(",", Upgrades)}]";
                    return $"Play#{Target}";
                case MoveKind.Acquire: return $"Acquire{Target}";
                case MoveKind.Claim: return $"Claim{Target}";
                default: return "Rest";
            }
        }
    }
}
