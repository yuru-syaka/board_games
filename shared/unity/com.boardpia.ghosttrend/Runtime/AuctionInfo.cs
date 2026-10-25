using System.Collections.Generic;
using System.Linq;

namespace BoardGames.GhostTrend
{
    /// <summary>
    /// 進行中の競り(不変)。ダブルに添えるかを聞いている間も、ここに「ダブルの 1 枚目」を入れておく。
    /// Bids は入札(伏せた額)用で、まだ決めていない人は -1。プレイヤー視点では、本人以外の決めた額は -2(決めた・額は秘密)。
    /// </summary>
    public sealed class AuctionInfo
    {
        public const int NotSubmitted = -1;
        public const int Hidden = -2;

        /// <summary>何回目の競りか(1 から)。</summary>
        public int Number { get; }
        /// <summary>代金を受け取る人。ダブルで別の人が添えたら、その人に変わる。</summary>
        public int Auctioneer { get; }
        /// <summary>最初にカードを出した人(ダブルで誰も添えなかったとき、この人が引き取る)。</summary>
        public int Originator { get; }
        /// <summary>競りの種類(ダブルに添えるかを聞いている間は Double)。</summary>
        public AuctionType Type { get; }
        public IReadOnlyList<int> Cards { get; }
        /// <summary>今の最高額(公開・一巡)。値が付いていなければ 0。</summary>
        public int HighBid { get; }
        /// <summary>最高額を付けた人(いなければ -1)。</summary>
        public int HighBidder { get; }
        /// <summary>指値の値段(決まる前は -1)。</summary>
        public int Price { get; }
        /// <summary>公開競りで降りた人。</summary>
        public IReadOnlyList<bool> Passed { get; }
        /// <summary>入札の額(決めていなければ -1)。</summary>
        public IReadOnlyList<int> Bids { get; }
        /// <summary>一巡・入札・指値・ダブルの手番の順。</summary>
        public IReadOnlyList<int> Order { get; }
        /// <summary>Order の何番目の人の番か。</summary>
        public int Step { get; }

        public AuctionInfo(int number, int auctioneer, int originator, AuctionType type, IEnumerable<int> cards, int highBid,
            int highBidder, int price, IEnumerable<bool> passed, IEnumerable<int> bids, IEnumerable<int> order, int step)
        {
            Number = number;
            Auctioneer = auctioneer;
            Originator = originator;
            Type = type;
            Cards = cards.ToArray();
            HighBid = highBid;
            HighBidder = highBidder;
            Price = price;
            Passed = passed.ToArray();
            Bids = bids.ToArray();
            Order = order.ToArray();
            Step = step;
        }

        /// <summary>競っている画家。</summary>
        public int Artist => TrendCards.ArtistOf(Cards[0]);

        public AuctionInfo With(int? auctioneer = null, AuctionType? type = null, IEnumerable<int> cards = null, int? highBid = null,
            int? highBidder = null, int? price = null, IEnumerable<bool> passed = null, IEnumerable<int> bids = null,
            IEnumerable<int> order = null, int? step = null) =>
            new AuctionInfo(Number, auctioneer ?? Auctioneer, Originator, type ?? Type, cards ?? Cards, highBid ?? HighBid,
                highBidder ?? HighBidder, price ?? Price, passed ?? Passed, bids ?? Bids, order ?? Order, step ?? Step);

        /// <summary>viewer に見せる形(本人以外の伏せた入札額を隠す)。</summary>
        public AuctionInfo ForViewer(int viewer)
        {
            if (Type != AuctionType.Sealed) return this;
            var bids = Bids.Select((b, p) => p == viewer || b < 0 ? b : Hidden);
            return With(bids: bids);
        }
    }
}
