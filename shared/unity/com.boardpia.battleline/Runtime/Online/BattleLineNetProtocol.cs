using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoardGames.Core;

namespace BoardGames.BattleLine.Online
{
    public enum NetMessageKind
    {
        Invalid,
        /// <summary>ホスト → ゲスト: 局面(ゲスト視点に伏せ済み)。</summary>
        Snapshot,
        /// <summary>ゲスト → ホスト: 行いたい行動。</summary>
        Move,
        /// <summary>双方向: もう一度対戦したい。</summary>
        Rematch,
    }

    /// <summary>
    /// 通信メッセージの文字列化。形式は "種類|版|本体…"。
    /// Snapshot: S|1|viewer|current|phase|pending|turn|step|passes|status|winner|reason|troopDeck|tacticsDeck|
    ///           tactics0|tactics1|leader0|leader1|oppTroops|oppTactics|lastMover|lastMove|hand|oppHand|discard|flags
    ///   lastMove = "-" または "kind,card,flag,target,toFlag,deck"
    ///   hand / discard = カード Id の ',' 区切り(空なら空文字)。oppHand は公開時のみ(非公開は "-")
    ///   flags = フラッグ 9 本を ';' 区切り。1 本は "claimer:seq0:seq1:cards0:cards1:env"
    ///           cards は '.' 区切り、env は "card@owner" の '.' 区切り
    /// Move:     M|1|kind,card,flag,target,toFlag,deck
    /// Rematch:  R|1
    /// </summary>
    public static class BattleLineNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 26;

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
            sb.Append("S|").Append(Version);
            Field(sb, v.Viewer);
            Field(sb, v.CurrentPlayer);
            Field(sb, (int)v.Phase);
            Field(sb, v.PendingCount);
            Field(sb, v.Turn);
            Field(sb, v.Step);
            Field(sb, v.ConsecutivePasses);
            Field(sb, (int)s.Result.Status);
            Field(sb, s.Result.Winner);
            Field(sb, (int)s.EndReason);
            Field(sb, v.TroopDeckCount);
            Field(sb, v.TacticsDeckCount);
            Field(sb, v.TacticsPlayed(0));
            Field(sb, v.TacticsPlayed(1));
            Field(sb, v.LeaderPlayed(0) ? 1 : 0);
            Field(sb, v.LeaderPlayed(1) ? 1 : 0);
            Field(sb, v.OpponentHandTroops);
            Field(sb, v.OpponentHandTactics);
            Field(sb, v.LastMover);
            sb.Append('|').Append(v.LastMove.HasValue ? EncodeMoveBody(v.LastMove.Value) : "-");
            sb.Append('|').Append(string.Join(",", v.Hand));
            sb.Append('|').Append(v.OpponentHand != null ? string.Join(",", v.OpponentHand) : "-");
            sb.Append('|').Append(string.Join(",", v.Discard));
            sb.Append('|');
            for (int i = 0; i < v.Flags.Count; i++)
            {
                var f = v.Flags[i];
                if (i > 0) sb.Append(';');
                sb.Append(f.Claimer).Append(':').Append(f.CompletedSeq(0)).Append(':').Append(f.CompletedSeq(1)).Append(':')
                    .Append(string.Join(".", f.CardsOf(0))).Append(':').Append(string.Join(".", f.CardsOf(1))).Append(':');
                for (int e = 0; e < f.Environment.Count; e++)
                {
                    if (e > 0) sb.Append('.');
                    sb.Append(f.Environment[e]).Append('@').Append(f.EnvironmentOwner(e));
                }
            }
            return sb.ToString();
        }

        public static bool TryDecodeSnapshot(string message, out MatchSnapshot snapshot)
        {
            snapshot = null;
            if (KindOf(message) != NetMessageKind.Snapshot) return false;
            var p = message.Split('|');
            if (p.Length != SnapshotFields) return false;

            var n = new int[19];
            for (int i = 0; i < n.Length; i++)
                if (!Int(p[i + 2], out n[i])) return false;
            int viewer = n[0], current = n[1], phase = n[2], pending = n[3], turn = n[4], step = n[5], passes = n[6];
            int status = n[7], winner = n[8], reason = n[9], troopDeck = n[10], tacticsDeck = n[11];
            int tactics0 = n[12], tactics1 = n[13], leader0 = n[14], leader1 = n[15], oppTroops = n[16], oppTactics = n[17];
            int lastMover = n[18];

            if (!IsPlayer(viewer) || !IsPlayer(current) || (lastMover != -1 && !IsPlayer(lastMover))) return false;
            if (!Enum.IsDefined(typeof(TurnPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status) ||
                !Enum.IsDefined(typeof(EndReason), reason))
                return false;
            if (pending < 0 || turn < 0 || step < 0 || passes < 0) return false;
            if (!InRange(troopDeck, Card.TroopCount) || !InRange(tacticsDeck, Card.TacticsCount)) return false;
            if (!InRange(tactics0, Card.TacticsCount) || !InRange(tactics1, Card.TacticsCount)) return false;
            if (!InRange(leader0, 1) || !InRange(leader1, 1)) return false;
            if (!InRange(oppTroops, Card.TroopCount) || !InRange(oppTactics, Card.TacticsCount)) return false;

            BattleLineMove? lastMove = null;
            if (p[21] != "-")
            {
                if (!TryDecodeMoveBody(p[21], true, out var m)) return false;
                lastMove = m;
            }

            var seen = new bool[Card.Total];
            if (!TryCards(p[22], ',', seen, out var hand)) return false;
            List<int> oppHand = null;
            if (p[23] != "-" && !TryCards(p[23], ',', seen, out oppHand)) return false;
            if (!TryCards(p[24], ',', seen, out var discard)) return false;

            var flagParts = p[25].Split(';');
            if (flagParts.Length != BattleLineState.FlagCount) return false;
            var flags = new Flag[flagParts.Length];
            for (int i = 0; i < flagParts.Length; i++)
            {
                var f = flagParts[i].Split(':');
                if (f.Length != 6) return false;
                if (!Int(f[0], out int claimer) || !Int(f[1], out int seq0) || !Int(f[2], out int seq1)) return false;
                if (claimer != -1 && !IsPlayer(claimer)) return false;
                var flag = new Flag(i);
                for (int side = 0; side < 2; side++)
                {
                    if (!TryCards(f[3 + side], '.', seen, out var cards) || cards.Count > Flag.MudSize) return false;
                    foreach (int c in cards)
                    {
                        if (!Card.IsFormationCard(c)) return false;
                        flag.AddCard(side, c);
                    }
                }
                if (f[5].Length > 0)
                {
                    foreach (var entry in f[5].Split('.'))
                    {
                        var e = entry.Split('@');
                        if (e.Length != 2 || !Int(e[0], out int card) || !Int(e[1], out int owner)) return false;
                        if (!Card.IsEnvironment(card) || !IsPlayer(owner) || seen[card]) return false;
                        seen[card] = true;
                        flag.AddEnvironment(card, owner);
                    }
                }
                flag.SetCompletedSeq(0, seq0);
                flag.SetCompletedSeq(1, seq1);
                if (claimer >= 0) flag.SetClaimer(claimer);
                flags[i] = flag;
            }

            // 見えているカード + 伏せたカードの枚数 = 全 70 枚
            int visible = 0;
            foreach (bool b in seen)
                if (b) visible++;
            int hidden = troopDeck + tacticsDeck + (oppHand == null ? oppTroops + oppTactics : 0);
            if (visible + hidden != Card.Total) return false;

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

            var view = new BattleLinePlayerView(flags, hand, discard, new[] { tactics0, tactics1 },
                new[] { leader0 == 1, leader1 == 1 }, oppHand, viewer, current, (TurnPhase)phase, pending, turn, step, passes,
                troopDeck, tacticsDeck, oppTroops, oppTactics, lastMove, lastMover);
            snapshot = new MatchSnapshot(view, result, (EndReason)reason);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(BattleLineMove m) => $"M|{Version}|{EncodeMoveBody(m)}";

        public static bool TryDecodeMove(string message, out BattleLineMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            return p.Length == 3 && TryDecodeMoveBody(p[2], false, out move);
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

        static void Field(StringBuilder sb, int value) => sb.Append('|').Append(value.ToString(CultureInfo.InvariantCulture));

        static string EncodeMoveBody(BattleLineMove m) =>
            string.Join(",", (int)m.Kind, m.Card, m.Flag, m.Target, m.ToFlag, (int)m.Deck);

        /// <summary>allowHidden: 局面の「直前の行動」では、相手が戻したカードが伏せられている(Card = −1)ことがある。</summary>
        static bool TryDecodeMoveBody(string body, bool allowHidden, out BattleLineMove move)
        {
            move = default;
            var f = body.Split(',');
            if (f.Length != 6) return false;
            if (!Int(f[0], out int kind) || !Int(f[1], out int card) || !Int(f[2], out int flag) ||
                !Int(f[3], out int target) || !Int(f[4], out int toFlag) || !Int(f[5], out int deck))
                return false;
            if (!Enum.IsDefined(typeof(MoveKind), kind) || !Enum.IsDefined(typeof(DeckKind), deck)) return false;
            if (card != -1 && !Card.IsValid(card)) return false;
            if (card == -1 && !(allowHidden && kind == (int)MoveKind.ScoutReturn) &&
                kind != (int)MoveKind.Pass && kind != (int)MoveKind.Draw)
                return false;
            if (target != -1 && !Card.IsValid(target)) return false;
            if (flag < -1 || flag >= BattleLineState.FlagCount || toFlag < -1 || toFlag >= BattleLineState.FlagCount) return false;
            move = new BattleLineMove((MoveKind)kind, card, flag, target, toFlag, (DeckKind)deck);
            return true;
        }

        static bool TryCards(string text, char separator, bool[] seen, out List<int> cards)
        {
            cards = new List<int>();
            if (text.Length == 0) return true;
            foreach (var s in text.Split(separator))
            {
                if (!Int(s, out int c) || !Card.IsValid(c) || seen[c]) return false;
                seen[c] = true;
                cards.Add(c);
            }
            return true;
        }

        static bool Int(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static bool InRange(int value, int max) => value >= 0 && value <= max;

        static bool IsPlayer(int p) => p == 0 || p == 1;
    }
}
