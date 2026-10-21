using System;

namespace BoardGames.CenturyGhost
{
    /// <summary>人間(P0)対 CPU(P1)。CPU の手は View が演出を挟んでから PlayCpuTurn で進める。</summary>
    public sealed class LocalCpuMatch : ICenturyMatch
    {
        public const int Human = 0;
        public const int Cpu = 1;

        readonly CenturyRules _rules = new CenturyRules();
        readonly Random _rng;
        CenturyCpu _cpu;
        CenturyState _state;

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
            _cpu = new CenturyCpu(new Random(_rng.Next()));
            _state = CenturySetup.CreateRandom(_rng);
            Publish();
        }

        public void SubmitLocalMove(CenturyMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            var move = _cpu.ChooseMove(CenturyPlayerView.Create(_state, Cpu));
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
