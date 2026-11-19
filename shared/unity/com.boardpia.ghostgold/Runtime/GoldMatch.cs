using System;
using BoardGames.Core;

namespace BoardGames.GhostGold
{
    /// <summary>画面に出してよい情報だけをまとめた、あるプレイヤー視点の局面。</summary>
    public sealed class MatchSnapshot
    {
        public GoldPlayerView View { get; }
        public int PlayerCount => View.PlayerCount;
        public int CurrentPlayer => View.CurrentPlayer;
        public int Ply => View.Ply;
        public GameResult Result => View.Result;
        public bool IsNewGame => View.Ply == 0;

        public MatchSnapshot(GoldPlayerView view)
        {
            View = view;
        }

        /// <summary>next が新しい対局の局面か(手数が戻った・終局のあとに終局していない局面が来た)。</summary>
        public static bool StartsNewGame(MatchSnapshot previous, MatchSnapshot next) =>
            previous == null ? next.IsNewGame : next.Ply < previous.Ply || (previous.Result.IsOver && !next.Result.IsOver);

        public static MatchSnapshot For(GoldState state, int viewer) => new MatchSnapshot(GoldPlayerView.Create(state, viewer));
    }

    /// <summary>画面から見た 1 対局。View は Snapshot を描画し、自分の手を SubmitLocalMove で送るだけ。</summary>
    public interface IGoldMatch
    {
        int LocalPlayer { get; }
        MatchSnapshot Snapshot { get; }
        event Action<MatchSnapshot> SnapshotChanged;
        void SubmitLocalMove(GoldMove move);
    }

    /// <summary>人間(P0)対 CPU(P1..)。CPU の決定は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : IGoldMatch
    {
        public const int Human = 0;

        readonly GoldRules _rules = new GoldRules();
        readonly Random _rng;
        readonly int _playerCount;
        GoldCpu[] _cpus;
        GoldState _state;

        public int LocalPlayer => Human;
        public int PlayerCount => _playerCount;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng, int playerCount)
        {
            if (!GoldLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _rng = rng ?? new Random();
            _playerCount = playerCount;
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer != Human;

        public void StartNewGame()
        {
            _cpus = new GoldCpu[_playerCount];
            for (int p = 1; p < _playerCount; p++) _cpus[p] = new GoldCpu(new Random(_rng.Next()));
            _state = GoldSetup.CreateRandom(_rng, _playerCount);
            Publish();
        }

        public void SubmitLocalMove(GoldMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>
        /// CPU の決定を、次の 1 枚がめくられる(または夜・対局が終わる)か、あなたの番になるまで進める。
        /// CPU の決定は公開まで見えないので、1 人ずつ見せる必要はない。
        /// </summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            int path = _state.Path.Count, night = _state.Night;
            while (IsCpuTurn)
            {
                int p = _state.CurrentPlayer;
                _state = _rules.ApplyMove(_state, _cpus[p].ChooseMove(GoldPlayerView.Create(_state, p)));
                if (_state.Path.Count != path || _state.Night != night) break;
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
