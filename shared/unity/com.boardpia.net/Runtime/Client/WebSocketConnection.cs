using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace BoardGames.Net
{
    public enum SocketState
    {
        Closed,
        Connecting,
        Open,
    }

    /// <summary>
    /// テキストだけを送受信する WebSocket。
    /// - WebGL のビルドでは、ブラウザの WebSocket を使う(BoardPiaWebSocket.jslib)。
    /// - エディターでは .NET の ClientWebSocket を使う(エディターでも本物のサーバーにつないで確かめられる)。
    /// 届いた出来事は、持ち主が毎フレーム Update() を呼んだときに、メインスレッドでイベントとして通知する。
    /// </summary>
    public sealed class WebSocketConnection : IDisposable
    {
        public SocketState State { get; private set; } = SocketState.Closed;

        public event Action Opened;
        public event Action<string> MessageReceived;
        /// <summary>切れた(理由。自分で閉じたときも呼ばれる)。</summary>
        public event Action<string> Closed;

        public void Connect(string url)
        {
            if (State != SocketState.Closed) return;
            State = SocketState.Connecting;
            ConnectNative(url);
        }

        public bool Send(string message) => State == SocketState.Open && SendNative(message);

        public void Close() => CloseNative();

        public void Dispose() => ReleaseNative();

        /// <summary>届いた出来事を通知する。持ち主の Update から毎フレーム呼ぶ。</summary>
        public void Update()
        {
            while (TryNextEvent(out var ev))
            {
                switch (ev[0])
                {
                    case 'o':
                        State = SocketState.Open;
                        Opened?.Invoke();
                        break;
                    case 'm':
                        MessageReceived?.Invoke(ev.Substring(1));
                        break;
                    case 'c':
                        if (State == SocketState.Closed) break;
                        State = SocketState.Closed;
                        Closed?.Invoke(ev.Substring(1));
                        break;
                }
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // ---------- ブラウザ(jslib) ----------

        [DllImport("__Internal")] static extern int BoardPiaWs_Connect(string url);
        [DllImport("__Internal")] static extern int BoardPiaWs_Send(int id, string text);
        [DllImport("__Internal")] static extern void BoardPiaWs_Close(int id);
        [DllImport("__Internal")] static extern void BoardPiaWs_Release(int id);
        [DllImport("__Internal")] static extern string BoardPiaWs_Next(int id);

        int _id;

        void ConnectNative(string url)
        {
            if (_id != 0) BoardPiaWs_Release(_id);
            _id = BoardPiaWs_Connect(url);
        }

        bool SendNative(string message) => _id != 0 && BoardPiaWs_Send(_id, message) == 1;

        void CloseNative()
        {
            if (_id != 0) BoardPiaWs_Close(_id);
        }

        void ReleaseNative()
        {
            if (_id != 0) BoardPiaWs_Release(_id);
            _id = 0;
            State = SocketState.Closed;
        }

        bool TryNextEvent(out string ev)
        {
            ev = _id != 0 ? BoardPiaWs_Next(_id) : null;
            return !string.IsNullOrEmpty(ev);
        }
#else
        // ---------- エディター(.NET の ClientWebSocket) ----------

        // 接続ごとに新しく作る(前の接続の受信ループが、あとから古い出来事を入れないように)
        ConcurrentQueue<string> _events = new ConcurrentQueue<string>();
        ClientWebSocket _socket;
        CancellationTokenSource _cancel;

        void ConnectNative(string url)
        {
            ReleaseSocket();
            _socket = new ClientWebSocket();
            _cancel = new CancellationTokenSource();
            var socket = _socket;
            var token = _cancel.Token;
            var events = _events = new ConcurrentQueue<string>();
            Task.Run(async () =>
            {
                try
                {
                    await socket.ConnectAsync(new Uri(url), token);
                    events.Enqueue("o");
                    var buffer = new byte[8192];
                    var text = new StringBuilder();
                    while (socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            events.Enqueue("c" + (int)(result.CloseStatus ?? WebSocketCloseStatus.Empty));
                            return;
                        }
                        text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (!result.EndOfMessage) continue;
                        events.Enqueue("m" + text);
                        text.Clear();
                    }
                    events.Enqueue("c" + socket.State);
                }
                catch (Exception e)
                {
                    events.Enqueue("c" + e.Message);
                }
            });
        }

        bool SendNative(string message)
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return false;
            var bytes = Encoding.UTF8.GetBytes(message);
            // 送信は 1 つずつ順に(ClientWebSocket は同時に 2 つ送れない)
            lock (socket)
            {
                socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            }
            return true;
        }

        void CloseNative()
        {
            var socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }

        void ReleaseNative()
        {
            ReleaseSocket();
            State = SocketState.Closed;
        }

        void ReleaseSocket()
        {
            _cancel?.Cancel();
            _socket?.Dispose();
            _socket = null;
            _cancel = null;
            _events = new ConcurrentQueue<string>();
        }

        bool TryNextEvent(out string ev) => _events.TryDequeue(out ev);
#endif
    }
}
