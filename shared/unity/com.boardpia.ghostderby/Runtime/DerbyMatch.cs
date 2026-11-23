using System;
using BoardGames.Core;

namespace BoardGames.GhostDerby
{
    /// <summary>画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。</summary>
    public sealed class MatchSnapshot
    {
        public DerbyPlayerView View { get; }
        public int PlayerCount => View.PlayerCount;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GameResult Result => View.Result;
        public bool IsNewGame => View.Ply == 0;

        public MatchSnapshot(DerbyPlayerView view)
        {
            View = view;
        }

        /// <summary>next が新しい対局の局面か(手数が戻った・終局のあとに終局していない局面が来た)。</summary>
        public static bool StartsNewGame(MatchSnapshot previous, MatchSnapshot next) =>
            previous == null ? next.IsNewGame : next.Ply < previous.Ply || (previous.Result.IsOver && !next.Result.IsOver);

        public static MatchSnapshot For(DerbyState state, int viewer) => new MatchSnapshot(DerbyPlayerView.Create(state, viewer));
    }

    /// <summary>画面から見た 1 対局。View は Snapshot を描画し、自分の手を SubmitLocalMove で送るだけ。</summary>
    public interface IDerbyMatch
    {
        int LocalPlayer { get; }
        MatchSnapshot Snapshot { get; }
        event Action<MatchSnapshot> SnapshotChanged;
        void SubmitLocalMove(DerbyMove move);
    }

    /// <summary>人間(P0)対 CPU(P1..)。CPU の決定は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : IDerbyMatch
    {
        public const int Human = 0;

        readonly DerbyRules _rules = new DerbyRules();
        readonly Random _rng;
        readonly int _playerCount;
        DerbyCpu[] _cpus;
        DerbyState _state;

        public int LocalPlayer => Human;
        public int PlayerCount => _playerCount;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng, int playerCount)
        {
            if (!DerbyLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _rng = rng ?? new Random();
            _playerCount = playerCount;
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer != Human;

        public void StartNewGame()
        {
            _cpus = new DerbyCpu[_playerCount];
            for (int p = 1; p < _playerCount; p++) _cpus[p] = new DerbyCpu(new Random(_rng.Next()));
            _state = DerbySetup.CreateRandom(_rng, _playerCount);
            Publish();
        }

        public void SubmitLocalMove(DerbyMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>
        /// CPU の決定を、次の場面になるか、あなたの番になるまで進める。
        /// CPU の決定は公開まで見えないので、1 人ずつ見せる必要はない。
        /// </summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            int phase = (int)_state.Phase, race = _state.Race;
            while (IsCpuTurn)
            {
                int p = _state.CurrentPlayer;
                _state = _rules.ApplyMove(_state, _cpus[p].ChooseMove(DerbyPlayerView.Create(_state, p)));
                if ((int)_state.Phase != phase || _state.Race != race) break;
            }
            Publish();
        }

        void Publish()
        {
            Snapshot = MatchSnapshot.For(_state, Human);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
