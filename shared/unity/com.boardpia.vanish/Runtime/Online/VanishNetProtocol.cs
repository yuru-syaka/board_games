using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.Vanish.Online
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
    /// Snapshot: S|1|current|ply|status|winner|reason|lastMove|vanished|line|order0|order1
    ///   lastMove = "-" または置いたマス
    ///   vanished = 直前の手で消えたマス(無ければ -1)
    ///   line     = "-" または "c0,c1,c2"
    ///   orderN   = プレイヤー N の盤上の駒のマスを古い順に ',' 区切り(無ければ "-")
    /// Move:     M|1|cell
    /// Rematch:  R|1
    /// </summary>
    public static class VanishNetProtocol
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

        public static string EncodeSnapshot(VanishState s)
        {
            var sb = new StringBuilder(64);
            sb.Append("S|").Append(Version)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append((int)s.EndReason)
                .Append('|').Append(s.LastMove.HasValue ? s.LastMove.Value.Cell.ToString(CultureInfo.InvariantCulture) : "-")
                .Append('|').Append(s.LastVanished)
                .Append('|').Append(s.WinningLine != null ? string.Join(",", s.WinningLine) : "-");
            for (int p = 0; p < 2; p++)
            {
                var pieces = s.PiecesOf(p);
                sb.Append('|').Append(pieces.Count > 0 ? string.Join(",", pieces) : "-");
            }
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out VanishState state)
        {
            state = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 12) return false;
            if (!Int(p[2], out int current) || !Int(p[3], out int ply) || !Int(p[4], out int status) ||
                !Int(p[5], out int winner) || !Int(p[6], out int reason) || !Int(p[8], out int vanished))
                return false;
            if (!IsPlayer(current) || ply < 0 || ply > VanishState.MaxPlies) return false;
            if (!Enum.IsDefined(typeof(GameStatus), status) || !Enum.IsDefined(typeof(EndReason), reason)) return false;
            if (vanished != VanishState.Empty && !IsCell(vanished)) return false;

            VanishMove? lastMove = null;
            if (p[7] != "-")
            {
                if (!Int(p[7], out int cell) || !IsCell(cell)) return false;
                lastMove = VanishMove.Place(cell);
            }

            int[] line = null;
            if (p[9] != "-" && !TryDecodeLine(p[9], out line)) return false;

            var order = new int[2][];
            var used = new bool[VanishState.CellCount];
            for (int player = 0; player < 2; player++)
            {
                if (!TryDecodeOrder(p[10 + player], used, out order[player])) return false;
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

            state = new VanishState(order, current, ply, result, (EndReason)reason, lastMove, vanished, line);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(VanishMove m) => $"M|{Version}|{m.Cell}";

        public static bool TryDecodeMove(string message, out VanishMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 3 || !Int(p[2], out int cell) || !IsCell(cell)) return false;
            move = VanishMove.Place(cell);
            return true;
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static bool TryDecodeOrder(string body, bool[] used, out int[] order)
        {
            order = null;
            if (body == "-")
            {
                order = Array.Empty<int>();
                return true;
            }
            var f = body.Split(',');
            if (f.Length > VanishState.MaxPieces) return false;
            var cells = new List<int>(f.Length);
            foreach (var s in f)
            {
                if (!Int(s, out int c) || !IsCell(c) || used[c]) return false;
                used[c] = true;
                cells.Add(c);
            }
            order = cells.ToArray();
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
            foreach (var l in VanishState.Lines)
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

        static bool IsCell(int c) => c >= 0 && c < VanishState.CellCount;
    }
}
