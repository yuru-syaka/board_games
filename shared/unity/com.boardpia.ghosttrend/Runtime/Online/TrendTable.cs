using System;
using BoardGames.Core;

namespace BoardGames.GhostTrend.Online
{
    /// <summary>
    /// サーバーの 1 対局(人間 2 人 + CPU)。人間は席 0 = プレイヤー0、席 1 = プレイヤー1、残りの席は CPU。
    /// 人間の手を適用した直後に、人間の番になるまで CPU の手をまとめて進める。
    /// CPU の乱数は「種 + 手数」から作るので、サーバーが「種 + 人間の手の一覧」から作り直しても同じ局面になる。
    /// Unity とサーバー(BoardGames.Server の GhostTrendEngine)の両方で使う。
    /// </summary>
    public sealed class TrendTable
    {
        public const int HumanSeats = 2;

        readonly TrendRules _rules = new TrendRules();
        readonly int _seed;
        TrendState _state;

        public TrendTable(int playerCount, int seed)
        {
            if (!TrendLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _seed = seed;
            _state = TrendSetup.CreateRandom(new Random(seed), playerCount);
            RunCpus();
        }

        public TrendState State => _state;
        public int PlayerCount => _state.PlayerCount;
        public bool IsOver => _state.Result.IsOver;

        /// <summary>今の手番の席(人間の番だけになる。終局後は -1)。</summary>
        public int CurrentSeat => IsOver ? -1 : _state.CurrentPlayer;

        /// <summary>サーバー向けの勝敗: 人間 2 人の所持金を比べる(多いほうの勝ち、同額は引き分け)。</summary>
        public GameResult SeatResult
        {
            get
            {
                if (!IsOver) return GameResult.Ongoing;
                int m0 = _state.Money[0], m1 = _state.Money[1];
                return m0 == m1 ? GameResult.Draw : GameResult.WinFor(m0 > m1 ? 0 : 1);
            }
        }

        public static bool IsCpu(int player) => player >= HumanSeats;

        /// <summary>seat の席の手を検証して適用し、CPU の手を進める。手番違い・非合法なら false。</summary>
        public bool TryApply(int seat, TrendMove move)
        {
            if (IsOver || seat < 0 || seat >= HumanSeats || _state.CurrentPlayer != seat || !_rules.IsLegal(_state, move)) return false;
            _state = _rules.ApplyMove(_state, move);
            RunCpus();
            return true;
        }

        /// <summary>seat の席に送る局面(NetProtocol の "S|…")。</summary>
        public string SnapshotFor(int seat) => TrendNetProtocol.EncodeSnapshot(MatchSnapshot.For(_state, seat));

        void RunCpus()
        {
            while (!_state.Result.IsOver && IsCpu(_state.CurrentPlayer))
            {
                var cpu = new TrendCpu(new Random(unchecked(_seed * 486187739 + _state.Ply * 7919 + 17)));
                var move = cpu.ChooseMove(TrendPlayerView.Create(_state, _state.CurrentPlayer));
                _state = _rules.ApplyMove(_state, move);
            }
        }
    }
}
