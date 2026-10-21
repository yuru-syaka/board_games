namespace BoardGames.BattleLine
{
    public enum TacticKind
    {
        None,
        Leader,
        Cavalry,
        Shield,
        Fog,
        Mud,
        Scout,
        Redeploy,
        Deserter,
        Traitor,
    }

    public enum DeckKind
    {
        Troop,
        Tactics,
    }

    /// <summary>
    /// カード Id の解釈。部隊カード 0〜59(色 × 10 + 数字 − 1)、戦術カード 60〜69。
    /// カードは Id(int)のまま扱い、意味はこのクラスで引く。
    /// </summary>
    public static class Card
    {
        public const int Colors = 6;
        public const int MaxValue = 10;
        public const int TroopCount = Colors * MaxValue;
        public const int TacticsCount = 10;
        public const int Total = TroopCount + TacticsCount;

        public const int LeaderA = 60;
        public const int LeaderB = 61;
        public const int Cavalry = 62;
        public const int Shield = 63;
        public const int Fog = 64;
        public const int Mud = 65;
        public const int Scout = 66;
        public const int Redeploy = 67;
        public const int Deserter = 68;
        public const int Traitor = 69;

        public static bool IsValid(int id) => id >= 0 && id < Total;
        public static bool IsTroop(int id) => id >= 0 && id < TroopCount;
        public static bool IsTactic(int id) => id >= TroopCount && id < Total;

        public static int Troop(int color, int value) => color * MaxValue + (value - 1);

        /// <summary>部隊カードの色(0〜5)。</summary>
        public static int ColorOf(int id) => id / MaxValue;

        /// <summary>部隊カードの数字(1〜10)。</summary>
        public static int ValueOf(int id) => id % MaxValue + 1;

        public static TacticKind KindOf(int id)
        {
            switch (id)
            {
                case LeaderA:
                case LeaderB: return TacticKind.Leader;
                case Cavalry: return TacticKind.Cavalry;
                case Shield: return TacticKind.Shield;
                case Fog: return TacticKind.Fog;
                case Mud: return TacticKind.Mud;
                case Scout: return TacticKind.Scout;
                case Redeploy: return TacticKind.Redeploy;
                case Deserter: return TacticKind.Deserter;
                case Traitor: return TacticKind.Traitor;
                default: return TacticKind.None;
            }
        }

        /// <summary>隊列に入る戦術(リーダー・騎兵・盾兵)。</summary>
        public static bool IsMorale(int id)
        {
            var k = KindOf(id);
            return k == TacticKind.Leader || k == TacticKind.Cavalry || k == TacticKind.Shield;
        }

        /// <summary>フラッグに置く戦術(霧・泥沼)。</summary>
        public static bool IsEnvironment(int id) => id == Fog || id == Mud;

        /// <summary>効果を使って捨てる戦術(偵察・再配置・脱走・裏切り)。</summary>
        public static bool IsGuile(int id) => id >= Scout && id <= Traitor;

        /// <summary>隊列に置けるカード(部隊 + 士気戦術)。</summary>
        public static bool IsFormationCard(int id) => IsTroop(id) || IsMorale(id);

        public static DeckKind DeckOf(int id) => IsTroop(id) ? DeckKind.Troop : DeckKind.Tactics;

        public static string Describe(int id)
        {
            if (IsTroop(id)) return $"{"RoYGBP"[ColorOf(id)]}{ValueOf(id)}";
            return IsTactic(id) ? KindOf(id).ToString() : $"#{id}";
        }
    }
}
