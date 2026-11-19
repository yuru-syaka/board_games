using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostGold.Online
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
    /// Snapshot: S|1|viewer|人数|current|night|phase|ply|status|winner|deck|leftover|hazardRemoved|relics|path|players|events
    ///   deck          = 山札の枚数(順番は送らない)
    ///   hazardRemoved = 種類ごとの取り除かれた枚数を ','
    ///   relics        = 秘宝のありか(RelicStatus)を ','
    ///   path          = この夜にめくったカード ID を ','
    ///   players = 1 人ぶん "exploring:carry:safe:relics:amulet:decided:choice" を ';'(relics は '/'。ほかの人の choice は -1)
    ///   events  = "seq,kind,player,card,amount,amount2" を ';'
    /// Move:     M|1|choice(0 = 進む、1 = 帰る)
    /// Rematch:  R|1
    /// </summary>
    public static class GoldNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 17;

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
                .Append('|').Append(s.Night)
                .Append('|').Append((int)s.Phase)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append(s.Deck.Count)
                .Append('|').Append(s.Leftover)
                .Append('|').Append(Join(s.HazardRemoved, ','))
                .Append('|').Append(Join(s.Relics.Select(r => (int)r), ','))
                .Append('|').Append(Join(s.Path, ','))
                .Append('|').Append(string.Join(";", s.Players.Select(EncodePlayer)))
                .Append('|').Append(string.Join(";", v.Events.Select(EncodeEvent)));
            return sb.ToString();
        }

        static string EncodePlayer(GoldPlayer p) =>
            string.Join(":", p.Exploring ? "1" : "0", I(p.Carry), I(p.Safe), Join(p.Relics, '/'), p.HasAmulet ? "1" : "0",
                p.Decided ? "1" : "0", I((int)p.Choice));

        static string EncodeEvent(GoldEvent e) => string.Join(",", e.Seq, (int)e.Kind, e.Player, e.Card, e.Amount, e.Amount2);

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
            if (!Int(p[2], out int viewer) || !Int(p[3], out int n) || !Int(p[4], out int current) || !Int(p[5], out int night) ||
                !Int(p[6], out int phase) || !Int(p[7], out int ply) || !Int(p[8], out int status) || !Int(p[9], out int winner) ||
                !Int(p[10], out int deck) || !Int(p[11], out int leftover))
                return false;
            if (!GoldLogic.IsValidPlayerCount(n) || !IsPlayer(viewer, n)) return false;
            if (current != -1 && !IsPlayer(current, n)) return false;
            if (night < 1 || night > GoldLogic.Nights || ply < 0 || deck < 0 || deck > GoldLogic.CardCount || leftover < 0) return false;
            if (!Enum.IsDefined(typeof(GoldPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status)) return false;
            if (!Ints(p[12], ',', out var removed) || removed.Length != GoldLogic.HazardKinds ||
                removed.Any(x => x < 0 || x > GoldLogic.HazardCopies))
                return false;
            if (!Ints(p[13], ',', out var relics) || relics.Length != GoldLogic.RelicCount ||
                relics.Any(x => !Enum.IsDefined(typeof(RelicStatus), x)))
                return false;
            if (!Ints(p[14], ',', out var path) || path.Length > GoldLogic.CardCount || !path.All(GoldCards.IsCard)) return false;

            var players = p[15].Split(';');
            if (players.Length != n) return false;
            var state = new GoldState(n)
            {
                CurrentPlayer = current,
                Night = night,
                Phase = (GoldPhase)phase,
                Ply = ply,
                Leftover = leftover,
            };
            removed.CopyTo(state.HazardRemoved, 0);
            for (int i = 0; i < relics.Length; i++) state.Relics[i] = (RelicStatus)relics[i];
            state.Path.AddRange(path);
            for (int k = 0; k < deck; k++) state.Deck.Add(-1);
            for (int i = 0; i < n; i++)
                if (!TryDecodePlayer(players[i], state.Players[i], i == viewer)) return false;

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

            var events = new List<GoldEvent>();
            if (p[16].Length > 0)
                foreach (var e in p[16].Split(';'))
                {
                    if (!TryDecodeEvent(e, n, out var ev)) return false;
                    events.Add(ev);
                }

            snapshot = new MatchSnapshot(GoldPlayerView.FromMasked(state, viewer, events));
            return true;
        }

        static bool TryDecodePlayer(string body, GoldPlayer p, bool isViewer)
        {
            var f = body.Split(':');
            if (f.Length != 7) return false;
            if (!Bit(f[0], out bool exploring) || !Int(f[1], out int carry) || !Int(f[2], out int safe) || carry < 0 || safe < 0) return false;
            if (!Ints(f[3], '/', out var relics) || relics.Any(c => GoldCards.KindOf(c) != CardKind.Relic || !GoldCards.IsCard(c))) return false;
            if (!Bit(f[4], out bool amulet) || !Bit(f[5], out bool decided) || !Int(f[6], out int choice)) return false;
            if (choice < -1 || choice > 1) return false;
            // ほかの人の決定は、公開まで届かない
            if (!isViewer && choice != -1) return false;
            p.Exploring = exploring;
            p.Carry = carry;
            p.Safe = safe;
            p.Relics.AddRange(relics);
            p.HasAmulet = amulet;
            p.Decided = decided;
            p.Choice = (Choice)choice;
            return true;
        }

        static bool TryDecodeEvent(string body, int n, out GoldEvent ev)
        {
            ev = default;
            var f = body.Split(',');
            if (f.Length != 6) return false;
            var x = new int[6];
            for (int i = 0; i < 6; i++)
                if (!Int(f[i], out x[i])) return false;
            if (!Enum.IsDefined(typeof(EventKind), x[1])) return false;
            if (x[2] < -1 || x[2] >= n) return false;
            if (x[3] != -1 && !GoldCards.IsCard(x[3])) return false;
            ev = new GoldEvent(x[0], (EventKind)x[1], x[2], x[3], x[4], x[5]);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(GoldMove m) =>
            string.Format(CultureInfo.InvariantCulture, "M|{0}|{1}", Version, (int)m.Choice);

        public static bool TryDecodeMove(string message, out GoldMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 3 || !Int(p[2], out int choice) || (choice != 0 && choice != 1)) return false;
            move = GoldMove.Create((Choice)choice);
            return true;
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static string I(int x) => x.ToString(CultureInfo.InvariantCulture);

        static string Join(IEnumerable<int> values, char sep) => string.Join(sep.ToString(), values.Select(I));

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

        static bool Bit(string s, out bool value)
        {
            value = s == "1";
            return s == "0" || s == "1";
        }

        static bool IsPlayer(int p, int n) => p >= 0 && p < n;
    }
}
