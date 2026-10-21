// 対局中の合言葉を、ブラウザのタブの中(sessionStorage)に覚えるための部品。WebGL のビルドでだけ使われる。
// sessionStorage が使えないとき(プライベートモードの制限など)は、何もしない(再読み込みで戻れないだけ)。
mergeInto(LibraryManager.library, {
  BoardPiaSession_Get: function (keyPtr) {
    var value = null;
    try { value = window.sessionStorage.getItem(UTF8ToString(keyPtr)); } catch (e) { value = null; }
    if (value === null) return 0;
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  BoardPiaSession_Set: function (keyPtr, valuePtr) {
    try { window.sessionStorage.setItem(UTF8ToString(keyPtr), UTF8ToString(valuePtr)); } catch (e) { }
  },

  BoardPiaSession_Remove: function (keyPtr) {
    try { window.sessionStorage.removeItem(UTF8ToString(keyPtr)); } catch (e) { }
  },
});
