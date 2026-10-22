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
        /// <summary>通信が切れたあと、合言葉(Token)で同じ席に戻る。</summary>
        Resume,
        /// <summary>勝ちを申請する(相手が戻らない・相手の手番の時間切れ)。サーバーが時刻を確かめる。</summary>
        Claim,
        /// <summary>ログインしている人の ID トークン(Cognito の JWT。IdToken)。部屋に入る前に送り、Authed を待つ。</summary>
        Auth,

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
        /// <summary>この席の合言葉(Token)。通信が切れたときに Resume で使う。</summary>
        Token,
        /// <summary>Resume で同じ席に戻れた(席・ルームコード・相手がいるか)。このあと今の局面が届く。</summary>
        Resumed,
        /// <summary>相手の通信が切れた。Seconds 秒は戻るのを待つ(過ぎたら Claim できる)。</summary>
        OpponentAway,
        /// <summary>通信が切れていた相手が戻った。</summary>
        OpponentBack,
        /// <summary>勝ちが決まった(Reason: abandon = 相手が戻らなかった、timeout = 相手の時間切れ)。</summary>
        Won,
        /// <summary>負けが決まった(Reason は Won と同じ)。</summary>
        Lost,
        /// <summary>手番の残り時間(Seat の手番で、あと Seconds 秒)。Seat が -1 なら時間の計測なし(終局後など)。</summary>
        Turn,
        /// <summary>Auth が受け付けられた(Name: 表示名。まだ決めていなければ null)。</summary>
        Authed,
        /// <summary>両方の席の名前とレート(Names / Ratings。ログインしていない席は名前 null・レート -1)。対局の始まりに届く。</summary>
        Players,
        /// <summary>レートが動いた(Rating: 新しいレート、Delta: 増減)。終局のあと、レートのある対局でだけ届く。</summary>
        Rated,
    }

    /// <summary>ロビーのメッセージを読み取った結果。</summary>
    public readonly struct LobbyMessage
    {
        public LobbyMessageKind Kind { get; }
        /// <summary>ゲームの ID(Quick / Create / Join)。</summary>
        public string Game { get; }
        /// <summary>ルームコード(Join、Joined、Resumed。無ければ null)。</summary>
        public string Code { get; }
        /// <summary>割り当てられた席(Joined、Resumed。0 か 1)。</summary>
        public int Seat { get; }
        /// <summary>入った時点で相手がいるか(Joined、Resumed)。</summary>
        public bool OpponentPresent { get; }
        /// <summary>エラーの理由(Error)。LobbyErrors の値。</summary>
        public string ErrorCode { get; }
        /// <summary>合言葉(Resume、Token)。</summary>
        public string Token { get; }
        /// <summary>待つ秒数(OpponentAway)。</summary>
        public int Seconds { get; }
        /// <summary>勝ち負けの理由(Won、Lost)。LobbyEndReasons の値。</summary>
        public string Reason { get; }
        /// <summary>ID トークン(Auth)。</summary>
        public string IdToken { get; }
        /// <summary>自分の表示名(Authed。決めていなければ null)。</summary>
        public string Name { get; }
        /// <summary>席 0 / 1 の表示名(Players。ログインしていない・決めていなければ null)。</summary>
        public string[] Names { get; }
        /// <summary>席 0 / 1 のレート(Players。ログインしていなければ -1)。</summary>
        public int[] Ratings { get; }
        /// <summary>新しいレート(Rated)。</summary>
        public int Rating { get; }
        /// <summary>レートの増減(Rated)。</summary>
        public int Delta { get; }

        public LobbyMessage(LobbyMessageKind kind, string game = null, string code = null, int seat = 0, bool opponentPresent = false,
            string errorCode = null, string token = null, int seconds = 0, string reason = null, string idToken = null,
            string name = null, string[] names = null, int[] ratings = null, int rating = 0, int delta = 0)
        {
            IdToken = idToken;
            Name = name;
            Names = names;
            Ratings = ratings;
            Rating = rating;
            Delta = delta;
            Kind = kind;
            Game = game;
            Code = code;
            Seat = seat;
            OpponentPresent = opponentPresent;
            ErrorCode = errorCode;
            Token = token;
            Seconds = seconds;
            Reason = reason;
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
        /// <summary>Resume した対局がもう無い(終わった・相手が抜けた・合言葉が違う)。</summary>
        public const string SessionExpired = "session_expired";
        /// <summary>Claim したが、まだ勝ちを申請できる時刻ではない。</summary>
        public const string TooEarly = "too_early";
        /// <summary>Auth の ID トークンを確かめられなかった(期限切れ・別のサイトのもの)。ログインしていない扱いで続けられる。</summary>
        public const string AuthFailed = "auth_failed";
    }

    /// <summary>Won / Lost の理由。</summary>
    public static class LobbyEndReasons
    {
        /// <summary>通信が切れた側が、待ち時間のうちに戻らなかった。</summary>
        public const string Abandon = "abandon";
        /// <summary>手番の側が、制限時間のうちに手を打たなかった。</summary>
        public const string Timeout = "timeout";

        public static bool IsValid(string reason) => reason == Abandon || reason == Timeout;
    }

    /// <summary>
    /// オンライン対戦サーバーとのロビーのメッセージ(マッチング・部屋の出入り・通信が切れたとき)。形式は "@種類|値|値…"。
    /// 対局中のメッセージ(局面・手・再戦)は各ゲームの NetProtocol("S|1|…" など)をそのまま使い、
    /// ロビーのメッセージとは先頭の '@' で見分ける。
    ///   ブラウザ → サーバー: @quick|game  @create|game  @join|game|code  @leave  @ping  @resume|token  @claim
    ///   サーバー → ブラウザ: @joined|seat|code(無ければ -)|opp(0/1)  @opp  @left  @error|reason  @pong
    ///                        @token|token  @resumed|seat|code|opp  @away|seconds  @back  @won|reason  @lost|reason
    ///                        @turn|seat(終局後などは -)|seconds
    ///                        @authed|name  @players|name0|rating0|name1|rating1  @rated|rating|delta
    ///                        (name は URL エンコード、無ければ空。rating はログインしていなければ -)
    ///   ログインしているとき: ブラウザ → サーバー @auth|IDトークン(部屋に入る前に送り、@authed か @error|auth_failed を待つ)
    /// 知らない種類のメッセージは読み飛ばす(古いブラウザ側が新しいサーバーにつないでも壊れないように)。
    /// Unity とサーバー(AWS Lambda)の両方がこのファイルを使う。
    /// </summary>
    public static class LobbyProtocol
    {
        public const char Prefix = '@';
        public const int RoomCodeLength = 4;
        /// <summary>合言葉の長さ(16 進数 32 文字 = 128 ビット)。</summary>
        public const int TokenLength = 32;
        const int MaxGameIdLength = 20;
        /// <summary>メッセージの長さの上限(ID トークンが 1〜2 KB あるため)。サーバーもこれより長いものは読まない。</summary>
        public const int MaxMessageLength = 4096;
        /// <summary>表示名の長さの上限(文字数)。</summary>
        public const int MaxNameLength = 12;

        public static bool IsLobbyMessage(string message) => !string.IsNullOrEmpty(message) && message[0] == Prefix;

        // ---------- 作る ----------

        public static string Quick(string game) => "@quick|" + RequireGame(game);
        public static string Create(string game) => "@create|" + RequireGame(game);
        public static string Join(string game, string code) => "@join|" + RequireGame(game) + "|" + RequireCode(code);
        public static string Leave() => "@leave";
        public static string Ping() => "@ping";
        public static string Resume(string token) => "@resume|" + RequireToken(token);
        public static string Claim() => "@claim";
        public static string Auth(string idToken) =>
            "@auth|" + (IsIdToken(idToken) ? idToken : throw new ArgumentException("not a JWT", nameof(idToken)));

        public static string Joined(int seat, string code, bool opponentPresent) => SeatMessage("@joined|", seat, code, opponentPresent);
        public static string Resumed(int seat, string code, bool opponentPresent) => SeatMessage("@resumed|", seat, code, opponentPresent);
        public static string OpponentJoined() => "@opp";
        public static string OpponentLeft() => "@left";
        public static string Error(string reason) => "@error|" + reason;
        public static string Pong() => "@pong";
        public static string Token(string token) => "@token|" + RequireToken(token);
        public static string OpponentAway(int seconds) => "@away|" + Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture);
        public static string OpponentBack() => "@back";
        public static string Won(string reason) => "@won|" + RequireReason(reason);
        public static string Lost(string reason) => "@lost|" + RequireReason(reason);

        public static string Turn(int seat, int seconds)
        {
            if (seat != 0 && seat != 1) throw new ArgumentOutOfRangeException(nameof(seat));
            return "@turn|" + seat.ToString(CultureInfo.InvariantCulture) + "|" + Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>時間の計測なし(終局後など)。</summary>
        public static string TurnOff() => "@turn|-|0";

        public static string Authed(string name) => "@authed|" + EncodeName(name);

        /// <summary>両方の席の名前とレート。rating が負ならログインしていない席。</summary>
        public static string Players(string name0, int rating0, string name1, int rating1) =>
            "@players|" + EncodeName(name0) + "|" + EncodeRating(rating0) + "|" + EncodeName(name1) + "|" + EncodeRating(rating1);

        public static string Rated(int rating, int delta) =>
            "@rated|" + Math.Max(0, rating).ToString(CultureInfo.InvariantCulture) + "|" + delta.ToString(CultureInfo.InvariantCulture);

        // ---------- 読む ----------

        public static bool TryParse(string message, out LobbyMessage result)
        {
            result = default;
            if (!IsLobbyMessage(message) || message.Length > MaxMessageLength) return false;
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
                case "resume" when p.Length == 2 && IsToken(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Resume, token: p[1]);
                    return true;
                case "claim" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.Claim);
                    return true;
                case "joined" when TryParseSeat(p, out int seat, out string code, out bool opp):
                    result = new LobbyMessage(LobbyMessageKind.Joined, code: code, seat: seat, opponentPresent: opp);
                    return true;
                case "resumed" when TryParseSeat(p, out int seat, out string code, out bool opp):
                    result = new LobbyMessage(LobbyMessageKind.Resumed, code: code, seat: seat, opponentPresent: opp);
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
                case "token" when p.Length == 2 && IsToken(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Token, token: p[1]);
                    return true;
                case "away" when p.Length == 2 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds):
                    result = new LobbyMessage(LobbyMessageKind.OpponentAway, seconds: seconds);
                    return true;
                case "back" when p.Length == 1:
                    result = new LobbyMessage(LobbyMessageKind.OpponentBack);
                    return true;
                case "won" when p.Length == 2 && LobbyEndReasons.IsValid(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Won, reason: p[1]);
                    return true;
                case "lost" when p.Length == 2 && LobbyEndReasons.IsValid(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Lost, reason: p[1]);
                    return true;
                case "turn" when p.Length == 3 && (p[1] == "0" || p[1] == "1" || p[1] == "-") &&
                                 int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out int turnSeconds):
                    result = new LobbyMessage(LobbyMessageKind.Turn, seat: p[1] == "-" ? -1 : p[1] == "1" ? 1 : 0, seconds: turnSeconds);
                    return true;
                case "auth" when p.Length == 2 && IsIdToken(p[1]):
                    result = new LobbyMessage(LobbyMessageKind.Auth, idToken: p[1]);
                    return true;
                case "authed" when p.Length == 2 && TryDecodeName(p[1], out string authedName):
                    result = new LobbyMessage(LobbyMessageKind.Authed, name: authedName);
                    return true;
                case "players" when p.Length == 5 && TryDecodeName(p[1], out string n0) && TryParseRating(p[2], out int r0) &&
                                    TryDecodeName(p[3], out string n1) && TryParseRating(p[4], out int r1):
                    result = new LobbyMessage(LobbyMessageKind.Players, names: new[] { n0, n1 }, ratings: new[] { r0, r1 });
                    return true;
                case "rated" when p.Length == 3 && int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int rating) &&
                                  int.TryParse(p[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int delta):
                    result = new LobbyMessage(LobbyMessageKind.Rated, rating: rating, delta: delta);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>JWT の形(base64url の 3 つの部分を '.' でつないだもの)か。署名は確かめない(サーバーが確かめる)。</summary>
        public static bool IsIdToken(string s)
        {
            if (s == null || s.Length < 20 || s.Length > MaxMessageLength - 16) return false;
            int dots = 0;
            foreach (char c in s)
            {
                if (c == '.') dots++;
                else if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return false;
            }
            return dots == 2;
        }

        static string EncodeName(string name) => string.IsNullOrEmpty(name) ? "" : Uri.EscapeDataString(name);

        static bool TryDecodeName(string s, out string name)
        {
            name = null;
            if (s.Length == 0) return true;
            try { name = Uri.UnescapeDataString(s); }
            catch (UriFormatException) { return false; }
            return name.Length > 0 && name.Length <= MaxNameLength * 2; // 絵文字は 2 文字に数えられるので余裕を持たせる
        }

        static string EncodeRating(int rating) => rating < 0 ? "-" : rating.ToString(CultureInfo.InvariantCulture);

        static bool TryParseRating(string s, out int rating)
        {
            rating = -1;
            return s == "-" || int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out rating);
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

        /// <summary>合言葉(16 進数の小文字 32 文字)か。</summary>
        public static bool IsToken(string s)
        {
            if (s == null || s.Length != TokenLength) return false;
            foreach (char c in s)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))
                    return false;
            return true;
        }

        static string SeatMessage(string head, int seat, string code, bool opponentPresent)
        {
            if (seat != 0 && seat != 1) throw new ArgumentOutOfRangeException(nameof(seat));
            return head + seat.ToString(CultureInfo.InvariantCulture) + "|" + (code == null ? "-" : RequireCode(code)) + "|" +
                   (opponentPresent ? "1" : "0");
        }

        static bool TryParseSeat(string[] p, out int seat, out string code, out bool opponentPresent)
        {
            seat = 0;
            code = null;
            opponentPresent = false;
            if (p.Length != 4 || (p[1] != "0" && p[1] != "1") || (p[2] != "-" && !IsRoomCode(p[2])) || (p[3] != "0" && p[3] != "1"))
                return false;
            seat = p[1] == "1" ? 1 : 0;
            code = p[2] == "-" ? null : p[2];
            opponentPresent = p[3] == "1";
            return true;
        }

        static string RequireGame(string game) =>
            IsGameId(game) ? game : throw new ArgumentException("game id must be 1-20 lowercase letters", nameof(game));

        static string RequireCode(string code) =>
            IsRoomCode(code) ? code : throw new ArgumentException("room code must be 4 digits", nameof(code));

        static string RequireToken(string token) =>
            IsToken(token) ? token : throw new ArgumentException("token must be 32 lowercase hex characters", nameof(token));

        static string RequireReason(string reason) =>
            LobbyEndReasons.IsValid(reason) ? reason : throw new ArgumentException("unknown end reason", nameof(reason));
    }
}
