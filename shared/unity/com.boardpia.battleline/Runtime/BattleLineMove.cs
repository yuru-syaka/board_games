using System;

namespace BoardGames.BattleLine
{
    public enum MoveKind
    {
        /// <summary>部隊カード・士気戦術を自分側の隊列に置く / 環境戦術をフラッグに置く。Card, Flag。</summary>
        Place,
        /// <summary>偵察を出す。Card。</summary>
        Scout,
        /// <summary>再配置: 自分のカード Target(Flag にある)を ToFlag へ(−1 なら捨てる)。Card, Flag, Target, ToFlag。</summary>
        Redeploy,
        /// <summary>脱走: 相手のカード Target(Flag にある)を捨てる。Card, Flag, Target。</summary>
        Deserter,
        /// <summary>裏切り: 相手の部隊カード Target(Flag にある)を自分側の ToFlag へ。Card, Flag, Target, ToFlag。</summary>
        Traitor,
        /// <summary>出せる部隊カードが無いときのパス。</summary>
        Pass,
        /// <summary>山札 Deck から 1 枚引く(通常のドロー・偵察のドロー)。</summary>
        Draw,
        /// <summary>偵察: 手札の Card を山札の上に戻す。</summary>
        ScoutReturn,
    }

    /// <summary>1 つの行動。手番は「出す → (確保は自動)→ 引く」の複数の行動で進む。</summary>
    public readonly struct BattleLineMove : IEquatable<BattleLineMove>
    {
        public readonly MoveKind Kind;
        public readonly int Card;
        public readonly int Flag;
        public readonly int Target;
        public readonly int ToFlag;
        public readonly DeckKind Deck;

        public BattleLineMove(MoveKind kind, int card, int flag, int target, int toFlag, DeckKind deck)
        {
            Kind = kind;
            Card = card;
            Flag = flag;
            Target = target;
            ToFlag = toFlag;
            Deck = deck;
        }

        public static BattleLineMove Place(int card, int flag) => new BattleLineMove(MoveKind.Place, card, flag, -1, -1, default);
        public static BattleLineMove UseScout(int card) => new BattleLineMove(MoveKind.Scout, card, -1, -1, -1, default);

        public static BattleLineMove UseRedeploy(int card, int flag, int target, int toFlag) =>
            new BattleLineMove(MoveKind.Redeploy, card, flag, target, toFlag, default);

        public static BattleLineMove UseDeserter(int card, int flag, int target) =>
            new BattleLineMove(MoveKind.Deserter, card, flag, target, -1, default);

        public static BattleLineMove UseTraitor(int card, int flag, int target, int toFlag) =>
            new BattleLineMove(MoveKind.Traitor, card, flag, target, toFlag, default);

        public static BattleLineMove Pass => new BattleLineMove(MoveKind.Pass, -1, -1, -1, -1, default);
        public static BattleLineMove Draw(DeckKind deck) => new BattleLineMove(MoveKind.Draw, -1, -1, -1, -1, deck);
        /// <summary>偵察で手札を戻す。Deck は戻す先(カードの種類で決まる。相手視点ではカードを伏せて Deck だけ見せる)。</summary>
        public static BattleLineMove ReturnCard(int card) =>
            new BattleLineMove(MoveKind.ScoutReturn, card, -1, -1, -1, BattleLine.Card.IsValid(card) ? BattleLine.Card.DeckOf(card) : default);

        /// <summary>相手から見た「戻したカード」(中身を伏せる)。</summary>
        public static BattleLineMove HiddenReturn(DeckKind deck) => new BattleLineMove(MoveKind.ScoutReturn, -1, -1, -1, -1, deck);

        /// <summary>手札からカードを出す行動か(出す段階の行動。パスは含まない)。</summary>
        public bool IsPlay => Kind <= MoveKind.Traitor;

        public bool Equals(BattleLineMove o) =>
            Kind == o.Kind && Card == o.Card && Flag == o.Flag && Target == o.Target && ToFlag == o.ToFlag &&
            (Kind != MoveKind.Draw || Deck == o.Deck);

        public override bool Equals(object obj) => obj is BattleLineMove m && Equals(m);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = h * 31 + Card;
                h = h * 31 + Flag;
                h = h * 31 + Target;
                h = h * 31 + ToFlag;
                return h * 31 + (Kind == MoveKind.Draw ? (int)Deck : 0);
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case MoveKind.Place: return $"Place({BattleLine.Card.Describe(Card)}@{Flag})";
                case MoveKind.Scout: return "Scout";
                case MoveKind.Redeploy: return $"Redeploy({BattleLine.Card.Describe(Target)}@{Flag}->{ToFlag})";
                case MoveKind.Deserter: return $"Deserter({BattleLine.Card.Describe(Target)}@{Flag})";
                case MoveKind.Traitor: return $"Traitor({BattleLine.Card.Describe(Target)}@{Flag}->{ToFlag})";
                case MoveKind.Draw: return $"Draw({Deck})";
                case MoveKind.ScoutReturn: return $"Return({BattleLine.Card.Describe(Card)})";
                default: return Kind.ToString();
            }
        }
    }
}
