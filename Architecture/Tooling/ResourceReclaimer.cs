namespace LibmpvIptvClient.Architecture.Tooling;

public sealed class ResourceReclaimer
{
    private readonly List<WeakReference<IDisposable>> _tracked = new();

    public void Track(IDisposable disposable)
    {
        _tracked.Add(new WeakReference<IDisposable>(disposable));
    }

    public int Reclaim()
    {
        var released = 0;
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            if (!_tracked[i].TryGetTarget(out var target))
            {
                _tracked.RemoveAt(i);
                released++;
                continue;
            }

            try
            {
                target.Dispose();
                _tracked.RemoveAt(i);
                released++;
            }
            catch
            {
            }
        }

        return released;
    }
}
