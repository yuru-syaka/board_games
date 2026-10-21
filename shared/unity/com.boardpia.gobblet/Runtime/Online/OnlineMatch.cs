using System;

namespace BoardGames.Gobblet.Online
{
    /// <summary>
    /// オンライン対局(ホスト権威型)。ホスト = P0 が正式な局面を持ち、ゲスト = P1 から届いた手を検証して適用し、
    /// 新しい局面を送り返す。ゲストは受け取った局面を表示するだけ。
    /// サーバー(AWS)が局面を持つときは、両プレイヤーともゲストとして動き、席(P0 / P1)はサーバーが決める。
    /// </summary>
    public sealed class OnlineMatch : IGobbletMatch, IDisposable
    {
        public const int HostPlayer = 0;
        public const int GuestPlayer = 1;

        readonly IMatchTransport _transport;
        readonly GobbletRules _rules = new GobbletRules();
        readonly Random _rng;
        readonly int _guestPlayer = GuestPlayer;

        public bool IsHost { get; }
        /// <summary>この画面で操作するプレイヤー(ホスト = 0、ゲスト = 1)。</summary>
        public int LocalPlayer => IsHost ? HostPlayer : _guestPlayer;
        public GobbletState State { get; private set; }

        public bool LocalWantsRematch { get; private set; }
        public bool OpponentWantsRematch { get; private set; }

        public event Action<GobbletState> StateChanged;
        public event Action RematchStateChanged;
        /// <summary>壊れた・想定外のメッセージを受け取った(内容は破棄される)。</summary>
        public event Action<string> ProtocolError;

        public OnlineMatch(IMatchTransport transport, bool isHost, Random rng = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            IsHost = isHost;
            _rng = rng ?? new Random();
            _transport.MessageReceived += OnMessage;
        }

        /// <summary>
        /// サーバーが局面を持つ対戦のゲストとして作る。localPlayer はサーバーが割り当てた席(0 か 1)。
        /// 手と再戦の希望をサーバーへ送り、サーバーから届いた局面を表示する。
        /// </summary>
        public OnlineMatch(IMatchTransport transport, int localPlayer)
            : this(transport, false)
        {
            if (localPlayer != 0 && localPlayer != 1) throw new ArgumentOutOfRangeException(nameof(localPlayer));
            _guestPlayer = localPlayer;
        }

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        /// <summary>ホストが新しい対局を始める(先手はランダム)。</summary>
        public void StartNewGame()
        {
            if (!IsHost) throw new InvalidOperationException("Only the host can start a game");
            LocalWantsRematch = false;
            OpponentWantsRematch = false;
            Publish(GobbletState.Initial(_rng.Next(2)));
            RematchStateChanged?.Invoke();
        }

        public bool SubmitMove(GobbletMove move)
        {
            if (State == null || State.Result.IsOver || State.CurrentPlayer != LocalPlayer) return false;
            if (!_rules.IsLegal(State, move)) return false;

            if (IsHost) Publish(_rules.ApplyMove(State, move));
            else _transport.Send(GobbletNetProtocol.EncodeMove(move)); // 最終判断はホスト
            return true;
        }

        /// <summary>再戦を希望する。双方が希望したらホストが新しい対局を始める。</summary>
        public void RequestRematch()
        {
            if (State == null || !State.Result.IsOver || LocalWantsRematch) return;
            LocalWantsRematch = true;
            _transport.Send(GobbletNetProtocol.EncodeRematch());
            RematchStateChanged?.Invoke();
            if (IsHost && OpponentWantsRematch) StartNewGame();
        }

        void OnMessage(string message)
        {
            switch (GobbletNetProtocol.KindOf(message))
            {
                case NetMessageKind.Snapshot when !IsHost:
                    if (!GobbletNetProtocol.TryDecodeSnapshot(message, out var state))
                    {
                        ProtocolError?.Invoke("invalid snapshot");
                        return;
                    }
                    if (state.Ply == 0 && state.LastMove == null)
                    {
                        LocalWantsRematch = false;
                        OpponentWantsRematch = false;
                        RematchStateChanged?.Invoke();
                    }
                    State = state;
                    StateChanged?.Invoke(state);
                    break;

                case NetMessageKind.Move when IsHost:
                    if (!GobbletNetProtocol.TryDecodeMove(message, out var move))
                    {
                        ProtocolError?.Invoke("invalid move message");
                        return;
                    }
                    if (State == null || State.Result.IsOver || State.CurrentPlayer != GuestPlayer || !_rules.IsLegal(State, move))
                    {
                        // 手番違い・非合法: 現在の局面を送り直して同期を取り直す
                        ProtocolError?.Invoke("rejected move " + move);
                        if (State != null) _transport.Send(GobbletNetProtocol.EncodeSnapshot(State));
                        return;
                    }
                    Publish(_rules.ApplyMove(State, move));
                    break;

                case NetMessageKind.Rematch:
                    if (State == null || !State.Result.IsOver) return;
                    OpponentWantsRematch = true;
                    RematchStateChanged?.Invoke();
                    if (IsHost && LocalWantsRematch) StartNewGame();
                    break;

                default:
                    ProtocolError?.Invoke("unexpected message");
                    break;
            }
        }

        /// <summary>ホスト: 局面を更新してゲストへ送る。</summary>
        void Publish(GobbletState state)
        {
            State = state;
            _transport.Send(GobbletNetProtocol.EncodeSnapshot(state));
            StateChanged?.Invoke(state);
        }
    }
}
