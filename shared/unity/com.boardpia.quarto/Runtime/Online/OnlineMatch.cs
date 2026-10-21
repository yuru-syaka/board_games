using System;

namespace BoardGames.Quarto.Online
{
    /// <summary>
    /// オンライン対局(ホスト権威型)。ホスト = P0 が完全な局面を持ち、ゲスト = P1 から届いた手を検証して適用し、
    /// ゲストには同じ局面(ゲスト視点)を送る。ゲストは受け取った局面を表示するだけ。
    /// </summary>
    public sealed class OnlineMatch : IQuartoMatch, IDisposable
    {
        public const int HostPlayer = 0;
        public const int GuestPlayer = 1;

        readonly IMatchTransport _transport;
        readonly QuartoRules _rules = new QuartoRules();
        readonly Random _rng;
        QuartoState _state; // ホストだけが持つ

        public bool IsHost { get; }
        public int LocalPlayer => IsHost ? HostPlayer : GuestPlayer;
        public MatchSnapshot Snapshot { get; private set; }

        public bool LocalWantsRematch { get; private set; }
        public bool OpponentWantsRematch { get; private set; }

        public event Action<MatchSnapshot> SnapshotChanged;
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

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        /// <summary>ホストが新しい対局を始める(先手はランダム)。</summary>
        public void StartNewGame()
        {
            if (!IsHost) throw new InvalidOperationException("Only the host can start a game");
            _state = QuartoSetup.CreateRandom(_rng);
            LocalWantsRematch = false;
            OpponentWantsRematch = false;
            Broadcast();
            RematchStateChanged?.Invoke();
        }

        public void SubmitLocalMove(QuartoMove move)
        {
            if (Snapshot == null || Snapshot.Result.IsOver || Snapshot.CurrentPlayer != LocalPlayer) return;

            if (IsHost)
            {
                TryApply(move, HostPlayer);
            }
            else
            {
                // 自分の視点で合法でなければ送らない(最終判断はホスト)
                if (!Snapshot.IsLegal(move)) return;
                _transport.Send(QuartoNetProtocol.EncodeMove(move));
            }
        }

        /// <summary>再戦を希望する。双方が希望したらホストが新しい対局を始める。</summary>
        public void RequestRematch()
        {
            if (Snapshot == null || !Snapshot.Result.IsOver || LocalWantsRematch) return;
            LocalWantsRematch = true;
            _transport.Send(QuartoNetProtocol.EncodeRematch());
            RematchStateChanged?.Invoke();
            if (IsHost && OpponentWantsRematch) StartNewGame();
        }

        void OnMessage(string message)
        {
            switch (QuartoNetProtocol.KindOf(message))
            {
                case NetMessageKind.Snapshot when !IsHost:
                    if (!QuartoNetProtocol.TryDecodeSnapshot(message, out var snapshot) || snapshot.Viewer != GuestPlayer)
                    {
                        ProtocolError?.Invoke("invalid snapshot");
                        return;
                    }
                    if (snapshot.IsNewGame)
                    {
                        LocalWantsRematch = false;
                        OpponentWantsRematch = false;
                        RematchStateChanged?.Invoke();
                    }
                    Snapshot = snapshot;
                    SnapshotChanged?.Invoke(snapshot);
                    break;

                case NetMessageKind.Move when IsHost:
                    if (!QuartoNetProtocol.TryDecodeMove(message, out var move))
                    {
                        ProtocolError?.Invoke("invalid move message");
                        return;
                    }
                    if (!TryApply(move, GuestPlayer))
                    {
                        // 手番違い・非合法: 現在の局面を送り直して同期を取り直す
                        ProtocolError?.Invoke("rejected move " + move);
                        if (_state != null) _transport.Send(QuartoNetProtocol.EncodeSnapshot(MatchSnapshot.For(_state, GuestPlayer)));
                    }
                    break;

                case NetMessageKind.Rematch:
                    if (Snapshot == null || !Snapshot.Result.IsOver) return;
                    OpponentWantsRematch = true;
                    RematchStateChanged?.Invoke();
                    if (IsHost && LocalWantsRematch) StartNewGame();
                    break;

                default:
                    ProtocolError?.Invoke("unexpected message");
                    break;
            }
        }

        bool TryApply(QuartoMove move, int player)
        {
            if (_state == null || _state.Result.IsOver || _state.CurrentPlayer != player) return false;
            if (!_rules.IsLegal(_state, move)) return false;
            _state = _rules.ApplyMove(_state, move);
            Broadcast();
            return true;
        }

        void Broadcast()
        {
            _transport.Send(QuartoNetProtocol.EncodeSnapshot(MatchSnapshot.For(_state, GuestPlayer)));
            Snapshot = MatchSnapshot.For(_state, HostPlayer);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
