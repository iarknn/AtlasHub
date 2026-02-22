using System.Threading;

namespace AtlasHub.Services;

public sealed class LiveEpgTickerService : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly object _gate = new();

    private Timer? _timer;

    public event Action? Tick;

    public bool IsRunning
    {
        get { lock (_gate) return _timer is not null; }
    }

    public LiveEpgTickerService()
        : this(TimeSpan.FromSeconds(30))
    {
    }

    public LiveEpgTickerService(TimeSpan interval)
    {
        _interval = interval;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null)
                return;

            _timer = new Timer(_ => Tick?.Invoke(), null, TimeSpan.Zero, _interval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}