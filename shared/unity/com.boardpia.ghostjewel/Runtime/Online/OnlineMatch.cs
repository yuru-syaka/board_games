using System;

namespace BoardGames.GhostJewel.Online
{
    /// <summary>
    /// オンライン対局(サーバー権威型)。全席が人間(2〜4 人)で、途中で抜けた席だけサーバーの CPU が代わる。
    /// この画面はサーバーが決めた席(0〜3)のゲストとして、手と再戦の希望を送り、届いた局面を表示するだけ。
    /// </summary>
    public sealed class OnlineMatch : IJewelMatch, IDisposable
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
            if (localPlayer < 0 || localPlayer >= JewelLogic.MaxPlayers) throw new ArgumentOutOfRangeException(nameof(localPlayer));
            LocalPlayer = localPlayer;
            _transport.MessageReceived += OnMessage;
        }

        public void Dispose() => _transport.MessageReceived -= OnMessage;

        public void SubmitLocalMove(JewelMove move)
        {
            if (Snapshot == null || !Snapshot.View.IsLegal(move)) return; // 最終判断はサーバー
            _transport.Send(JewelNetProtocol.EncodeMove(move));
        }

        /// <summary>再戦を希望する。つながっている全員が希望したらサーバーが新しい対局を始める。</summary>
        public void RequestRematch()
        {
            if (Snapshot == null || !Snapshot.Result.IsOver || LocalWantsRematch) return;
            LocalWantsRematch = true;
            _transport.Send(JewelNetProtocol.EncodeRematch());
            RematchStateChanged?.Invoke();
        }

        void OnMessage(string message)
        {
            switch (JewelNetProtocol.KindOf(message))
            {
                case NetMessageKind.Snapshot:
                    if (!JewelNetProtocol.TryDecodeSnapshot(message, out var snapshot) || snapshot.View.Viewer != LocalPlayer)
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
