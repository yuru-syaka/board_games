using System;

namespace BoardGames.Vanish
{
    /// <summary>
    /// 画面から見た CPU 対局。人間 = P0、CPU = CpuPlayer。
    /// CPU の手は View が演出を挟んでから PlayCpuTurn で進める。
    /// </summary>
    public sealed class VanishMatch : IVanishMatch
    {
        readonly VanishRules _rules = new VanishRules();
        readonly Random _rng;
        VanishCpu _cpu;

        /// <summary>CPU が担当するプレイヤー。</summary>
        public int CpuPlayer { get; }
        /// <summary>最新の局面。対局開始前は null。</summary>
        public VanishState State { get; private set; }
        public event Action<VanishState> StateChanged;

        public VanishMatch(Random rng, int cpuPlayer)
        {
            if (cpuPlayer != 0 && cpuPlayer != 1) throw new ArgumentOutOfRangeException(nameof(cpuPlayer));
            _rng = rng ?? new Random();
            CpuPlayer = cpuPlayer;
        }

        public bool IsCpuTurn => State != null && !State.Result.IsOver && State.CurrentPlayer == CpuPlayer;

        /// <summary>人間が操作する手番か。</summary>
        public bool IsHumanTurn => State != null && !State.Result.IsOver && !IsCpuTurn;

        /// <summary>新しい対局を始める。firstPlayer を省略すると先手はランダム。</summary>
        public void StartNewGame(int? firstPlayer = null)
        {
            _cpu = new VanishCpu(new Random(_rng.Next()));
            State = VanishState.Initial(firstPlayer ?? _rng.Next(2));
            StateChanged?.Invoke(State);
        }

        /// <summary>人間の手を指す。CPU の手番・非合法の手は無視して false を返す。</summary>
        public bool SubmitMove(VanishMove move)
        {
            if (!IsHumanTurn || !_rules.IsLegal(State, move)) return false;
            State = _rules.ApplyMove(State, move);
            StateChanged?.Invoke(State);
            return true;
        }

        public void PlayCpuTurn()
        {
            if (!IsCpuTurn) return;
            State = _rules.ApplyMove(State, _cpu.ChooseMove(State));
            StateChanged?.Invoke(State);
        }
    }
}
