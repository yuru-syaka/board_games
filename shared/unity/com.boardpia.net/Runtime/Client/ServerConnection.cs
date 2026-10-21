using System;

namespace BoardGames.Net
{
    /// <summary>
    /// サーバーとの接続の状態。各ゲームの OnlineState と同じ並び(数値でそのまま変換できる)。
    /// </summary>
    public enum ServerState
    {
        Offline,
        Connecting,
        Lobby,
        Matching,
        InRoom,
    }

    /// <summary>
    /// 自前のオンライン対戦サーバー(wss://ws.boardgamepia.com)との接続。6 本のゲームで共通。
    /// - ロビー: 接続、ランダムマッチ、ルームコード(作る / 入る)、退出。メッセージは LobbyProtocol("@…")。
    /// - 対局中のメッセージ(局面・手・再戦)は中身を見ずに GameMessage で渡す / SendGameMessage で送る。
    /// - 何も送らないと API Gateway が 10 分で切るので、5 分おきに合図(@ping)を送る。
    /// 各ゲームの AwsOnlineService(View/Online/)は、これを各ゲームの IOnlineService の形に合わせるだけ。
    /// Unity に依存しない(時刻は Update の引数でもらう)ので、Unity なしでもテストできる。
    /// </summary>
    public sealed class ServerConnection : IDisposable
    {
        public const string DefaultUrl = "wss://ws.boardgamepia.com";
        /// <summary>合図を送る間隔(秒)。API Gateway は 10 分間何も届かないと切る。</summary>
        public const double PingInterval = 300;

        readonly ITextSocket _socket;
        readonly string _url;
        readonly string _gameId;
        Action _afterConnect;
        double _now;
        double _nextPingAt;

        public ServerState State { get; private set; } = ServerState.Offline;
        /// <summary>サーバーが割り当てた席(0 か 1)。0 = 部屋を作った・先に待っていた側。</summary>
        public int LocalSeat { get; private set; }
        public bool OpponentPresent { get; private set; }
        /// <summary>ルームコード(ランダムマッチのときは null)。</summary>
        public string RoomCode { get; private set; }

        public event Action StateChanged;
        public event Action JoinedRoom;
        public event Action OpponentJoined;
        public event Action OpponentLeft;
        /// <summary>利用者に見せるエラー(日本語)。</summary>
        public event Action<string> Error;
        /// <summary>対局中のメッセージ(各ゲームの NetProtocol)。OnlineMatch に渡す。</summary>
        public event Action<string> GameMessage;

        public ServerConnection(string gameId, string url = DefaultUrl, ITextSocket socket = null)
        {
            if (!LobbyProtocol.IsGameId(gameId)) throw new ArgumentException("invalid game id", nameof(gameId));
            _gameId = gameId;
            _url = url;
            _socket = socket ?? new WebSocketConnection();
            _socket.Opened += OnOpened;
            _socket.MessageReceived += OnSocketMessage;
            _socket.Closed += OnClosed;
        }

        public void Dispose()
        {
            _socket.Opened -= OnOpened;
            _socket.MessageReceived -= OnSocketMessage;
            _socket.Closed -= OnClosed;
            _socket.Dispose();
        }

        /// <summary>届いた出来事の処理と、合図の送信。持ち主が毎フレーム呼ぶ(now は秒)。</summary>
        public void Update(double now)
        {
            _now = now;
            _socket.Update();
            if (_socket.State == SocketState.Open && _now >= _nextPingAt) SendRaw(LobbyProtocol.Ping());
        }

        // ---------- 操作 ----------

        public void Connect()
        {
            if (_socket.State == SocketState.Open)
            {
                SetState(RoomCode != null || OpponentPresent ? ServerState.InRoom : ServerState.Lobby);
                return;
            }
            if (_socket.State == SocketState.Connecting) return;
            SetState(ServerState.Connecting);
            _socket.Connect(_url);
        }

        public void JoinRandom() => InLobby(() => SendLobby(LobbyProtocol.Quick(_gameId)));

        public void CreateCodeRoom() => InLobby(() => SendLobby(LobbyProtocol.Create(_gameId)));

        public void JoinCodeRoom(string code)
        {
            if (!LobbyProtocol.IsRoomCode(code))
            {
                Error?.Invoke("ルームコードは数字 4 桁です。");
                return;
            }
            InLobby(() => SendLobby(LobbyProtocol.Join(_gameId, code)));
        }

        public void LeaveRoom()
        {
            _afterConnect = null;
            if (_socket.State == SocketState.Open && (State == ServerState.InRoom || State == ServerState.Matching))
                SendRaw(LobbyProtocol.Leave());
            RoomCode = null;
            OpponentPresent = false;
            if (State == ServerState.InRoom || State == ServerState.Matching)
                SetState(_socket.State == SocketState.Open ? ServerState.Lobby : ServerState.Offline);
        }

        /// <summary>対局中のメッセージ(手・再戦)をサーバーへ送る。部屋にいないときは送らない。</summary>
        public void SendGameMessage(string message)
        {
            if (State == ServerState.InRoom) SendRaw(message);
        }

        // ---------- サーバーから ----------

        void OnOpened()
        {
            _nextPingAt = _now + PingInterval;
            SetState(ServerState.Lobby);
            var next = _afterConnect;
            _afterConnect = null;
            next?.Invoke();
        }

        void OnSocketMessage(string message)
        {
            if (!LobbyProtocol.IsLobbyMessage(message))
            {
                GameMessage?.Invoke(message);
                return;
            }
            if (!LobbyProtocol.TryParse(message, out var lobby)) return;
            switch (lobby.Kind)
            {
                case LobbyMessageKind.Joined:
                    LocalSeat = lobby.Seat;
                    RoomCode = lobby.Code;
                    OpponentPresent = lobby.OpponentPresent;
                    SetState(ServerState.InRoom);
                    JoinedRoom?.Invoke();
                    break;
                case LobbyMessageKind.OpponentJoined:
                    OpponentPresent = true;
                    OpponentJoined?.Invoke();
                    break;
                case LobbyMessageKind.OpponentLeft:
                    OpponentPresent = false;
                    RoomCode = null;
                    SetState(ServerState.Lobby);
                    OpponentLeft?.Invoke();
                    break;
                case LobbyMessageKind.Error:
                    if (State == ServerState.Matching) SetState(ServerState.Lobby);
                    Error?.Invoke(ErrorText(lobby.ErrorCode));
                    break;
            }
        }

        void OnClosed(string reason)
        {
            bool wasInRoom = State == ServerState.InRoom;
            bool wasConnecting = State == ServerState.Connecting;
            _afterConnect = null;
            RoomCode = null;
            OpponentPresent = false;
            SetState(ServerState.Offline);
            if (wasInRoom) Error?.Invoke("通信が切断されました。");
            else if (wasConnecting) Error?.Invoke("サーバーに接続できませんでした。");
        }

        // ---------- 内部 ----------

        /// <summary>サーバーにつながっていなければつなぎ、つながってから action を実行する。</summary>
        void InLobby(Action action)
        {
            if (_socket.State == SocketState.Open)
            {
                action();
                return;
            }
            _afterConnect = action;
            Connect();
        }

        void SendLobby(string message)
        {
            SetState(ServerState.Matching);
            if (!SendRaw(message))
            {
                SetState(ServerState.Offline);
                Error?.Invoke("サーバーに接続できませんでした。");
            }
        }

        bool SendRaw(string message)
        {
            _nextPingAt = _now + PingInterval;
            return _socket.Send(message);
        }

        void SetState(ServerState s)
        {
            State = s;
            StateChanged?.Invoke();
        }

        static string ErrorText(string code)
        {
            switch (code)
            {
                case LobbyErrors.RoomNotFound: return "ルームが見つかりません。コードを確かめてください。";
                case LobbyErrors.RoomFull: return "そのルームは満員です。";
                case LobbyErrors.ServerBusy: return "サーバーが混み合っています。少し待ってからもう一度お試しください。";
                default: return "サーバーとの通信でエラーが起きました。";
            }
        }
    }
}
