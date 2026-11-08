using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostSecret.Online
{
    /// <summary>
    /// サーバーの 1 対局。全席が人間(2〜4 人)で始まり、途中で抜けた・戻らなかった・時間切れが続いた席だけ CPU が代わる。
    /// CPU の手は、その席の番になったらまとめて進める(ウラナイ・アカリの続きの決定も含む)。CPU の乱数は「種 + 手数」から作るので、
    /// サーバーが「種 + 手の一覧(CPU に代わった時点も含む)」から作り直しても同じ局面になる。
    /// Unity とサーバー(BoardGames.Server の GhostSecretEngine)の両方で使う。
    /// </summary>
    public sealed class SecretTable
    {
        /// <summary>時間切れの自動の手が続いて、この回数になったら CPU に代わる。</summary>
        public const int TimeoutStrikesToTakeover = 3;

        readonly SecretRules _rules = new SecretRules();
        readonly int _seed;
        readonly bool[] _cpu;
        // CPU に代わった順(早く抜けた人ほど順位が下)
        readonly List<int> _takeoverOrder = new List<int>();
        SecretState _state;

        public SecretTable(int playerCount, int seed)
        {
            if (!SecretLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            _seed = seed;
            _cpu = new bool[playerCount];
            _state = SecretSetup.CreateRandom(new Random(seed), playerCount);
        }

        public SecretState State => _state;
        public int PlayerCount => _state.PlayerCount;
        public bool IsOver => _state.Result.IsOver;

        /// <summary>今の手番の席(人間の番だけになる。終局後は -1)。</summary>
        public int CurrentSeat => IsOver ? -1 : _state.CurrentPlayer;

        public bool IsCpu(int seat) => seat >= 0 && seat < _cpu.Length && _cpu[seat];

        public int HumanCount => _cpu.Count(c => !c);

        /// <summary>
        /// 席ごとの順位(0 = 1 位)。人間の席はかけらの多い順で、同数なら同じ順位。
        /// CPU に代わった席は人間より下で、早く代わった席ほど下。終局前は今のかけらで数える。
        /// </summary>
        public IReadOnlyList<int> Ranks
        {
            get
            {
                var ranks = new int[PlayerCount];
                var humans = Enumerable.Range(0, PlayerCount).Where(s => !_cpu[s]).ToList();
                foreach (int s in humans)
                    ranks[s] = humans.Count(o => _state.Players[o].Tokens > _state.Players[s].Tokens);
                int next = humans.Count;
                for (int i = _takeoverOrder.Count - 1; i >= 0; i--) ranks[_takeoverOrder[i]] = next++;
                return ranks;
            }
        }

        /// <summary>サーバー向けの勝敗: 1 位が 1 人ならその席の勝ち、1 位が並んだら引き分け。</summary>
        public GameResult SeatResult
        {
            get
            {
                if (!IsOver) return GameResult.Ongoing;
                var ranks = Ranks;
                var first = Enumerable.Range(0, PlayerCount).Where(s => ranks[s] == 0).ToList();
                return first.Count == 1 ? GameResult.WinFor(first[0]) : GameResult.Draw;
            }
        }

        /// <summary>seat の席の手を検証して適用し、CPU の席の番が続けば進める。手番違い・非合法・CPU の席なら false。</summary>
        public bool TryApply(int seat, SecretMove move)
        {
            if (IsOver || seat < 0 || seat >= PlayerCount || _cpu[seat] || _state.CurrentPlayer != seat || !_rules.IsLegal(_state, move))
                return false;
            _state = _rules.ApplyMove(_state, move);
            RunCpus();
            return true;
        }

        /// <summary>以後その席を CPU が代わる。その席の番なら、すぐに進める。</summary>
        public bool Takeover(int seat)
        {
            if (IsOver || seat < 0 || seat >= PlayerCount || _cpu[seat]) return false;
            _cpu[seat] = true;
            _takeoverOrder.Add(seat);
            RunCpus();
            return true;
        }

        /// <summary>時間切れの席の代わりに指す手(適用はしない)。CPU と同じ考え方で選ぶ。</summary>
        public bool TryGetAutoMove(int seat, out SecretMove move)
        {
            move = default;
            if (IsOver || seat < 0 || seat >= PlayerCount || _cpu[seat] || _state.CurrentPlayer != seat) return false;
            move = CpuMove();
            return true;
        }

        /// <summary>seat の席に送る局面(NetProtocol の "S|…")。</summary>
        public string SnapshotFor(int seat) => SecretNetProtocol.EncodeSnapshot(MatchSnapshot.For(_state, seat));

        SecretMove CpuMove()
        {
            var cpu = new SecretCpu(new Random(unchecked(_seed * 486187739 + _state.Ply * 7919 + 17)));
            return cpu.ChooseMove(SecretPlayerView.Create(_state, _state.CurrentPlayer));
        }

        void RunCpus()
        {
            while (!_state.Result.IsOver && _cpu[_state.CurrentPlayer])
                _state = _rules.ApplyMove(_state, CpuMove());
        }
    }
}
