using System;

namespace BoardGames.GhostDerby
{
    public enum MoveKind
    {
        /// <summary>先賭け(A = おばけ、B = コイン)。</summary>
        Bet,
        /// <summary>いたずら札を山札へ(A = カード、B = 相手のおばけ。追い風は -1)。</summary>
        Trick,
        /// <summary>追い賭け(A = おばけ、B = コイン)。</summary>
        Chase,
        /// <summary>追い賭けをしない。</summary>
        Pass,
    }

    public readonly struct DerbyMove : IEquatable<DerbyMove>
    {
        public readonly MoveKind Kind;
        public readonly int A;
        public readonly int B;

        DerbyMove(MoveKind kind, int a, int b)
        {
            Kind = kind;
            A = a;
            B = b;
        }

        public static DerbyMove Bet(int runner, int amount) => new DerbyMove(MoveKind.Bet, runner, amount);
        public static DerbyMove Trick(int card, int target) => new DerbyMove(MoveKind.Trick, card, target);
        public static DerbyMove Chase(int runner, int amount) => new DerbyMove(MoveKind.Chase, runner, amount);
        public static DerbyMove Pass => new DerbyMove(MoveKind.Pass, -1, 0);

        /// <summary>通信から作る(合法かどうかはルールが判断する)。</summary>
        public static DerbyMove Create(MoveKind kind, int a, int b) => new DerbyMove(kind, a, b);

        public bool Equals(DerbyMove o) => Kind == o.Kind && A == o.A && B == o.B;
        public override bool Equals(object obj) => obj is DerbyMove m && Equals(m);
        public override int GetHashCode() => ((int)Kind * 397 ^ A) * 397 ^ B;
        public override string ToString() => $"{Kind}({A},{B})";
    }
}
