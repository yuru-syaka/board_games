// ログイン中の ID トークンを、Unity(C#)に渡すための部品(ステップ 5-3)。WebGL のビルドでだけ使われる。
// トークンはサイトの js/auth.js(window.BoardPiaAuth)が持っていて、期限が近ければ更新してから返す(非同期)。
// C# 側(IdTokenSource.cs)は Request で頼み、毎フレーム Poll で受け取る(WebSocket の部品と同じ、C# を直接呼ばない形)。
//   Poll の返り値: null = まだ、"" = ログインしていない(または取れなかった)、それ以外 = ID トークン
var BoardPiaAuthLib = {
  $BoardPiaAuthState: {
    done: false,
    token: "",
  },

  BoardPiaAuth_Request: function () {
    var state = BoardPiaAuthState;
    state.done = false;
    state.token = "";
    var auth = window.BoardPiaAuth;
    if (!auth || !auth.isConfigured() || !auth.isSignedIn()) {
      state.done = true;
      return;
    }
    auth.getIdToken().then(
      function (token) { state.token = token || ""; state.done = true; },
      function () { state.done = true; });
  },

  BoardPiaAuth_Poll: function () {
    var state = BoardPiaAuthState;
    if (!state.done) return 0;
    state.done = false;
    var text = state.token;
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },
};

autoAddDeps(BoardPiaAuthLib, "$BoardPiaAuthState");
mergeInto(LibraryManager.library, BoardPiaAuthLib);
