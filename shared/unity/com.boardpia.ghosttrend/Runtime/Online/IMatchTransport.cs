using System;

namespace BoardGames.GhostTrend.Online
{
    /// <summary>
    /// サーバーとの文字列メッセージのやり取りだけを表す。WebSocket などの具体的な通信はこれを実装する。
    /// 送ったメッセージは順序どおり確実に届くことを前提とする。
    /// </summary>
    public interface IMatchTransport
    {
        void Send(string message);
        event Action<string> MessageReceived;
    }
}
