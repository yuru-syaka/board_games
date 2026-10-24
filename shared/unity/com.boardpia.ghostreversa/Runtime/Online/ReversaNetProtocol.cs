using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostReversa.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>サーバー(ホスト) → プレイヤー: 局面(受け取る人の視点に伏せ済み)。</summary>
        Snapshot,
        /// <summary>プレイヤー → サーバー(ホスト): 指したい手。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。
    /// Snapshot: S|1|viewer|current|first|ply|phase|status|winner|reason|lastMove|cells|hands|discards
    ///   lastMove = "-" または "kind,piece,x,y,up(0/1)"
    ///   cells    = 9 マスを ';' 区切り。各マスは "-"(空き)または "owner,kind,up(0/1)"(ポルターガイストの owner は -1)
    ///   hands    = "blue,red,total;blue,red,total"(P0;P1。見えない内訳は -1)
    ///   discards = "d0,d1"(TileKind の数値。見えない・まだ捨てていないなら 0)
    /// Move:     M|1|kind,piece,x,y,up
    /// Rematch:  R|1
    /// </summary>
    public static class ReversaNetProtocol
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
            var v = s.View;
            var sb = new StringBuilder(160);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(v.CurrentPlayer)
                .Append('|').Append(v.FirstPlayer)
                .Append('|').Append(v.Ply)
                .Append('|').Append((int)v.Phase)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append((int)s.EndReason)
                .Append('|').Append(v.LastMove.HasValue ? EncodeMoveBody(v.LastMove.Value) : "-")
                .Append('|');
            for (int i = 0; i < ReversaBoard.CellCount; i++)
            {
                if (i > 0) sb.Append(';');
                var t = v.Cells[i];
                if (!t.HasValue)
                {
                    sb.Append('-');
                    continue;
                }
                sb.Append(t.Value.Owner).Append(',').Append((int)t.Value.Kind).Append(',').Append(t.Value.FaceUp ? 1 : 0);
            }
            sb.Append('|');
            for (int p = 0; p < 2; p++)
            {
                if (p > 0) sb.Append(';');
                sb.Append(v.HandOf(p, TileKind.Blue)).Append(',').Append(v.HandOf(p, TileKind.Red)).Append(',').Append(v.HandCount(p));
            }
            sb.Append('|').Append((int)v.DiscardOf(0)).Append(',').Append((int)v.DiscardOf(1));
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 14) return false;
            if (!Int(p[2], out int viewer) || !Int(p[3], out int current) || !Int(p[4], out int first) || !Int(p[5], out int ply) ||
                !Int(p[6], out int phase) || !Int(p[7], out int status) || !Int(p[8], out int winner) || !Int(p[9], out int reason))
                return false;
            if (!IsPlayer(viewer) || !IsPlayer(current) || !IsPlayer(first) || ply < 0 || ply > ReversaBoard.MaxPlies) return false;
            if (!Enum.IsDefined(typeof(GamePhase), phase) || !Enum.IsDefined(typeof(GameStatus), status) ||
                !Enum.IsDefined(typeof(EndReason), reason))
                return false;

            ReversaMove? lastMove = null;
            if (p[10] != "-")
            {
                if (!TryDecodeMoveBody(p[10], out var m)) return false;
                lastMove = m;
            }

            var cellParts = p[11].Split(';');
            if (cellParts.Length != ReversaBoard.CellCount) return false;
            var cells = new List<Tile?>(ReversaBoard.CellCount);
            foreach (var entry in cellParts)
            {
                if (entry == "-")
                {
                    cells.Add(null);
                    continue;
                }
                var f = entry.Split(',');
                if (f.Length != 3 || !Int(f[0], out int owner) || !Int(f[1], out int kind) || !Int(f[2], out int up)) return false;
                if (!Enum.IsDefined(typeof(TileKind), kind) || (up != 0 && up != 1)) return false;
                bool polter = kind == (int)TileKind.Poltergeist;
                if (polter ? owner != Tile.NoOwner || up != 1 : !IsPlayer(owner)) return false;
                cells.Add(new Tile(owner, (TileKind)kind, up == 1));
            }

            var handParts = p[12].Split(';');
            if (handParts.Length != 2) return false;
            var blue = new int[2];
            var red = new int[2];
            var total = new int[2];
            for (int i = 0; i < 2; i++)
            {
                var f = handParts[i].Split(',');
                if (f.Length != 3 || !Int(f[0], out blue[i]) || !Int(f[1], out red[i]) || !Int(f[2], out total[i])) return false;
                if (!InHandRange(blue[i]) || !InHandRange(red[i]) || total[i] < 0 || total[i] > ReversaBoard.PiecesPerColor * 2) return false;
            }

            var discardParts = p[13].Split(',');
            if (discardParts.Length != 2) return false;
            var discards = new TileKind[2];
            for (int i = 0; i < 2; i++)
            {
                if (!Int(discardParts[i], out int d) || (d != (int)TileKind.Unknown && !ReversaBoard.IsColor((TileKind)d))) return false;
                discards[i] = (TileKind)d;
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

            var view = ReversaPlayerView.FromParts(cells, blue, red, total, discards, viewer, first, current, ply,
                (GamePhase)phase, lastMove);
            snapshot = new MatchSnapshot(view, result, (EndReason)reason);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(ReversaMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out ReversaMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string EncodeMoveBody(ReversaMove m) =>
            string.Join(",", (int)m.Kind, (int)m.Piece, m.X, m.Y, m.FaceUp ? 1 : 0);

        static bool TryDecodeMoveBody(string body, out ReversaMove move)
        {
            move = default;
            var f = body.Split(',');
            if (f.Length != 5) return false;
            if (!Int(f[0], out int kind) || !Int(f[1], out int piece) || !Int(f[2], out int x) || !Int(f[3], out int y) ||
                !Int(f[4], out int up))
                return false;
            if (!Enum.IsDefined(typeof(MoveKind), kind) || !Enum.IsDefined(typeof(TileKind), piece) || (up != 0 && up != 1)) return false;
            switch ((MoveKind)kind)
            {
                case MoveKind.Discard:
                    move = ReversaMove.Discard((TileKind)piece);
                    return true;
                case MoveKind.Pass:
                    move = ReversaMove.Pass();
                    return true;
                default:
                    if (!ReversaBoard.InBounds(x, y)) return false;
                    move = ReversaMove.Place((TileKind)piece, x, y, up == 1);
                    return true;
            }
        }

        static bool InHandRange(int n) => n == ReversaPlayerView.Hidden || (n >= 0 && n <= ReversaBoard.PiecesPerColor * 2);

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
