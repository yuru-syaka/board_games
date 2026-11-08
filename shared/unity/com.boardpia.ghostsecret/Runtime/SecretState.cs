using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostSecret
{
    public enum SecretPhase
    {
        /// <summary>手番の人がカードを 1 枚出す。</summary>
        Play,
        /// <summary>ウラナイで見た 2 枚を戻す順番を決める。</summary>
        Fortune,
        /// <summary>アカリで見た相手と交換するかを決める。</summary>
        Lamp,
        /// <summary>対局が終わった。</summary>
        Over,
    }

    /// <summary>1 人ぶんの持ち物。</summary>
    public sealed class SecretPlayer
    {
        /// <summary>手札(ふだんは 1 枚、自分の手番は 2 枚)。見えないカードは -1。</summary>
        public readonly List<int> Hand = new List<int>();
        /// <summary>このラウンドに自分の前に出したカード(出した順。全員に見える)。</summary>
        public readonly List<int> Played = new List<int>();
        /// <summary>Known[q] = この人が知っている、q の今の手札(知らなければ -1)。</summary>
        public readonly int[] Known;
        public bool Out;
        public bool Protected;
        /// <summary>秘密のかけらの数。</summary>
        public int Tokens;

        public SecretPlayer(int playerCount)
        {
            Known = new int[playerCount];
            for (int i = 0; i < playerCount; i++) Known[i] = -1;
        }

        public int PlayedSum
        {
            get
            {
                int sum = 0;
                foreach (int c in Played) sum += c >= 0 ? SecretCards.ValueOf(c) : 0;
                return sum;
            }
        }

        public SecretPlayer Clone()
        {
            var p = new SecretPlayer(Known.Length) { Out = Out, Protected = Protected, Tokens = Tokens };
            p.Hand.AddRange(Hand);
            p.Played.AddRange(Played);
            Known.CopyTo(p.Known, 0);
            return p;
        }
    }

    /// <summary>
    /// 局面。ルールは局面を書き換えず、Clone してから変える(IGameRules の決まりどおり不変として扱う)。
    /// </summary>
    public sealed class SecretState
    {
        public int PlayerCount { get; }
        public SecretPlayer[] Players { get; }
        /// <summary>山札(末尾がいちばん上)。見えないカードは -1。</summary>
        public List<int> Deck { get; }
        /// <summary>2 人のときに表向きで脇に置いたカード(全員に見える)。</summary>
        public List<int> FaceUpAside { get; }
        /// <summary>ウラナイで見ている 2 枚(上にくる順。見ているあいだだけ)。</summary>
        public List<int> Peek { get; }
        public List<SecretEvent> Events { get; }

        /// <summary>伏せて脇に置いた 1 枚(見えなければ -1。フキトバシで引かれたら -2)。</summary>
        public int SetAside = -1;
        public int CurrentPlayer;
        /// <summary>このラウンドの最初の手番の人。</summary>
        public int RoundStarter;
        /// <summary>ラウンドの番号(1..)。</summary>
        public int Round;
        public SecretPhase Phase;
        /// <summary>アカリで選んだ相手(Lamp のときだけ)。</summary>
        public int PendingTarget = -1;
        /// <summary>ラウンドごとのシャッフルの種。</summary>
        public int Seed;
        public int Ply;
        public GameResult Result = GameResult.Ongoing;

        public const int SetAsideTaken = -2;

        public SecretState(int playerCount)
        {
            PlayerCount = playerCount;
            Players = new SecretPlayer[playerCount];
            for (int i = 0; i < playerCount; i++) Players[i] = new SecretPlayer(playerCount);
            Deck = new List<int>();
            FaceUpAside = new List<int>();
            Peek = new List<int>();
            Events = new List<SecretEvent>();
        }

        public SecretPlayer Current => Players[CurrentPlayer];

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var p in Players)
                    if (!p.Out) n++;
                return n;
            }
        }

        public SecretState Clone()
        {
            var s = new SecretState(PlayerCount)
            {
                SetAside = SetAside,
                CurrentPlayer = CurrentPlayer,
                RoundStarter = RoundStarter,
                Round = Round,
                Phase = Phase,
                PendingTarget = PendingTarget,
                Seed = Seed,
                Ply = Ply,
                Result = Result,
            };
            for (int i = 0; i < PlayerCount; i++) s.Players[i] = Players[i].Clone();
            s.Deck.AddRange(Deck);
            s.FaceUpAside.AddRange(FaceUpAside);
            s.Peek.AddRange(Peek);
            s.Events.AddRange(Events);
            return s;
        }

        public void AddEvent(EventKind kind, int player, int target = -1, int card = -1, int card2 = -1, int guess = -1, int amount = 0,
            int viewers = SecretEvent.Everyone)
        {
            Events.Add(new SecretEvent(Events.Count, kind, player, target, card, card2, guess, amount, viewers));
        }
    }
}
