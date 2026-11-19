using System;

namespace BoardGames.GhostGold
{
    /// <summary>「進む」か「帰る」か。</summary>
    public enum Choice
    {
        /// <summary>まだ決めていない(または、ほかの人からは見えない)。</summary>
        None = -1,
        Go = 0,
        Return = 1,
    }

    /// <summary>手。探検中の人が「進む」か「帰る」かを決める、の 1 種類だけ。</summary>
    public readonly struct GoldMove : IEquatable<GoldMove>
    {
        public readonly Choice Choice;

        GoldMove(Choice choice)
        {
            Choice = choice;
        }

        public static GoldMove Go => new GoldMove(Choice.Go);
        public static GoldMove Return => new GoldMove(Choice.Return);

        /// <summary>通信などから作る。Go・Return 以外は不正な手(合法手にはならない)。</summary>
        public static GoldMove Create(Choice choice) => new GoldMove(choice);

        public bool IsValid => Choice == Choice.Go || Choice == Choice.Return;

        public bool Equals(GoldMove other) => Choice == other.Choice;
        public override bool Equals(object obj) => obj is GoldMove m && Equals(m);
        public override int GetHashCode() => (int)Choice;
        public override string ToString() => Choice.ToString();
    }
}
