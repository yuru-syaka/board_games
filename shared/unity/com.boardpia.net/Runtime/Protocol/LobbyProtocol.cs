using System;
using System.Globalization;

namespace BoardGames.Net
{
    public enum LobbyMessageKind
    {
        Invalid,

        // ---------- ブラウザ → サーバー ----------
        /// <summary>ランダムマッチ。空いている相手がいなければ待ち行列に入る。</summary>
        Quick,
        /// <summary>ルームコードの部屋を作る。</summary>
        Create,
        /// <summary>ルームコードで部屋に入る。</summary>
        Join,
        /// <summary>部屋・待ち行列から抜ける。</summary>
        Leave,
        /// <summary>接続を保つための合図。</summary>
        Ping,

        // ---------- サーバー → ブラウザ ----------
        /// <summary>部屋に入った(席・ルームコード・相手がいるか)。</summary>
        Joined,
        /// <summary>相手が入ってきた(このあとサーバーが対局を始める)。</summary>
        OpponentJoined,
        /// <summary>相手が抜けた。</summary>
        OpponentLeft,
        /// <summary>要求が受け付けられなかった(理由は ErrorCode)。</summary>
        Error,
        /// <summary>Ping への返事。</summary>
        Pong,
    }

    /// <summary>ロビーのメッセージを読み取った結果。</summary>
    public readonly struct LobbyMessage
    {
        public LobbyMessageKind Kind { get; }
        /// <summary>ゲームの ID(Quick / Create / Join)。</summary>
        public string Game { get; }
        /// <summary>ルームコード(Join、Joined。無ければ null)。</summary>
        public string Code { get; }
        /// <summary>割り当てられた席(Joined。0 か 1)。</summary>
        public int Seat { get; }
        /// <summary>入った時点で相手がいるか(Joined)。</summary>
        public bool OpponentPresent { get; }
        /// <summary>エラーの理由(Error)。LobbyErrors の値。</summary>
        public string ErrorCode { get; }

        public LobbyMessage(LobbyMessageKind kind, string game = null, string code = null, int seat = 0, bool opponentPresent = false,
            string errorCode = null)
        {
            Kind = kind;
            Game = game;
            Code = code;
            Seat = seat;
            OpponentPresent = opponentPresent;
            ErrorCode = errorCode;
        }
    }

    /// <summary>Error の理由。</summary>
    public static class LobbyErrors
    {
        public const string RoomNotFound = "room_not_found";
        public const string RoomFull = "room_full";
        public const string UnknownGame = "unknown_game";
        public const string BadRequest = "bad_request";
        public const string ServerBusy = "server_busy";
    }

    /// <summary>
    /// オンライン対戦サーバーとのロビーのメッセージ(マッチング・部屋の出入り)。形式は "@種類|値|値…"。
    /// 対局中のメッセージ(局面・手・再戦)は各ゲームの NetProtocol("S|1|…" など)をそのまま使い、
    /// ロビーのメッセージとは先頭の '@' で見分ける。
    ///   ブラウザ → サーバー: @quick|game  @create|game  @join|game|code  @leave  @ping
    ///   サーバー → ブラウザ: @joined|seat|code(無ければ -)|opp(0/1)  @opp  @left  @error|reason  @pong
    /// Unity とサーバー(AWS Lambda)の両方がこのファイルを使う。
    /// </summary>
    public static class LobbyProtocol
    {
        public const char Prefix = '@';
        public const int RoomCodeLength = 4;
        const int MaxGameIdLength = 20;

        public static bool IsLobbyMessage(string message) => !string.IsNullOrEmpty(message) && message[0] == Prefix;

        // ---------- 作る ----------

        public static string Quick(string game) => "@quick|" + RequireGame(game);
        public static string Create(string game) => "@create|" + RequireGame(game);
        public static string Join(string game, string code) => "@join|" + RequireGame(game) + "|" + RequireCode(code);
        public static string Leave() => "@leave";
        public static string Ping() => "@ping";

        public static string Joined(int seat, string code, bool opponentPresent)
        {
            if (seat != 0 && seat != 1) throw new ArgumentOutOfRangeException(nameof(seat));
            return "@joined|" + seat.ToString(CultureInfo.InvariantCulture) + "|" + (code == null ? "-" : RequireCode(code)) + "|" +
                   (opponentPresent ? "1" : "0");
        }

        public static string OpponentJoined() => "@opp";
        public static string OpponentLeft() => "@left";
        public static string Error(string reason) => "@error|" + reason;
        public static string Pong() => "@pong";

        // ---------- 読む ----------

        public static bool TryParse(string message, out LobbyMessage result)
        {
            result = default;
            if (!IsLobbyMessage(message) || message.Length > 64) return false;
            var p = message.Substring(1).Split('|');
            switch (p[0])
            {
                case "quick" when p.Length == 2 && IsGameId(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Quick, game: p[1]);
                    return true;
                case "create" when p.Length == 2 && IsGameId(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Create, game: p[1]);
                    return true;
                case "join" when p.Length == 3 && IsGameId(p[1]) && IsRoomCode(p[2]):
                    result = new LobbyMessage(LobbyMessageKind.Join, game: p[1], code: p[2]);
                    return true;
                case "leave" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.Leave);
                    return true;
                case "ping" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.Ping);
                    return true;
                case "joined" when p.Length == 4 && (p[1] == "0" || p[1] == "1") && (p[2] == "-" || IsRoomCode(p[2])) &&
                                   (p[3] == "0" || p[3] == "1"):
                    result = new LobbyMessage(LobbyMessageKind.Joined, code: p[2] == "-" ? null : p[2], seat: p[1] == "1" ? 1 : 0,
                        opponentPresent: p[3] == "1");
                    return true;
                case "opp" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.OpponentJoined);
                    return true;
                case "left" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.OpponentLeft);
                    return true;
                case "error" when p.Length == 2 && p[1].Length > 0:
                    result = new LobbyMessage(LobbyMessageKind.Error, errorCode: p[1]);
                    return true;
                case "pong" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.Pong);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>ゲームの ID(英小文字、1〜20 文字)か。</summary>
        public static bool IsGameId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > MaxGameIdLength) return false;
            foreach (char c in s)
                if (c < 'a' || c > 'z')
                    return false;
            return true;
        }

        /// <summary>ルームコード(数字 4 桁)か。</summary>
        public static bool IsRoomCode(string s)
        {
            if (s == null || s.Length != RoomCodeLength) return false;
            foreach (char c in s)
                if (c < '0' || c > '9')
                    return false;
            return true;
        }

        static string RequireGame(string game) =>
            IsGameId(game) ? game : throw new ArgumentException("game id must be 1-20 lowercase letters", nameof(game));

        static string RequireCode(string code) =>
            IsRoomCode(code) ? code : throw new ArgumentException("room code must be 4 digits", nameof(code));
    }
}
