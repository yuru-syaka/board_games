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
    /// - 対局中に通信が切れたら、自動でつなぎ直して合言葉で同じ席に戻る(ConnectionLost → Reconnected)。
    ///   合言葉はタブの中(ISessionStore)にも覚えるので、ページを再読み込みしても Connect で戻れる(IsResuming)。
    /// - 相手の通信が切れたら OpponentAway(待つ秒数)、戻ったら OpponentBack。待ち時間を過ぎたら Claim で勝ちを申請する。
    /// - 手番の残り時間は TurnTimer(手番の席, 秒)。相手の手番で時間が過ぎたら Claim で勝ちを申請する。
    /// 各ゲームの AwsOnlineService(View/Online/)は、これを各ゲームの IOnlineService の形に合わせるだけ。
    /// Unity に依存しない(時刻は Update の引数でもらう)ので、Unity なしでもテストできる。
    /// </summary>
    public sealed class ServerConnection : IDisposable
    {
        public const string DefaultUrl = "wss://ws.boardgamepia.com";
        /// <summary>合図を送る間隔(秒)。API Gateway は 10 分間何も届かないと切る。</summary>
        public const double PingInterval = 300;
        /// <summary>つなぎ直しをあきらめるまでの時間(秒)。サーバーの待ち時間(90 秒)より少し長くする。</summary>
        public const double ReconnectGiveUp = 100;
        /// <summary>つなぎ直す間隔の上限(秒)。1 → 2 → 4 → 8 → 8 …</summary>
        const double MaxReconnectDelay = 8;

        readonly ITextSocket _socket;
        readonly ISessionStore _sessions;
        readonly string _url;
        readonly string _gameId;
        readonly string _sessionKey;
        Action _afterConnect;
        double _now;
        double _nextPingAt;
        string _token;
        /// <summary>対局中に切れて、つなぎ直しているところ。</summary>
        bool _reconnecting;
        double _reconnectAt;
        double _reconnectDeadline;
        int _reconnectTries;
        /// <summary>ページを開いた直後で、覚えていた合言葉で戻ろうとしているところ。</summary>
        bool _resumingSaved;

        public ServerState State { get; private set; } = ServerState.Offline;
        /// <summary>サーバーが割り当てた席(0 か 1)。0 = 部屋を作った・先に待っていた側。</summary>
        public int LocalSeat { get; private set; }
        public bool OpponentPresent { get; private set; }
        /// <summary>ルームコード(ランダムマッチのときは null)。</summary>
        public string RoomCode { get; private set; }
        /// <summary>JoinedRoom が「再読み込み前の対局に戻った」ことで起きたときだけ true(画面はメニューを閉じて対局に戻る)。</summary>
        public bool IsResuming { get; private set; }
        /// <summary>今つなぎ直しているところか(通信が切れたあと)。</summary>
        public bool IsReconnecting => _reconnecting;
        /// <summary>このタブに、戻れるかもしれない対局の合言葉が残っているか(ページの再読み込み後)。</summary>
        public bool HasSavedSession => LobbyProtocol.IsToken(_sessions.Load(_sessionKey));

        public event Action StateChanged;
        public event Action JoinedRoom;
        public event Action OpponentJoined;
        public event Action OpponentLeft;
        /// <summary>利用者に見せるエラー・対局の打ち切り(日本語)。</summary>
        public event Action<string> Error;
        /// <summary>対局中のメッセージ(各ゲームの NetProtocol)。OnlineMatch に渡す。</summary>
        public event Action<string> GameMessage;
        /// <summary>自分の通信が切れた(つなぎ直しを始めた)。</summary>
        public event Action ConnectionLost;
        /// <summary>つなぎ直して、同じ席に戻れた(このあと今の局面が GameMessage で届く)。</summary>
        public event Action Reconnected;
        /// <summary>相手の通信が切れた(引数: 戻るのを待つ秒数。過ぎたら Claim できる)。</summary>
        public event Action<int> OpponentAway;
        /// <summary>通信が切れていた相手が戻った。</summary>
        public event Action OpponentBack;
        /// <summary>手番の残り時間(手番の席, 秒)。席が -1 なら時間の計測なし(終局後など)。</summary>
        public event Action<int, int> TurnTimer;

        public ServerConnection(string gameId, string url = DefaultUrl, ITextSocket socket = null, ISessionStore sessions = null)
        {
            if (!LobbyProtocol.IsGameId(gameId)) throw new ArgumentException("invalid game id", nameof(gameId));
            _gameId = gameId;
            _url = url;
            _sessionKey = "boardpia.session." + gameId;
            _socket = socket ?? new WebSocketConnection();
            _sessions = sessions ?? new BrowserSessionStore();
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

        /// <summary>届いた出来事の処理、合図の送信、つなぎ直し。持ち主が毎フレーム呼ぶ(now は秒)。</summary>
        public void Update(double now)
        {
            _now = now;
            _socket.Update();
            if (_socket.State == SocketState.Open && _now >= _nextPingAt) SendRaw(LobbyProtocol.Ping());
            if (_reconnecting && _socket.State == SocketState.Closed && _now >= _reconnectAt)
            {
                if (_now >= _reconnectDeadline) GiveUpReconnecting();
                else _socket.Connect(_url);
            }
        }

        // ---------- 操作 ----------

        /// <summary>サーバーにつなぐ。このタブに対局の合言葉が残っていれば、その対局に戻ろうとする。</summary>
        public void Connect()
        {
            if (_socket.State == SocketState.Open)
            {
                SetState(RoomCode != null || OpponentPresent ? ServerState.InRoom : ServerState.Lobby);
                return;
            }
            if (_socket.State == SocketState.Connecting || _reconnecting) return;
            var saved = _sessions.Load(_sessionKey);
            _resumingSaved = State == ServerState.Offline && LobbyProtocol.IsToken(saved);
            if (_resumingSaved) _token = saved;
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
            _reconnecting = false;
            if (_socket.State == SocketState.Open && (State == ServerState.InRoom || State == ServerState.Matching))
                SendRaw(LobbyProtocol.Leave());
            ForgetSession();
            RoomCode = null;
            OpponentPresent = false;
            if (State == ServerState.InRoom || State == ServerState.Matching)
                SetState(_socket.State == SocketState.Open ? ServerState.Lobby : ServerState.Offline);
        }

        /// <summary>対局中のメッセージ(手・再戦)をサーバーへ送る。部屋にいないとき・つなぎ直している間は送らない。</summary>
        public void SendGameMessage(string message)
        {
            if (State == ServerState.InRoom) SendRaw(message);
        }

        /// <summary>勝ちを申請する(相手が戻らない)。サーバーが時刻を確かめ、認めれば Error で結果を知らせる。</summary>
        public void Claim()
        {
            if (State == ServerState.InRoom) SendRaw(LobbyProtocol.Claim());
        }

        // ---------- サーバーから ----------

        void OnOpened()
        {
            _nextPingAt = _now + PingInterval;
            if (_reconnecting || _resumingSaved)
            {
                SendRaw(LobbyProtocol.Resume(_token));
                return;
            }
            SetState(ServerState.Lobby);
            RunAfterConnect();
        }

        void OnSocketMessage(string message)
        {
            if (!LobbyProtocol.IsLobbyMessage(message))
            {
                GameMessage?.Invoke(message);
                return;
            }
            if (!LobbyProtocol.TryParse(message, out var lobby)) return; // 知らない種類は読み飛ばす
            switch (lobby.Kind)
            {
                case LobbyMessageKind.Joined:
                    LocalSeat = lobby.Seat;
                    RoomCode = lobby.Code;
                    OpponentPresent = lobby.OpponentPresent;
                    SetState(ServerState.InRoom);
                    JoinedRoom?.Invoke();
                    break;
                case LobbyMessageKind.Token:
                    _token = lobby.Token;
                    _sessions.Save(_sessionKey, lobby.Token);
                    break;
                case LobbyMessageKind.Resumed:
                    OnResumed(lobby);
                    break;
                case LobbyMessageKind.OpponentJoined:
                    OpponentPresent = true;
                    OpponentJoined?.Invoke();
                    break;
                case LobbyMessageKind.OpponentLeft:
                    ForgetSession();
                    OpponentPresent = false;
                    RoomCode = null;
                    SetState(ServerState.Lobby);
                    OpponentLeft?.Invoke();
                    break;
                case LobbyMessageKind.OpponentAway:
                    OpponentAway?.Invoke(lobby.Seconds);
                    break;
                case LobbyMessageKind.OpponentBack:
                    OpponentBack?.Invoke();
                    break;
                case LobbyMessageKind.Turn:
                    TurnTimer?.Invoke(lobby.Seat, lobby.Seconds);
                    break;
                case LobbyMessageKind.Won:
                case LobbyMessageKind.Lost:
                    EndMatch(EndText(lobby.Kind == LobbyMessageKind.Won, lobby.Reason));
                    break;
                case LobbyMessageKind.Error:
                    OnServerError(lobby.ErrorCode);
                    break;
            }
        }

        void OnResumed(LobbyMessage lobby)
        {
            LocalSeat = lobby.Seat;
            RoomCode = lobby.Code;
            OpponentPresent = lobby.OpponentPresent;
            if (_reconnecting)
            {
                _reconnecting = false;
                _reconnectTries = 0;
                Reconnected?.Invoke();
                return;
            }
            // ページを再読み込みしたあと: 画面には「部屋に入った」として知らせ、画面は対局に戻る
            _resumingSaved = false;
            IsResuming = true;
            SetState(ServerState.InRoom);
            JoinedRoom?.Invoke();
            IsResuming = false;
        }

        void OnServerError(string code)
        {
            if (code == LobbyErrors.TooEarly) return; // まだ申請できない: 画面の残り時間のとおりに待つ
            if (code == LobbyErrors.SessionExpired)
            {
                ForgetSession();
                if (_resumingSaved)
                {
                    // 再読み込み前の対局はもう終わっていた: 何も言わずにロビーへ(押していた操作があれば続ける)
                    _resumingSaved = false;
                    SetState(ServerState.Lobby);
                    RunAfterConnect();
                    return;
                }
                if (_reconnecting)
                {
                    _reconnecting = false;
                    EndMatch("対局は終了しました。（通信が切れている間に、相手が勝ちを申請したか退出しました）");
                    return;
                }
            }
            if (State == ServerState.Matching) SetState(ServerState.Lobby);
            Error?.Invoke(ErrorText(code));
        }

        void OnClosed(string reason)
        {
            // 対局中に切れた: 部屋はサーバーに残っているので、つなぎ直して同じ席に戻る
            if ((State == ServerState.InRoom || _reconnecting) && _token != null)
            {
                if (!_reconnecting)
                {
                    _reconnecting = true;
                    _reconnectTries = 0;
                    _reconnectDeadline = _now + ReconnectGiveUp;
                    ConnectionLost?.Invoke();
                }
                _reconnectAt = _now + Math.Min(MaxReconnectDelay, Math.Pow(2, _reconnectTries++));
                return;
            }

            bool wasInRoom = State == ServerState.InRoom;
            bool wasConnecting = State == ServerState.Connecting;
            _afterConnect = null;
            _resumingSaved = false;
            RoomCode = null;
            OpponentPresent = false;
            SetState(ServerState.Offline);
            if (wasInRoom) Error?.Invoke("通信が切断されました。");
            else if (wasConnecting) Error?.Invoke("サーバーに接続できませんでした。");
        }

        void GiveUpReconnecting()
        {
            _reconnecting = false;
            ForgetSession();
            RoomCode = null;
            OpponentPresent = false;
            SetState(ServerState.Offline);
            Error?.Invoke("通信が切断されました。");
        }

        // ---------- 内部 ----------

        /// <summary>サーバーにつながっていなければつなぎ、つながってから action を実行する。</summary>
        void InLobby(Action action)
        {
            if (_socket.State == SocketState.Open && !_resumingSaved)
            {
                action();
                return;
            }
            _afterConnect = action;
            Connect();
        }

        void RunAfterConnect()
        {
            var next = _afterConnect;
            _afterConnect = null;
            next?.Invoke();
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

        /// <summary>対局が決着した(勝ち・負け)。部屋を出てロビーへ戻り、画面には理由を知らせる。</summary>
        void EndMatch(string text)
        {
            ForgetSession();
            RoomCode = null;
            OpponentPresent = false;
            SetState(_socket.State == SocketState.Open ? ServerState.Lobby : ServerState.Offline);
            Error?.Invoke(text);
        }

        void ForgetSession()
        {
            _token = null;
            _sessions.Delete(_sessionKey);
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

        static string EndText(bool won, string reason)
        {
            if (reason == LobbyEndReasons.Timeout)
                return won ? "相手が時間切れになったため、あなたの勝ちです。" : "時間切れのため、あなたの負けです。";
            return won ? "相手が戻らなかったため、あなたの勝ちです。" : "通信が切れたまま戻れなかったため、あなたの負けです。";
        }

        static string ErrorText(string code)
        {
            switch (code)
            {
                case LobbyErrors.RoomNotFound: return "ルームが見つかりません。コードを確かめてください。";
                case LobbyErrors.RoomFull: return "そのルームは満員です。";
                case LobbyErrors.ServerBusy: return "サーバーが混み合っています。少し待ってからもう一度お試しください。";
                case LobbyErrors.SessionExpired: return "対局は終了しました。";
                default: return "サーバーとの通信でエラーが起きました。";
            }
        }
    }
}
