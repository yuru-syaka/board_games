#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BoardGames.Net
{
    /// <summary>
    /// ログインしている人の ID トークンを受け取る場所(ステップ 5-3)。受け取りは非同期(期限が近ければブラウザが更新する)なので、
    /// Request で頼み、TryTake で受け取れたかを毎フレーム確かめる。
    /// </summary>
    public interface IIdTokenSource
    {
        /// <summary>ID トークンを頼む(前に頼んだものは捨てる)。</summary>
        void Request();

        /// <summary>
        /// 頼んだ結果が出ていれば true(token はログインしていなければ null)。まだなら false。
        /// 1 回受け取ったら、次に Request するまで false。
        /// </summary>
        bool TryTake(out string token);
    }

    /// <summary>
    /// ブラウザのサイト(js/auth.js)から受け取る(BoardPiaAuth.jslib)。
    /// エディターではログインできないので、いつも「ログインしていない」。
    /// </summary>
    public sealed class BrowserIdTokenSource : IIdTokenSource
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void BoardPiaAuth_Request();
        [DllImport("__Internal")] static extern string BoardPiaAuth_Poll();

        public void Request() => BoardPiaAuth_Request();

        public bool TryTake(out string token)
        {
            token = BoardPiaAuth_Poll();
            if (token == null) return false;
            if (token.Length == 0) token = null;
            return true;
        }
#else
        bool _requested;

        public void Request() => _requested = true;

        public bool TryTake(out string token)
        {
            token = null;
            if (!_requested) return false;
            _requested = false;
            return true;
        }
#endif
    }
}
