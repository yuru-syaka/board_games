namespace BoardGames.Geister
{
    public enum GhostType
    {
        Unknown,
        Good,
        Evil,
    }

    public enum GhostStatus
    {
        OnBoard,
        Captured,
        Escaped,
    }

    /// <summary>おばけ 1 体の不変データ。Id は対局中ずっと同じ。</summary>
    public readonly struct Ghost
    {
        public readonly int Id;
        public readonly int Owner;
        public readonly GhostType Type;
        public readonly int X;
        public readonly int Y;
        public readonly GhostStatus Status;

        public Ghost(int id, int owner, GhostType type, int x, int y, GhostStatus status)
        {
            Id = id;
            Owner = owner;
            Type = type;
            X = x;
            Y = y;
            Status = status;
        }

        public bool IsOnBoard => Status == GhostStatus.OnBoard;

        public Ghost MovedTo(int x, int y) => new Ghost(Id, Owner, Type, x, y, Status);
        public Ghost WithStatus(GhostStatus status) => new Ghost(Id, Owner, Type, X, Y, status);
        public Ghost WithType(GhostType type) => new Ghost(Id, Owner, type, X, Y, Status);

        public override string ToString() => $"#{Id} P{Owner} {Type} ({X},{Y}) {Status}";
    }
}
