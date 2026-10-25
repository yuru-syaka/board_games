using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostTrend
{
    /// <summary>
    /// CPU(強さ 1 種類)。そのプレイヤーの視点(TrendPlayerView)だけを見て手を選ぶ。
    /// 画家ごとに「このラウンドで 1 枚がいくらで売れそうか」を見積もり、それを元に出品・入札する。
    /// </summary>
    public sealed class TrendCpu : ICpuPlayer<TrendPlayerView, TrendMove>
    {
        readonly Random _rng;

        public TrendCpu(Random rng)
        {
            _rng = rng ?? new Random();
        }

        public TrendMove ChooseMove(TrendPlayerView v)
        {
            if (!v.IsMyTurn) throw new InvalidOperationException("Not this player's turn");
            var est = EstimateValues(v);
            TrendMove move;
            switch (v.Phase)
            {
                case TrendPhase.Play: move = ChoosePlay(v, est); break;
                case TrendPhase.DoubleOffer: move = ChooseDouble(v, est); break;
                case TrendPhase.SetPrice: move = ChoosePrice(v, est); break;
                default: move = ChooseBid(v, est); break;
            }
            // 念のため: 合法でなければ安全な手に切り替える
            if (v.IsLegal(move)) return move;
            var legal = v.GetLegalMoves();
            foreach (var m in legal)
                if (m.Kind == MoveKind.Pass || m.Kind == MoveKind.Decline) return m;
            return legal[0];
        }

        // ---------- 見積もり ----------

        /// <summary>画家ごとの、このラウンドの 1 枚の売値の見積もり。</summary>
        public static double[] EstimateValues(TrendPlayerView v)
        {
            int artists = TrendCards.ArtistCount;
            var mine = new int[artists];
            foreach (var c in v.Hand) mine[TrendCards.ArtistOf(c)]++;

            // まだ見えていないカード(相手の手札と山札)の画家ごとの枚数
            var seen = new int[artists];
            foreach (var s in v.Summaries)
                for (int a = 0; a < artists; a++) seen[a] += s.Counts[a];
            if (v.Round < TrendLogic.Rounds)
                for (int a = 0; a < artists; a++) seen[a] += v.Counts[a];
            var unknown = new double[artists];
            double unknownTotal = 0;
            for (int a = 0; a < artists; a++)
            {
                unknown[a] = Math.Max(0, TrendCards.CountOf(a) - seen[a] - mine[a]);
                unknownTotal += unknown[a];
            }
            int othersHand = 0;
            for (int p = 0; p < v.PlayerCount; p++)
                if (p != v.Viewer) othersHand += v.HandCounts[p];

            var proj = new double[artists];
            for (int a = 0; a < artists; a++)
            {
                double others = unknownTotal > 0 ? othersHand * unknown[a] / unknownTotal * 0.45 : 0;
                proj[a] = v.Counts[a] + mine[a] * 0.7 + others;
                if (v.Counts[a] == 0 && proj[a] < 0.5) proj[a] = 0;
            }

            var order = Enumerable.Range(0, artists)
                .OrderByDescending(a => proj[a]).ThenBy(a => a).ToArray();
            int round = Math.Min(v.Round, TrendLogic.Rounds - 1);
            var result = new double[artists];
            double third = proj[order[2]], fourth = proj[order[3]];
            for (int i = 0; i < artists; i++)
            {
                int a = order[i];
                double before = round > 0 ? v.CumulativeValue(a, round - 1) : 0;
                if (i < 3)
                {
                    double confidence = Clamp(0.55 + 0.15 * (proj[a] - fourth), 0.5, 1.0);
                    result[a] = (before + TrendLogic.RankValues[i]) * confidence;
                }
                else
                {
                    double chance = Clamp(0.3 - 0.1 * (third - proj[a]), 0, 0.3);
                    result[a] = (before + 10) * chance;
                }
            }
            return result;
        }

        /// <summary>counts のときにラウンドが終わったら、各プレイヤーが受け取る売上。</summary>
        static int[] PayoutsIfRoundEnds(TrendPlayerView v, int[] counts)
        {
            var ranks = TrendLogic.RankArtists(counts);
            int round = Math.Min(v.Round, TrendLogic.Rounds - 1);
            var pay = new int[v.PlayerCount];
            for (int p = 0; p < v.PlayerCount; p++)
            foreach (var c in v.Bought[p])
            {
                int a = TrendCards.ArtistOf(c);
                if (ranks[a] < 0) continue;
                pay[p] += v.CumulativeValue(a, round - 1) + TrendLogic.RankValues[ranks[a]];
            }
            return pay;
        }

        /// <summary>このカードでラウンドを終えたときの、自分にとっての得点(高いほど得)。</summary>
        double RoundEndScore(TrendPlayerView v, int artist)
        {
            var counts = v.Counts.ToArray();
            counts[artist]++;
            var pay = PayoutsIfRoundEnds(v, counts);
            int best = 0;
            for (int p = 0; p < pay.Length; p++)
                if (p != v.Viewer) best = Math.Max(best, pay[p]);
            return pay[v.Viewer] - best * 0.8;
        }

        bool EndsRound(TrendPlayerView v, int card) => v.Counts[TrendCards.ArtistOf(card)] + 1 >= TrendLogic.RoundEndCount;

        // ---------- 出品 ----------

        TrendMove ChoosePlay(TrendPlayerView v, double[] est)
        {
            var myBought = new int[TrendCards.ArtistCount];
            foreach (var c in v.Bought[v.Viewer]) myBought[TrendCards.ArtistOf(c)]++;

            double bestScore = double.MinValue;
            int bestCard = v.Hand[0];
            foreach (var card in v.Hand)
            {
                int a = TrendCards.ArtistOf(card);
                var type = TrendCards.TypeOf(card);
                double score;
                if (EndsRound(v, card))
                {
                    score = RoundEndScore(v, a) + 2;
                }
                else
                {
                    int k = 1;
                    if (type == AuctionType.Double)
                        k = v.Hand.Any(c => c != card && TrendCards.ArtistOf(c) == a && TrendCards.TypeOf(c) != AuctionType.Double) ? 2 : 0;
                    score = est[a] * Math.Max(k, 0.6) * 0.55 + myBought[a] * 4;
                    if (type == AuctionType.Open) score += 2;
                    if (v.Counts[a] + 1 == TrendLogic.RoundEndCount - 1) score -= 3; // 次の人にラウンドを終わらせる機会を渡す
                }
                score += _rng.NextDouble() * 3;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCard = card;
                }
            }
            return TrendMove.Play(bestCard);
        }

        TrendMove ChooseDouble(TrendPlayerView v, double[] est)
        {
            var a = v.Auction;
            int artist = a.Artist;
            var partners = v.Hand.Where(c => TrendCards.ArtistOf(c) == artist && TrendCards.TypeOf(c) != AuctionType.Double).ToList();
            if (partners.Count == 0) return TrendMove.Decline();

            if (v.Counts[artist] + 1 >= TrendLogic.RoundEndCount)
                return RoundEndScore(v, artist) > 0 ? TrendMove.AddDouble(partners[0]) : TrendMove.Decline();

            double revenue = est[artist] * 2 * 0.55;
            bool originator = a.Originator == v.Viewer;
            if (!originator && revenue < 8) return TrendMove.Decline();

            // 添えるカードの種類: 公開 > 入札 > 一巡 > 指値
            int Rank(int c)
            {
                switch (TrendCards.TypeOf(c))
                {
                    case AuctionType.Open: return 0;
                    case AuctionType.Sealed: return 1;
                    case AuctionType.OnceAround: return 2;
                    default: return 3;
                }
            }
            return TrendMove.AddDouble(partners.OrderBy(Rank).First());
        }

        // ---------- 競り ----------

        double CardsValue(TrendPlayerView v, double[] est) => est[v.Auction.Artist] * v.Auction.Cards.Count;

        double MaxBid(TrendPlayerView v, double[] est)
        {
            double value = CardsValue(v, est);
            bool auctioneer = v.Auction.Auctioneer == v.Viewer;
            double rate = auctioneer ? 0.7 : 0.5 + 0.03 * v.PlayerCount;
            double noise = 0.92 + _rng.NextDouble() * 0.16;
            return Math.Min(v.MyMoney, Math.Floor(value * rate * noise));
        }

        TrendMove ChoosePrice(TrendPlayerView v, double[] est)
        {
            double value = CardsValue(v, est);
            int price = (int)Math.Round(value * (0.55 + _rng.NextDouble() * 0.1));
            price = Math.Max(1, price);
            return TrendMove.SetPrice(Math.Min(v.MyMoney, price));
        }

        TrendMove ChooseBid(TrendPlayerView v, double[] est)
        {
            var a = v.Auction;
            int max = (int)MaxBid(v, est);
            switch (a.Type)
            {
                case AuctionType.Fixed:
                {
                    double value = CardsValue(v, est);
                    bool ok = a.Price <= v.MyMoney && a.Price <= value * (0.6 + 0.03 * v.PlayerCount);
                    return ok ? TrendMove.Accept() : TrendMove.Pass();
                }
                case AuctionType.Sealed:
                {
                    int bid = (int)Math.Floor(max * (0.65 + _rng.NextDouble() * 0.25));
                    return TrendMove.Bid(Math.Max(0, Math.Min(v.MyMoney, bid)));
                }
                case AuctionType.OnceAround:
                {
                    bool last = a.Step == a.Order.Count - 1;
                    if (max <= a.HighBid) return TrendMove.Pass();
                    if (last) return TrendMove.Bid(a.HighBid + 1);
                    int bid = (int)Math.Floor(max * (0.75 + _rng.NextDouble() * 0.2));
                    return bid > a.HighBid ? TrendMove.Bid(bid) : TrendMove.Pass();
                }
                default: // 公開競り
                {
                    if (max <= a.HighBid) return TrendMove.Pass();
                    int step = Math.Max(1, (int)Math.Round(CardsValue(v, est) * 0.08));
                    return TrendMove.Bid(Math.Min(max, a.HighBid + step));
                }
            }
        }

        static double Clamp(double x, double min, double max) => x < min ? min : x > max ? max : x;
    }
}
