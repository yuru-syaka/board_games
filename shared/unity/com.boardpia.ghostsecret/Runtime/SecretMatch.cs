using System;
using BoardGames.Core;

namespace BoardGames.GhostSecret
{
    /// <summary>画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。</summary>
    public sealed class MatchSnapshot
    {
        public SecretPlayerView View { get; }
        public int PlayerCount => View.PlayerCount;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GameResult Result => View.Result;
        public bool IsNewGame => View.Ply == 0;

        public MatchSnapshot(SecretPlayerView view)
        {
            View = view;
        }

        /// <summary>next が新しい対局の局面か(手数が戻った・終局のあとに終局していない局面が来た)。</summary>
        public static bool StartsNewGame(MatchSnapshot previous, MatchSnapshot next) =>
            previous == null ? next.IsNewGame : next.Ply < previous.Ply || (previous.Result.IsOver && !next.Result.IsOver);

        public static MatchSnapshot For(SecretState state, int viewer) => new MatchSnapshot(SecretPlayerView.Create(state, viewer));
    }

    /// <summary>画面から見た 1 対局。View は Snapshot を描画し、自分の手を SubmitLocalMove で送るだけ。</summary>
    public interface ISecretMatch
    {
        int LocalPlayer { get; }
        MatchSnapshot Snapshot { get; }
        event Action<MatchSnapshot> SnapshotChanged;
        void SubmitLocalMove(SecretMove move);
    }

    /// <summary>人間(P0)対 CPU(P1..)。CPU の手は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : ISecretMatch
    {
        public const int Human = 0;

        readonly SecretRules _rules = new SecretRules();
        readonly Random _rng;
        readonly int _playerCount;
        SecretCpu _cpu;
        SecretState _state;

        public int LocalPlayer => Human;
        public int PlayerCount => _playerCount;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng, int playerCount)
        {
            if (!SecretLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _rng = rng ?? new Random();
            _playerCount = playerCount;
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer != Human;

        public void StartNewGame()
        {
            _cpu = new SecretCpu(new Random(_rng.Next()));
            _state = SecretSetup.CreateRandom(_rng, _playerCount);
            Publish();
        }

        public void SubmitLocalMove(SecretMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>CPU の手番を 1 つ進める(ウラナイ・アカリの続きの決定も、まとめて進める)。</summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            int p = _state.CurrentPlayer;
            int round = _state.Round;
            do
            {
                var move = _cpu.ChooseMove(SecretPlayerView.Create(_state, p));
                _state = _rules.ApplyMove(_state, move);
            } while (!_state.Result.IsOver && _state.CurrentPlayer == p && _state.Round == round && _state.Phase != SecretPhase.Play);
            Publish();
        }

        void Publish()
        {
            Snapshot = MatchSnapshot.For(_state, Human);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
