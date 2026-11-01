namespace ImmersingStudent.Internal;

/// <summary>
/// 简单的异步读写锁。普通查询/修改共享读锁，Reload 等全局操作使用写锁。
/// </summary>
internal sealed class AsyncReaderWriterLock
{
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly SemaphoreSlim _resourceGate = new(1, 1);
    private int _readers;

    public async Task<IDisposable> EnterReadAsync(CancellationToken cancellationToken)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (Interlocked.Increment(ref _readers) == 1)
            {
                try
                {
                    await _resourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Decrement(ref _readers);
                    throw;
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }

        return new Releaser(this, writer: false);
    }

    public async Task<IDisposable> EnterWriteAsync(CancellationToken cancellationToken)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _resourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _writerGate.Release();
            throw;
        }

        return new Releaser(this, writer: true);
    }

    private void ExitRead()
    {
        if (Interlocked.Decrement(ref _readers) == 0)
        {
            _resourceGate.Release();
        }
    }

    private void ExitWrite()
    {
        _resourceGate.Release();
        _writerGate.Release();
    }

    private sealed class Releaser(AsyncReaderWriterLock owner, bool writer) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (writer)
            {
                owner.ExitWrite();
            }
            else
            {
                owner.ExitRead();
            }
        }
    }
}
