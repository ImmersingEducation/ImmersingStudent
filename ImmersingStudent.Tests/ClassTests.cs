using ImmersingStudent.Enums;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class ClassTests
{
    [Fact]
    public async Task CreateClassAsync_ReturnsClassAndCachesIt()
    {
        var client = TestData.CreateClient(out _);

        var created = await client.CreateClassAsync(Grade.JuniorSecondGrade, "3班");

        Assert.Equal(Grade.JuniorSecondGrade, created.Grade);
        Assert.Equal("3班", created.NameWithoutGrade);
        Assert.Empty(created.Students);
        Assert.Empty(created.GroupingRules);

        var loaded = await client.ReadOnly.GetClassAsync(created.Guid);
        Assert.Same(created, loaded);
    }

    [Fact]
    public async Task CreateClassAsync_EmptyName_ThrowsValidationException()
    {
        var client = TestData.CreateClient(out _);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateClassAsync(Grade.FirstGrade, "   "));

        Assert.Contains(exception.Errors, error => error.Code == "classNameEmpty");
    }

    [Fact]
    public async Task CreateClassAsync_DuplicateNameInSameGrade_Throws()
    {
        var client = TestData.CreateClient(out _);
        await client.CreateClassAsync(Grade.FirstGrade, "1班");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateClassAsync(Grade.FirstGrade, "1班"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateClassName");
    }

    [Fact]
    public async Task CreateClassAsync_SameNameInDifferentGrade_IsAllowed()
    {
        var client = TestData.CreateClient(out _);
        await client.CreateClassAsync(Grade.FirstGrade, "1班");

        var created = await client.CreateClassAsync(Grade.SecondGrade, "1班");

        Assert.Equal(Grade.SecondGrade, created.Grade);
    }

    [Fact]
    public async Task RenameClassAsync_UpdatesName()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await client.RenameClassAsync(@class.Guid, "4班");

        Assert.Equal("4班", @class.NameWithoutGrade);
    }

    [Fact]
    public async Task RenameClassAsync_DuplicateName_ThrowsAndKeepsOriginal()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "2班");
        var dataSource = new FakeDataSource(first, second);
        var client = new Client(dataSource);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.RenameClassAsync(second.Guid, "1班"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateClassName");
        Assert.Equal("2班", second.NameWithoutGrade);
    }

    [Fact]
    public async Task RenameClassAsync_MissingClass_ThrowsClassNotFound()
    {
        var client = TestData.CreateClient(out _);
        var missing = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ClassNotFoundException>(
            () => client.RenameClassAsync(missing, "新名字"));

        Assert.Equal(missing, exception.ClassGuid);
    }

    [Fact]
    public async Task SetGradeAsync_UpdatesGrade()
    {
        var client = TestData.CreateClient(out _);
        var @class = await client.CreateClassAsync(Grade.FirstGrade, "1班");

        await client.SetGradeAsync(@class.Guid, Grade.SixthGrade);

        Assert.Equal(Grade.SixthGrade, @class.Grade);
    }

    [Fact]
    public async Task SetGradeAsync_WouldDuplicateInTargetGrade_Throws()
    {
        var first = TestData.NewClass(Grade.FirstGrade, "1班");
        var second = TestData.NewClass(Grade.SecondGrade, "1班");
        var client = new Client(new FakeDataSource(first, second));

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.SetGradeAsync(second.Guid, Grade.FirstGrade));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateClassName");
    }

    [Fact]
    public async Task DeleteClassAsync_ManualMode_DefersUntilSave()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();

        await client.DeleteClassAsync(@class.Guid);

        Assert.Equal(0, dataSource.DeleteClassCalls);
        Assert.True(dataSource.Contains(@class.Guid));

        await client.SaveAsync();

        Assert.Equal(1, dataSource.DeleteClassCalls);
        Assert.False(dataSource.Contains(@class.Guid));
        Assert.Null(await client.ReadOnly.FindClassAsync(@class.Guid));
    }

    [Fact]
    public async Task DeleteClassAsync_AutoMode_DeletesImmediately()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync(SaveMode.Auto);

        await client.DeleteClassAsync(@class.Guid);

        Assert.Equal(1, dataSource.DeleteClassCalls);
        Assert.False(dataSource.Contains(@class.Guid));
    }

    [Fact]
    public async Task DeleteClassAsync_NewClassThatWasNeverSaved_DoesNotReachDataSource()
    {
        var client = TestData.CreateClient(out var dataSource);
        var @class = await client.CreateClassAsync(Grade.FirstGrade, "1班");

        await client.DeleteClassAsync(@class.Guid);
        await client.SaveAsync();

        Assert.Equal(0, dataSource.DeleteClassCalls);
        Assert.Empty(dataSource.DeletedClassGuids);
    }

    [Fact]
    public async Task DeleteClassAsync_MissingClass_Throws()
    {
        var client = TestData.CreateClient(out _);
        var missing = Guid.NewGuid();

        await Assert.ThrowsAsync<ClassNotFoundException>(() => client.DeleteClassAsync(missing));
    }

    [Fact]
    public async Task GetClassesAsync_ReturnsSharedCachedInstances()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var first = await client.ReadOnly.GetClassesAsync();
        var second = await client.ReadOnly.GetClassesAsync();

        Assert.Same(@class, Assert.Single(first));
        Assert.Same(first[0], second[0]);
    }
}
