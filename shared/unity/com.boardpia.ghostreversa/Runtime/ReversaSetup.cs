using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostReversa
{
    /// <summary>初期状態(と、テスト・CPU の推測用の任意の状態)を作る。</summary>
    public static class ReversaSetup
    {
        /// <summary>空の盤、両者とも青 3・赤 3 を持ち、先手が捨てる手から始まる状態。</summary>
        public static ReversaState Create(int firstPlayer)
        {
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            int n = ReversaBoard.PiecesPerColor;
            return new ReversaState(new Tile?[ReversaBoard.CellCount], new[] { n, n }, new[] { n, n },
                new[] { TileKind.Unknown, TileKind.Unknown }, firstPlayer, firstPlayer, 0, GamePhase.Discard,
                GameResult.Ongoing, EndReason.None, null);
        }

        /// <summary>先手をランダムに決めて始める。</summary>
        public static ReversaState CreateRandom(Random rng) => Create((rng ?? new Random()).Next(2));

        /// <summary>
        /// 任意の対局中の状態を作る(テスト用・CPU の推測用)。
        /// blue / red / discards は [P0, P1]。決着の判定はしない(Ongoing のまま)。
        /// </summary>
        public static ReversaState CreateCustom(IReadOnlyList<Tile?> cells, int[] blue, int[] red, TileKind[] discards,
            int firstPlayer, int currentPlayer, int ply, GamePhase phase = GamePhase.Play)
        {
            if (cells == null || cells.Count != ReversaBoard.CellCount) throw new ArgumentException("cells must have 9 entries");
            if (blue == null || blue.Length != 2 || red == null || red.Length != 2) throw new ArgumentException("hands");
            if (discards == null || discards.Length != 2) throw new ArgumentException("discards");
            var copy = new Tile?[ReversaBoard.CellCount];
            for (int i = 0; i < copy.Length; i++) copy[i] = cells[i];
            return new ReversaState(copy, (int[])blue.Clone(), (int[])red.Clone(), (TileKind[])discards.Clone(),
                firstPlayer, currentPlayer, ply, phase, GameResult.Ongoing, EndReason.None, null);
        }
    }
}
