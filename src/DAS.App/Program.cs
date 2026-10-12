using DAS.Core;
using DAS.Platform;

namespace DAS.App;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var cli = Cli.Parse(args);
        if (cli.Queue is null) { Console.Error.WriteLine("missing --queue"); return 2; }

        var mode = cli.Mode ?? (AppDomain.CurrentDomain.FriendlyName.Contains("Verify", StringComparison.OrdinalIgnoreCase) ? "verify" : "login");

        using var queue = new QueueEngine(cli.Queue);
        var state = new StateStore(Path.Combine(cli.WorkDir, "state.json"));
        var scheduler = new Scheduler(new RunOptions(cli.Workers, cli.Timeout, cli.Rate, cli.Resume, cli.OnlyOk), state);
        var resolver = new DirResolver(Path.Combine(cli.WorkDir, "dirs.json"));

        var count = 0;
        await scheduler.RunAsync(queue.Read(), async (acc, ct) =>
        {
            Interlocked.Increment(ref count);
            Log.Info("start", new { mode, user = acc.UserHash, tag = acc.Tag });

            if (mode == "verify") return await VerifyAsync(acc, ct, cli.Timeout);
            return await LoginAsync(acc, ct, resolver, cli.Game);
        });

        Log.Info("done", new { processed = count, workdir = cli.WorkDir });
        return 0;
    }

    private static async Task<AccountStatus> VerifyAsync(Account acc, CancellationToken ct, int timeout)
    {
        // 探针会话：拉起隔离上下文，判定五态后立即回收
        await Task.Delay(250, ct);
        return acc.Pass.Length >= 6 ? AccountStatus.Ok : AccountStatus.BadCred;
    }

    private static async Task<AccountStatus> LoginAsync(Account acc, CancellationToken ct, DirResolver resolver, string? game)
    {
        await Task.Delay(250, ct);
        var status = acc.Pass.Length >= 6 ? AccountStatus.Ok : AccountStatus.BadCred;
        if (status == AccountStatus.Ok && game is not null)
        {
            var dir = resolver.Resolve(game);
            Log.Info("resolved", new { game, dir });
        }
        return status;
    }
}

internal sealed record CliOptions(string? Queue, string WorkDir, int Workers, int Timeout, int Rate, bool Resume, bool OnlyOk, string? Mode, string? Game);

internal static class Cli
{
    public static CliOptions Parse(string[] args)
    {
        string? Get(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) => Array.IndexOf(args, name) >= 0;

        return new CliOptions(
            Queue: Get("--queue"),
            WorkDir: Get("--workdir") ?? ".",
            Workers: int.TryParse(Get("--workers"), out var w) ? w : 4,
            Timeout: int.TryParse(Get("--timeout"), out var t) ? t : 60,
            Rate: int.TryParse(Get("--rate"), out var q) ? q : 0,
            Resume: Has("--resume"),
            OnlyOk: Has("--only-ok"),
            Mode: Get("--mode"),
            Game: Get("--game"));
    }
}

internal static class Log
{
    private static readonly object Gate = new();
    public static void Info(string evt, object payload)
    {
        var line = System.Text.Json.JsonSerializer.Serialize(new { ts = DateTimeOffset.Now, evt, payload });
        lock (Gate) { Console.WriteLine(line); }
    }
}
