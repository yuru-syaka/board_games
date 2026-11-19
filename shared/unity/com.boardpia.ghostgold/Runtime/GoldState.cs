using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostGold
{
    public enum GoldPhase
    {
        /// <summary>探検中の人が「進む・帰る」を決める。</summary>
        Decide,
        /// <summary>対局が終わった。</summary>
        Over,
    }

    /// <summary>秘宝がどこにあるか。</summary>
    public enum RelicStatus
    {
        /// <summary>まだ山札に入っていない(その夜が来ていない)。</summary>
        NotYet,
        /// <summary>山札の中。</summary>
        InDeck,
        /// <summary>通路にめくられている(まだだれのものでもない)。</summary>
        OnPath,
        /// <summary>だれかがもらった。</summary>
        Taken,
        /// <summary>夜の終わりに通路に残っていたので、なくなった。</summary>
        Lost,
    }

    /// <summary>1 人ぶんの持ち物。すべて全員に見える(Choice だけは公開まで本人にしか見えない)。</summary>
    public sealed class GoldPlayer
    {
        /// <summary>この夜、まだ館の中にいる。</summary>
        public bool Exploring;
        /// <summary>この夜の手持ちの金貨(おどかされると失う)。</summary>
        public int Carry;
        /// <summary>金庫の金貨(もう失わない)。</summary>
        public int Safe;
        /// <summary>もらった秘宝のカード ID。</summary>
        public readonly List<int> Relics = new List<int>();
        /// <summary>お守りをまだ使っていない。</summary>
        public bool HasAmulet = true;
        /// <summary>今の決定(まだ決めていなければ None。ほかの人から見ると、決めていても None)。</summary>
        public Choice Choice = Choice.None;
        /// <summary>今の決定をもう決めたか(全員に見える)。</summary>
        public bool Decided;

        public int RelicValue => Relics.Sum(GoldCards.RelicValue);

        /// <summary>点数 = 金庫の金貨 + 秘宝の値打ち。</summary>
        public int Score => Safe + RelicValue;

        public GoldPlayer Clone()
        {
            var p = new GoldPlayer
            {
                Exploring = Exploring, Carry = Carry, Safe = Safe, HasAmulet = HasAmulet, Choice = Choice, Decided = Decided,
            };
            p.Relics.AddRange(Relics);
            return p;
        }
    }

    /// <summary>
    /// 局面。ルールは局面を書き換えず、Clone してから変える(IGameRules の決まりどおり不変として扱う)。
    /// </summary>
    public sealed class GoldState
    {
        public readonly int PlayerCount;
        public readonly GoldPlayer[] Players;

        /// <summary>何夜目(1..5)。</summary>
        public int Night = 1;
        public GoldPhase Phase = GoldPhase.Decide;
        /// <summary>次に決める人(探検中でまだ決めていない人のうち、席の番号がいちばん小さい人)。終局後は -1。</summary>
        public int CurrentPlayer = -1;

        /// <summary>山札(最後の要素がいちばん上)。見えないカードは -1。</summary>
        public readonly List<int> Deck = new List<int>();
        /// <summary>この夜にめくったカード(めくった順)。</summary>
        public readonly List<int> Path = new List<int>();
        /// <summary>通路の余り(帰る人が分ける金貨)。</summary>
        public int Leftover;
        /// <summary>種類ごとの、取り除かれた怪異の枚数。</summary>
        public readonly int[] HazardRemoved = new int[GoldLogic.HazardKinds];
        /// <summary>秘宝(1..5 夜目)のありか。RelicStatus[n - 1]。</summary>
        public readonly RelicStatus[] Relics = new RelicStatus[GoldLogic.RelicCount];

        public int Ply;
        public GameResult Result = GameResult.Ongoing;
        /// <summary>シャッフルの種(夜ごとの山札はこの種と夜の番号から作る)。</summary>
        public int Seed;

        public readonly List<GoldEvent> Events = new List<GoldEvent>();
        public int NextSeq;

        public GoldState(int playerCount)
        {
            PlayerCount = playerCount;
            Players = new GoldPlayer[playerCount];
            for (int i = 0; i < playerCount; i++) Players[i] = new GoldPlayer();
        }

        public GoldPlayer Current => CurrentPlayer >= 0 ? Players[CurrentPlayer] : null;

        public int ExploringCount => Players.Count(p => p.Exploring);

        /// <summary>この夜の通路にある、kind の怪異の枚数。</summary>
        public int HazardOnPath(int kind) => Path.Count(c => GoldCards.HazardOf(c) == kind);

        public GoldState Clone()
        {
            var s = new GoldState(PlayerCount)
            {
                Night = Night, Phase = Phase, CurrentPlayer = CurrentPlayer, Leftover = Leftover, Ply = Ply, Result = Result,
                Seed = Seed, NextSeq = NextSeq,
            };
            for (int i = 0; i < PlayerCount; i++) s.Players[i] = Players[i].Clone();
            s.Deck.AddRange(Deck);
            s.Path.AddRange(Path);
            HazardRemoved.CopyTo(s.HazardRemoved, 0);
            Relics.CopyTo(s.Relics, 0);
            s.Events.AddRange(Events);
            return s;
        }

        public void AddEvent(EventKind kind, int player = -1, int card = -1, int amount = 0, int amount2 = 0) =>
            Events.Add(new GoldEvent(NextSeq++, kind, player, card, amount, amount2));
    }
}
