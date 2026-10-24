using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Ordena.Frontend;

// One Ordena per data folder: starting it again brings the open one to the front instead of opening a
// second window on the same database. Per data folder rather than per user, so a copy running on other
// data (ORDENA_DATA_DIR: the demo data, the smoke tests) never meets the user's own.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _running;
    private readonly EventWaitHandle _wakeUp;
    private readonly RegisteredWaitHandle _waiting;

    private SingleInstance(Mutex running, EventWaitHandle wakeUp, Action onStartedAgain)
    {
        _running = running;
        _wakeUp = wakeUp;
        _waiting = ThreadPool.RegisterWaitForSingleObject(
            wakeUp, (_, _) => onStartedAgain(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // The first start gets the instance and keeps it until the app ends; onStartedAgain then runs on a pool
    // thread each time Ordena is started again. A later start gets null, after waking the first one.
    // Take it and dispose of it on the same thread: a mutex belongs to the thread that took it.
    public static SingleInstance? TryAcquire(string dataRoot, Action onStartedAgain)
    {
        var name = Name(dataRoot);
        var running = new Mutex(initiallyOwned: false, name + "-running");
        var wakeUp = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show");
        bool owned;
        try
        {
            owned = running.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the last one crashed without letting go
        }

        if (owned)
            return new SingleInstance(running, wakeUp, onStartedAgain);

        // The one that is open may come to the front: it was started by the user just now, this one wasn't.
        AllowSetForegroundWindow(AnyProcess);
        wakeUp.Set();
        wakeUp.Dispose();
        running.Dispose();
        return null;
    }

    public void Dispose()
    {
        _waiting.Unregister(null);
        _wakeUp.Dispose();
        _running.ReleaseMutex();
        _running.Dispose();
    }

    // "Local\Ordena-1A2B…": this Windows session, this data folder, whatever its spelling.
    private static string Name(string dataRoot)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
        return @"Local\Ordena-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
