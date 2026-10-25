using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BoardGames.Core;

namespace BoardGames.GhostTrend.Online
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
    /// Snapshot: S|1|viewer|人数|current|ply|round|phase|status|winner|deck|auctionCount|money|handCounts|hand|bought|counts|values|auction|summaries|events
    ///   数の並びは ','、プレイヤーごと・ラウンドごとの並びは ';' で区切る。空の並びは空文字。
    ///   auction  = "-" または "number,auctioneer,originator,type,highBid,highBidder,price,step,cards,passed,bids,order"
    ///              (cards / bids / order は '/' 区切り、passed は 0 / 1 を人数分並べた文字列)
    ///   summaries = "round:counts:ranks:cardValues:payouts"(中は '/' 区切り)を ';' 区切り
    ///   events   = "seq,kind,player,card,card2,amount,other,auctionNo" を ';' 区切り
    /// Move:     M|1|kind,card,amount
    /// Rematch:  R|1
    /// </summary>
    public static class TrendNetProtocol
    {
        public const int Version = 1;
        const int SnapshotFields = 21;

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
            var sb = new StringBuilder(1024);
            sb.Append("S|").Append(Version)
                .Append('|').Append(v.Viewer)
                .Append('|').Append(v.PlayerCount)
                .Append('|').Append(v.CurrentPlayer)
                .Append('|').Append(v.Ply)
                .Append('|').Append(v.Round)
                .Append('|').Append((int)v.Phase)
                .Append('|').Append((int)v.Result.Status)
                .Append('|').Append(v.Result.Winner)
                .Append('|').Append(v.DeckCount)
                .Append('|').Append(v.AuctionCount)
                .Append('|').Append(Join(v.Money, ','))
                .Append('|').Append(Join(v.HandCounts, ','))
                .Append('|').Append(Join(v.Hand, ','))
                .Append('|').Append(string.Join(";", v.Bought.Select(b => Join(b, ','))))
                .Append('|').Append(Join(v.Counts, ','))
                .Append('|').Append(string.Join(";", v.Values.Select(r => Join(r, ','))))
                .Append('|').Append(EncodeAuction(v.Auction))
                .Append('|').Append(string.Join(";", v.Summaries.Select(EncodeSummary)))
                .Append('|').Append(string.Join(";", v.Events.Select(EncodeEvent)));
            return sb.ToString();
        }

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
            if (!Int(p[2], out int viewer) || !Int(p[3], out int n) || !Int(p[4], out int current) || !Int(p[5], out int ply) ||
                !Int(p[6], out int round) || !Int(p[7], out int phase) || !Int(p[8], out int status) || !Int(p[9], out int winner) ||
                !Int(p[10], out int deck) || !Int(p[11], out int auctionCount))
                return false;
            if (!TrendLogic.IsValidPlayerCount(n) || viewer < 0 || viewer >= n || current < -1 || current >= n) return false;
            if (ply < 0 || round < 0 || round > TrendLogic.Rounds || deck < 0 || auctionCount < 0) return false;
            if (!Enum.IsDefined(typeof(TrendPhase), phase) || !Enum.IsDefined(typeof(GameStatus), status)) return false;

            if (!Ints(p[12], ',', out var money) || money.Length != n) return false;
            if (!Ints(p[13], ',', out var handCounts) || handCounts.Length != n || handCounts.Any(c => c < 0)) return false;
            if (!Ints(p[14], ',', out var hand) || !hand.All(TrendCards.IsValid) || hand.Length != handCounts[viewer]) return false;
            var boughtParts = p[15].Split(';');
            if (boughtParts.Length != n) return false;
            var bought = new int[n][];
            for (int i = 0; i < n; i++)
                if (!Ints(boughtParts[i], ',', out bought[i]) || !bought[i].All(TrendCards.IsValid)) return false;
            if (!Ints(p[16], ',', out var counts) || counts.Length != TrendCards.ArtistCount) return false;
            var valueParts = p[17].Split(';');
            if (valueParts.Length != TrendLogic.Rounds) return false;
            var values = new int[TrendLogic.Rounds][];
            for (int r = 0; r < values.Length; r++)
                if (!Ints(valueParts[r], ',', out values[r]) || values[r].Length != TrendCards.ArtistCount) return false;

            AuctionInfo auction = null;
            if (p[18] != "-" && !TryDecodeAuction(p[18], n, out auction)) return false;

            var summaries = new List<RoundSummary>();
            if (p[19].Length > 0)
                foreach (var s in p[19].Split(';'))
                {
                    if (!TryDecodeSummary(s, n, out var summary)) return false;
                    summaries.Add(summary);
                }

            var events = new List<TrendEvent>();
            if (p[20].Length > 0)
                foreach (var e in p[20].Split(';'))
                {
                    if (!TryDecodeEvent(e, out var ev)) return false;
                    events.Add(ev);
                }

            GameResult result;
            switch ((GameStatus)status)
            {
                case GameStatus.Win:
                    if (winner < 0 || winner >= n) return false;
                    result = GameResult.WinFor(winner);
                    break;
                case GameStatus.Draw:
                    result = GameResult.Draw;
                    break;
                default:
                    result = GameResult.Ongoing;
                    break;
            }

            var view = new TrendPlayerView(viewer, n, hand, handCounts, money, bought, deck, round, counts, values, (TrendPhase)phase,
                current, auction, auctionCount, ply, events, summaries, result);
            snapshot = new MatchSnapshot(view);
            return true;
        }

        static string EncodeAuction(AuctionInfo a)
        {
            if (a == null) return "-";
            return string.Join(",",
                a.Number, a.Auctioneer, a.Originator, (int)a.Type, a.HighBid, a.HighBidder, a.Price, a.Step,
                Join(a.Cards, '/'),
                new string(a.Passed.Select(x => x ? '1' : '0').ToArray()),
                Join(a.Bids, '/'),
                Join(a.Order, '/'));
        }

        static bool TryDecodeAuction(string body, int n, out AuctionInfo auction)
        {
            auction = null;
            var f = body.Split(',');
            if (f.Length != 12) return false;
            if (!Int(f[0], out int number) || !Int(f[1], out int auctioneer) || !Int(f[2], out int originator) ||
                !Int(f[3], out int type) || !Int(f[4], out int highBid) || !Int(f[5], out int highBidder) ||
                !Int(f[6], out int price) || !Int(f[7], out int step))
                return false;
            if (!IsPlayer(auctioneer, n) || !IsPlayer(originator, n) || highBidder < -1 || highBidder >= n) return false;
            if (!Enum.IsDefined(typeof(AuctionType), type)) return false;
            if (!Ints(f[8], '/', out var cards) || cards.Length < 1 || cards.Length > 2 || !cards.All(TrendCards.IsValid)) return false;
            if (f[9].Length != n || f[9].Any(c => c != '0' && c != '1')) return false;
            var passed = f[9].Select(c => c == '1');
            if (!Ints(f[10], '/', out var bids) || bids.Length != n) return false;
            if (!Ints(f[11], '/', out var order) || order.Any(x => !IsPlayer(x, n))) return false;
            if (step < 0 || (order.Length > 0 && step >= order.Length)) return false;
            auction = new AuctionInfo(number, auctioneer, originator, (AuctionType)type, cards, highBid, highBidder, price, passed, bids,
                order, step);
            return true;
        }

        static string EncodeSummary(RoundSummary s) =>
            string.Join(":", s.Round.ToString(CultureInfo.InvariantCulture), Join(s.Counts, '/'), Join(s.Ranks, '/'),
                Join(s.CardValues, '/'), Join(s.Payouts, '/'));

        static bool TryDecodeSummary(string body, int n, out RoundSummary summary)
        {
            summary = null;
            var f = body.Split(':');
            if (f.Length != 5 || !Int(f[0], out int round) || round < 0 || round >= TrendLogic.Rounds) return false;
            int artists = TrendCards.ArtistCount;
            if (!Ints(f[1], '/', out var counts) || counts.Length != artists) return false;
            if (!Ints(f[2], '/', out var ranks) || ranks.Length != artists) return false;
            if (!Ints(f[3], '/', out var values) || values.Length != artists) return false;
            if (!Ints(f[4], '/', out var payouts) || payouts.Length != n) return false;
            summary = new RoundSummary(round, counts, ranks, values, payouts);
            return true;
        }

        static string EncodeEvent(TrendEvent e) =>
            string.Join(",", e.Seq, (int)e.Kind, e.Player, e.Card, e.Card2, e.Amount, e.Other, e.AuctionNo);

        static bool TryDecodeEvent(string body, out TrendEvent ev)
        {
            ev = default;
            if (!Ints(body, ',', out var f) || f.Length != 8) return false;
            if (!Enum.IsDefined(typeof(EventKind), f[1])) return false;
            if ((f[3] >= 0 && !TrendCards.IsValid(f[3])) || (f[4] >= 0 && !TrendCards.IsValid(f[4]))) return false;
            ev = new TrendEvent(f[0], (EventKind)f[1], f[2], f[3], f[4], f[5], f[6], f[7]);
            return true;
        }

        // ---------- Move ----------

        public static string EncodeMove(TrendMove m) =>
            $"M|{Version}|{(int)m.Kind},{m.Card.ToString(CultureInfo.InvariantCulture)},{m.Amount.ToString(CultureInfo.InvariantCulture)}";

        public static bool TryDecodeMove(string message, out TrendMove move)
        {
            move = default;
            if (KindOf(message) != NetMessageKind.Move) return false;
            var p = message.Split('|');
            if (p.Length != 3 || !Ints(p[2], ',', out var f) || f.Length != 3) return false;
            if (!Enum.IsDefined(typeof(MoveKind), f[0])) return false;
            move = TrendMove.Create((MoveKind)f[0], f[1], f[2]);
            return true;
        }

        // ---------- Rematch ----------

        public static string EncodeRematch() => $"R|{Version}";

        // ---------- 内部 ----------

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
