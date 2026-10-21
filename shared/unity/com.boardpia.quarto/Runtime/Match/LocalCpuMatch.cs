using System;

namespace BoardGames.Quarto
{
    /// <summary>人間(P0)対 CPU(P1)。CPU の手は View が演出を挟んでから PlayCpuTurn で 1 行動ずつ進める。</summary>
    public sealed class LocalCpuMatch : IQuartoMatch
    {
        public const int Human = 0;
        public const int Cpu = 1;

        readonly QuartoRules _rules = new QuartoRules();
        readonly Random _rng;
        QuartoCpu _cpu;
        QuartoState _state;

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
            _cpu = new QuartoCpu(new Random(_rng.Next()));
            _state = QuartoSetup.CreateRandom(_rng);
            Publish();
        }

        public void SubmitLocalMove(QuartoMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>CPU の行動を 1 つ(置く または 渡す)進める。</summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            var move = _cpu.ChooseMove(_state);
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
