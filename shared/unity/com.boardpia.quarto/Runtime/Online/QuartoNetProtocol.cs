using System;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.Quarto.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>ホスト → ゲスト: 局面。</summary>
        Snapshot,
        /// <summary>ゲスト → ホスト: 指したい手。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。
    /// Snapshot: S|1|viewer|current|phase|hand|ply|status|winner|reason|lastMove|cells
    ///   lastMove = "-" または "kind(0=置く/1=渡す),x,y,piece"
    ///   cells    = 16 マスの駒 Id(空きは -1)を ',' 区切り(マス番号 = y * 4 + x の順)
    /// Move:     M|1|kind,x,y,piece
    /// Rematch:  R|1
    /// そろったラインは受け取った側が盤から計算し直す。
    /// </summary>
    public static class QuartoNetProtocol
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
            var st = s.State;
            var sb = new StringBuilder(128);
            sb.Append("S|").Append(Version)
                .Append('|').Append(s.Viewer)
                .Append('|').Append(st.CurrentPlayer)
                .Append('|').Append((int)st.Phase)
                .Append('|').Append(st.PieceInHand)
                .Append('|').Append(st.Ply)
                .Append('|').Append((int)st.Result.Status)
                .Append('|').Append(st.Result.Winner)
                .Append('|').Append((int)st.EndReason)
                .Append('|').Append(st.LastMove.HasValue ? EncodeMoveBody(st.LastMove.Value) : "-")
                .Append('|');
            for (int i = 0; i < st.Cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(st.Cells[i]);
            }
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 12) return false;
            if (!Int(p[2], out int viewer) || !Int(p[3], out int current) || !Int(p[4], out int phase) ||
                !Int(p[5], out int hand) || !Int(p[6], out int ply) || !Int(p[7], out int status) ||
                !Int(p[8], out int winner) || !Int(p[9], out int reason))
                return false;
            if (!IsPlayer(viewer) || !IsPlayer(current)) return false;
            if (!Enum.IsDefined(typeof(TurnPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status) ||
                !Enum.IsDefined(typeof(EndReason), reason))
                return false;

            QuartoMove? lastMove = null;
            if (p[10] != "-")
            {
                if (!TryDecodeMoveBody(p[10], out var m)) return false;
                lastMove = m;
            }

            var f = p[11].Split(',');
            if (f.Length != QuartoBoard.CellCount) return false;
            var cells = new int[QuartoBoard.CellCount];
            for (int i = 0; i < cells.Length; i++)
                if (!Int(f[i], out cells[i])) return false;

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

            if (!QuartoSetup.TryRestore(cells, current, (TurnPhase)phase, hand, ply, result, (EndReason)reason, lastMove,
                    out var state))
                return false;
            snapshot = new MatchSnapshot(state, viewer);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(QuartoMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out QuartoMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string EncodeMoveBody(QuartoMove m) =>
            string.Join(",", (int)m.Kind, m.X, m.Y, m.Piece);

        static bool TryDecodeMoveBody(string body, out QuartoMove move)
        {
            move = default;
            var f = body.Split(',');
            if (f.Length != 4) return false;
            if (!Int(f[0], out int kind) || !Int(f[1], out int x) || !Int(f[2], out int y) || !Int(f[3], out int piece))
                return false;
            if (kind == (int)MoveKind.Place)
            {
                if (!QuartoBoard.InBounds(x, y) || piece != -1) return false;
                move = QuartoMove.Place(x, y);
                return true;
            }
            if (kind == (int)MoveKind.Give)
            {
                if (!QuartoPiece.IsValid(piece) || x != -1 || y != -1) return false;
                move = QuartoMove.Give(piece);
                return true;
            }
            return false;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
