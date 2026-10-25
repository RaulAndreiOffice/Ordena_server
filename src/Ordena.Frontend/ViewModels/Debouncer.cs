namespace Ordena.Frontend.ViewModels;

// Runs work after a pause (in typing, say), cancelling the call before it, so only the latest one goes on.
// The work gets a token that a newer call cancels, and checks it before showing anything.
internal sealed class Debouncer
{
    private CancellationTokenSource? _pending;

    public void Cancel() => _pending?.Cancel();

    public async Task RunAsync(TimeSpan delay, Func<CancellationToken, Task> work)
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, pending.Token);

            await work(pending.Token);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            // A newer call took over.
        }
    }
}
