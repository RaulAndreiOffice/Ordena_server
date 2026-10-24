using Ordena.Frontend;

namespace Ordena.Tests.Frontend;

// One Ordena per data folder. Each later start runs on a thread of its own here, as it would be a process
// of its own: a mutex is re-entrant on the thread that holds it.
public sealed class SingleInstanceTests
{
    [Fact]
    public void Starting_again_on_the_same_data_wakes_the_open_one_and_ends()
    {
        var root = NewDataRoot();
        using var woken = new ManualResetEventSlim();
        using var first = SingleInstance.TryAcquire(root, woken.Set);

        // The same folder, spelled another way.
        var second = OnAnotherThread(() => SingleInstance.TryAcquire(root.ToUpperInvariant() + @"\", () => { }));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.True(woken.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Once_the_open_one_has_closed_the_next_start_runs()
    {
        var root = NewDataRoot();
        SingleInstance.TryAcquire(root, () => { })!.Dispose();

        Assert.True(OnAnotherThread(() => RunsAlone(root)));
    }

    // The demo data or a smoke test (ORDENA_DATA_DIR) beside the user's own Ordena.
    [Fact]
    public void Ordena_on_other_data_runs_beside_it()
    {
        using var users = SingleInstance.TryAcquire(NewDataRoot(), () => { });

        Assert.NotNull(users);
        Assert.True(OnAnotherThread(() => RunsAlone(NewDataRoot())));
    }

    private static bool RunsAlone(string root)
    {
        using var instance = SingleInstance.TryAcquire(root, () => { });
        return instance is not null;
    }

    private static string NewDataRoot() => Path.Combine(Path.GetTempPath(), "Ordena.Tests", Guid.NewGuid().ToString("N"));

    private static T OnAnotherThread<T>(Func<T> start)
    {
        T result = default!;
        var thread = new Thread(() => result = start());
        thread.Start();
        thread.Join();
        return result;
    }
}
