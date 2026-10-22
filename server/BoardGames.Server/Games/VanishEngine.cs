using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;

namespace BoardGames.Server.Games;

/// <summary>ゴースト・マルバツゲーム。完全情報なので、両方の席に同じ局面を送る。</summary>
public sealed class VanishEngine : IGameEngine
{
    public string Id => "vanish";

    public IGameSession Start(int seed) => new Session(VanishState.Initial(new System.Random(seed).Next(2)));

    sealed class Session : IGameSession
    {
        readonly VanishRules _rules = new VanishRules();
        VanishState _state;

        public Session(VanishState state) => _state = state;

        public bool IsOver => _state.Result.IsOver;

        public GameResult Result => _state.Result;

        public int CurrentPlayer => _state.CurrentPlayer;

        public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
        {
            normalizedMove = null;
            if (!VanishNetProtocol.TryDecodeMove(moveMessage, out var move)) return false;
            if (_state.Result.IsOver || _state.CurrentPlayer != seat || !_rules.IsLegal(_state, move)) return false;
            _state = _rules.ApplyMove(_state, move);
            normalizedMove = VanishNetProtocol.EncodeMove(move);
            return true;
        }

        public string SnapshotFor(int seat) => VanishNetProtocol.EncodeSnapshot(_state);
    }
}
