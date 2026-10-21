namespace BoardGames.Server.Games;

/// <summary>サーバーで遊べるゲームの一覧。ゲームを AWS に移したら、ここに足す。</summary>
public sealed class GameRegistry
{
    readonly Dictionary<string, IGameEngine> _engines;

    public GameRegistry(IEnumerable<IGameEngine> engines) => _engines = engines.ToDictionary(e => e.Id);

    public static GameRegistry Default() => new GameRegistry(new IGameEngine[]
    {
        new VanishEngine(),
    });

    public IGameEngine? Find(string id) => _engines.GetValueOrDefault(id);
}
