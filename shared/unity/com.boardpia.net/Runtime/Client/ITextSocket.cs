using System;

namespace BoardGames.Net
{
    /// <summary>
    /// テキストだけを送受信する接続。本物は WebSocketConnection、テストでは偽物に差し替える。
    /// 届いた出来事は Update() を呼んだときに通知する(呼んだスレッドで通知される)。
    /// </summary>
    public interface ITextSocket : IDisposable
    {
        SocketState State { get; }

        event Action Opened;
        event Action<string> MessageReceived;
        /// <summary>切れた(理由。自分で閉じたときも呼ばれる)。</summary>
        event Action<string> Closed;

        void Connect(string url);
        bool Send(string message);
        void Close();
        void Update();
    }
}
