using ImmersingStudent.Enums;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class ExtraPropertyTests
{
    [Fact]
    public async Task SetExtraPropertyAsync_Class_AddsAndUpdatesProperty()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await client.SetExtraPropertyAsync(
            @class.Guid,
            @class.Guid,
            ExtraPropertyOwner.Class,
            "theme",
            "blue");
        await client.SetExtraPropertyAsync(
            @class.Guid,
            @class.Guid,
            ExtraPropertyOwner.Class,
            "theme",
            "red");

        var property = Assert.Single(@class.ExtraProperties);
        Assert.Equal("theme", property.Id);
        Assert.Equal("red", property.Value);
    }

    [Fact]
    public async Task SetExtraPropertyAsync_Student_AddsProperty()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        await client.SetExtraPropertyAsync(
            @class.Guid,
            student.Guid,
            ExtraPropertyOwner.Student,
            "nickname",
            "小张");

        Assert.Equal("小张", Assert.Single(student.ExtraProperties).Value);
    }

    [Fact]
    public async Task SetExtraPropertyAsync_GroupingRule_AddsProperty()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");

        await client.SetExtraPropertyAsync(
            @class.Guid,
            rule.Guid,
            ExtraPropertyOwner.GroupingRule,
            "order",
            3);

        Assert.Equal(3, Assert.Single(rule.ExtraProperties).Value);
    }

    [Fact]
    public async Task SetExtraPropertyAsync_Group_AddsProperty()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.SetExtraPropertyAsync(
            @class.Guid,
            group.Guid,
            ExtraPropertyOwner.Group,
            "color",
            "green");

        Assert.Equal("green", Assert.Single(group.ExtraProperties).Value);
    }

    [Fact]
    public async Task SetExtraPropertyAsync_ClassOwnerMismatch_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.SetExtraPropertyAsync(
                @class.Guid,
                Guid.NewGuid(),
                ExtraPropertyOwner.Class,
                "theme",
                "blue"));

        Assert.Contains(exception.Errors, error => error.Code == "invalidOwnerGuid");
    }

    [Fact]
    public async Task SetExtraPropertyAsync_MissingOwner_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<StudentNotFoundException>(
            () => client.SetExtraPropertyAsync(
                @class.Guid,
                Guid.NewGuid(),
                ExtraPropertyOwner.Student,
                "nickname",
                "小张"));
    }

    [Fact]
    public async Task SetExtraPropertyAsync_BlankId_ThrowsArgumentException()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SetExtraPropertyAsync(
                @class.Guid,
                @class.Guid,
                ExtraPropertyOwner.Class,
                "  ",
                "value"));
    }

    [Fact]
    public async Task SetExtraPropertyAsync_UnknownOwnerKind_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SetExtraPropertyAsync(
                @class.Guid,
                @class.Guid,
                (ExtraPropertyOwner)99,
                "id",
                "value"));
    }

    [Fact]
    public async Task ExtraProperties_ArePersistedWithClass()
    {
        var (client, dataSource, @class) = await TestData.CreateClientAsync();
        await client.SetExtraPropertyAsync(
            @class.Guid,
            @class.Guid,
            ExtraPropertyOwner.Class,
            "theme",
            "blue");

        await client.SaveAsync();

        var saved = dataSource.GetSavedClass(@class.Guid);
        Assert.NotNull(saved);
        Assert.Equal("blue", Assert.Single(saved.ExtraProperties).Value);
    }

    [Fact]
    public void GetExtraProperty_ReturnsTypedValue()
    {
        var properties = new List<ExtraProperty>
        {
            new ExtraProperty<string> { Id = "name", Value = "value" },
            new ExtraProperty<int> { Id = "count", Value = 5 }
        };

        Assert.Equal("value", ReadOnlyClient.GetExtraProperty<string>(properties, "name"));
        Assert.Equal(5, ReadOnlyClient.GetExtraProperty<int>(properties, "count"));
    }

    [Fact]
    public void GetExtraProperty_MissingOrTypeMismatch_ReturnsDefault()
    {
        var properties = new List<ExtraProperty>
        {
            new ExtraProperty<string> { Id = "name", Value = "value" }
        };

        Assert.Null(ReadOnlyClient.GetExtraProperty<int?>(properties, "name"));
        Assert.Null(ReadOnlyClient.GetExtraProperty<string>(properties, "missing"));
    }

    [Fact]
    public void GetExtraProperty_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => ReadOnlyClient.GetExtraProperty<string>(null!, "id"));
        Assert.Throws<ArgumentNullException>(
            () => ReadOnlyClient.GetExtraProperty<string>([], null!));
    }
}
