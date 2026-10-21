using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.Gobblet.Online
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
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。完全情報ゲームなので局面はそのまま送る。
    /// Snapshot: S|1|current|ply|status|winner|reason|lastMove|line|stacks
    ///   lastMove = "-" または "from,size,to"
    ///   line     = "-" または "c0,c1,c2"
    ///   stacks   = 9 マス分を ';' 区切り。各マスは下→上に「持ち主 大きさ」の 2 桁を並べたもの(空マスは空文字)
    /// Move:     M|1|from,size,to
    /// Rematch:  R|1
    /// </summary>
    public static class GobbletNetProtocol
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

        public static string EncodeSnapshot(GobbletState s)
        {
            var sb = new StringBuilder(128);
            sb.Append("S|").Append(Version)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append((int)s.EndReason)
                .Append('|').Append(s.LastMove.HasValue ? EncodeMoveBody(s.LastMove.Value) : "-")
                .Append('|').Append(s.WinningLine != null ? string.Join(",", s.WinningLine) : "-")
                .Append('|');
            for (int c = 0; c < GobbletState.CellCount; c++)
            {
                if (c > 0) sb.Append(';');
                foreach (var p in s.GetStack(c)) sb.Append(p.Owner).Append((int)p.Size);
            }
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out GobbletState state)
        {
            state = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 10) return false;
            if (!Int(p[2], out int current) || !Int(p[3], out int ply) || !Int(p[4], out int status) ||
                !Int(p[5], out int winner) || !Int(p[6], out int reason))
                return false;
            if (!IsPlayer(current) || ply < 0 || ply > GobbletState.MaxPlies) return false;
            if (!Enum.IsDefined(typeof(GameStatus), status) || !Enum.IsDefined(typeof(EndReason), reason)) return false;

            GobbletMove? lastMove = null;
            if (p[7] != "-")
            {
                if (!TryDecodeMoveBody(p[7], out var m)) return false;
                lastMove = m;
            }

            int[] line = null;
            if (p[8] != "-" && !TryDecodeLine(p[8], out line)) return false;

            var cells = p[9].Split(';');
            if (cells.Length != GobbletState.CellCount) return false;
            var stacks = new Piece[GobbletState.CellCount][];
            var counts = new int[6];
            for (int c = 0; c < cells.Length; c++)
            {
                string cell = cells[c];
                if (cell.Length % 2 != 0 || cell.Length > 6) return false;
                stacks[c] = new Piece[cell.Length / 2];
                for (int i = 0; i < stacks[c].Length; i++)
                {
                    int owner = cell[i * 2] - '0';
                    int size = cell[i * 2 + 1] - '0';
                    if (!IsPlayer(owner) || size < 0 || size > 2) return false;
                    if (i > 0 && size <= (int)stacks[c][i - 1].Size) return false;
                    if (++counts[owner * 3 + size] > GobbletState.PiecesPerSize) return false;
                    stacks[c][i] = new Piece(owner, (PieceSize)size);
                }
            }

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

            state = new GobbletState(stacks, current, ply, result, (EndReason)reason, lastMove, line);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(GobbletMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out GobbletMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string EncodeMoveBody(GobbletMove m) => string.Join(",", m.From, (int)m.Size, m.To);

        static bool TryDecodeMoveBody(string body, out GobbletMove move)
        {
            move = default;
            var f = body.Split(',');
            if (f.Length != 3) return false;
            if (!Int(f[0], out int from) || !Int(f[1], out int size) || !Int(f[2], out int to)) return false;
            if (from < GobbletMove.FromHand || from >= GobbletState.CellCount) return false;
            if (size < 0 || size > 2 || to < 0 || to >= GobbletState.CellCount) return false;
            move = new GobbletMove(from, (PieceSize)size, to);
            return true;
        }

        static bool TryDecodeLine(string body, out int[] line)
        {
            line = null;
            var f = body.Split(',');
            if (f.Length != 3) return false;
            var cells = new int[3];
            for (int i = 0; i < 3; i++)
                if (!Int(f[i], out cells[i])) return false;
            foreach (var l in GobbletState.Lines)
            {
                if (l[0] != cells[0] || l[1] != cells[1] || l[2] != cells[2]) continue;
                line = l;
                return true;
            }
            return false;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
