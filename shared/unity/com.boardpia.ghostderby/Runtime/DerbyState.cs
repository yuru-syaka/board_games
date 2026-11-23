using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostDerby
{
    public enum DerbyPhase
    {
        /// <summary>先賭けを伏せて決める。</summary>
        Bet,
        /// <summary>いたずら札を伏せて山札に入れる。</summary>
        Trick,
        /// <summary>追い賭けを伏せて決める。</summary>
        Chase,
        Over,
    }

    public sealed class DerbyPlayer
    {
        public int Coins = DerbyLogic.StartCoins;
        /// <summary>手札(ほかの人から見ると -1)。</summary>
        public readonly List<int> Hand = new List<int>();
        public int BetRunner = -1;
        public int BetAmount;
        public int ChaseRunner = -1;
        public int ChaseAmount;
        public int ChaseOdds;
        public bool Decided;
        /// <summary>1 着に賭けて当てた回数。</summary>
        public int Hits;

        public DerbyPlayer Clone()
        {
            var p = new DerbyPlayer
            {
                Coins = Coins, BetRunner = BetRunner, BetAmount = BetAmount, ChaseRunner = ChaseRunner, ChaseAmount = ChaseAmount,
                ChaseOdds = ChaseOdds, Decided = Decided, Hits = Hits,
            };
            p.Hand.AddRange(Hand);
            return p;
        }
    }

    /// <summary>局面。ルールは局面を書き換えず、Clone してから変える。</summary>
    public sealed class DerbyState
    {
        public readonly int PlayerCount;
        public readonly DerbyPlayer[] Players;

        public int Race = 1;
        public DerbyPhase Phase = DerbyPhase.Bet;
        public int CurrentPlayer = -1;

        public readonly int[] Odds = new int[DerbyLogic.Runners];
        public readonly int[] Position = new int[DerbyLogic.Runners];
        /// <summary>金縛りの残り回数。</summary>
        public readonly int[] Frozen = new int[DerbyLogic.Runners];
        /// <summary>ゴールしたおばけ(着順)。</summary>
        public readonly List<int> Finish = new List<int>();

        /// <summary>レースの山札(最後がいちばん上)。見えないカードは -1。</summary>
        public readonly List<int> Deck = new List<int>();
        /// <summary>いたずら札の山(見えないカードは -1)。</summary>
        public readonly List<int> DrawPile = new List<int>();
        /// <summary>今の山札からめくった走る札(残りの内訳を数えるのに使う)。</summary>
        public readonly List<int> Seen = new List<int>();
        /// <summary>山札に入れたいたずら札の相手(カード番号 - FirstTrick で引く。公開までだれにも見せない)。</summary>
        public readonly int[] TrickTarget = new int[DerbyLogic.TrickCount];
        public readonly int[] TrickOwner = new int[DerbyLogic.TrickCount];

        public bool ChaseDone;
        public int Rebuilds;
        public int Ply;
        public GameResult Result = GameResult.Ongoing;
        public int Seed;

        public readonly List<DerbyEvent> Events = new List<DerbyEvent>();
        public int NextSeq;

        public DerbyState(int playerCount)
        {
            PlayerCount = playerCount;
            Players = new DerbyPlayer[playerCount];
            for (int i = 0; i < playerCount; i++) Players[i] = new DerbyPlayer();
            for (int i = 0; i < DerbyLogic.TrickCount; i++) TrickTarget[i] = TrickOwner[i] = -1;
        }

        public bool IsFinished(int runner) => Finish.Contains(runner);

        /// <summary>runner より前にいるおばけの数(0 = 先頭。同じマスは同じ順位)。</summary>
        public int RankOf(int runner)
        {
            int place = Finish.IndexOf(runner);
            if (place >= 0) return place;
            int ahead = Finish.Count;
            for (int r = 0; r < DerbyLogic.Runners; r++)
                if (!IsFinished(r) && Position[r] > Position[runner]) ahead++;
            return ahead;
        }

        public DerbyState Clone()
        {
            var s = new DerbyState(PlayerCount)
            {
                Race = Race, Phase = Phase, CurrentPlayer = CurrentPlayer, ChaseDone = ChaseDone, Rebuilds = Rebuilds, Ply = Ply,
                Result = Result, Seed = Seed, NextSeq = NextSeq,
            };
            for (int i = 0; i < PlayerCount; i++) s.Players[i] = Players[i].Clone();
            Odds.CopyTo(s.Odds, 0);
            Position.CopyTo(s.Position, 0);
            Frozen.CopyTo(s.Frozen, 0);
            TrickTarget.CopyTo(s.TrickTarget, 0);
            TrickOwner.CopyTo(s.TrickOwner, 0);
            s.Finish.AddRange(Finish);
            s.Deck.AddRange(Deck);
            s.DrawPile.AddRange(DrawPile);
            s.Seen.AddRange(Seen);
            s.Events.AddRange(Events);
            return s;
        }

        public void AddEvent(EventKind kind, int player = -1, int card = -1, int amount = 0, int amount2 = 0) =>
            Events.Add(new DerbyEvent(NextSeq++, kind, player, card, amount, amount2));
    }
}
