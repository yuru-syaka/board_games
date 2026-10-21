using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.CenturyGhost.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>ホスト → ゲスト: 局面(山札の並びを含まない)。</summary>
        Snapshot,
        /// <summary>ゲスト → ホスト: 指したい手。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体"。
    /// Snapshot: S|1|viewer|first|current|ply|final|status|winner|reason|marketDeck|ghostDeck|lastMove|table|p0|p1
    ///   lastMove = "-" または "kind,target,times,upgrades"(upgrades はランクを '.' 区切り)
    ///   table    = "market/marketSpirits/ghostRow/goldLeft/silverLeft"
    ///   pN       = "spirits/hand/played/ghosts/gold/silver"
    ///   リストは '.' 区切り、魂は "YYCM" 形式(空は "_")。
    /// Move:     M|1|kind,target,times,upgrades
    /// Rematch:  R|1
    /// </summary>
    public static class CenturyNetProtocol
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
            var sb = new StringBuilder(512);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(v.FirstPlayer)
                .Append('|').Append(v.CurrentPlayer)
                .Append('|').Append(v.Ply)
                .Append('|').Append(v.FinalRound ? 1 : 0)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append((int)s.EndReason)
                .Append('|').Append(v.MarketDeckCount)
                .Append('|').Append(v.GhostDeckCount)
                .Append('|').Append(v.LastMove != null ? EncodeMoveBody(v.LastMove) : "-")
                .Append('|').Append(EncodeTable(v.Table))
                .Append('|').Append(EncodePlayer(v.Players[0]))
                .Append('|').Append(EncodePlayer(v.Players[1]));
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != 16) return false;
            if (!Int(p[2], out int viewer) || !Int(p[3], out int first) || !Int(p[4], out int current) ||
                !Int(p[5], out int ply) || !Int(p[6], out int final) || !Int(p[7], out int status) ||
                !Int(p[8], out int winner) || !Int(p[9], out int reason) || !Int(p[10], out int marketDeck) ||
                !Int(p[11], out int ghostDeck))
                return false;
            if (!IsPlayer(viewer) || !IsPlayer(first) || !IsPlayer(current) || ply < 0 || ply > CenturyLogic.MaxPlies) return false;
            if (final != 0 && final != 1 || marketDeck < 0 || ghostDeck < 0) return false;
            if (!Enum.IsDefined(typeof(GameStatus), status) || !Enum.IsDefined(typeof(EndReason), reason)) return false;

            CenturyMove lastMove = null;
            if (p[12] != "-" && !TryDecodeMoveBody(p[12], out lastMove)) return false;
            if (!TryDecodeTable(p[13], out var table)) return false;
            if (!TryDecodePlayer(p[14], out var p0) || !TryDecodePlayer(p[15], out var p1)) return false;

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

            var view = new CenturyPlayerView(viewer, new[] { p0, p1 }, table, marketDeck, ghostDeck, first, current, ply, final == 1,
                lastMove);
            snapshot = new MatchSnapshot(view, result, (EndReason)reason);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(CenturyMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out CenturyMove move)
        {
            move = null;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string EncodeMoveBody(CenturyMove m) =>
            string.Join(",", (int)m.Kind, m.Target, m.Times, List(m.Upgrades));

        static bool TryDecodeMoveBody(string body, out CenturyMove move)
        {
            move = null;
            var f = body.Split(',');
            if (f.Length != 4) return false;
            if (!Int(f[0], out int kind) || !Int(f[1], out int target) || !Int(f[2], out int times)) return false;
            if (!Enum.IsDefined(typeof(MoveKind), kind) || times < 0) return false;
            if (!TryList(f[3], out var ups) || ups.Count > 8 || ups.Any(r => r < 0 || r >= Spirits.Ranks)) return false;
            move = CenturyMove.FromParts((MoveKind)kind, target, times, ups);
            return true;
        }

        static string EncodeTable(Table t) =>
            string.Join("/", List(t.Market), string.Join(".", t.MarketSpirits.Select(SpiritsText)), List(t.GhostRow), t.GoldLeft,
                t.SilverLeft);

        static bool TryDecodeTable(string s, out Table table)
        {
            table = null;
            var f = s.Split('/');
            if (f.Length != 5) return false;
            if (!TryList(f[0], out var market) || !TryList(f[2], out var ghostRow)) return false;
            if (!Int(f[3], out int gold) || !Int(f[4], out int silver) || gold < 0 || silver < 0) return false;
            if (market.Count > CenturyLogic.MarketSize || ghostRow.Count > CenturyLogic.GhostRowSize) return false;
            if (!market.All(CardCatalog.IsAction) || !ghostRow.All(CardCatalog.IsGhost)) return false;
            var spirits = new List<Spirits>();
            if (market.Count > 0)
            {
                foreach (var part in f[1].Split('.'))
                {
                    if (!TrySpirits(part, out var sp)) return false;
                    spirits.Add(sp);
                }
            }
            else if (f[1].Length != 0)
            {
                return false;
            }
            if (spirits.Count != market.Count) return false;
            table = new Table(market, spirits, ghostRow, gold, silver);
            return true;
        }

        static string EncodePlayer(PlayerData p) =>
            string.Join("/", SpiritsText(p.Spirits), List(p.Hand), List(p.Played), List(p.Ghosts), p.Gold, p.Silver);

        static bool TryDecodePlayer(string s, out PlayerData player)
        {
            player = null;
            var f = s.Split('/');
            if (f.Length != 6) return false;
            if (!TrySpirits(f[0], out var spirits) || spirits.Total > CenturyLogic.MaxSpirits) return false;
            if (!TryList(f[1], out var hand) || !TryList(f[2], out var played) || !TryList(f[3], out var ghosts)) return false;
            if (!hand.All(CardCatalog.IsAction) || !played.All(CardCatalog.IsAction) || !ghosts.All(CardCatalog.IsGhost)) return false;
            if (!Int(f[4], out int gold) || !Int(f[5], out int silver) || gold < 0 || silver < 0) return false;
            player = new PlayerData(spirits, hand, played, ghosts, gold, silver);
            return true;
        }

        static string SpiritsText(Spirits s) => s.IsEmpty ? "_" : s.ToString();

        static bool TrySpirits(string s, out Spirits spirits)
        {
            spirits = Spirits.Empty;
            if (s == "_") return true;
            if (s.Length == 0 || s.Length > 20) return false;
            try
            {
                spirits = Spirits.Parse(s);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        static string List(IEnumerable<int> values) => string.Join(".", values);

        static bool TryList(string s, out List<int> values)
        {
            values = new List<int>();
            if (s.Length == 0) return true;
            foreach (var part in s.Split('.'))
            {
                if (!Int(part, out int v)) return false;
                values.Add(v);
            }
            return true;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
