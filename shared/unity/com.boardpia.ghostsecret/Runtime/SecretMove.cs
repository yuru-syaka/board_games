namespace BoardGames.GhostSecret
{
    public enum MoveKind
    {
        /// <summary>手札の Card を出す。Target = 選んだ人(選ばないカード・選べる人がいないときは -1)。Guess = ササヤキで言う数字(それ以外は -1)。</summary>
        Play,
        /// <summary>ウラナイで見た 2 枚を戻す。Choice = 0 ならそのまま、1 なら上下を入れ替える。</summary>
        Fortune,
        /// <summary>アカリで見た相手と、手札を交換するか。Choice = 1 で交換、0 でしない。</summary>
        Lamp,
    }

    public readonly struct SecretMove : System.IEquatable<SecretMove>
    {
        public readonly MoveKind Kind;
        public readonly int Card;
        public readonly int Target;
        public readonly int Guess;
        public readonly int Choice;

        SecretMove(MoveKind kind, int card, int target, int guess, int choice)
        {
            Kind = kind;
            Card = card;
            Target = target;
            Guess = guess;
            Choice = choice;
        }

        public static SecretMove Play(int card, int target = -1, int guess = -1) => new SecretMove(MoveKind.Play, card, target, guess, 0);
        public static SecretMove FortuneOrder(bool swap) => new SecretMove(MoveKind.Fortune, -1, -1, -1, swap ? 1 : 0);
        public static SecretMove LampSwap(bool swap) => new SecretMove(MoveKind.Lamp, -1, -1, -1, swap ? 1 : 0);

        /// <summary>値から作る(通信で届いた手を組み立て直すとき。合法かどうかはルールが確かめる)。</summary>
        public static SecretMove Create(MoveKind kind, int card, int target, int guess, int choice) =>
            kind == MoveKind.Play ? Play(card, target, guess) : new SecretMove(kind, -1, -1, -1, choice);

        public bool Equals(SecretMove other) =>
            Kind == other.Kind && Card == other.Card && Target == other.Target && Guess == other.Guess && Choice == other.Choice;

        public override bool Equals(object obj) => obj is SecretMove m && Equals(m);

        public override int GetHashCode() => (((((int)Kind * 31 + Card) * 31 + Target) * 31 + Guess) * 31) + Choice;

        public override string ToString() =>
            Kind == MoveKind.Play ? $"Play(c{Card} t{Target} g{Guess})" : $"{Kind}({Choice})";
    }
}
