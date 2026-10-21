// ゴースト・マルバツ以外の 5 本。どれも「初期局面を作る ＋ ルールで判定 ＋ NetProtocol で文字列にする」だけで、
// ルールそのものは shared/ の Logic（Unity と同じコード）を呼ぶ。
// 初期局面の乱数は種（seed）から作るので、同じ種なら必ず同じ局面になる（サーバーは毎回並べ直すため）。

using Gobblet = BoardGames.Gobblet;
using Quarto = BoardGames.Quarto;
using Geister = BoardGames.Geister;
using BattleLine = BoardGames.BattleLine;
using Century = BoardGames.CenturyGhost;

namespace BoardGames.Server.Games;

/// <summary>ゴースト・ゴブル。完全情報なので、両方の席に同じ局面を送る。</summary>
public sealed class GobbletEngine : IGameEngine
{
    static readonly Gobblet.GobbletRules Rules = new();

    public string Id => "gobblet";

    public IGameSession Start(int seed) => new RuleSession<Gobblet.GobbletState, Gobblet.GobbletMove>(
        Gobblet.GobbletState.Initial(new Random(seed).Next(2)),
        Rules, Rules.IsLegal, s => s.CurrentPlayer,
        Gobblet.Online.GobbletNetProtocol.TryDecodeMove, Gobblet.Online.GobbletNetProtocol.EncodeMove,
        (s, seat) => Gobblet.Online.GobbletNetProtocol.EncodeSnapshot(s));
}

/// <summary>ゴースト・クアルト。完全情報だが、局面は席ごとの視点で送る（形式がそうなっているため）。</summary>
public sealed class QuartoEngine : IGameEngine
{
    static readonly Quarto.QuartoRules Rules = new();

    public string Id => "quarto";

    public IGameSession Start(int seed) => new RuleSession<Quarto.QuartoState, Quarto.QuartoMove>(
        Quarto.QuartoSetup.CreateRandom(new Random(seed)),
        Rules, Rules.IsLegal, s => s.CurrentPlayer,
        Quarto.Online.QuartoNetProtocol.TryDecodeMove, Quarto.Online.QuartoNetProtocol.EncodeMove,
        (s, seat) => Quarto.Online.QuartoNetProtocol.EncodeSnapshot(Quarto.MatchSnapshot.For(s, seat)));
}

/// <summary>ゴースト・ブラフ。相手の駒の種類は、席ごとに伏せて送る。</summary>
public sealed class GeisterEngine : IGameEngine
{
    static readonly Geister.GeisterRules Rules = new();

    public string Id => "geister";

    public IGameSession Start(int seed) => new RuleSession<Geister.GeisterState, Geister.GeisterMove>(
        Geister.GeisterSetup.CreateRandom(new Random(seed)),
        Rules, Rules.IsLegal, s => s.CurrentPlayer,
        Geister.Online.GeisterNetProtocol.TryDecodeMove, Geister.Online.GeisterNetProtocol.EncodeMove,
        (s, seat) => Geister.Online.GeisterNetProtocol.EncodeSnapshot(Geister.MatchSnapshot.For(s, seat)));
}

/// <summary>ゴースト・ライン。相手の手札は枚数だけ、山札は残りの枚数だけを送る。</summary>
public sealed class BattleLineEngine : IGameEngine
{
    static readonly BattleLine.BattleLineRules Rules = new();

    public string Id => "battleline";

    public IGameSession Start(int seed) => new RuleSession<BattleLine.BattleLineState, BattleLine.BattleLineMove>(
        BattleLine.BattleLineSetup.CreateRandom(new Random(seed)),
        Rules, Rules.IsLegal, s => s.CurrentPlayer,
        BattleLine.Online.BattleLineNetProtocol.TryDecodeMove, BattleLine.Online.BattleLineNetProtocol.EncodeMove,
        (s, seat) => BattleLine.Online.BattleLineNetProtocol.EncodeSnapshot(BattleLine.MatchSnapshot.For(s, seat)));
}

/// <summary>センチュリー・ゴースト。山札 2 つの並びは送らない（枚数だけ）。</summary>
public sealed class CenturyGhostEngine : IGameEngine
{
    static readonly Century.CenturyRules Rules = new();

    public string Id => "centuryghost";

    public IGameSession Start(int seed) => new RuleSession<Century.CenturyState, Century.CenturyMove>(
        Century.CenturySetup.CreateRandom(new Random(seed)),
        Rules, Rules.IsLegal, s => s.CurrentPlayer,
        Century.Online.CenturyNetProtocol.TryDecodeMove, Century.Online.CenturyNetProtocol.EncodeMove,
        (s, seat) => Century.Online.CenturyNetProtocol.EncodeSnapshot(Century.MatchSnapshot.For(s, seat)));
}
