using System;

namespace BoardGames.Geister
{
    /// <summary>1 手。IsEscape のとき盤外への脱出を表し、ToX/ToY は From と同じ。</summary>
    public readonly struct GeisterMove : IEquatable<GeisterMove>
    {
        public readonly int GhostId;
        public readonly int FromX;
        public readonly int FromY;
        public readonly int ToX;
        public readonly int ToY;
        public readonly bool IsEscape;

        GeisterMove(int ghostId, int fromX, int fromY, int toX, int toY, bool isEscape)
        {
            GhostId = ghostId;
            FromX = fromX;
            FromY = fromY;
            ToX = toX;
            ToY = toY;
            IsEscape = isEscape;
        }

        public static GeisterMove Step(int ghostId, int fromX, int fromY, int toX, int toY) =>
            new GeisterMove(ghostId, fromX, fromY, toX, toY, false);

        public static GeisterMove Escape(int ghostId, int x, int y) =>
            new GeisterMove(ghostId, x, y, x, y, true);

        public bool Equals(GeisterMove other) =>
            GhostId == other.GhostId && FromX == other.FromX && FromY == other.FromY &&
            ToX == other.ToX && ToY == other.ToY && IsEscape == other.IsEscape;

        public override bool Equals(object obj) => obj is GeisterMove other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(GhostId, FromX, FromY, ToX, ToY, IsEscape);

        public override string ToString() =>
            IsEscape ? $"#{GhostId} escape from ({FromX},{FromY})" : $"#{GhostId} ({FromX},{FromY})->({ToX},{ToY})";
    }
}
