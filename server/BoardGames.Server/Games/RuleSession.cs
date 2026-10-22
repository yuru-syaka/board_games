using System.Diagnostics.CodeAnalysis;
using BoardGames.Core;

namespace BoardGames.Server.Games;

/// <summary>NetProtocol の TryDecodeMove の形。</summary>
public delegate bool MoveDecoder<TMove>(string message, out TMove move);

/// <summary>
/// どのゲームにも共通の「局面を持ち、手を検証して進め、席ごとの局面を返す」部分。
/// ゲームごとの違い（ルール・メッセージの形式・伏せ方）は、作るときに渡す関数で決める。
/// </summary>
public sealed class RuleSession<TState, TMove> : IGameSession
{
    readonly IGameRules<TState, TMove> _rules;
    readonly Func<TState, TMove, bool> _isLegal;
    readonly Func<TState, int> _currentPlayer;
    readonly MoveDecoder<TMove> _decodeMove;
    readonly Func<TMove, string> _encodeMove;
    readonly Func<TState, int, string> _snapshotFor;
    TState _state;

    public RuleSession(
        TState initial,
        IGameRules<TState, TMove> rules,
        Func<TState, TMove, bool> isLegal,
        Func<TState, int> currentPlayer,
        MoveDecoder<TMove> decodeMove,
        Func<TMove, string> encodeMove,
        Func<TState, int, string> snapshotFor)
    {
        _state = initial;
        _rules = rules;
        _isLegal = isLegal;
        _currentPlayer = currentPlayer;
        _decodeMove = decodeMove;
        _encodeMove = encodeMove;
        _snapshotFor = snapshotFor;
    }

    public bool IsOver => _rules.GetResult(_state).IsOver;

    public GameResult Result => _rules.GetResult(_state);

    public int CurrentPlayer => _currentPlayer(_state);

    public bool TryApply(int seat, string moveMessage, [NotNullWhen(true)] out string? normalizedMove)
    {
        normalizedMove = null;
        if (!_decodeMove(moveMessage, out var move)) return false;
        if (IsOver || _currentPlayer(_state) != seat || !_isLegal(_state, move)) return false;
        _state = _rules.ApplyMove(_state, move);
        normalizedMove = _encodeMove(move);
        return true;
    }

    public string SnapshotFor(int seat) => _snapshotFor(_state, seat);
}
