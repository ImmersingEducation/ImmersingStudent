using ImmersingStudent.Abstractions;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;

namespace ImmersingStudent.Internal;

/// <summary>
/// 客户端共享的班级缓存。保证同一班级在多次查询中返回同一个实例。
/// </summary>
internal sealed class ClassStore
{
    private readonly IDataSource _dataSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _dirtySync = new();
    private readonly object _locksSync = new();
    private readonly Dictionary<Guid, Class> _classes = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _classLocks = [];
    private readonly HashSet<Guid> _dirty = [];
    private readonly HashSet<Guid> _pendingDeletes = [];
    private readonly HashSet<Guid> _neverSaved = [];
    private bool _allLoaded;

    public ClassStore(IDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAllLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            return _classes.Values.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Class?> FindClassAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_classes.TryGetValue(classGuid, out var cached))
            {
                return cached;
            }

            if (_allLoaded)
            {
                return null;
            }

            var loaded = await _dataSource.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                _classes[loaded.Guid] = loaded;
            }

            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Class> GetClassAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        return await FindClassAsync(classGuid, cancellationToken).ConfigureAwait(false)
               ?? throw new ClassNotFoundException(classGuid);
    }

    public async Task AddAsync(Class @class, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@class);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _classes[@class.Guid] = @class;
            lock (_dirtySync)
            {
                _neverSaved.Add(@class.Guid);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void MarkDirty(Guid classGuid)
    {
        lock (_dirtySync)
        {
            _dirty.Add(classGuid);
        }
    }

    /// <summary>
    /// 保存单个班级。调用方必须已经持有该班级的锁。
    /// </summary>
    public async Task SaveClassCoreAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        var @class = await FindClassAsync(classGuid, cancellationToken).ConfigureAwait(false)
                     ?? throw new ClassNotFoundException(classGuid);

        await _dataSource.SaveClassAsync(@class, cancellationToken).ConfigureAwait(false);

        lock (_dirtySync)
        {
            _dirty.Remove(classGuid);
            _neverSaved.Remove(classGuid);
        }
    }

    /// <summary>
    /// 保存单个班级，内部会获取该班级的锁。
    /// </summary>
    public async Task SaveClassAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
        {
            await SaveClassCoreAsync(classGuid, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveDirtyAsync(CancellationToken cancellationToken)
    {
        Guid[] dirty;
        Guid[] pendingDeletes;

        lock (_dirtySync)
        {
            dirty = _dirty.ToArray();
            pendingDeletes = _pendingDeletes.ToArray();
        }

        foreach (var classGuid in pendingDeletes)
        {
            using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
            {
                await _dataSource.DeleteClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

                lock (_dirtySync)
                {
                    _pendingDeletes.Remove(classGuid);
                    _neverSaved.Remove(classGuid);
                }
            }
        }

        foreach (var classGuid in dirty)
        {
            using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
            {
                await SaveClassCoreAsync(classGuid, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteClassAsync(
        Guid classGuid,
        bool immediate,
        CancellationToken cancellationToken)
    {
        using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
        {
            bool wasNeverSaved;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_classes.Remove(classGuid))
                {
                    throw new ClassNotFoundException(classGuid);
                }

                lock (_dirtySync)
                {
                    wasNeverSaved = _neverSaved.Remove(classGuid);
                    _dirty.Remove(classGuid);

                    if (!wasNeverSaved)
                    {
                        _pendingDeletes.Add(classGuid);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (immediate && !wasNeverSaved)
            {
                await _dataSource.DeleteClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

                lock (_dirtySync)
                {
                    _pendingDeletes.Remove(classGuid);
                }
            }
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var classes = await _dataSource.GetClassesAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _classes.Clear();
            foreach (var @class in classes)
            {
                _classes[@class.Guid] = @class;
            }

            _allLoaded = true;
        }
        finally
        {
            _gate.Release();
        }

        lock (_dirtySync)
        {
            _dirty.Clear();
            _pendingDeletes.Clear();
            _neverSaved.Clear();
        }
    }

    public async Task ReloadAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
        {
            var @class = await _dataSource.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (@class is null)
                {
                    _classes.Remove(classGuid);
                }
                else
                {
                    _classes[classGuid] = @class;
                }
            }
            finally
            {
                _gate.Release();
            }

            lock (_dirtySync)
            {
                _dirty.Remove(classGuid);
                _pendingDeletes.Remove(classGuid);
                _neverSaved.Remove(classGuid);
            }
        }
    }

    public async Task<IDisposable> LockClassAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        SemaphoreSlim semaphore;

        lock (_locksSync)
        {
            if (!_classLocks.TryGetValue(classGuid, out semaphore!))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _classLocks[classGuid] = semaphore;
            }
        }

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreReleaser(semaphore);
    }

    private async Task EnsureAllLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_allLoaded)
        {
            return;
        }

        var classes = await _dataSource.GetClassesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var @class in classes)
        {
            // 保留已经缓存的实例，避免破坏调用方持有的引用。
            _classes.TryAdd(@class.Guid, @class);
        }

        _allLoaded = true;
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
