using System;

namespace BoardGames.BattleLine
{
    /// <summary>人間(P0)対 CPU(P1)。CPU の行動は View が演出を挟んでから PlayCpuTurn で 1 つずつ進める。</summary>
    public sealed class LocalCpuMatch : IBattleLineMatch
    {
        public const int Human = 0;
        public const int Cpu = 1;

        readonly BattleLineRules _rules = new BattleLineRules();
        readonly Random _rng;
        BattleLineCpu _cpu;
        BattleLineState _state;

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
            _cpu = new BattleLineCpu(new Random(_rng.Next()));
            _state = BattleLineSetup.CreateRandom(_rng);
            Publish();
        }

        public void SubmitLocalMove(BattleLineMove move)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != Human) return;
            if (!_rules.IsLegal(_state, move)) return;
            _state = _rules.ApplyMove(_state, move);
            Publish();
        }

        /// <summary>CPU の行動を 1 つ進める(出す・引く・偵察の各段階は別々に呼ぶ)。</summary>
        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            var move = _cpu.ChooseMove(BattleLinePlayerView.Create(_state, Cpu));
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
