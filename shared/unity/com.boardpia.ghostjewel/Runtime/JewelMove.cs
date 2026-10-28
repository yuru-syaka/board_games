namespace BoardGames.GhostJewel
{
    public enum MoveKind
    {
        /// <summary>ちがう色の宝石を 1 個ずつ(ふつうは 3 色。残りの色が少なければその数)。A = 色のビット。</summary>
        TakeGems,
        /// <summary>同じ色の宝石を 2 個。A = 色(その山に 4 個以上あるときだけ)。</summary>
        TakeTwo,
        /// <summary>場のカードを予約する(ゴールドが残っていれば 1 個もらえる)。A = カード ID。</summary>
        Reserve,
        /// <summary>山札のいちばん上を見ないで予約する。A = 段(0..2)。</summary>
        ReserveDeck,
        /// <summary>場か、自分が予約したカードを買う。A = カード ID。</summary>
        Buy,
        /// <summary>10 個を超えた宝石を 1 個返す。A = 種類(0..5)。</summary>
        Return,
        /// <summary>何もできないときだけ、手番を飛ばす。</summary>
        Pass,
    }

    public readonly struct JewelMove : System.IEquatable<JewelMove>
    {
        public readonly MoveKind Kind;
        public readonly int A;

        JewelMove(MoveKind kind, int a)
        {
            Kind = kind;
            A = a;
        }

        public static JewelMove TakeGems(int colorMask) => new JewelMove(MoveKind.TakeGems, colorMask);
        public static JewelMove TakeGemsOf(params int[] colors)
        {
            int mask = 0;
            foreach (var c in colors) mask |= 1 << c;
            return TakeGems(mask);
        }
        public static JewelMove TakeTwo(int color) => new JewelMove(MoveKind.TakeTwo, color);
        public static JewelMove Reserve(int card) => new JewelMove(MoveKind.Reserve, card);
        public static JewelMove ReserveDeck(int tier) => new JewelMove(MoveKind.ReserveDeck, tier);
        public static JewelMove Buy(int card) => new JewelMove(MoveKind.Buy, card);
        public static JewelMove Return(int gem) => new JewelMove(MoveKind.Return, gem);
        public static JewelMove Pass() => new JewelMove(MoveKind.Pass, 0);

        /// <summary>種類と値から作る(通信で届いた手を組み立て直すとき。合法かどうかはルールが確かめる)。</summary>
        public static JewelMove Create(MoveKind kind, int a) => new JewelMove(kind, kind == MoveKind.Pass ? 0 : a);

        public static int CountBits(int mask)
        {
            int n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        public bool Equals(JewelMove other) => Kind == other.Kind && A == other.A;
        public override bool Equals(object obj) => obj is JewelMove m && Equals(m);
        public override int GetHashCode() => ((int)Kind * 397) ^ A;
        public override string ToString() => $"{Kind}({A})";
    }
}
