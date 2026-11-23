using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostDerby.Online
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
    /// Snapshot: S|1|viewer|人数|current|race|phase|ply|status|winner|deck|draw|odds|position|frozen|finish|seen|chaseDone|players|events
    ///   deck / draw = 枚数だけ(中身は送らない)
    ///   players = 1 人ぶん "coins:hand:betRunner:betAmount:chaseRunner:chaseAmount:chaseOdds:decided:hits" を ';'
    ///             (hand は '/'。ほかの人の手札は -1、公開前の賭けは -1 と 0)
    ///   events  = "seq,kind,player,card,amount,amount2" を ';'
    /// Move:     M|1|kind|a|b
    /// Rematch:  R|1
    /// </summary>
    public static class DerbyNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 20;

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
            var sb = new StringBuilder(2048);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(s.PlayerCount)
                .Append('|').Append(s.CurrentPlayer)
                .Append('|').Append(s.Race)
                .Append('|').Append((int)s.Phase)
                .Append('|').Append(s.Ply)
                .Append('|').Append((int)s.Result.Status)
                .Append('|').Append(s.Result.Winner)
                .Append('|').Append(s.Deck.Count)
                .Append('|').Append(s.DrawPile.Count)
                .Append('|').Append(Join(s.Odds, ','))
                .Append('|').Append(Join(s.Position, ','))
                .Append('|').Append(Join(s.Frozen, ','))
                .Append('|').Append(Join(s.Finish, ','))
                .Append('|').Append(Join(s.Seen, ','))
                .Append('|').Append(s.ChaseDone ? '1' : '0')
                .Append('|').Append(string.Join(";", s.Players.Select(EncodePlayer)))
                .Append('|').Append(string.Join(";", v.Events.Select(EncodeEvent)));
            return sb.ToString();
        }

        static string EncodePlayer(DerbyPlayer p) =>
            string.Join(":", I(p.Coins), Join(p.Hand, '/'), I(p.BetRunner), I(p.BetAmount), I(p.ChaseRunner), I(p.ChaseAmount),
                I(p.ChaseOdds), p.Decided ? "1" : "0", I(p.Hits));

        static string EncodeEvent(DerbyEvent e) => string.Join(",", e.Seq, (int)e.Kind, e.Player, e.Card, e.Amount, e.Amount2);

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
            if (!Int(p[2], out int viewer) || !Int(p[3], out int n) || !Int(p[4], out int current) || !Int(p[5], out int race) ||
                !Int(p[6], out int phase) || !Int(p[7], out int ply) || !Int(p[8], out int status) || !Int(p[9], out int winner) ||
                !Int(p[10], out int deck) || !Int(p[11], out int draw) || !Bit(p[17], out bool chaseDone))
                return false;
            if (!DerbyLogic.IsValidPlayerCount(n) || !IsPlayer(viewer, n)) return false;
            if (current != -1 && !IsPlayer(current, n)) return false;
            if (race < 1 || race > DerbyLogic.Races || ply < 0 || deck < 0 || deck > DerbyLogic.CardCount ||
                draw < 0 || draw > DerbyLogic.TrickCount)
                return false;
            if (!Enum.IsDefined(typeof(DerbyPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status)) return false;
            if (!Ints(p[12], ',', out var odds) || odds.Length != DerbyLogic.Runners || odds.Any(x => !DerbyCards.OddsTable.Contains(x))) return false;
            if (!Ints(p[13], ',', out var pos) || pos.Length != DerbyLogic.Runners || pos.Any(x => x < 0 || x > DerbyLogic.Goal)) return false;
            if (!Ints(p[14], ',', out var frozen) || frozen.Length != DerbyLogic.Runners || frozen.Any(x => x < 0 || x > n)) return false;
            if (!Ints(p[15], ',', out var finish) || finish.Length > DerbyLogic.Finishers || !finish.All(DerbyLogic.IsRunner)) return false;
            if (!Ints(p[16], ',', out var seen) || seen.Length > DerbyLogic.RunCardCount || !seen.All(DerbyCards.IsRun)) return false;

            var players = p[18].Split(';');
            if (players.Length != n) return false;
            var state = new DerbyState(n)
            {
                CurrentPlayer = current,
                Race = race,
                Phase = (DerbyPhase)phase,
                Ply = ply,
                ChaseDone = chaseDone,
            };
            odds.CopyTo(state.Odds, 0);
            pos.CopyTo(state.Position, 0);
            frozen.CopyTo(state.Frozen, 0);
            state.Finish.AddRange(finish);
            state.Seen.AddRange(seen);
            for (int k = 0; k < deck; k++) state.Deck.Add(-1);
            for (int k = 0; k < draw; k++) state.DrawPile.Add(-1);
            for (int i = 0; i < n; i++)
                if (!TryDecodePlayer(players[i], state.Players[i], i == viewer, state.Phase)) return false;

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

            var events = new List<DerbyEvent>();
            if (p[19].Length > 0)
                foreach (var e in p[19].Split(';'))
                {
                    if (!TryDecodeEvent(e, n, out var ev)) return false;
                    events.Add(ev);
                }

            snapshot = new MatchSnapshot(DerbyPlayerView.FromMasked(state, viewer, events));
            return true;
        }

        static bool TryDecodePlayer(string body, DerbyPlayer p, bool isViewer, DerbyPhase phase)
        {
            var f = body.Split(':');
            if (f.Length != 9) return false;
            if (!Int(f[0], out int coins) || coins < 0) return false;
            if (!Ints(f[1], '/', out var hand) || hand.Length > DerbyLogic.HandSize) return false;
            // ほかの人の手札は届かない
            if (isViewer ? !hand.All(DerbyCards.IsTrick) : hand.Any(c => c != -1)) return false;
            if (!Int(f[2], out int betRunner) || !Int(f[3], out int betAmount) || !Int(f[4], out int chaseRunner) ||
                !Int(f[5], out int chaseAmount) || !Int(f[6], out int chaseOdds) || !Bit(f[7], out bool decided) ||
                !Int(f[8], out int hits) || hits < 0)
                return false;
            if (betRunner != -1 && !DerbyLogic.IsRunner(betRunner)) return false;
            if (chaseRunner != -1 && !DerbyLogic.IsRunner(chaseRunner)) return false;
            if (betAmount < 0 || betAmount > DerbyLogic.MaxBet || chaseAmount < 0 || chaseAmount > DerbyLogic.MaxChase) return false;
            if (chaseOdds < 0 || chaseOdds > 5) return false;
            // ほかの人の、公開前の賭けは届かない
            if (!isViewer && phase == DerbyPhase.Bet && (betRunner != -1 || betAmount != 0)) return false;
            if (!isViewer && phase == DerbyPhase.Chase && (chaseRunner != -1 || chaseAmount != 0)) return false;
            p.Coins = coins;
            p.Hand.AddRange(hand);
            p.BetRunner = betRunner;
            p.BetAmount = betAmount;
            p.ChaseRunner = chaseRunner;
            p.ChaseAmount = chaseAmount;
            p.ChaseOdds = chaseOdds;
            p.Decided = decided;
            p.Hits = hits;
            return true;
        }

        static bool TryDecodeEvent(string body, int n, out DerbyEvent ev)
        {
            ev = default;
            var f = body.Split(',');
            if (f.Length != 6) return false;
            var x = new int[6];
            for (int i = 0; i < 6; i++)
                if (!Int(f[i], out x[i])) return false;
            if (!Enum.IsDefined(typeof(EventKind), x[1])) return false;
            if (x[2] < -1 || x[2] >= n) return false;
            if (x[3] != -1 && !DerbyCards.IsCard(x[3])) return false;
            ev = new DerbyEvent(x[0], (EventKind)x[1], x[2], x[3], x[4], x[5]);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(DerbyMove m) =>
            string.Format(CultureInfo.InvariantCulture, "M|{0}|{1}|{2}|{3}", Version, (int)m.Kind, m.A, m.B);

        public static bool TryDecodeMove(string message, out DerbyMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 5 || !Int(p[2], out int kind) || !Int(p[3], out int a) || !Int(p[4], out int b)) return false;
            if (!Enum.IsDefined(typeof(MoveKind), kind)) return false;
            move = DerbyMove.Create((MoveKind)kind, a, b);
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
