using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostReversa
{
    /// <summary>
    /// ゴースト・リバーサのルール。仕様は docs/games/ghostreversa.md(Anomie プロジェクト)。
    /// 判定の順: 指した人の同色 3 体表 → 相手の同色 3 体表 → 両者とも置けない → 手数の上限。
    /// </summary>
    public sealed class ReversaRules : IGameRules<ReversaState, ReversaMove>
    {
        public IReadOnlyList<ReversaMove> GetLegalMoves(ReversaState state) => state.GetLegalMoves(state.CurrentPlayer);

        public bool IsLegal(ReversaState state, ReversaMove move) =>
            state != null && state.GetLegalMoves(state.CurrentPlayer).Contains(move);

        public GameResult GetResult(ReversaState state) => state.Result;

        public ReversaState ApplyMove(ReversaState state, ReversaMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException($"Illegal move {move}", nameof(move));

            int p = state.CurrentPlayer;
            int opp = 1 - p;
            var cells = state.CopyCells();
            var blue = state.CopyBlue();
            var red = state.CopyRed();
            var discards = state.CopyDiscards();
            int ply = state.Ply + 1;

            switch (move.Kind)
            {
                case MoveKind.Discard:
                {
                    Take(blue, red, p, move.Piece);
                    discards[p] = move.Piece;
                    bool bothDone = discards[opp] != TileKind.Unknown;
                    return new ReversaState(cells, blue, red, discards, state.FirstPlayer,
                        bothDone ? state.FirstPlayer : opp, ply, bothDone ? GamePhase.Play : GamePhase.Discard,
                        GameResult.Ongoing, EndReason.None, move);
                }

                case MoveKind.Pass:
                    return Finish(cells, blue, red, discards, state.FirstPlayer, opp, ply, GameResult.Ongoing, EndReason.None, move);

                default:
                {
                    ReversaBoard.ApplyPlacement(cells, p, move, out var returned);
                    if (returned.HasValue) Give(blue, red, returned.Value.Owner, returned.Value.Kind);
                    if (move.Piece != TileKind.Poltergeist) Take(blue, red, p, move.Piece);

                    if (ReversaBoard.HasWinningSet(cells, p))
                        return Finish(cells, blue, red, discards, state.FirstPlayer, opp, ply, GameResult.WinFor(p), EndReason.Completed, move);
                    if (ReversaBoard.HasWinningSet(cells, opp))
                        return Finish(cells, blue, red, discards, state.FirstPlayer, opp, ply, GameResult.WinFor(opp), EndReason.Gifted, move);

                    bool oppCan = ReversaBoard.HasPlacement(cells, opp, blue[opp], red[opp]);
                    bool selfCan = ReversaBoard.HasPlacement(cells, p, blue[p], red[p]);
                    if (!oppCan && !selfCan)
                        return Finish(cells, blue, red, discards, state.FirstPlayer, opp, ply, GameResult.Draw, EndReason.NoMoves, move);

                    // 相手が置けないときは、相手の合法手がパスだけになる(置けるのは自分なので、パスが続くことはない)
                    return Finish(cells, blue, red, discards, state.FirstPlayer, opp, ply, GameResult.Ongoing, EndReason.None, move);
                }
            }
        }

        static ReversaState Finish(Tile?[] cells, int[] blue, int[] red, TileKind[] discards, int first, int next, int ply,
            GameResult result, EndReason reason, ReversaMove move)
        {
            if (!result.IsOver && ply >= ReversaBoard.MaxPlies)
            {
                result = GameResult.Draw;
                reason = EndReason.TurnLimit;
            }
            return new ReversaState(cells, blue, red, discards, first, next, ply, GamePhase.Play, result, reason, move);
        }

        static void Take(int[] blue, int[] red, int player, TileKind kind)
        {
            if (kind == TileKind.Blue) blue[player]--;
            else if (kind == TileKind.Red) red[player]--;
        }

        static void Give(int[] blue, int[] red, int player, TileKind kind)
        {
            if (kind == TileKind.Blue) blue[player]++;
            else if (kind == TileKind.Red) red[player]++;
        }
    }
}
