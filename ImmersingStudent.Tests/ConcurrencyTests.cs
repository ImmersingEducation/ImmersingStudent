using ImmersingStudent.Enums;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Internal;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task ConcurrentCreateClassAsync_SameName_OnlyOneSucceeds()
    {
        var client = TestData.CreateClient(out _);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try
            {
                await client.CreateClassAsync(Grade.FirstGrade, "1班");
                return true;
            }
            catch (ValidationException)
            {
                return false;
            }
        }));

        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Single(await client.ReadOnly.GetClassesAsync());
    }

    [Fact]
    public async Task ConcurrentRenameClassAsync_ToSameName_OnlyOneSucceeds()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "2班");
        var client = new StudentService(new FakeDataSource(first, second));

        var outcomes = await Task.WhenAll(
            TryRenameAsync(client, first.Guid, "3班"),
            TryRenameAsync(client, second.Guid, "3班"));

        Assert.Equal(1, outcomes.Count(exception => exception is null));
        Assert.Equal(1, outcomes.Count(exception => exception is ValidationException));
    }

    [Fact]
    public async Task ConcurrentMutations_OnDifferentClasses_AllSucceed()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "2班");
        var client = new StudentService(new FakeDataSource(first, second));
        var cachedFirst = await client.ReadOnly.GetClassAsync(first.Guid);
        var cachedSecond = await client.ReadOnly.GetClassAsync(second.Guid);

        var firstTasks = Enumerable.Range(0, 40)
            .Select(index => client.AddStudentAsync(
                cachedFirst.Guid,
                $"a{index}",
                $"A{index}",
                Gender.Male));
        var secondTasks = Enumerable.Range(0, 40)
            .Select(index => client.AddStudentAsync(
                cachedSecond.Guid,
                $"b{index}",
                $"B{index}",
                Gender.Female));

        await Task.WhenAll(firstTasks.Concat(secondTasks));

        Assert.Equal(40, cachedFirst.Students.Count);
        Assert.Equal(40, cachedSecond.Students.Count);
        Assert.Equal(40, cachedFirst.Students.Select(student => student.IdInClass).Distinct().Count());
        Assert.Equal(40, cachedSecond.Students.Select(student => student.IdInClass).Distinct().Count());
    }

    [Fact]
    public async Task DeleteAndSaveConcurrently_StillEndsDeleted()
    {
        for (var index = 0; index < 20; index++)
        {
            var (client, dataSource, @class) = await TestData.CreateClientAsync();

            await Task.WhenAll(
                client.DeleteClassAsync(@class.Guid),
                client.SaveAsync());

            await client.SaveAsync();

            Assert.False(dataSource.Contains(@class.Guid));
            Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
            Assert.Empty(await client.ReadOnly.GetClassesAsync());
        }
    }

    [Fact]
    public async Task ReloadConcurrentWithMutationsAndQueries_Completes()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var tasks = new List<Task>();
        for (var index = 0; index < 10; index++)
        {
            tasks.Add(client.AddStudentAsync(
                @class.Guid,
                $"s{index}",
                $"学生{index}",
                Gender.Male,
                cancellationToken: timeout.Token));
            tasks.Add(client.SaveAsync(timeout.Token));
            tasks.Add(client.ReloadAsync(timeout.Token));
            tasks.Add(client.ReadOnly.GetClassesAsync(timeout.Token));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ReadLocks_AllowConcurrentReaders()
    {
        var readerWriterLock = new AsyncReaderWriterLock();

        using var first = await readerWriterLock.EnterReadAsync(CancellationToken.None);
        using var second = await readerWriterLock
            .EnterReadAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLock_ExcludesReaders()
    {
        var readerWriterLock = new AsyncReaderWriterLock();
        Task<IDisposable> readTask;

        using (await readerWriterLock.EnterWriteAsync(CancellationToken.None))
        {
            readTask = readerWriterLock.EnterReadAsync(CancellationToken.None);
            Assert.False(readTask.IsCompleted);
        }

        using var read = await readTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WriteLock_WaitsForExistingReaders()
    {
        var readerWriterLock = new AsyncReaderWriterLock();
        var read = await readerWriterLock.EnterReadAsync(CancellationToken.None);
        var writeTask = readerWriterLock.EnterWriteAsync(CancellationToken.None);

        Assert.False(writeTask.IsCompleted);

        read.Dispose();

        using var write = await writeTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ClassLocks_AreReleasedAndRemoved()
    {
        var store = new ClassStore(new FakeDataSource());

        for (var index = 0; index < 100; index++)
        {
            using (await store.LockClassAsync(Guid.NewGuid(), CancellationToken.None))
            {
            }
        }

        Assert.Equal(0, store.ClassLockCount);
    }

    [Fact]
    public async Task CancelledClassLockWait_DoesNotLeakEntry()
    {
        var store = new ClassStore(new FakeDataSource());
        var classGuid = Guid.NewGuid();

        using (await store.LockClassAsync(classGuid, CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = store.LockClassAsync(classGuid, cancellation.Token);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.Equal(1, store.ClassLockCount);
        }

        Assert.Equal(0, store.ClassLockCount);
    }

    private static async Task<Exception?> TryRenameAsync(StudentService client, Guid classGuid, string name)
    {
        try
        {
            await client.RenameClassAsync(classGuid, name);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
