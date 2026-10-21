using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.Quarto
{
    /// <summary>初期状態・特殊局面の生成。</summary>
    public static class QuartoSetup
    {
        /// <summary>空の盤。先手 firstPlayer が最初に駒を渡す。</summary>
        public static QuartoState Create(int firstPlayer)
        {
            if (firstPlayer != 0 && firstPlayer != 1) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            var cells = new int[QuartoBoard.CellCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = QuartoBoard.Empty;
            return new QuartoState(cells, firstPlayer, TurnPhase.Give, -1, 0, GameResult.Ongoing, EndReason.None, null, null);
        }

        /// <summary>先手ランダムの初期状態。</summary>
        public static QuartoState CreateRandom(Random rng) => Create(rng.Next(2));

        /// <summary>
        /// テストや特殊局面用: 盤と手もちを自由に指定した対局中の状態を作る。
        /// cells は 16 要素(マス番号 = y * 4 + x、空きは -1)。phase == Place のときは pieceInHand が必要。
        /// </summary>
        public static QuartoState CreateCustom(IReadOnlyList<int> cells, int currentPlayer, TurnPhase phase, int pieceInHand = -1,
            int ply = -1)
        {
            if (!IsConsistent(cells, currentPlayer, phase, pieceInHand))
                throw new ArgumentException("inconsistent position");
            var copy = Copy(cells);
            int placed = CountPlaced(copy);
            // 行動は 渡す → 置く → 渡す … の順なので、k 個置いた後の「渡す」待ちは 2k 手目、「置く」待ちは 2k+1 手目
            if (ply < 0) ply = placed * 2 + (phase == TurnPhase.Place ? 1 : 0);
            return new QuartoState(copy, currentPlayer, phase, pieceInHand, ply, GameResult.Ongoing, EndReason.None, null, null);
        }

        /// <summary>
        /// 通信で受け取った値から局面を組み立てる。矛盾があれば false。
        /// そろったラインは盤から計算し直す。
        /// </summary>
        public static bool TryRestore(IReadOnlyList<int> cells, int currentPlayer, TurnPhase phase, int pieceInHand, int ply,
            GameResult result, EndReason reason, QuartoMove? lastMove, out QuartoState state)
        {
            state = null;
            if (ply < 0 || ply > QuartoBoard.CellCount * 2) return false;
            if (!result.IsOver && !IsConsistent(cells, currentPlayer, phase, pieceInHand)) return false;
            if (result.IsOver && (cells == null || cells.Count != QuartoBoard.CellCount || !AllUnique(cells))) return false;
            if ((reason == EndReason.None) != !result.IsOver) return false;

            var copy = Copy(cells);
            var lines = QuartoBoard.FindWinningLines(copy);
            if (reason == EndReason.Quarto && (lines.Count == 0 || result.Status != GameStatus.Win)) return false;
            if (reason == EndReason.BoardFull && (lines.Count > 0 || CountPlaced(copy) != QuartoBoard.CellCount)) return false;
            if (reason == EndReason.None && lines.Count > 0) return false;

            state = new QuartoState(copy, currentPlayer, phase, pieceInHand, ply, result, reason, lastMove, lines.ToArray());
            return true;
        }

        static bool IsConsistent(IReadOnlyList<int> cells, int currentPlayer, TurnPhase phase, int pieceInHand)
        {
            if (cells == null || cells.Count != QuartoBoard.CellCount) return false;
            if (currentPlayer != 0 && currentPlayer != 1) return false;
            if (!AllUnique(cells)) return false;
            if (QuartoBoard.FindWinningLines(cells).Count > 0) return false;
            int placed = 0;
            foreach (int p in cells)
                if (p >= 0) placed++;
            if (phase == TurnPhase.Place)
            {
                if (!QuartoPiece.IsValid(pieceInHand) || placed == QuartoBoard.CellCount) return false;
                foreach (int p in cells)
                    if (p == pieceInHand) return false;
            }
            else
            {
                if (pieceInHand != -1 || placed == QuartoBoard.CellCount) return false;
            }
            return true;
        }

        static bool AllUnique(IReadOnlyList<int> cells)
        {
            int seen = 0;
            foreach (int p in cells)
            {
                if (p == QuartoBoard.Empty) continue;
                if (!QuartoPiece.IsValid(p) || (seen & (1 << p)) != 0) return false;
                seen |= 1 << p;
            }
            return true;
        }

        static int[] Copy(IReadOnlyList<int> cells)
        {
            var copy = new int[QuartoBoard.CellCount];
            for (int i = 0; i < copy.Length; i++) copy[i] = cells[i];
            return copy;
        }

        static int CountPlaced(int[] cells)
        {
            int n = 0;
            foreach (int p in cells)
                if (p >= 0) n++;
            return n;
        }
    }
}
