using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class GroupTests
{
    private static async Task<(StudentService StudentService, Class Class, GroupingRule Rule)> CreateRuleAsync()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");
        return (client, @class, rule);
    }

    [Fact]
    public async Task CreateGroupAsync_AddsGroupToRule()
    {
        var (client, @class, rule) = await CreateRuleAsync();

        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        Assert.Equal("第一组", group.Name);
        Assert.Contains(group, rule.Groups);
        Assert.Empty(group.StudentGuids);
    }

    [Fact]
    public async Task CreateGroupAsync_BlankName_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateGroupAsync(@class.Guid, rule.Guid, " "));

        Assert.Contains(exception.Errors, error => error.Code == "groupNameEmpty");
    }

    [Fact]
    public async Task CreateGroupAsync_DuplicateNameIgnoringCase_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        await client.CreateGroupAsync(@class.Guid, rule.Guid, "Group A");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.CreateGroupAsync(@class.Guid, rule.Guid, "group a"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateGroupName");
    }

    [Fact]
    public async Task CreateGroupAsync_DuplicateNameInAnotherRule_IsAllowed()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var otherRule = await client.CreateGroupingRuleAsync(@class.Guid, "学习");
        await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        var group = await client.CreateGroupAsync(@class.Guid, otherRule.Guid, "第一组");

        Assert.Equal("第一组", group.Name);
    }

    [Fact]
    public async Task CreateGroupAsync_MissingRule_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => client.CreateGroupAsync(@class.Guid, Guid.NewGuid(), "第一组"));
    }

    [Fact]
    public async Task RenameGroupAsync_UpdatesName()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.RenameGroupAsync(@class.Guid, rule.Guid, group.Guid, "第二组");

        Assert.Equal("第二组", group.Name);
    }

    [Fact]
    public async Task RenameGroupAsync_DuplicateName_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var second = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.RenameGroupAsync(@class.Guid, rule.Guid, second.Guid, "第一组"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateGroupName");
    }

    [Fact]
    public async Task RenameGroupAsync_MissingGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var missing = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<GroupNotFoundException>(
            () => client.RenameGroupAsync(@class.Guid, rule.Guid, missing, "第二组"));

        Assert.Equal(missing, exception.GroupGuid);
    }

    [Fact]
    public async Task DeleteGroupAsync_RemovesGroup()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.DeleteGroupAsync(@class.Guid, rule.Guid, group.Guid);

        Assert.Empty(rule.Groups);
    }

    [Fact]
    public async Task DeleteGroupAsync_MissingGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();

        await Assert.ThrowsAsync<GroupNotFoundException>(
            () => client.DeleteGroupAsync(@class.Guid, rule.Guid, Guid.NewGuid()));
    }

    [Fact]
    public async Task AddStudentToGroupAsync_AddsStudent()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        Assert.Equal([student.Guid], group.StudentGuids);
    }

    [Fact]
    public async Task AddStudentToGroupAsync_AlreadyInSameGroup_IsNoOp()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        Assert.Single(group.StudentGuids);
    }

    [Fact]
    public async Task AddStudentToGroupAsync_StudentAlreadyInAnotherGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var first = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var second = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, first.Guid, student.Guid);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.AddStudentToGroupAsync(@class.Guid, rule.Guid, second.Guid, student.Guid));

        Assert.Contains(exception.Errors, error => error.Code == "studentAlreadyGrouped");
        Assert.DoesNotContain(student.Guid, second.StudentGuids);
    }

    [Fact]
    public async Task AddStudentToGroupAsync_StudentCanBeInDifferentRules()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var otherRule = await client.CreateGroupingRuleAsync(@class.Guid, "学习");
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var first = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var second = await client.CreateGroupAsync(@class.Guid, otherRule.Guid, "第一组");

        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, first.Guid, student.Guid);
        await client.AddStudentToGroupAsync(@class.Guid, otherRule.Guid, second.Guid, student.Guid);

        Assert.Contains(student.Guid, first.StudentGuids);
        Assert.Contains(student.Guid, second.StudentGuids);
    }

    [Fact]
    public async Task AddStudentToGroupAsync_MissingStudent_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await Assert.ThrowsAsync<StudentNotFoundException>(
            () => client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, Guid.NewGuid()));
    }

    [Fact]
    public async Task AddStudentToGroupAsync_MissingGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        await Assert.ThrowsAsync<GroupNotFoundException>(
            () => client.AddStudentToGroupAsync(@class.Guid, rule.Guid, Guid.NewGuid(), student.Guid));
    }

    [Fact]
    public async Task RemoveStudentFromGroupAsync_RemovesStudent()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        await client.RemoveStudentFromGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        Assert.Empty(group.StudentGuids);
    }

    [Fact]
    public async Task RemoveStudentFromGroupAsync_NotAMember_IsNoOp()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

        await client.RemoveStudentFromGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        Assert.Empty(group.StudentGuids);
    }

    [Fact]
    public async Task MoveStudentToGroupAsync_MovesStudent()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var from = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var to = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, from.Guid, student.Guid);

        await client.MoveStudentToGroupAsync(@class.Guid, rule.Guid, from.Guid, to.Guid, student.Guid);

        Assert.Empty(from.StudentGuids);
        Assert.Equal([student.Guid], to.StudentGuids);
    }

    [Fact]
    public async Task MoveStudentToGroupAsync_ToSameGroup_KeepsStudent()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        await client.MoveStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, group.Guid, student.Guid);

        Assert.Equal([student.Guid], group.StudentGuids);
    }

    [Fact]
    public async Task MoveStudentToGroupAsync_NotInSourceGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var from = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var to = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.MoveStudentToGroupAsync(@class.Guid, rule.Guid, from.Guid, to.Guid, student.Guid));

        Assert.Contains(exception.Errors, error => error.Code == "studentNotInSourceGroup");
    }

    [Fact]
    public async Task MoveStudentToGroupAsync_AlreadyInThirdGroup_Throws()
    {
        var (client, @class, rule) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var from = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var to = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");
        var third = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第三组");

        from.StudentGuids.Add(student.Guid);
        third.StudentGuids.Add(student.Guid);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.MoveStudentToGroupAsync(@class.Guid, rule.Guid, from.Guid, to.Guid, student.Guid));

        Assert.Contains(exception.Errors, error => error.Code == "studentAlreadyGrouped");
    }

    [Fact]
    public async Task MoveStudentToGroupAsync_MissingRule_Throws()
    {
        var (client, @class, _) = await CreateRuleAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);

        await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => client.MoveStudentToGroupAsync(
                @class.Guid,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                student.Guid));
    }
}
