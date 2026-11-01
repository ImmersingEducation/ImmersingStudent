using ImmersingStudent.Enums;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task ManualMode_MutationDoesNotSaveUntilSaveAsync()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();

        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        Assert.Equal(0, dataSource.SaveClassCalls);
        Assert.Empty(dataSource.SavedClasses);

        await client.SaveAsync();

        Assert.Equal(1, dataSource.SaveClassCalls);
        var saved = dataSource.GetSavedClass(@class.Guid);
        Assert.NotNull(saved);
        Assert.Equal(student.Guid, Assert.Single(saved.Students).Guid);
    }

    [Fact]
    public async Task AutoMode_MutationSavesImmediately()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync(SaveMode.Auto);

        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        Assert.Equal(1, dataSource.SaveClassCalls);
        var saved = dataSource.GetSavedClass(@class.Guid);
        Assert.NotNull(saved);
        Assert.Equal(student.Guid, Assert.Single(saved.Students).Guid);
    }

    [Fact]
    public async Task AutoMode_CreateClassSavesImmediately()
    {
        var client = TestData.CreateClient(out var dataSource, SaveMode.Auto);

        var @class = await client.CreateClassAsync(Grade.FirstGrade, "1班");

        Assert.True(dataSource.Contains(@class.Guid));
        Assert.Equal(1, dataSource.SaveClassCalls);
    }

    [Fact]
    public async Task SaveAsync_WithoutChanges_DoesNotCallDataSource()
    {
        var client = TestData.CreateClient(out var dataSource, out _);

        await client.SaveAsync();

        Assert.Equal(0, dataSource.SaveClassCalls);
    }

    [Fact]
    public async Task SaveAsync_ClearsDirtyState()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();
        await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        await client.SaveAsync();
        await client.SaveAsync();

        Assert.Equal(1, dataSource.SaveClassCalls);
    }

    [Fact]
    public async Task SaveAsync_ByClassGuid_SavesOnlyThatClass()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "2班");
        var dataSource = new FakeDataSource(first, second);
        var client = new Client(dataSource);
        var cachedFirst = await client.ReadOnly.GetClassAsync(first.Guid);
        var cachedSecond = await client.ReadOnly.GetClassAsync(second.Guid);
        cachedFirst.NameWithoutGrade = "修改1";
        cachedSecond.NameWithoutGrade = "修改2";

        await client.SaveAsync(first.Guid);

        Assert.Equal(1, dataSource.SaveClassCalls);
        Assert.Equal("修改1", dataSource.GetSavedClass(first.Guid)?.NameWithoutGrade);
        Assert.Equal("2班", dataSource.GetSavedClass(second.Guid)?.NameWithoutGrade);
    }

    [Fact]
    public async Task SaveAsync_ByClassGuid_MissingClass_Throws()
    {
        var client = TestData.CreateClient(out _);

        await Assert.ThrowsAsync<ClassNotFoundException>(
            () => client.SaveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AutoMode_SaveFailure_KeepsDirtyStateForRetry()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync(SaveMode.Auto);
        dataSource.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male));

        Assert.Empty(dataSource.SavedClasses);

        await client.SaveAsync();

        var saved = dataSource.GetSavedClass(@class.Guid);
        Assert.NotNull(saved);
        Assert.Single(saved.Students);
    }

    [Fact]
    public async Task ReloadAsync_DiscardsUnsavedChanges()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        @class.NameWithoutGrade = "未保存";

        await client.ReloadAsync();

        var reloaded = await client.ReadOnly.GetClassAsync(@class.Guid);
        Assert.NotSame(@class, reloaded);
        Assert.Equal("3班", reloaded.NameWithoutGrade);
    }

    [Fact]
    public async Task ReloadAsync_ByClassGuid_OnlyReplacesThatClass()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "2班");
        var client = new Client(new FakeDataSource(first, second));
        var firstCached = await client.ReadOnly.GetClassAsync(first.Guid);
        var secondCached = await client.ReadOnly.GetClassAsync(second.Guid);
        firstCached.NameWithoutGrade = "未保存1";
        secondCached.NameWithoutGrade = "未保存2";

        await client.ReloadAsync(first.Guid);

        var reloadedFirst = await client.ReadOnly.GetClassAsync(first.Guid);
        var reloadedSecond = await client.ReadOnly.GetClassAsync(second.Guid);
        Assert.Equal("1班", reloadedFirst.NameWithoutGrade);
        Assert.Same(secondCached, reloadedSecond);
        Assert.Equal("未保存2", reloadedSecond.NameWithoutGrade);
    }

    [Fact]
    public async Task ReloadAsync_ByClassGuid_WhenClassDeletedFromSource_RemovesIt()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();

        await dataSource.DeleteClassAsync(@class.Guid);
        await client.ReloadAsync(@class.Guid);

        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
    }

    [Fact]
    public async Task CancelledToken_PropagatesToDataSource()
    {
        var client = TestData.CreateClient(out _, out _);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ReadOnly.GetClassesAsync(source.Token));
    }

    [Fact]
    public async Task CancelledToken_CancelsMutation()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.AddStudentAsync(
                @class.Guid,
                "1",
                "张三",
                Gender.Male,
                cancellationToken: source.Token));

        Assert.Empty(@class.Students);
    }

    [Fact]
    public async Task ManualDelete_DoesNotResurrectThroughQueriesBeforeSave()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();

        // 先走一次“全部加载”路径，覆盖 EnsureAllLoadedCoreAsync。
        Assert.Single(await client.ReadOnly.GetClassesAsync());

        await client.DeleteClassAsync(@class.Guid);

        Assert.Empty(await client.ReadOnly.GetClassesAsync());
        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
        Assert.True(dataSource.Contains(@class.Guid));

        await client.SaveAsync();

        Assert.False(dataSource.Contains(@class.Guid));
        Assert.Empty(await client.ReadOnly.GetClassesAsync());
        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
    }

    [Fact]
    public async Task AutoDeleteFailure_KeepsClassHiddenAndRetriesOnSave()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync(SaveMode.Auto);
        dataSource.FailNextDelete = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DeleteClassAsync(@class.Guid));

        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
        Assert.Empty(await client.ReadOnly.GetClassesAsync());
        Assert.True(dataSource.Contains(@class.Guid));

        await client.SaveAsync();

        Assert.False(dataSource.Contains(@class.Guid));
        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
    }

    [Fact]
    public async Task SaveAsync_ByClassGuid_ValidatesDirectMutations()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("1", "张三", 1),
            TestData.NewStudent("2", "李四", 2)
        ]);
        var (client, dataSource, cached) = await TestData.CreateClientAsync(seed: @class);

        cached.Students[1].Id = "1";

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.SaveAsync(cached.Guid));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateStudentId");
        Assert.Equal("2", dataSource.GetSavedClass(cached.Guid)!.Students[1].Id);
    }

    [Fact]
    public async Task SaveAsync_ValidatesCachedClassesBeforeWritingAnything()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();
        await client.SetExtraPropertyAsync(
            @class.Guid,
            @class.Guid,
            ExtraPropertyOwner.Class,
            "note",
            "dirty");

        // 直接修改制造非法状态，模拟绕过客户端直接改模型的场景。
        @class.Students.Add(TestData.NewStudent("1", "张三", 1));
        @class.Students.Add(TestData.NewStudent("1", "李四", 2));

        var exception = await Assert.ThrowsAsync<ValidationException>(() => client.SaveAsync());

        Assert.Contains(exception.Errors, error => error.Code == "duplicateStudentId");
        Assert.Equal(0, dataSource.SaveClassCalls);
        Assert.Empty(dataSource.GetSavedClass(@class.Guid)!.Students);
    }
}
