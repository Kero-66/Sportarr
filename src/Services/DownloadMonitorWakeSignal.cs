namespace Sportarr.Api.Services;

public sealed class DownloadMonitorWakeSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestCheck()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending check covers concurrent notifications.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);

    public void ClearPending() => _signal.Wait(0);

    public void Dispose() => _signal.Dispose();
}
