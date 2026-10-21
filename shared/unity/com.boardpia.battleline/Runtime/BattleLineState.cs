using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.BattleLine
{
    /// <summary>手番の中の段階。</summary>
    public enum TurnPhase
    {
        /// <summary>カードを出す(またはパス)。</summary>
        Play,
        /// <summary>山札から 1 枚引く。</summary>
        Draw,
        /// <summary>偵察: 山札から引く(残り PendingCount 枚)。</summary>
        ScoutDraw,
        /// <summary>偵察: 手札を山札に戻す(残り PendingCount 枚)。</summary>
        ScoutReturn,
    }

    public enum EndReason
    {
        None,
        /// <summary>フラッグを 5 本確保。</summary>
        FiveFlags,
        /// <summary>隣り合う 3 本のフラッグを確保。</summary>
        Breakthrough,
        /// <summary>両者が続けてパスした(確保数で判定)。</summary>
        Stalemate,
    }

    /// <summary>
    /// 完全な局面(非公開情報を含む)。外からは不変として扱い、ルールは ApplyMove で複製してから変更する。
    /// 山札は List の末尾が一番上。
    /// </summary>
    public sealed class BattleLineState
    {
        public const int FlagCount = 9;
        public const int HandSize = 7;
        public const int Players = 2;

        readonly Flag[] _flags;
        readonly List<int>[] _hands;
        readonly List<int> _troopDeck;
        readonly List<int> _tacticsDeck;
        readonly List<int> _discard;
        readonly int[] _tacticsPlayed;
        readonly bool[] _leaderPlayed;

        public IReadOnlyList<Flag> Flags => _flags;
        public IReadOnlyList<int> HandOf(int player) => _hands[player];
        public IReadOnlyList<int> TroopDeck => _troopDeck;
        public IReadOnlyList<int> TacticsDeck => _tacticsDeck;
        /// <summary>表向きの捨て札(使った計略、脱走・再配置で捨てたカード)。</summary>
        public IReadOnlyList<int> Discard => _discard;
        public int TacticsPlayed(int player) => _tacticsPlayed[player];
        public bool LeaderPlayed(int player) => _leaderPlayed[player];

        public int CurrentPlayer { get; internal set; }
        public TurnPhase Phase { get; internal set; }
        /// <summary>偵察で残っている「引く / 戻す」の枚数。</summary>
        public int PendingCount { get; internal set; }
        /// <summary>経過した手番の数(0 始まり)。</summary>
        public int Turn { get; internal set; }
        /// <summary>隊列の完成順を記録するための通し番号。</summary>
        public int Seq { get; internal set; }
        public int ConsecutivePasses { get; internal set; }
        public GameResult Result { get; internal set; } = GameResult.Ongoing;
        public EndReason EndReason { get; internal set; }
        /// <summary>直前の行動(対局開始時は null)。</summary>
        public BattleLineMove? LastMove { get; internal set; }
        /// <summary>直前の行動をしたプレイヤー(対局開始時は −1)。</summary>
        public int LastMover { get; internal set; } = -1;
        /// <summary>行動の通し番号(画面側で「新しい対局」と「途中の局面」を区別するのに使う)。</summary>
        public int Step { get; internal set; }

        internal BattleLineState()
        {
            _flags = new Flag[FlagCount];
            for (int i = 0; i < FlagCount; i++) _flags[i] = new Flag(i);
            _hands = new[] { new List<int>(9), new List<int>(9) };
            _troopDeck = new List<int>(Card.TroopCount);
            _tacticsDeck = new List<int>(Card.TacticsCount);
            _discard = new List<int>();
            _tacticsPlayed = new int[Players];
            _leaderPlayed = new bool[Players];
        }

        BattleLineState(BattleLineState s)
        {
            _flags = new Flag[FlagCount];
            for (int i = 0; i < FlagCount; i++) _flags[i] = s._flags[i].Clone();
            _hands = new[] { new List<int>(s._hands[0]), new List<int>(s._hands[1]) };
            _troopDeck = new List<int>(s._troopDeck);
            _tacticsDeck = new List<int>(s._tacticsDeck);
            _discard = new List<int>(s._discard);
            _tacticsPlayed = (int[])s._tacticsPlayed.Clone();
            _leaderPlayed = (bool[])s._leaderPlayed.Clone();
            CurrentPlayer = s.CurrentPlayer;
            Phase = s.Phase;
            PendingCount = s.PendingCount;
            Turn = s.Turn;
            Seq = s.Seq;
            ConsecutivePasses = s.ConsecutivePasses;
            Result = s.Result;
            EndReason = s.EndReason;
            LastMove = s.LastMove;
            LastMover = s.LastMover;
            Step = s.Step;
        }

        internal BattleLineState Clone() => new BattleLineState(this);

        public int DeckCount(DeckKind deck) => deck == DeckKind.Troop ? _troopDeck.Count : _tacticsDeck.Count;

        public int ClaimedCount(int player)
        {
            int n = 0;
            foreach (var f in _flags)
                if (f.Claimer == player) n++;
            return n;
        }

        // ---------- 変更(ルール内部・局面の組み立て用) ----------

        internal Flag FlagAt(int index) => _flags[index];
        internal List<int> Hand(int player) => _hands[player];
        internal List<int> Deck(DeckKind deck) => deck == DeckKind.Troop ? _troopDeck : _tacticsDeck;
        internal List<int> DiscardPile => _discard;
        internal void AddTacticPlayed(int player) => _tacticsPlayed[player]++;
        internal void SetTacticsPlayed(int player, int count) => _tacticsPlayed[player] = count;
        internal void SetLeaderPlayed(int player, bool value) => _leaderPlayed[player] = value;
    }
}
