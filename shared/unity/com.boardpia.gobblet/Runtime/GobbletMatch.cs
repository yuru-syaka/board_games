using System;

namespace BoardGames.Gobblet
{
    /// <summary>
    /// 画面から見た 1 対局。人間 vs CPU(CpuPlayer = 1)と、1 台の画面を交代で使うふたり対戦(CpuPlayer = -1)。
    /// CPU の手は View が演出を挟んでから PlayCpuTurn で進める。
    /// </summary>
    public sealed class GobbletMatch : IGobbletMatch
    {
        public const int NoCpu = -1;

        readonly GobbletRules _rules = new GobbletRules();
        readonly Random _rng;
        GobbletCpu _cpu;

        /// <summary>CPU が担当するプレイヤー(いなければ NoCpu)。</summary>
        public int CpuPlayer { get; }
        public bool HasCpu => CpuPlayer != NoCpu;
        /// <summary>最新の局面。対局開始前は null。</summary>
        public GobbletState State { get; private set; }
        public event Action<GobbletState> StateChanged;

        public GobbletMatch(Random rng, int cpuPlayer)
        {
            if (cpuPlayer != NoCpu && cpuPlayer != 0 && cpuPlayer != 1) throw new ArgumentOutOfRangeException(nameof(cpuPlayer));
            _rng = rng ?? new Random();
            CpuPlayer = cpuPlayer;
        }

        public bool IsCpuTurn => HasCpu && State != null && !State.Result.IsOver && State.CurrentPlayer == CpuPlayer;

        /// <summary>人間が操作する手番か。</summary>
        public bool IsHumanTurn => State != null && !State.Result.IsOver && !IsCpuTurn;

        /// <summary>新しい対局を始める。firstPlayer を省略すると先手はランダム。</summary>
        public void StartNewGame(int? firstPlayer = null)
        {
            _cpu = HasCpu ? new GobbletCpu(new Random(_rng.Next())) : null;
            State = GobbletState.Initial(firstPlayer ?? _rng.Next(2));
            StateChanged?.Invoke(State);
        }

        /// <summary>人間の手を指す。CPU の手番・非合法の手は無視して false を返す。</summary>
        public bool SubmitMove(GobbletMove move)
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
