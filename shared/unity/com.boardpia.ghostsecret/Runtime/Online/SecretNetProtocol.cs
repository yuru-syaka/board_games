using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostSecret.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>サーバー → 画面: 局面(その席の視点に伏せ済み)。</summary>
        Snapshot,
        /// <summary>画面 → サーバー: 指したい手。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。
    /// Snapshot: S|1|viewer|人数|current|starter|round|phase|pending|ply|status|winner|deck|setAside|faceUp|peek|players|events
    ///   deck     = 山札の枚数
    ///   setAside = 伏せて脇に置いたカード(見えない -1 / 引かれた -2)
    ///   faceUp・peek = カード ID を ','(peek は見ていない人には -1)
    ///   players = 1 人ぶん "hand:played:known:out:protected:tokens" を ';'(中のリストは '/' 区切り。見えない手札は -1)
    ///   events  = "seq,kind,player,target,card,card2,guess,amount,viewers" を ';'
    /// Move:     M|1|kind,card,target,guess,choice
    /// Rematch:  R|1
    /// </summary>
    public static class SecretNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 18;

        public static NetMessageKind KindOf(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length < 3 || message[1] != '|') return NetMessageKind.Invalid;
            var parts = message.Split('|');
            if (parts.Length < 2 || parts[1] != Version.ToString(CultureInfo.InvariantCulture)) return NetMessageKind.Invalid;
            switch (message[0])
            {
                case 'S': return NetMessageKind.Snapshot;
                case 'M': return NetMessageKind.Move;
                case 'R': return NetMessageKind.Rematch;
                default: return NetMessageKind.Invalid;
            }
        }

        // ---------- Snapshot ----------

        public static string EncodeSnapshot(MatchSnapshot snapshot)
        {
            var v = snapshot.View;
            var s = v.Masked;
            var sb = new StringBuilder(1024);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(s.PlayerCount)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.RoundStarter)
                .Append('|').Append(s.Round)
                .Append('|').Append((int)s.Phase)
                .Append('|').Append(s.PendingTarget)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append(s.Deck.Count)
                .Append('|').Append(s.SetAside)
                .Append('|').Append(Join(s.FaceUpAside, ','))
                .Append('|').Append(Join(s.Peek, ','))
                .Append('|').Append(string.Join(";", s.Players.Select(EncodePlayer)))
                .Append('|').Append(string.Join(";", v.Events.Select(EncodeEvent)));
            return sb.ToString();
        }

        static string EncodePlayer(SecretPlayer p) =>
            string.Join(":", Join(p.Hand, '/'), Join(p.Played, '/'), Join(p.Known, '/'), p.Out ? "1" : "0", p.Protected ? "1" : "0",
                p.Tokens.ToString(CultureInfo.InvariantCulture));

        static string EncodeEvent(SecretEvent e) =>
            string.Join(",", e.Seq, (int)e.Kind, e.Player, e.Target, e.Card, e.Card2, e.Guess, e.Amount, e.Viewers);

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                return TryDecodeSnapshotCore(message, out snapshot);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is IndexOutOfRangeException ||
                                      e is OverflowException)
            {
                snapshot = null;
                return false;
            }
        }

        static bool TryDecodeSnapshotCore(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != SnapshotFields) return false;
            if (!Int(p[2], out int viewer) || !Int(p[3], out int n) || !Int(p[4], out int current) || !Int(p[5], out int starter) ||
                !Int(p[6], out int round) || !Int(p[7], out int phase) || !Int(p[8], out int pending) || !Int(p[9], out int ply) ||
                !Int(p[10], out int status) || !Int(p[11], out int winner) || !Int(p[12], out int deck) || !Int(p[13], out int setAside))
                return false;
            if (!SecretLogic.IsValidPlayerCount(n) || !IsPlayer(viewer, n) || !IsPlayer(current, n) || !IsPlayer(starter, n)) return false;
            if (round < 1 || ply < 0 || deck < 0 || deck > SecretLogic.CardCount) return false;
            if (pending != -1 && !IsPlayer(pending, n)) return false;
            if (setAside != -1 && setAside != SecretState.SetAsideTaken) return false;
            if (!Enum.IsDefined(typeof(SecretPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status)) return false;
            if (!Ints(p[14], ',', out var faceUp) || faceUp.Length > SecretLogic.FaceUpAsideForTwo || !faceUp.All(SecretCards.IsCard)) return false;
            if (!Ints(p[15], ',', out var peek) || peek.Length > 2 || peek.Any(c => c != -1 && !SecretCards.IsCard(c))) return false;

            var players = p[16].Split(';');
            if (players.Length != n) return false;
            var state = new SecretState(n)
            {
                CurrentPlayer = current,
                RoundStarter = starter,
                Round = round,
                Phase = (SecretPhase)phase,
                PendingTarget = pending,
                Ply = ply,
                SetAside = setAside,
            };
            for (int i = 0; i < n; i++)
                if (!TryDecodePlayer(players[i], state.Players[i], n, i == viewer)) return false;
            for (int k = 0; k < deck; k++) state.Deck.Add(-1);
            state.FaceUpAside.AddRange(faceUp);
            state.Peek.AddRange(peek);

            switch ((GameStatus)status)
            {
                case GameStatus.Win:
                    if (!IsPlayer(winner, n)) return false;
                    state.Result = GameResult.WinFor(winner);
                    break;
                case GameStatus.Draw:
                    state.Result = GameResult.Draw;
                    break;
                default:
                    state.Result = GameResult.Ongoing;
                    break;
            }

            var events = new List<SecretEvent>();
            if (p[17].Length > 0)
                foreach (var e in p[17].Split(';'))
                {
                    if (!TryDecodeEvent(e, n, out var ev)) return false;
                    events.Add(ev);
                }

            snapshot = new MatchSnapshot(SecretPlayerView.FromMasked(state, viewer, events));
            return true;
        }

        static bool TryDecodePlayer(string body, SecretPlayer p, int n, bool isViewer)
        {
            var f = body.Split(':');
            if (f.Length != 6) return false;
            if (!Ints(f[0], '/', out var hand) || hand.Length > 2) return false;
            // 自分の手札は全部見える。ほかの人の手札は -1
            if (isViewer ? !hand.All(SecretCards.IsCard) : hand.Any(c => c != -1)) return false;
            if (!Ints(f[1], '/', out var played) || !played.All(SecretCards.IsCard)) return false;
            if (!Ints(f[2], '/', out var known) || known.Length != n || known.Any(c => c != -1 && !SecretCards.IsCard(c))) return false;
            if (!isViewer && known.Any(c => c != -1)) return false;
            if ((f[3] != "0" && f[3] != "1") || (f[4] != "0" && f[4] != "1")) return false;
            if (!Int(f[5], out int tokens) || tokens < 0) return false;
            p.Hand.AddRange(hand);
            p.Played.AddRange(played);
            known.CopyTo(p.Known, 0);
            p.Out = f[3] == "1";
            p.Protected = f[4] == "1";
            p.Tokens = tokens;
            return true;
        }

        static bool TryDecodeEvent(string body, int n, out SecretEvent ev)
        {
            ev = default;
            var f = body.Split(',');
            if (f.Length != 9) return false;
            var x = new int[9];
            for (int i = 0; i < 9; i++)
                if (!Int(f[i], out x[i])) return false;
            if (!Enum.IsDefined(typeof(EventKind), x[1])) return false;
            if (x[2] < -1 || x[2] >= n || x[3] < -1 || x[3] >= n) return false;
            if ((x[4] != -1 && !SecretCards.IsCard(x[4])) || (x[5] != -1 && !SecretCards.IsCard(x[5]))) return false;
            ev = new SecretEvent(x[0], (EventKind)x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8]);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(SecretMove m) =>
            string.Format(CultureInfo.InvariantCulture, "M|{0}|{1},{2},{3},{4},{5}", Version, (int)m.Kind, m.Card, m.Target, m.Guess, m.Choice);

        public static bool TryDecodeMove(string message, out SecretMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 3 || !Ints(p[2], ',', out var f) || f.Length != 5) return false;
            if (!Enum.IsDefined(typeof(MoveKind), f[0])) return false;
            move = SecretMove.Create((MoveKind)f[0], f[1], f[2], f[3], f[4]);
            return true;
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string Join(IEnumerable<int> values, char sep) =>
            string.Join(sep.ToString(), values.Select(x => x.ToString(CultureInfo.InvariantCulture)));

        static bool Ints(string s, char sep, out int[] values)
        {
            if (s.Length == 0)
            {
                values = new int[0];
                return true;
            }
            var parts = s.Split(sep);
            values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!Int(parts[i], out values[i])) return false;
            return true;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p, int n) => p >= 0 && p < n;
    }
}
