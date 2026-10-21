using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.Geister.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>ホスト → ゲスト: 局面(ゲスト視点に伏せ済み)。</summary>
        Snapshot,
        /// <summary>ゲスト → ホスト: 指したい手。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。
    /// Snapshot: S|1|viewer|current|ply|status|winner|reason|capturedId|lastMove|ghosts
    ///   lastMove = "-" または "id,fx,fy,tx,ty,escape(0/1)"
    ///   ghosts   = "id,owner,type,x,y,status" を ';' 区切り
    /// Move:     M|1|id,fx,fy,tx,ty,escape
    /// Rematch:  R|1
    /// </summary>
    public static class GeisterNetProtocol
    {
        public const int Version = 1;

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

        public static string EncodeSnapshot(MatchSnapshot s)
        {
            var sb = new StringBuilder(256);
            sb.Append("S|").Append(Version)
                .Append('|').Append(s.Viewer)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append((int)s.EndReason)
                .Append('|').Append(s.LastCapturedId)
                .Append('|').Append(s.LastMove.HasValue ? EncodeMoveBody(s.LastMove.Value) : "-")
                .Append('|');
            var ghosts = s.View.Ghosts;
            for (int i = 0; i < ghosts.Count; i++)
            {
                var g = ghosts[i];
                if (i > 0) sb.Append(';');
                sb.Append(g.Id).Append(',').Append(g.Owner).Append(',').Append((int)g.Type).Append(',')
                    .Append(g.X).Append(',').Append(g.Y).Append(',').Append((int)g.Status);
            }
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 11) return false;
            if (!Int(p[2], out int viewer) || !Int(p[3], out int current) || !Int(p[4], out int ply) ||
                !Int(p[5], out int status) || !Int(p[6], out int winner) || !Int(p[7], out int reason) ||
                !Int(p[8], out int capturedId))
                return false;
            if (!IsPlayer(viewer) || !IsPlayer(current) || ply < 0 || ply > GeisterBoard.MaxPlies) return false;
            if (!Enum.IsDefined(typeof(GameStatus), status) || !Enum.IsDefined(typeof(EndReason), reason)) return false;

            GeisterMove? lastMove = null;
            if (p[9] != "-")
            {
                if (!TryDecodeMoveBody(p[9], out var m)) return false;
                lastMove = m;
            }

            var ghosts = new List<Ghost>(16);
            foreach (var entry in p[10].Split(';'))
            {
                var f = entry.Split(',');
                if (f.Length != 6) return false;
                if (!Int(f[0], out int id) || !Int(f[1], out int owner) || !Int(f[2], out int type) ||
                    !Int(f[3], out int x) || !Int(f[4], out int y) || !Int(f[5], out int st))
                    return false;
                if (!IsPlayer(owner) || !Enum.IsDefined(typeof(GhostType), type) ||
                    !Enum.IsDefined(typeof(GhostStatus), st))
                    return false;
                if (st == (int)GhostStatus.OnBoard && !GeisterBoard.InBounds(x, y)) return false;
                ghosts.Add(new Ghost(id, owner, (GhostType)type, x, y, (GhostStatus)st));
            }
            if (ghosts.Count != GeisterBoard.GhostsPerPlayer * 2) return false;

            GameResult result;
            switch ((GameStatus)status)
            {
                case GameStatus.Win:
                    if (!IsPlayer(winner)) return false;
                    result = GameResult.WinFor(winner);
                    break;
                case GameStatus.Draw:
                    result = GameResult.Draw;
                    break;
                default:
                    result = GameResult.Ongoing;
                    break;
            }

            var view = GeisterPlayerView.FromParts(ghosts, viewer, current, ply, lastMove);
            snapshot = new MatchSnapshot(view, result, (EndReason)reason, capturedId);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(GeisterMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out GeisterMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string EncodeMoveBody(GeisterMove m) =>
            string.Join(",", m.GhostId, m.FromX, m.FromY, m.ToX, m.ToY, m.IsEscape ? 1 : 0);

        static bool TryDecodeMoveBody(string body, out GeisterMove move)
        {
            move = default;
            var f = body.Split(',');
            if (f.Length != 6) return false;
            if (!Int(f[0], out int id) || !Int(f[1], out int fx) || !Int(f[2], out int fy) ||
                !Int(f[3], out int tx) || !Int(f[4], out int ty) || !Int(f[5], out int esc))
                return false;
            if (esc != 0 && esc != 1) return false;
            move = esc == 1 ? GeisterMove.Escape(id, fx, fy) : GeisterMove.Step(id, fx, fy, tx, ty);
            return true;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
