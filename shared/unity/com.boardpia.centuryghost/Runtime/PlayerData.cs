using System.Collections.Generic;
using System.Linq;

namespace BoardGames.CenturyGhost
{
    /// <summary>プレイヤー 1 人分の公開情報(不変)。</summary>
    public sealed class PlayerData
    {
        public Spirits Spirits { get; }
        /// <summary>使える手札(カード Id)。</summary>
        public IReadOnlyList<int> Hand { get; }
        /// <summary>使用済みのカード Id。</summary>
        public IReadOnlyList<int> Played { get; }
        /// <summary>呼んだおばけ Id(呼んだ順)。</summary>
        public IReadOnlyList<int> Ghosts { get; }
        public int Gold { get; }
        public int Silver { get; }

        public PlayerData(Spirits spirits, IEnumerable<int> hand, IEnumerable<int> played, IEnumerable<int> ghosts, int gold, int silver)
        {
            Spirits = spirits;
            Hand = hand.ToArray();
            Played = played.ToArray();
            Ghosts = ghosts.ToArray();
            Gold = gold;
            Silver = silver;
        }

        public int GhostPoints => Ghosts.Sum(id => CardCatalog.Ghost(id).Points);
        public int CoinPoints => Gold * CenturyLogic.GoldValue + Silver * CenturyLogic.SilverValue;
        public int Score => GhostPoints + CoinPoints + Spirits.Score;
        public int CardCount => Hand.Count + Played.Count;

        public PlayerData WithSpirits(Spirits s) => new PlayerData(s, Hand, Played, Ghosts, Gold, Silver);

        public override string ToString() =>
            $"{Spirits} hand[{string.Join(",", Hand)}] played[{string.Join(",", Played)}] ghosts[{string.Join(",", Ghosts)}] g{Gold}s{Silver}";
    }

    /// <summary>場の公開情報(市場・市場に置かれた魂・おばけの列・残りコイン)。不変。</summary>
    public sealed class Table
    {
        public IReadOnlyList<int> Market { get; }
        /// <summary>市場の各カードに置かれた魂(Market と同じ長さ)。</summary>
        public IReadOnlyList<Spirits> MarketSpirits { get; }
        public IReadOnlyList<int> GhostRow { get; }
        public int GoldLeft { get; }
        public int SilverLeft { get; }

        public Table(IEnumerable<int> market, IEnumerable<Spirits> marketSpirits, IEnumerable<int> ghostRow, int goldLeft, int silverLeft)
        {
            Market = market.ToArray();
            MarketSpirits = marketSpirits.ToArray();
            GhostRow = ghostRow.ToArray();
            GoldLeft = goldLeft;
            SilverLeft = silverLeft;
            if (Market.Count != MarketSpirits.Count) throw new System.ArgumentException("Market / spirits length mismatch");
        }

        /// <summary>おばけの列の index 番目を取ったときにもらえるコイン(金, 銀)。</summary>
        public (bool gold, bool silver) BonusAt(int index)
        {
            if (index == 0)
            {
                if (GoldLeft > 0) return (true, false);
                if (SilverLeft > 0) return (false, true);
            }
            else if (index == 1 && GoldLeft > 0 && SilverLeft > 0)
            {
                return (false, true);
            }
            return (false, false);
        }
    }
}
