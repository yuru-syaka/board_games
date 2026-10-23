using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using BoardGames.Net;
using BoardGames.Vanish;
using BoardGames.Vanish.Online;

// オンライン対戦サーバーの負荷試験（ステップ 6-2）。
//   - ルームコードで組む（片方が部屋を作り、もう片方がそのコードで入る）。本物の利用者とは組まれない
//   - 両者とも、ルール上正しい手からランダムに選び、手番のたびに think 秒（ランダム）待ってから打つ
//   - 終局したら退出する。測るもの: 組めるまでの時間、手を送ってから次の局面が届くまでの時間、エラー
// 引数: --pairs 組の数（既定 5）、--url 接続先、--think-min / --think-max 秒、--ramp 全員がつなぎ終えるまでの秒

var opts = Options.Parse(args);
Console.WriteLine($"負荷試験: {opts.Pairs} 組（{opts.Pairs * 2} 人）→ {opts.Url}  考える時間 {opts.ThinkMin}〜{opts.ThinkMax} 秒");
Console.WriteLine($"開始（UTC）: {DateTimeOffset.UtcNow:O}");

var stats = new Stats();
var total = Stopwatch.StartNew();
var tasks = Enumerable.Range(0, opts.Pairs).Select(async i =>
{
    await Task.Delay(TimeSpan.FromSeconds(opts.Ramp * i / Math.Max(1, opts.Pairs)));
    await Pair.RunAsync(i, opts, stats);
}).ToList();
await Task.WhenAll(tasks);
total.Stop();

Console.WriteLine($"終了（UTC）: {DateTimeOffset.UtcNow:O}  かかった時間 {total.Elapsed.TotalSeconds:F1} 秒");
stats.Print(opts.Pairs);
return stats.Errors.IsEmpty && stats.Completed == opts.Pairs ? 0 : 1;

sealed record Options(int Pairs, string Url, double ThinkMin, double ThinkMax, double Ramp)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        return new Options(int.Parse(Get("--pairs", "5")), Get("--url", "wss://ws.boardgamepia.com"),
            double.Parse(Get("--think-min", "0.5")), double.Parse(Get("--think-max", "1.5")), double.Parse(Get("--ramp", "5")));
    }
}

/// <summary>集計（いくつもの組から同時に書かれる）。</summary>
sealed class Stats
{
    public ConcurrentBag<double> MoveRoundTripMs { get; } = new();
    public ConcurrentBag<double> MatchMs { get; } = new();
    public ConcurrentBag<string> Errors { get; } = new();
    public ConcurrentBag<int> Plies { get; } = new();
    int _completed;
    long _sent;
    long _received;
    public int Completed => _completed;
    public void Complete() => Interlocked.Increment(ref _completed);
    public void Sent() => Interlocked.Increment(ref _sent);
    public void Received() => Interlocked.Increment(ref _received);

    public void Print(int pairs)
    {
        static string P(IEnumerable<double> values)
        {
            var v = values.OrderBy(x => x).ToArray();
            if (v.Length == 0) return "（なし）";
            double At(double q) => v[Math.Min(v.Length - 1, (int)Math.Ceiling(q * v.Length) - 1)];
            return $"{v.Length} 件  中央 {At(0.5):F0} ms  95% {At(0.95):F0} ms  最大 {v[^1]:F0} ms";
        }
        Console.WriteLine($"最後まで打てた対局: {_completed} / {pairs}（手数の合計 {Plies.Sum()}）");
        Console.WriteLine($"送ったメッセージ {_sent} 件、受け取ったメッセージ {_received} 件");
        Console.WriteLine($"組めるまで（部屋を作ってから最初の局面まで）: {P(MatchMs)}");
        Console.WriteLine($"手を送ってから次の局面まで: {P(MoveRoundTripMs)}");
        Console.WriteLine(Errors.IsEmpty ? "エラー: なし" : $"エラー: {Errors.Count} 件");
        foreach (var e in Errors.Take(20)) Console.WriteLine("  - " + e);
    }
}

/// <summary>2 人で 1 局。片方が部屋を作り、もう片方がそのルームコードで入る。</summary>
static class Pair
{
    public static async Task RunAsync(int index, Options opts, Stats stats)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var host = new Player($"#{index}A", opts, stats, new Random(index * 2 + 1));
        var guest = new Player($"#{index}B", opts, stats, new Random(index * 2 + 2));
        try
        {
            await host.ConnectAsync(timeout.Token);
            var created = Stopwatch.StartNew();
            await host.SendAsync(LobbyProtocol.Create("vanish"), timeout.Token);
            var code = await host.WaitForRoomCodeAsync(timeout.Token);

            await guest.ConnectAsync(timeout.Token);
            await guest.SendAsync(LobbyProtocol.Join("vanish", code), timeout.Token);
            var hostGame = host.PlayAsync(created, timeout.Token);
            var guestGame = guest.PlayAsync(null, timeout.Token);
            var plies = await Task.WhenAll(hostGame, guestGame);
            stats.Plies.Add(plies[0]);
            stats.Complete();
        }
        catch (Exception e)
        {
            stats.Errors.Add($"組 {index}: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            await host.CloseAsync();
            await guest.CloseAsync();
        }
    }
}

/// <summary>試験用のプレイヤー 1 人。受け取りも打つのも 1 つの流れの中で順に行う（OnlineMatch は同時に触らない）。</summary>
sealed class Player : IMatchTransport
{
    readonly string _name;
    readonly Options _opts;
    readonly Stats _stats;
    readonly Random _rng;
    readonly VanishRules _rules = new();
    readonly ClientWebSocket _ws = new();
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly Queue<string> _pending = new(); // 部屋に入る前に届いた対局のメッセージ
    Task<string?>? _receiving;
    int _joinedSeat = -1; // 部屋を作った側は、WaitForRoomCodeAsync で席を受け取る

    public event Action<string>? MessageReceived;

    public Player(string name, Options opts, Stats stats, Random rng)
    {
        _name = name;
        _opts = opts;
        _stats = stats;
        _rng = rng;
    }

    public Task ConnectAsync(CancellationToken ct) => _ws.ConnectAsync(new Uri(_opts.Url), ct);

    public async Task SendAsync(string message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);
            _stats.Sent();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // OnlineMatch から呼ばれる（手を送る）。受け取りと同じ流れの中で呼ぶので、ここで待ってよい
    void IMatchTransport.Send(string message) => SendAsync(message, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>部屋を作った側: @joined のルームコードを待つ。</summary>
    public async Task<string> WaitForRoomCodeAsync(CancellationToken ct)
    {
        while (true)
        {
            var message = await ReceiveAsync(ct) ?? throw new InvalidOperationException($"{_name}: 部屋を作る前に切れた");
            if (!LobbyProtocol.TryParse(message, out var lobby)) { _pending.Enqueue(message); continue; }
            if (lobby.Kind == LobbyMessageKind.Joined && lobby.Code != null)
            {
                _joinedSeat = lobby.Seat;
                return lobby.Code;
            }
            if (lobby.Kind == LobbyMessageKind.Error) throw new InvalidOperationException($"{_name}: @error|{lobby.ErrorCode}");
        }
    }

    /// <summary>対局を最後まで打つ。返り値は手数。</summary>
    public async Task<int> PlayAsync(Stopwatch? sinceCreate, CancellationToken ct)
    {
        OnlineMatch? match = null;
        DateTime? moveAt = null;      // 次に打つ時刻（自分の手番のとき）
        Stopwatch? sentAt = null;     // 手を送った時刻（次の局面が届くまでを測る）

        // 部屋を作った側は、席がもう決まっている
        if (_joinedSeat >= 0) match = NewMatch(_joinedSeat);
        while (true)
        {
            // 打つ時刻になっていれば打つ。そうでなければ、その時刻までメッセージを待つ
            if (match != null && moveAt is { } at && DateTime.UtcNow >= at)
            {
                moveAt = null;
                var moves = _rules.GetLegalMoves(match.State);
                sentAt = Stopwatch.StartNew();
                if (!match.SubmitMove(moves[_rng.Next(moves.Count)])) throw new InvalidOperationException($"{_name}: 手を打てなかった");
                continue;
            }
            var wait = moveAt is { } next ? next - DateTime.UtcNow : TimeSpan.FromSeconds(120);
            string? message;
            if (_pending.Count > 0) message = _pending.Dequeue();
            else
            {
                _receiving ??= ReceiveAsync(ct);
                var done = await Task.WhenAny(_receiving, Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct));
                if (done != _receiving)
                {
                    if (moveAt == null) throw new TimeoutException($"{_name}: 120 秒なにも届かない");
                    continue; // 打つ時刻になった
                }
                message = await _receiving;
                _receiving = null;
            }
            if (message == null) throw new InvalidOperationException($"{_name}: 対局の途中で切れた");

            if (LobbyProtocol.TryParse(message, out var lobby))
            {
                switch (lobby.Kind)
                {
                    case LobbyMessageKind.Joined:
                        match ??= NewMatch(lobby.Seat);
                        break;
                    case LobbyMessageKind.Error:
                        throw new InvalidOperationException($"{_name}: @error|{lobby.ErrorCode}");
                    case LobbyMessageKind.OpponentLeft:
                        if (match?.State?.Result.IsOver != true) throw new InvalidOperationException($"{_name}: 対局の途中で相手が抜けた");
                        break;
                }
                continue;
            }
            if (lobby.Kind == LobbyMessageKind.Invalid && message.StartsWith('@')) continue; // 知らないロビーのメッセージ

            // 対局のメッセージ（局面など）は OnlineMatch に渡す
            if (match == null) { _pending.Enqueue(message); continue; }
            var before = match.State;
            MessageReceived?.Invoke(message);
            if (ReferenceEquals(before, match.State)) continue; // 局面ではなかった（再戦の希望など）
            var state = match.State;
            if (before == null && sinceCreate != null) _stats.MatchMs.Add(sinceCreate.Elapsed.TotalMilliseconds);
            if (sentAt != null)
            {
                _stats.MoveRoundTripMs.Add(sentAt.Elapsed.TotalMilliseconds);
                sentAt = null;
            }
            if (state.Result.IsOver)
            {
                // 少し待ってから抜ける（相手にも最後の局面が届くように）
                await Task.Delay(500, ct);
                await SendAsync(LobbyProtocol.Leave(), ct);
                return state.Ply;
            }
            if (state.CurrentPlayer == match.LocalPlayer)
                moveAt = DateTime.UtcNow + TimeSpan.FromSeconds(_opts.ThinkMin + _rng.NextDouble() * (_opts.ThinkMax - _opts.ThinkMin));
        }
    }

    OnlineMatch NewMatch(int seat)
    {
        var match = new OnlineMatch(this, seat);
        match.ProtocolError += e => _stats.Errors.Add($"{_name}: 局面を読めない: {e}");
        return match;
    }

    /// <summary>1 つのメッセージを受け取る（切れたら null）。</summary>
    async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult r;
            try { r = await _ws.ReceiveAsync(buffer, ct); }
            catch (WebSocketException) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage)
            {
                _stats.Received();
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch (Exception) { /* もう切れている */ }
        _ws.Dispose();
    }
}
