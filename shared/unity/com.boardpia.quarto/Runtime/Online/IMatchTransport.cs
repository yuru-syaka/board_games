using System;

namespace BoardGames.Quarto.Online
{
    /// <summary>
    /// 対戦相手との文字列メッセージのやり取りだけを表す。Photon などの具体的な通信はこれを実装する。
    /// 送ったメッセージは相手にだけ、順序どおり確実に届くことを前提とする。
    /// </summary>
    public interface IMatchTransport
    {
        void Send(string message);
        event Action<string> MessageReceived;
    }
}
