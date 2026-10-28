using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostJewel
{
    public enum JewelPhase
    {
        /// <summary>ふつうの手番(宝石を取る・予約・買う)。</summary>
        Main,
        /// <summary>宝石が 10 個を超えたので、超えたぶんを返している。</summary>
        Return,
        /// <summary>終局。</summary>
        Over,
    }

    /// <summary>1 人ぶんの手元。</summary>
    public sealed class JewelPlayer
    {
        /// <summary>持っている宝石(長さ 6、ゴールドを含む)。</summary>
        public readonly int[] Gems = new int[Gem.All];
        /// <summary>買ったカードの色ごとの枚数(=割引。長さ 5)。</summary>
        public readonly int[] Bonus = new int[Gem.Colors];
        /// <summary>予約したカード(古い順)。</summary>
        public readonly List<int> Reserved = new List<int>();
        /// <summary>Reserved と同じ並びで、山札から(相手には見えない)予約したか。</summary>
        public readonly List<bool> ReservedFromDeck = new List<bool>();
        public readonly List<int> Bought = new List<int>();
        public readonly List<int> Nobles = new List<int>();
        public int Points;

        public int GemTotal
        {
            get
            {
                int n = 0;
                foreach (var g in Gems) n += g;
                return n;
            }
        }

        public JewelPlayer Clone()
        {
            var p = new JewelPlayer { Points = Points };
            Gems.CopyTo(p.Gems, 0);
            Bonus.CopyTo(p.Bonus, 0);
            p.Reserved.AddRange(Reserved);
            p.ReservedFromDeck.AddRange(ReservedFromDeck);
            p.Bought.AddRange(Bought);
            p.Nobles.AddRange(Nobles);
            return p;
        }
    }

    /// <summary>
    /// 局面。ルールは局面を書き換えず、Clone してから変える(IGameRules の決まりどおり不変として扱う)。
    /// </summary>
    public sealed class JewelState
    {
        public int PlayerCount { get; }
        public JewelPlayer[] Players { get; }
        /// <summary>場に残っている宝石(長さ 6)。</summary>
        public int[] Bank { get; }
        /// <summary>段ごとの山札(末尾がいちばん上)。</summary>
        public List<int>[] Decks { get; }
        /// <summary>段ごとの表向きのカード(空きは -1)。</summary>
        public int[][] Market { get; }
        /// <summary>まだだれも訪れていない館の主。</summary>
        public List<int> Nobles { get; }
        public List<JewelEvent> Events { get; }

        public int CurrentPlayer;
        /// <summary>最初の手番の人。最終ラウンドは、手番がこの人に戻る直前で終わる。</summary>
        public int StartPlayer;
        public JewelPhase Phase;
        public bool FinalRound;
        public int Ply;
        /// <summary>続けてパスした人数(全員がパスしたら打ち切り)。</summary>
        public int PassStreak;
        public GameResult Result = GameResult.Ongoing;

        public JewelState(int playerCount)
        {
            PlayerCount = playerCount;
            Players = new JewelPlayer[playerCount];
            for (int i = 0; i < playerCount; i++) Players[i] = new JewelPlayer();
            Bank = new int[Gem.All];
            Decks = new List<int>[JewelLogic.Tiers];
            Market = new int[JewelLogic.Tiers][];
            for (int t = 0; t < JewelLogic.Tiers; t++)
            {
                Decks[t] = new List<int>();
                Market[t] = new int[JewelLogic.MarketSize];
                for (int s = 0; s < JewelLogic.MarketSize; s++) Market[t][s] = -1;
            }
            Nobles = new List<int>();
            Events = new List<JewelEvent>();
        }

        public JewelPlayer Current => Players[CurrentPlayer];

        public JewelState Clone()
        {
            var s = new JewelState(PlayerCount)
            {
                CurrentPlayer = CurrentPlayer,
                StartPlayer = StartPlayer,
                Phase = Phase,
                FinalRound = FinalRound,
                Ply = Ply,
                PassStreak = PassStreak,
                Result = Result,
            };
            for (int i = 0; i < PlayerCount; i++) s.Players[i] = Players[i].Clone();
            Bank.CopyTo(s.Bank, 0);
            for (int t = 0; t < JewelLogic.Tiers; t++)
            {
                s.Decks[t].AddRange(Decks[t]);
                Market[t].CopyTo(s.Market[t], 0);
            }
            s.Nobles.AddRange(Nobles);
            s.Events.AddRange(Events);
            return s;
        }

        public void AddEvent(EventKind kind, int player, int card = -1, int tier = -1, int slot = -1, int gem = -1, int noble = -1,
            int amount = 0, int[] gems = null)
        {
            Events.Add(new JewelEvent(Events.Count, kind, player, card, tier, slot, gem, noble, amount, gems));
        }

        /// <summary>場のどこにそのカードがあるか(無ければ false)。</summary>
        public bool FindInMarket(int card, out int tier, out int slot)
        {
            for (tier = 0; tier < JewelLogic.Tiers; tier++)
                for (slot = 0; slot < JewelLogic.MarketSize; slot++)
                    if (Market[tier][slot] == card) return true;
            tier = -1;
            slot = -1;
            return false;
        }
    }
}
