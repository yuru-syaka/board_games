using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostDerby
{
    /// <summary>
    /// あるプレイヤー(Viewer)から見た局面。山札・いたずら札の山・ほかの人の手札・公開前の賭けは伏せる。
    /// 画面と CPU にはこれだけを渡す。
    /// </summary>
    public sealed class DerbyPlayerView
    {
        public const int RecentEventCount = 160;

        static readonly DerbyRules Rules = new DerbyRules();
        readonly DerbyState _state;

        public int Viewer { get; }
        public IReadOnlyList<DerbyEvent> Events { get; }

        DerbyPlayerView(DerbyState masked, int viewer, IReadOnlyList<DerbyEvent> events)
        {
            _state = masked;
            Viewer = viewer;
            Events = events;
        }

        public static DerbyPlayerView Create(DerbyState state, int viewer)
        {
            if (viewer < 0 || viewer >= state.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            var s = state.Clone();
            s.Seed = 0;
            for (int i = 0; i < s.Deck.Count; i++) s.Deck[i] = -1;
            for (int i = 0; i < s.DrawPile.Count; i++) s.DrawPile[i] = -1;
            for (int i = 0; i < DerbyLogic.TrickCount; i++) s.TrickTarget[i] = s.TrickOwner[i] = -1;
            for (int i = 0; i < s.PlayerCount; i++)
            {
                if (i == viewer) continue;
                var p = s.Players[i];
                for (int k = 0; k < p.Hand.Count; k++) p.Hand[k] = -1;
                // 公開前の賭けは、額も分からないように、賭ける前のコインの枚数に戻して見せる
                if (s.Phase == DerbyPhase.Bet && p.Decided)
                {
                    p.Coins += p.BetAmount;
                    p.BetRunner = -1;
                    p.BetAmount = 0;
                }
                if (s.Phase == DerbyPhase.Chase && p.Decided)
                {
                    p.Coins += p.ChaseAmount;
                    p.ChaseRunner = -1;
                    p.ChaseAmount = 0;
                    p.ChaseOdds = 0;
                }
            }
            var events = state.Events.Skip(Math.Max(0, state.Events.Count - RecentEventCount)).ToList();
            s.Events.Clear();
            return new DerbyPlayerView(s, viewer, events);
        }

        public static DerbyPlayerView FromMasked(DerbyState masked, int viewer, IReadOnlyList<DerbyEvent> events) =>
            new DerbyPlayerView(masked, viewer, events);

        public int LastEventSeq => Events.Count > 0 ? Events[Events.Count - 1].Seq : -1;
        public DerbyState Masked => _state;

        public int PlayerCount => _state.PlayerCount;
        public int Race => _state.Race;
        public DerbyPhase Phase => _state.Phase;
        public int CurrentPlayer => _state.CurrentPlayer;
        public int Ply => _state.Ply;
        public GameResult Result => _state.Result;
        public bool IsMyTurn => !_state.Result.IsOver && _state.CurrentPlayer == Viewer;

        public int DeckCount => _state.Deck.Count;
        public int OddsOf(int runner) => _state.Odds[runner];
        public int PositionOf(int runner) => _state.Position[runner];
        public int FrozenOf(int runner) => _state.Frozen[runner];
        public IReadOnlyList<int> Finish => _state.Finish;
        public IReadOnlyList<int> Seen => _state.Seen;
        public bool IsFinished(int runner) => _state.IsFinished(runner);
        public int RankOf(int runner) => _state.RankOf(runner);
        public int ChaseOddsOf(int runner) => DerbyCards.ChaseOdds(_state.RankOf(runner));

        public int Coins(int p) => _state.Players[p].Coins;
        public int Hits(int p) => _state.Players[p].Hits;
        public int HandCount(int p) => _state.Players[p].Hand.Count;
        public IReadOnlyList<int> MyHand => _state.Players[Viewer].Hand;
        public bool HasDecided(int p) => _state.Players[p].Decided;
        public int BetRunner(int p) => _state.Players[p].BetRunner;
        public int BetAmount(int p) => _state.Players[p].BetAmount;
        public int ChaseRunner(int p) => _state.Players[p].ChaseRunner;
        public int ChaseAmount(int p) => _state.Players[p].ChaseAmount;
        public int ChaseOddsPaid(int p) => _state.Players[p].ChaseOdds;

        public int[] Ranking() => DerbyRules.Ranking(_state);

        public IReadOnlyList<DerbyMove> GetLegalMoves() =>
            IsMyTurn ? Rules.GetLegalMoves(_state) : (IReadOnlyList<DerbyMove>)Array.Empty<DerbyMove>();

        public bool IsLegal(DerbyMove move) => IsMyTurn && Rules.IsLegal(_state, move);
    }
}
