using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostJewel.Online
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
    /// Snapshot: S|1|viewer|人数|current|start|phase|final|ply|passStreak|status|winner|bank|market|decks|nobles|players|events
    ///   bank   = 6 種の数を ','
    ///   market = 段 0..2 × 4 枚のカード ID(空きは -1)を ','(段 0 の 4 枚が先)
    ///   decks  = 段ごとの山札の枚数を ','
    ///   nobles = 残っている館の主を ','
    ///   players = 1 人ぶん "gems:bonus:points:reserved:fromDeck:bought:nobles" を ';'
    ///             (中は '/' 区切り。fromDeck は予約ごとの 0 / 1 を並べた文字列。相手の山札からの予約は -1)
    ///   events = "seq,kind,player,card,tier,slot,gem,noble,amount,gems" を ';'(gems は '/' 区切りの 6 個か "-")
    /// Move:     M|1|kind,a
    /// Rematch:  R|1
    /// </summary>
    public static class JewelNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 18;
        const int CardCount = 90;

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
            var market = new List<int>();
            for (int t = 0; t < JewelLogic.Tiers; t++) market.AddRange(s.Market[t]);
            var sb = new StringBuilder(1024);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(s.PlayerCount)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.StartPlayer)
                .Append('|').Append((int)s.Phase)
                .Append('|').Append(s.FinalRound ? 1 : 0)
                .Append('|').Append(s.Ply)
                .Append('|').Append(s.PassStreak)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append(Join(s.Bank, ','))
                .Append('|').Append(Join(market, ','))
                .Append('|').Append(Join(s.Decks.Select(d => d.Count), ','))
                .Append('|').Append(Join(s.Nobles, ','))
                .Append('|').Append(string.Join(";", s.Players.Select(EncodePlayer)))
                .Append('|').Append(string.Join(";", v.Events.Select(EncodeEvent)));
            return sb.ToString();
        }

        static string EncodePlayer(JewelPlayer p) =>
            string.Join(":", Join(p.Gems, '/'), Join(p.Bonus, '/'), p.Points.ToString(CultureInfo.InvariantCulture),
                Join(p.Reserved, '/'), new string(p.ReservedFromDeck.Select(x => x ? '1' : '0').ToArray()), Join(p.Bought, '/'),
                Join(p.Nobles, '/'));

        static string EncodeEvent(JewelEvent e) =>
            string.Join(",", e.Seq, (int)e.Kind, e.Player, e.Card, e.Tier, e.Slot, e.Gem, e.Noble, e.Amount,
                e.Gems == null ? "-" : Join(e.Gems, '/'));

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
            if (!Int(p[2], out int viewer) || !Int(p[3], out int n) || !Int(p[4], out int current) || !Int(p[5], out int start) ||
                !Int(p[6], out int phase) || !Int(p[7], out int final) || !Int(p[8], out int ply) || !Int(p[9], out int passStreak) ||
                !Int(p[10], out int status) || !Int(p[11], out int winner))
                return false;
            if (!JewelLogic.IsValidPlayerCount(n) || !IsPlayer(viewer, n) || !IsPlayer(current, n) || !IsPlayer(start, n)) return false;
            if (ply < 0 || passStreak < 0 || (final != 0 && final != 1)) return false;
            if (!Enum.IsDefined(typeof(JewelPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status)) return false;

            if (!Ints(p[12], ',', out var bank) || bank.Length != Gem.All || bank.Any(x => x < 0)) return false;
            if (!Ints(p[13], ',', out var market) || market.Length != JewelLogic.Tiers * JewelLogic.MarketSize ||
                market.Any(c => c != -1 && !IsCard(c)))
                return false;
            if (!Ints(p[14], ',', out var decks) || decks.Length != JewelLogic.Tiers || decks.Any(x => x < 0 || x > 40)) return false;
            if (!Ints(p[15], ',', out var nobles) || nobles.Any(x => x < 0 || x >= JewelCards.Nobles.Count)) return false;

            var players = p[16].Split(';');
            if (players.Length != n) return false;
            var state = new JewelState(n)
            {
                CurrentPlayer = current,
                StartPlayer = start,
                Phase = (JewelPhase)phase,
                FinalRound = final == 1,
                Ply = ply,
                PassStreak = passStreak,
            };
            for (int i = 0; i < n; i++)
                if (!TryDecodePlayer(players[i], state.Players[i], i == viewer)) return false;
            bank.CopyTo(state.Bank, 0);
            for (int t = 0; t < JewelLogic.Tiers; t++)
            {
                for (int s = 0; s < JewelLogic.MarketSize; s++) state.Market[t][s] = market[t * JewelLogic.MarketSize + s];
                for (int k = 0; k < decks[t]; k++) state.Decks[t].Add(-1);
            }
            state.Nobles.AddRange(nobles);

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

            var events = new List<JewelEvent>();
            if (p[17].Length > 0)
                foreach (var e in p[17].Split(';'))
                {
                    if (!TryDecodeEvent(e, n, out var ev)) return false;
                    events.Add(ev);
                }

            snapshot = new MatchSnapshot(JewelPlayerView.FromMasked(state, viewer, events));
            return true;
        }

        static bool TryDecodePlayer(string body, JewelPlayer p, bool isViewer)
        {
            var f = body.Split(':');
            if (f.Length != 7) return false;
            if (!Ints(f[0], '/', out var gems) || gems.Length != Gem.All || gems.Any(x => x < 0)) return false;
            if (!Ints(f[1], '/', out var bonus) || bonus.Length != Gem.Colors || bonus.Any(x => x < 0)) return false;
            if (!Int(f[2], out int points) || points < 0) return false;
            if (!Ints(f[3], '/', out var reserved) || reserved.Length > JewelLogic.MaxReserved) return false;
            if (f[4].Length != reserved.Length || f[4].Any(c => c != '0' && c != '1')) return false;
            // 自分の予約は全部見える。相手の予約は、山札からのものだけ -1
            for (int i = 0; i < reserved.Length; i++)
            {
                bool hidden = reserved[i] == -1;
                if (hidden ? isViewer || f[4][i] != '1' : !IsCard(reserved[i])) return false;
            }
            if (!Ints(f[5], '/', out var bought) || !bought.All(IsCard)) return false;
            if (!Ints(f[6], '/', out var nobles) || nobles.Any(x => x < 0 || x >= JewelCards.Nobles.Count)) return false;
            gems.CopyTo(p.Gems, 0);
            bonus.CopyTo(p.Bonus, 0);
            p.Points = points;
            p.Reserved.AddRange(reserved);
            p.ReservedFromDeck.AddRange(f[4].Select(c => c == '1'));
            p.Bought.AddRange(bought);
            p.Nobles.AddRange(nobles);
            return true;
        }

        static bool TryDecodeEvent(string body, int n, out JewelEvent ev)
        {
            ev = default;
            var f = body.Split(',');
            if (f.Length != 10) return false;
            var x = new int[9];
            for (int i = 0; i < 9; i++)
                if (!Int(f[i], out x[i])) return false;
            if (!Enum.IsDefined(typeof(EventKind), x[1])) return false;
            if (x[2] < -1 || x[2] >= n || (x[3] != -1 && !IsCard(x[3]))) return false;
            int[] gems = null;
            if (f[9] != "-" && (!Ints(f[9], '/', out gems) || gems.Length != Gem.All)) return false;
            ev = new JewelEvent(x[0], (EventKind)x[1], x[2], x[3], x[4], x[5], x[6], x[7], x[8], gems);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(JewelMove m) =>
            $"M|{Version}|{(int)m.Kind},{m.A.ToString(CultureInfo.InvariantCulture)}";

        public static bool TryDecodeMove(string message, out JewelMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 3 || !Ints(p[2], ',', out var f) || f.Length != 2) return false;
            if (!Enum.IsDefined(typeof(MoveKind), f[0])) return false;
            move = JewelMove.Create((MoveKind)f[0], f[1]);
            return true;
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static bool IsCard(int c) => c >= 0 && c < CardCount;

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
