namespace Ordena.Tests.TestSupport;

// Reports on the calling thread, at the moment of the call. Progress<T> posts to the thread pool,
// which would make a test that cancels "after the second file" depend on timing.
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
