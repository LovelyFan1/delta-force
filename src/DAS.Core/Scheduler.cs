using System.Collections.Concurrent;

namespace DAS.Core;

public sealed record RunOptions(int Workers = 4, int TimeoutSec = 60, int RateQps = 0, bool Resume = false, bool OnlyOk = false);

/// <summary>令牌桶 + 有限并发；单账号失败不影响队列。</summary>
public sealed class Scheduler
{
    private readonly RunOptions _opt;
    private readonly SemaphoreSlim _gate;
    private readonly StateStore _state;
    private readonly SemaphoreSlim? _rate;

    public Scheduler(RunOptions opt, StateStore state)
    {
        _opt = opt;
        _state = state;
        _gate = new SemaphoreSlim(opt.Workers, opt.Workers);
        _rate = opt.RateQps > 0 ? new SemaphoreSlim(opt.RateQps, opt.RateQps) : null;
    }

    public async Task RunAsync(IEnumerable<Account> accounts, Func<Account, CancellationToken, Task<AccountStatus>> work)
    {
        var tasks = new ConcurrentBag<Task>();

        foreach (var acc in accounts)
        {
            if (_opt.Resume && _state.IsCompleted(acc.UserHash)) continue;

            await _gate.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (_rate is not null) { await _rate.WaitAsync(); _ = Task.Delay(1000).ContinueWith(_ => _rate.Release()); }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_opt.TimeoutSec));
                    var status = await work(acc, cts.Token);
                    _state.Set(acc.UserHash, status, 1);
                }
                catch (OperationCanceledException) { _state.Set(acc.UserHash, AccountStatus.Unknown, 1, "timeout"); }
                catch (Exception ex) { _state.Set(acc.UserHash, AccountStatus.Unknown, 1, ex.Message); }
                finally { _gate.Release(); }
            }));
        }

        await Task.WhenAll(tasks);
    }
}
