// ブラウザの WebSocket を Unity(C#)から使うための部品。WebGL のビルドでだけ使われる。
// C# 側(WebSocketConnection.cs)は毎フレーム BoardPiaWs_Next を呼んで、届いた出来事を 1 つずつ受け取る
// (JavaScript から C# を直接呼ばないので、つなぎ方が単純で壊れにくい)。
//   出来事の形: "o"(つながった)、"m" + 本文(メッセージ)、"c" + 理由(切れた)
var BoardPiaWebSocketLib = {
  $BoardPiaWs: {
    next: 1,
    sockets: {},
  },

  BoardPiaWs_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = BoardPiaWs.next++;
    var entry = { socket: null, events: [] };
    BoardPiaWs.sockets[id] = entry;
    try {
      var socket = new WebSocket(url);
      socket.binaryType = "arraybuffer";
      socket.onopen = function () { entry.events.push("o"); };
      socket.onmessage = function (e) {
        var text = typeof e.data === "string" ? e.data : new TextDecoder("utf-8").decode(new Uint8Array(e.data));
        entry.events.push("m" + text);
      };
      socket.onclose = function (e) { entry.events.push("c" + e.code + (e.reason ? " " + e.reason : "")); };
      socket.onerror = function () { /* onclose が続けて呼ばれるので、ここでは何もしない */ };
      entry.socket = socket;
    } catch (err) {
      entry.events.push("c" + String(err));
    }
    return id;
  },

  BoardPiaWs_Send: function (id, textPtr) {
    var entry = BoardPiaWs.sockets[id];
    if (!entry || !entry.socket || entry.socket.readyState !== 1) return 0;
    entry.socket.send(UTF8ToString(textPtr));
    return 1;
  },

  BoardPiaWs_Close: function (id) {
    var entry = BoardPiaWs.sockets[id];
    if (!entry) return;
    if (entry.socket && entry.socket.readyState <= 1) entry.socket.close(1000);
  },

  BoardPiaWs_Release: function (id) {
    var entry = BoardPiaWs.sockets[id];
    if (!entry) return;
    if (entry.socket) {
      entry.socket.onopen = entry.socket.onmessage = entry.socket.onclose = entry.socket.onerror = null;
      if (entry.socket.readyState <= 1) entry.socket.close(1000);
    }
    delete BoardPiaWs.sockets[id];
  },

  // 次の出来事を返す(無ければ null)。返した文字列のメモリは Unity が解放する。
  BoardPiaWs_Next: function (id) {
    var entry = BoardPiaWs.sockets[id];
    if (!entry || entry.events.length === 0) return 0;
    var text = entry.events.shift();
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },
};

autoAddDeps(BoardPiaWebSocketLib, "$BoardPiaWs");
mergeInto(LibraryManager.library, BoardPiaWebSocketLib);
