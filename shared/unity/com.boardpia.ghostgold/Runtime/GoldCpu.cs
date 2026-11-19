using System;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostGold
{
    /// <summary>
    /// CPU。見えている情報(GoldPlayerView)から、次の 1 枚で「おどかされる」確率と、金貨が出たときの分け前の見込みを出し、
    /// 帰ったときにもらえる見込み(通路の余り・秘宝)と比べて決める。CPU ごとに慎重さ・粘り強さ・読みが違う
    /// (同じ考え方の CPU ばかりだと、同じ時に帰って金貨を同じだけ分け、引き分けが増えるため)。
    /// </summary>
    public sealed class GoldCpu : ICpuPlayer<GoldPlayerView, GoldMove>
    {
        readonly Random _rng;
        /// <summary>手持ちを失う危険を、どれだけ重く見るか(性格)。</summary>
        readonly double _caution;
        /// <summary>ほかの人が帰る見込み(この CPU の読み)。</summary>
        readonly double _othersReturn;
        /// <summary>まだ先へ進めることの値打ち(1 枚ぶんの見込みに足す。大きいほど粘る)。</summary>
        readonly double _keepGoing;

        public GoldCpu(Random rng)
        {
            _rng = rng ?? new Random();
            _caution = 0.4 + _rng.NextDouble() * 0.9;     // 0.4〜1.3
            _othersReturn = 0.2 + _rng.NextDouble() * 0.25; // 0.2〜0.45
            _keepGoing = 3.0 + _rng.NextDouble() * 2.0;   // 3〜5
        }

        public GoldMove ChooseMove(GoldPlayerView v)
        {
            var moves = v.GetLegalMoves();
            if (moves.Count == 0) throw new InvalidOperationException("no legal moves");
            return ShouldReturn(v) ? GoldMove.Return : GoldMove.Go;
        }

        bool ShouldReturn(GoldPlayerView v)
        {
            int me = v.Viewer;
            int carry = v.Carry(me);
            int others = Math.Max(0, v.ExploringCount - 1);

            // 帰ったときに、今より増える分(通路の余りの分け前と、1 人だけなら秘宝)
            double returners = 1 + others * _othersReturn;
            double alone = Math.Pow(1 - _othersReturn, others);
            double returnGain = v.Leftover / returners + v.RelicValueOnPath() * alone;

            // 進んだときの見込み(次の 1 枚)
            int deck = v.DeckCount;
            if (deck == 0) return true;
            double scare = v.ScareChance();
            var treasures = v.TreasuresInDeck();
            double treasureChance = Math.Min(1.0, (double)treasures.Count / deck);
            double avgTreasure = treasures.Count > 0 ? treasures.Average() : 0;
            double stayers = 1 + others * (1 - _othersReturn);
            int saved = v.HasAmulet(me) && carry >= 2 ? carry / 2 : 0;

            double goGain = treasureChance * avgTreasure / stayers + (1 - scare) * _keepGoing;
            double risk = scare * (carry - saved) * _caution;
            double noise = (_rng.NextDouble() - 0.5) * 3.0;

            return returnGain + risk > goGain + noise;
        }
    }
}
