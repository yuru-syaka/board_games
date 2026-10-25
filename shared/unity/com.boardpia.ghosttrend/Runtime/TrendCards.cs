using System;
using System.Collections.Generic;

namespace BoardGames.GhostTrend
{
    /// <summary>競りの種類。</summary>
    public enum AuctionType
    {
        /// <summary>公開競り: 順に値を上げるか降りる。手番が最高額の人に戻ったら落札。</summary>
        Open,
        /// <summary>一巡競り: 1 人 1 回だけ値を付ける。出品者は最後。</summary>
        OnceAround,
        /// <summary>入札: 全員が伏せた額を 1 回ずつ決め、まとめて公開する。</summary>
        Sealed,
        /// <summary>指値: 出品者が値段を決め、順に買うか見送る。</summary>
        Fixed,
        /// <summary>ダブル: 同じ画家のカードをもう 1 枚添えて、2 枚まとめて競りにかける。</summary>
        Double,
    }

    /// <summary>
    /// カードの表(70 枚)。カード ID は 0..69 で、画家の順・競りの種類の順に並べた固定の表で決まる。
    /// 画家は番号が小さいほど枚数が少なく、同数のときに上位になる。
    /// </summary>
    public static class TrendCards
    {
        public const int ArtistCount = 5;
        public const int AuctionTypeCount = 5;

        /// <summary>画家ごと・競りの種類ごとの枚数([画家, 種類])。</summary>
        static readonly int[,] Distribution =
        {
            // 公開 一巡 入札 指値 ダブル
            { 3, 2, 2, 3, 2 }, // 0 ルミナ 12
            { 3, 3, 2, 3, 2 }, // 1 ヨミ   13
            { 3, 3, 3, 3, 2 }, // 2 クロウ 14
            { 3, 3, 3, 3, 3 }, // 3 シオン 15
            { 4, 3, 3, 3, 3 }, // 4 ネオ   16
        };

        static readonly int[] ArtistOfCard;
        static readonly AuctionType[] TypeOfCard;

        public static int Count => ArtistOfCard.Length;

        static TrendCards()
        {
            var artists = new List<int>();
            var types = new List<AuctionType>();
            for (int a = 0; a < ArtistCount; a++)
            for (int t = 0; t < AuctionTypeCount; t++)
            for (int i = 0; i < Distribution[a, t]; i++)
            {
                artists.Add(a);
                types.Add((AuctionType)t);
            }
            ArtistOfCard = artists.ToArray();
            TypeOfCard = types.ToArray();
        }

        public static bool IsValid(int card) => card >= 0 && card < ArtistOfCard.Length;

        public static int ArtistOf(int card)
        {
            if (!IsValid(card)) throw new ArgumentOutOfRangeException(nameof(card));
            return ArtistOfCard[card];
        }

        public static AuctionType TypeOf(int card)
        {
            if (!IsValid(card)) throw new ArgumentOutOfRangeException(nameof(card));
            return TypeOfCard[card];
        }

        /// <summary>その画家のカードの総枚数。</summary>
        public static int CountOf(int artist)
        {
            int n = 0;
            for (int t = 0; t < AuctionTypeCount; t++) n += Distribution[artist, t];
            return n;
        }

        /// <summary>画家・種類からカードを 1 枚探す(テスト用)。nth は同じ組み合わせの何枚目か。</summary>
        public static int Find(int artist, AuctionType type, int nth = 0)
        {
            for (int c = 0; c < ArtistOfCard.Length; c++)
            {
                if (ArtistOfCard[c] != artist || TypeOfCard[c] != type) continue;
                if (nth-- == 0) return c;
            }
            throw new ArgumentException($"no card artist={artist} type={type}");
        }

        public static IEnumerable<int> AllCards()
        {
            for (int c = 0; c < ArtistOfCard.Length; c++) yield return c;
        }
    }

    /// <summary>ゲーム全体の決まった数。</summary>
    public static class TrendLogic
    {
        public const int MinPlayers = 3;
        public const int MaxPlayers = 5;
        public const int Rounds = 4;
        public const int InitialMoney = 100;
        /// <summary>この枚数目が出されたらラウンドが終わる。</summary>
        public const int RoundEndCount = 5;
        /// <summary>順位ごとに足される価値(1 位, 2 位, 3 位)。</summary>
        public static readonly int[] RankValues = { 30, 20, 10 };

        static readonly int[][] DealTable =
        {
            new[] { 10, 6, 6, 0 }, // 3 人
            new[] { 9, 4, 4, 0 },  // 4 人
            new[] { 8, 3, 3, 0 },  // 5 人
        };

        public static bool IsValidPlayerCount(int n) => n >= MinPlayers && n <= MaxPlayers;

        /// <summary>そのラウンドの始めに 1 人に配る枚数。</summary>
        public static int CardsToDeal(int playerCount, int round)
        {
            if (!IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            return round >= 0 && round < Rounds ? DealTable[playerCount - MinPlayers][round] : 0;
        }

        /// <summary>
        /// ラウンドで出された枚数から画家の順位を付ける。戻り値は画家ごとの順位(0..2)で、3 位以内に入らなければ -1。
        /// 同数なら番号の小さい画家が上。0 枚の画家は順位に入らない。
        /// </summary>
        public static int[] RankArtists(IReadOnlyList<int> counts)
        {
            var order = new List<int>();
            for (int a = 0; a < TrendCards.ArtistCount; a++)
                if (counts[a] > 0) order.Add(a);
            order.Sort((x, y) => counts[x] != counts[y] ? counts[y].CompareTo(counts[x]) : x.CompareTo(y));
            var rank = new int[TrendCards.ArtistCount];
            for (int a = 0; a < rank.Length; a++) rank[a] = -1;
            for (int i = 0; i < order.Count && i < RankValues.Length; i++) rank[order[i]] = i;
            return rank;
        }

        /// <summary>p の左隣(番号 +1)。</summary>
        public static int Next(int p, int playerCount) => (p + 1) % playerCount;
    }
}
