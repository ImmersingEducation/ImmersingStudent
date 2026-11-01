using ImmersingStudent.Exceptions;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class GroupingRuleTests
{
    [Fact]
    public async Task CreateGroupingRuleAsync_AddsRule()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");

        Assert.Equal("值日", rule.Name);
        Assert.Contains(rule, @class.GroupingRules);
        Assert.Empty(rule.Groups);
    }

    [Fact]
    public async Task CreateGroupingRuleAsync_BlankName_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateGroupingRuleAsync(@class.Guid, " "));

        Assert.Contains(exception.Errors, error => error.Code == "ruleNameEmpty");
    }

    [Fact]
    public async Task CreateGroupingRuleAsync_DuplicateNameIgnoringCase_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        await client.CreateGroupingRuleAsync(@class.Guid, "Duty");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateGroupingRuleAsync(@class.Guid, "duty"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateRuleName");
    }

    [Fact]
    public async Task RenameGroupingRuleAsync_UpdatesName()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");

        await client.RenameGroupingRuleAsync(@class.Guid, rule.Guid, "学习");

        Assert.Equal("学习", rule.Name);
    }

    [Fact]
    public async Task RenameGroupingRuleAsync_ToOwnName_IsAllowed()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");

        await client.RenameGroupingRuleAsync(@class.Guid, rule.Guid, "值日");

        Assert.Equal("值日", rule.Name);
    }

    [Fact]
    public async Task RenameGroupingRuleAsync_DuplicateName_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        await client.CreateGroupingRuleAsync(@class.Guid, "值日");
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "学习");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.RenameGroupingRuleAsync(@class.Guid, rule.Guid, "值日"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateRuleName");
    }

    [Fact]
    public async Task RenameGroupingRuleAsync_MissingRule_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var missing = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => client.RenameGroupingRuleAsync(@class.Guid, missing, "学习"));

        Assert.Equal(missing, exception.RuleGuid);
    }

    [Fact]
    public async Task DeleteGroupingRuleAsync_RemovesRuleAndGroups()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");

        await client.DeleteGroupingRuleAsync(@class.Guid, rule.Guid);

        Assert.Empty(@class.GroupingRules);
    }

    [Fact]
    public async Task DeleteGroupingRuleAsync_MissingRule_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => client.DeleteGroupingRuleAsync(@class.Guid, Guid.NewGuid()));
    }
}
