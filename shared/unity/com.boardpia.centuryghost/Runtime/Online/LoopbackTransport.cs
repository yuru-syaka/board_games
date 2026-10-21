using System;
using System.Collections.Generic;

namespace BoardGames.CenturyGhost.Online
{
    /// <summary>
    /// 同じプロセス内で 2 つの端点をつなぐ通信(テスト用)。送信はキューに積まれ、Pump で配送される。
    /// 受信処理の中から送信しても再入しない。
    /// </summary>
    public sealed class LoopbackTransport : IMatchTransport
    {
        readonly Queue<(LoopbackTransport to, string message)> _queue;
        LoopbackTransport _peer;

        public event Action<string> MessageReceived;

        /// <summary>送信されたメッセージの記録(テストでの盗聴チェック用)。</summary>
        public List<string> SentLog { get; } = new List<string>();

        LoopbackTransport(Queue<(LoopbackTransport, string)> queue)
        {
            _queue = queue;
        }

        public static void CreatePair(out LoopbackTransport a, out LoopbackTransport b)
        {
            var queue = new Queue<(LoopbackTransport, string)>();
            a = new LoopbackTransport(queue);
            b = new LoopbackTransport(queue);
            a._peer = b;
            b._peer = a;
        }

        public void Send(string message)
        {
            SentLog.Add(message);
            _queue.Enqueue((_peer, message));
        }

        /// <summary>キューが空になるまで配送する。</summary>
        public void Pump()
        {
            while (_queue.Count > 0)
            {
                var (to, message) = _queue.Dequeue();
                to.MessageReceived?.Invoke(message);
            }
        }
    }
}
