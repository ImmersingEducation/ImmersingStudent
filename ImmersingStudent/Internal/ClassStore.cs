using ImmersingEducation.Infrastructures.ImmersingStudent.Abstractions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Internal;

/// <summary>
/// 服务共享的班级缓存。保证同一班级在多次查询中返回同一个实例。
/// </summary>
internal sealed class ClassStore
{
    private readonly IDataSource _dataSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncReaderWriterLock _stateLock = new();
    private readonly object _dirtySync = new();
    private readonly object _locksSync = new();
    private readonly Dictionary<Guid, Class> _classes = [];
    private readonly Dictionary<Guid, LockEntry> _classLocks = [];
    private readonly HashSet<Guid> _dirty = [];
    private readonly HashSet<Guid> _pendingDeletes = [];
    private readonly HashSet<Guid> _neverSaved = [];
    private bool _allLoaded;

    public ClassStore(IDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <summary>
    /// 获取共享读锁。查询操作可以并发进行。
    /// </summary>
    public Task<IDisposable> LockReadAsync(CancellationToken cancellationToken)
    {
        return _stateLock.EnterReadAsync(cancellationToken);
    }

    /// <summary>
    /// 获取全局写锁。修改、Reload 等需要独占缓存的操作使用。
    /// </summary>
    public Task<IDisposable> LockWriteAsync(CancellationToken cancellationToken)
    {
        return _stateLock.EnterWriteAsync(cancellationToken);
    }

    public bool HasDirtyClasses
    {
        get
        {
            lock (_dirtySync)
            {
                return _dirty.Count > 0;
            }
        }
    }

    internal int ClassLockCount
    {
        get
        {
            lock (_locksSync)
            {
                return _classLocks.Count;
            }
        }
    }

    public async Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAllLoadedCoreAsync(cancellationToken).ConfigureAwait(false);

            HashSet<Guid> pendingDeletes;
            lock (_dirtySync)
            {
                pendingDeletes = [.. _pendingDeletes];
            }

            return _classes.Values
                .Where(@class => !pendingDeletes.Contains(@class.Guid))
                .ToList();
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
            if (IsPendingDelete(classGuid))
            {
                return null;
            }

            if (_classes.TryGetValue(classGuid, out var cached))
            {
                return cached;
            }

            if (_allLoaded)
            {
                return null;
            }

            var loaded = await _dataSource.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            if (loaded is null || IsPendingDelete(loaded.Guid))
            {
                return null;
            }

            _classes[loaded.Guid] = loaded;
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
                // 复用 Guid 时，旧班级的待删除状态应当失效。
                _pendingDeletes.Remove(@class.Guid);
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
        Guid[] pendingDeletes;
        lock (_dirtySync)
        {
            pendingDeletes = [.. _pendingDeletes];
        }

        foreach (var classGuid in pendingDeletes)
        {
            using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
            {
                lock (_dirtySync)
                {
                    // 在等待班级锁期间可能已经被其他 SaveAsync 处理，或重新加入了班级。
                    if (!_pendingDeletes.Contains(classGuid))
                    {
                        continue;
                    }
                }

                await _dataSource.DeleteClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // 防止删除失败/取消后再次被数据源重新加载进缓存。
                    _classes.Remove(classGuid);
                }
                finally
                {
                    _gate.Release();
                }

                lock (_dirtySync)
                {
                    _pendingDeletes.Remove(classGuid);
                    _neverSaved.Remove(classGuid);
                    _dirty.Remove(classGuid);
                }
            }
        }

        // 删除处理完后再取快照，避免把刚刚删除的班级又保存回去。
        Guid[] dirty;
        lock (_dirtySync)
        {
            dirty = [.. _dirty];
        }

        foreach (var classGuid in dirty)
        {
            using (await LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
            {
                lock (_dirtySync)
                {
                    // 等待班级锁期间可能被删除，或已被其他 SaveAsync 保存。
                    if (_pendingDeletes.Contains(classGuid) || !_dirty.Contains(classGuid))
                    {
                        continue;
                    }
                }

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

                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    _classes.Remove(classGuid);
                }
                finally
                {
                    _gate.Release();
                }

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
        LockEntry entry;

        lock (_locksSync)
        {
            if (!_classLocks.TryGetValue(classGuid, out entry!))
            {
                entry = new LockEntry();
                _classLocks[classGuid] = entry;
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseEntry(classGuid, entry);
            throw;
        }

        return new ClassLockReleaser(this, classGuid, entry);
    }

    private void ReleaseClassLock(Guid classGuid, LockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseEntry(classGuid, entry);
    }

    private void ReleaseEntry(Guid classGuid, LockEntry entry)
    {
        var dispose = false;

        lock (_locksSync)
        {
            entry.References--;

            if (entry.References == 0
                && _classLocks.TryGetValue(classGuid, out var current)
                && ReferenceEquals(current, entry))
            {
                _classLocks.Remove(classGuid);
                dispose = true;
            }
        }

        if (dispose)
        {
            entry.Semaphore.Dispose();
        }
    }

    private bool IsPendingDelete(Guid classGuid)
    {
        lock (_dirtySync)
        {
            return _pendingDeletes.Contains(classGuid);
        }
    }

    private async Task EnsureAllLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_allLoaded)
        {
            return;
        }

        var classes = await _dataSource.GetClassesAsync(cancellationToken).ConfigureAwait(false);

        HashSet<Guid> pendingDeletes;
        lock (_dirtySync)
        {
            pendingDeletes = [.. _pendingDeletes];
        }

        foreach (var @class in classes)
        {
            if (pendingDeletes.Contains(@class.Guid))
            {
                continue;
            }

            // 保留已经缓存的实例，避免破坏调用方持有的引用。
            _classes.TryAdd(@class.Guid, @class);
        }

        foreach (var classGuid in pendingDeletes)
        {
            // 清理可能因为旧版本逻辑残留的已删除班级。
            _classes.Remove(classGuid);
        }

        _allLoaded = true;
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class ClassLockReleaser(ClassStore owner, Guid classGuid, LockEntry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ReleaseClassLock(classGuid, entry);
            }
        }
    }
}
