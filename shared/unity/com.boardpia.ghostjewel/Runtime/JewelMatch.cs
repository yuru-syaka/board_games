using System;
using BoardGames.Core;

namespace BoardGames.GhostJewel
{
    /// <summary>画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。</summary>
    public sealed class MatchSnapshot
    {
        public JewelPlayerView View { get; }
        public int PlayerCount => View.PlayerCount;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GameResult Result => View.Result;
        public bool IsNewGame => View.Ply == 0;

        public MatchSnapshot(JewelPlayerView view)
        {
            View = view;
        }

        /// <summary>next が新しい対局の局面か(手数が戻った・終局のあとに終局していない局面が来た)。</summary>
        public static bool StartsNewGame(MatchSnapshot previous, MatchSnapshot next) =>
            previous == null ? next.IsNewGame : next.Ply < previous.Ply || (previous.Result.IsOver && !next.Result.IsOver);

        public static MatchSnapshot For(JewelState state, int viewer) => new MatchSnapshot(JewelPlayerView.Create(state, viewer));
    }

    /// <summary>画面から見た 1 対局。View は Snapshot を描画し、自分の手を SubmitLocalMove で送るだけ。</summary>
    public interface IJewelMatch
    {
        int LocalPlayer { get; }
        MatchSnapshot Snapshot { get; }
        event Action<MatchSnapshot> SnapshotChanged;
        void SubmitLocalMove(JewelMove move);
    }

    /// <summary>人間(P0)対 CPU(P1..)。CPU の手は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : IJewelMatch
    {
        public const int Human = 0;

        readonly JewelRules _rules = new JewelRules();
        readonly Random _rng;
        readonly int _playerCount;
        JewelCpu _cpu;
        JewelState _state;

        public int LocalPlayer => Human;
        public int PlayerCount => _playerCount;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng, int playerCount)
        {
            if (!JewelLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _rng = rng ?? new Random();
            _playerCount = playerCount;
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer != Human;

        public void StartNewGame()
        {
            _cpu = new JewelCpu(new Random(_rng.Next()));
            _state = JewelSetup.CreateRandom(_rng, _playerCount);
            Publish();
        }

        public void SubmitLocalMove(JewelMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>CPU の手番を 1 つ進める(宝石を返すあいだは、返し終わるまでまとめて進める)。</summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            int p = _state.CurrentPlayer;
            do
            {
                var move = _cpu.ChooseMove(JewelPlayerView.Create(_state, p));
                _state = _rules.ApplyMove(_state, move);
            } while (!_state.Result.IsOver && _state.CurrentPlayer == p && _state.Phase == JewelPhase.Return);
            Publish();
        }

        void Publish()
        {
            Snapshot = MatchSnapshot.For(_state, Human);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
