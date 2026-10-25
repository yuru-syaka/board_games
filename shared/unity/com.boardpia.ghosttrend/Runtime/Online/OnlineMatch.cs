using System;

namespace BoardGames.GhostTrend.Online
{
    /// <summary>
    /// オンライン対局(サーバー権威型)。サーバーが局面を持ち、CPU の席もサーバーが動かす。
    /// この画面はサーバーが決めた席(0 か 1)のゲストとして、手と再戦の希望を送り、届いた局面を表示するだけ。
    /// </summary>
    public sealed class OnlineMatch : ITrendMatch, IDisposable
    {
        readonly IMatchTransport _transport;

        public int LocalPlayer { get; }
        public MatchSnapshot Snapshot { get; private set; }

        public bool LocalWantsRematch { get; private set; }
        public bool OpponentWantsRematch { get; private set; }

        public event Action<MatchSnapshot> SnapshotChanged;
        public event Action RematchStateChanged;
        /// <summary>壊れた・想定外のメッセージを受け取った(内容は破棄される)。</summary>
        public event Action<string> ProtocolError;

        public OnlineMatch(IMatchTransport transport, int localPlayer)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (localPlayer != 0 && localPlayer != 1) throw new ArgumentOutOfRangeException(nameof(localPlayer));
            LocalPlayer = localPlayer;
            _transport.MessageReceived += OnMessage;
        }

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void SubmitLocalMove(TrendMove move)
        {
            if (Snapshot == null || !Snapshot.View.IsLegal(move)) return; // 最終判断はサーバー
            _transport.Send(TrendNetProtocol.EncodeMove(move));
        }

        /// <summary>再戦を希望する。2 人が希望したらサーバーが新しい対局を始める。</summary>
        public void RequestRematch()
        {
            if (Snapshot == null || !Snapshot.Result.IsOver || LocalWantsRematch) return;
            LocalWantsRematch = true;
            _transport.Send(TrendNetProtocol.EncodeRematch());
            RematchStateChanged?.Invoke();
        }

        void OnMessage(string message)
        {
            switch (TrendNetProtocol.KindOf(message))
            {
                case NetMessageKind.Snapshot:
                    if (!TrendNetProtocol.TryDecodeSnapshot(message, out var snapshot) || snapshot.Viewer != LocalPlayer)
                    {
                        ProtocolError?.Invoke("invalid snapshot");
                        return;
                    }
                    if (MatchSnapshot.StartsNewGame(Snapshot, snapshot))
                    {
                        LocalWantsRematch = false;
                        OpponentWantsRematch = false;
                        RematchStateChanged?.Invoke();
                    }
                    Snapshot = snapshot;
                    SnapshotChanged?.Invoke(snapshot);
                    break;

                case NetMessageKind.Rematch:
                    if (Snapshot == null || !Snapshot.Result.IsOver) return;
                    OpponentWantsRematch = true;
                    RematchStateChanged?.Invoke();
                    break;

                default:
                    ProtocolError?.Invoke("unexpected message");
                    break;
            }
        }
    }
}
