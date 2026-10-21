using System.Collections.Generic;

namespace BoardGames.BattleLine
{
    /// <summary>
    /// あるプレイヤーから見た局面。相手の手札は枚数(部隊 / 戦術の内訳)だけ、山札は残り枚数だけが見える。
    /// 決着後の公開用に、相手の手札を明かした視点も作れる。
    /// </summary>
    public sealed class BattleLinePlayerView
    {
        readonly Flag[] _flags;
        readonly List<int> _hand;
        readonly List<int> _discard;
        readonly int[] _tacticsPlayed;
        readonly bool[] _leaderPlayed;
        readonly List<int> _opponentHand;

        public int Viewer { get; }
        public int Opponent => 1 - Viewer;
        public int CurrentPlayer { get; }
        public TurnPhase Phase { get; }
        public int PendingCount { get; }
        public int Turn { get; }
        public int Step { get; }
        public int ConsecutivePasses { get; }
        public int TroopDeckCount { get; }
        public int TacticsDeckCount { get; }
        public int OpponentHandTroops { get; }
        public int OpponentHandTactics { get; }
        public BattleLineMove? LastMove { get; }
        public int LastMover { get; }

        public IReadOnlyList<Flag> Flags => _flags;
        public IReadOnlyList<int> Hand => _hand;
        public IReadOnlyList<int> Discard => _discard;
        /// <summary>相手の手札の中身。決着後の公開視点のときだけ(それ以外は null)。</summary>
        public IReadOnlyList<int> OpponentHand => _opponentHand;
        public int OpponentHandCount => OpponentHandTroops + OpponentHandTactics;
        public int TacticsPlayed(int player) => _tacticsPlayed[player];
        public bool LeaderPlayed(int player) => _leaderPlayed[player];
        public int DeckCount(DeckKind deck) => deck == DeckKind.Troop ? TroopDeckCount : TacticsDeckCount;

        internal BattleLinePlayerView(Flag[] flags, List<int> hand, List<int> discard, int[] tacticsPlayed, bool[] leaderPlayed,
            List<int> opponentHand, int viewer, int currentPlayer, TurnPhase phase, int pendingCount, int turn, int step,
            int consecutivePasses, int troopDeckCount, int tacticsDeckCount, int opponentHandTroops, int opponentHandTactics,
            BattleLineMove? lastMove, int lastMover)
        {
            _flags = flags;
            _hand = hand;
            _discard = discard;
            _tacticsPlayed = tacticsPlayed;
            _leaderPlayed = leaderPlayed;
            _opponentHand = opponentHand;
            Viewer = viewer;
            CurrentPlayer = currentPlayer;
            Phase = phase;
            PendingCount = pendingCount;
            Turn = turn;
            Step = step;
            ConsecutivePasses = consecutivePasses;
            TroopDeckCount = troopDeckCount;
            TacticsDeckCount = tacticsDeckCount;
            OpponentHandTroops = opponentHandTroops;
            OpponentHandTactics = opponentHandTactics;
            LastMove = lastMove;
            LastMover = lastMover;
        }

        public static BattleLinePlayerView Create(BattleLineState state, int viewer, bool revealOpponent = false)
        {
            int opp = 1 - viewer;
            var flags = new Flag[state.Flags.Count];
            for (int i = 0; i < flags.Length; i++) flags[i] = state.Flags[i].Clone();

            int oppTroops = 0, oppTactics = 0;
            foreach (int c in state.HandOf(opp))
            {
                if (Card.IsTroop(c)) oppTroops++;
                else oppTactics++;
            }

            // 相手が偵察で戻したカードは中身を伏せる
            var last = state.LastMove;
            if (last.HasValue && last.Value.Kind == MoveKind.ScoutReturn && state.LastMover == opp && !revealOpponent)
                last = BattleLineMove.HiddenReturn(last.Value.Deck);

            return new BattleLinePlayerView(flags, new List<int>(state.HandOf(viewer)), new List<int>(state.Discard),
                new[] { state.TacticsPlayed(0), state.TacticsPlayed(1) },
                new[] { state.LeaderPlayed(0), state.LeaderPlayed(1) },
                revealOpponent ? new List<int>(state.HandOf(opp)) : null,
                viewer, state.CurrentPlayer, state.Phase, state.PendingCount, state.Turn, state.Step, state.ConsecutivePasses,
                state.DeckCount(DeckKind.Troop), state.DeckCount(DeckKind.Tactics), oppTroops, oppTactics, last, state.LastMover);
        }

        /// <summary>手番が自分のときの合法手。</summary>
        public List<BattleLineMove> GetLegalMoves()
        {
            if (CurrentPlayer != Viewer) return new List<BattleLineMove>();
            return BattleLineLogic.GenerateMoves(_flags, _hand, Viewer, Phase, TroopDeckCount, TacticsDeckCount,
                _tacticsPlayed[Viewer], _tacticsPlayed[Opponent], _leaderPlayed[Viewer]);
        }

        public int ClaimedCount(int player)
        {
            int n = 0;
            foreach (var f in _flags)
                if (f.Claimer == player) n++;
            return n;
        }

        /// <summary>
        /// 自分から見て行方の分からない部隊カード(場・捨て札・自分の手札に無いもの = 相手の手札か山札)。
        /// </summary>
        public bool[] UnknownTroops()
        {
            var pool = BattleLineLogic.UnseenTroops(_flags, _discard);
            foreach (int c in _hand)
                if (Card.IsTroop(c)) pool[c] = false;
            return pool;
        }
    }
}
