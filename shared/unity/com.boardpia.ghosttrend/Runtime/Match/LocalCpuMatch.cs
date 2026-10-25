using System;

namespace BoardGames.GhostTrend
{
    /// <summary>人間(P0)対 CPU(P1..)。CPU の手は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : ITrendMatch
    {
        public const int Human = 0;

        readonly TrendRules _rules = new TrendRules();
        readonly Random _rng;
        readonly int _playerCount;
        TrendCpu _cpu;
        TrendState _state;

        public int LocalPlayer => Human;
        public int PlayerCount => _playerCount;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng, int playerCount)
        {
            if (!TrendLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _rng = rng ?? new Random();
            _playerCount = playerCount;
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer != Human;

        public void StartNewGame()
        {
            _cpu = new TrendCpu(new Random(_rng.Next()));
            _state = TrendSetup.CreateRandom(_rng, _playerCount);
            Publish();
        }

        public void SubmitLocalMove(TrendMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            var move = _cpu.ChooseMove(TrendPlayerView.Create(_state, _state.CurrentPlayer));
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        void Publish()
        {
            Snapshot = MatchSnapshot.For(_state, Human);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
