using System.Collections.Generic;
using System.Linq;

namespace BoardGames.GhostTrend
{
    public enum EventKind
    {
        /// <summary>ラウンドが始まり、カードが配られた(Amount = ラウンド 0..3)。</summary>
        RoundStart,
        /// <summary>Player が Card を出品した。</summary>
        Played,
        /// <summary>Player がダブルに Card を添えた(Player が出品者になる)。</summary>
        DoubleAdded,
        /// <summary>Player がダブルに添えなかった。</summary>
        Declined,
        /// <summary>Player が Amount を付けた(公開・一巡)。</summary>
        Bid,
        /// <summary>Player が降りた / 見送った。</summary>
        Pass,
        /// <summary>Player が伏せた額を決めた(Amount は競りが終わるまで本人以外には -1)。</summary>
        SealedBid,
        /// <summary>出品者 Player が指値 Amount を決めた。</summary>
        SetPrice,
        /// <summary>Player が指値で買った。</summary>
        Accept,
        /// <summary>Player が Amount で落札した(Other = 出品者、Card / Card2 = 絵)。</summary>
        Sold,
        /// <summary>ラウンドが終わった(Amount = ラウンド。内容は RoundSummary)。</summary>
        RoundEnd,
        /// <summary>対局が終わった。</summary>
        GameEnd,
    }

    /// <summary>画面に見せる出来事(公開情報)。CPU の行動をまとめて受け取ったときも、これを順に演出する。</summary>
    public readonly struct TrendEvent
    {
        /// <summary>通し番号(0 から)。</summary>
        public readonly int Seq;
        public readonly EventKind Kind;
        public readonly int Player;
        public readonly int Card;
        public readonly int Card2;
        public readonly int Amount;
        public readonly int Other;
        /// <summary>どの競りの出来事か(伏せた入札額を隠すため)。</summary>
        public readonly int AuctionNo;

        public TrendEvent(int seq, EventKind kind, int player, int card = -1, int card2 = -1, int amount = 0, int other = -1,
            int auctionNo = 0)
        {
            Seq = seq;
            Kind = kind;
            Player = player;
            Card = card;
            Card2 = card2;
            Amount = amount;
            Other = other;
            AuctionNo = auctionNo;
        }

        public TrendEvent WithAmount(int amount) => new TrendEvent(Seq, Kind, Player, Card, Card2, amount, Other, AuctionNo);

        public override string ToString() => $"#{Seq} {Kind} p{Player} c{Card}/{Card2} a{Amount} o{Other}";
    }

    /// <summary>1 ラウンドの精算(公開情報)。</summary>
    public sealed class RoundSummary
    {
        public int Round { get; }
        /// <summary>画家ごとの、このラウンドに出された枚数。</summary>
        public IReadOnlyList<int> Counts { get; }
        /// <summary>画家ごとの順位(0..2、圏外は -1)。</summary>
        public IReadOnlyList<int> Ranks { get; }
        /// <summary>画家ごとの、このラウンドで売れた 1 枚の値段(圏外は 0)。</summary>
        public IReadOnlyList<int> CardValues { get; }
        /// <summary>プレイヤーごとの売上。</summary>
        public IReadOnlyList<int> Payouts { get; }

        public RoundSummary(int round, IEnumerable<int> counts, IEnumerable<int> ranks, IEnumerable<int> cardValues,
            IEnumerable<int> payouts)
        {
            Round = round;
            Counts = counts.ToArray();
            Ranks = ranks.ToArray();
            CardValues = cardValues.ToArray();
            Payouts = payouts.ToArray();
        }
    }
}
