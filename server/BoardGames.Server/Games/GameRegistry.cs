namespace BoardGames.Server.Games;

/// <summary>サーバーで遊べるゲームの一覧。ゲームを AWS に移したら、ここに足す。</summary>
public sealed class GameRegistry
{
    readonly Dictionary<string, IGameEngine> _engines;

    public GameRegistry(IEnumerable<IGameEngine> engines) => _engines = engines.ToDictionary(e => e.Id);

    public static GameRegistry Default() => new GameRegistry(new IGameEngine[]
    {
        new VanishEngine(),
        new GobbletEngine(),
        new QuartoEngine(),
        new GeisterEngine(),
        new BattleLineEngine(),
        new CenturyGhostEngine(),
    });

    public IGameEngine? Find(string id) => _engines.GetValueOrDefault(id);

    /// <summary>ゲームの ID の一覧（ランキングなどの API で、知らないゲームをはねるため）。</summary>
    public IReadOnlyCollection<string> Ids => _engines.Keys;
}
