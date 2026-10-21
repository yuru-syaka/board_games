using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Geister
{
    /// <summary>
    /// 初期状態の生成。layout は 8 要素: [0..3] = 奥の列 x=1..4、[4..7] = 手前の列 x=1..4。
    /// P0 の奥の列は y=0、P1 の奥の列は y=5。駒 Id は P0 が 0..7、P1 が 8..15。
    /// </summary>
    public static class GeisterSetup
    {
        public static GeisterState Create(IReadOnlyList<GhostType> p0Layout, IReadOnlyList<GhostType> p1Layout,
            int firstPlayer)
        {
            Validate(p0Layout);
            Validate(p1Layout);
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));

            var ghosts = new Ghost[GeisterBoard.GhostsPerPlayer * 2];
            for (int i = 0; i < GeisterBoard.GhostsPerPlayer; i++)
            {
                int x = 1 + i % 4;
                bool back = i < 4;
                ghosts[i] = new Ghost(i, 0, p0Layout[i], x, back ? 0 : 1, GhostStatus.OnBoard);
                ghosts[i + 8] = new Ghost(i + 8, 1, p1Layout[i], x, back ? 5 : 4, GhostStatus.OnBoard);
            }

            return new GeisterState(ghosts, firstPlayer, 0, GameResult.Ongoing, EndReason.None, null, -1);
        }

        /// <summary>テストや特殊局面用: 駒を自由に置いた状態を作る。</summary>
        public static GeisterState CreateCustom(IReadOnlyList<Ghost> ghosts, int currentPlayer, int ply = 0)
        {
            var copy = new Ghost[ghosts.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = ghosts[i];
            return new GeisterState(copy, currentPlayer, ply, GameResult.Ongoing, EndReason.None, null, -1);
        }

        /// <summary>両者ランダム配置・先手ランダムの初期状態。</summary>
        public static GeisterState CreateRandom(Random rng) =>
            Create(RandomLayout(rng), RandomLayout(rng), rng.Next(2));

        public static GhostType[] RandomLayout(Random rng)
        {
            var layout = new GhostType[GeisterBoard.GhostsPerPlayer];
            for (int i = 0; i < layout.Length; i++)
                layout[i] = i < GeisterBoard.GoodPerPlayer ? GhostType.Good : GhostType.Evil;
            for (int i = layout.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (layout[i], layout[j]) = (layout[j], layout[i]);
            }
            return layout;
        }

        static void Validate(IReadOnlyList<GhostType> layout)
        {
            if (layout == null || layout.Count != GeisterBoard.GhostsPerPlayer)
                throw new ArgumentException("layout must have 8 entries");
            int good = 0, evil = 0;
            foreach (var t in layout)
            {
                if (t == GhostType.Good) good++;
                else if (t == GhostType.Evil) evil++;
            }
            if (good != GeisterBoard.GoodPerPlayer || evil != GeisterBoard.EvilPerPlayer)
                throw new ArgumentException("layout must contain 4 Good and 4 Evil");
        }
    }
}
