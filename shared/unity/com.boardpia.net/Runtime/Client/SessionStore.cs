using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BoardGames.Net
{
    /// <summary>
    /// 対局中の合言葉を覚えておく場所。ページを再読み込みしても、同じタブなら同じ席に戻れるようにする。
    /// </summary>
    public interface ISessionStore
    {
        string Load(string key);
        void Save(string key, string value);
        void Delete(string key);
    }

    /// <summary>
    /// ブラウザのタブの中(sessionStorage)に覚える。タブを閉じると消え、ほかのタブとは分かれている。
    /// エディターではメモリに覚える(エディターを止めると消える)。
    /// </summary>
    public sealed class BrowserSessionStore : ISessionStore
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string BoardPiaSession_Get(string key);
        [DllImport("__Internal")] static extern void BoardPiaSession_Set(string key, string value);
        [DllImport("__Internal")] static extern void BoardPiaSession_Remove(string key);

        public string Load(string key) => BoardPiaSession_Get(key);
        public void Save(string key, string value) => BoardPiaSession_Set(key, value);
        public void Delete(string key) => BoardPiaSession_Remove(key);
#else
        static readonly Dictionary<string, string> Memory = new Dictionary<string, string>();

        public string Load(string key) => Memory.TryGetValue(key, out var value) ? value : null;
        public void Save(string key, string value) => Memory[key] = value;
        public void Delete(string key) => Memory.Remove(key);
#endif
    }

    /// <summary>テスト用: メモリに覚えるだけ。</summary>
    public sealed class MemorySessionStore : ISessionStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string Load(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void Save(string key, string value) => _values[key] = value;
        public void Delete(string key) => _values.Remove(key);
    }
}
