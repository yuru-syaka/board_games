using System;

namespace BoardGames.GhostReversa
{
    /// <summary>人間(P0)対 CPU(P1)。CPU の手は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : IReversaMatch
    {
        public const int Human = 0;
        public const int Cpu = 1;

        readonly ReversaRules _rules = new ReversaRules();
        readonly Random _rng;
        ReversaCpu _cpu;
        ReversaState _state;

        public int LocalPlayer => Human;
        public MatchSnapshot Snapshot { get; private set; }
        public event Action<MatchSnapshot> SnapshotChanged;

        public LocalCpuMatch(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public bool IsCpuTurn => _state != null && !_state.Result.IsOver && _state.CurrentPlayer == Cpu;

        public void StartNewGame()
        {
            _cpu = new ReversaCpu(new Random(_rng.Next()));
            _state = ReversaSetup.CreateRandom(_rng);
            Publish();
        }

        public void SubmitLocalMove(ReversaMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            var move = _cpu.ChooseMove(ReversaPlayerView.Create(_state, Cpu));
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
