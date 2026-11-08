using ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;
using ImmersingEducation.Infrastructures.ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Tests;

public class ValidationTests
{
    [Fact]
    public async Task ValidateAsync_ValidClass_ReturnsNoErrors()
    {
        var student = TestData.NewStudent("1", "张三", 1);
        var rule = new GroupingRule("值日", [new Group("第一组", [student.Guid], [])], []);
        var @class = TestData.NewClass(students: [student], rules: [rule]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ValidateAsync_MissingClass_Throws()
    {
        var client = TestData.CreateClient(out _);

        await Assert.ThrowsAsync<ClassNotFoundException>(() => client.ValidateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ValidateAsync_DuplicateClassNameInSameGrade_ReturnsError()
    {
        var first = TestData.NewClass(nameWithoutGrade: "1班");
        var second = TestData.NewClass(nameWithoutGrade: "1班");
        var client = new StudentService(new FakeDataSource(first, second));

        var errors = await client.ValidateAsync(second.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateClassName");
    }

    [Fact]
    public async Task ValidateAsync_EmptyClassName_ReturnsError()
    {
        var @class = TestData.NewClass(nameWithoutGrade: " ");
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "classNameEmpty");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateStudentIdsIgnoringCase_ReturnsError()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("S001", idInClass: 1),
            TestData.NewStudent("s001", idInClass: 2)
        ]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateStudentId");
    }

    [Fact]
    public async Task ValidateAsync_EmptyStudentId_ReturnsError()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent(" ")]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "studentIdEmpty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateAsync_InvalidIdInClass_ReturnsError(int idInClass)
    {
        var student = TestData.NewStudent("1", idInClass: 1) with { IdInClass = idInClass };
        var @class = TestData.NewClass(students: [student]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "invalidIdInClass");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateIdInClass_ReturnsError()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("1", idInClass: 1),
            TestData.NewStudent("2", idInClass: 1)
        ]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateIdInClass");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateStudentGuid_ReturnsError()
    {
        var guid = Guid.NewGuid();
        var first = TestData.NewStudent("1", idInClass: 1) with { Guid = guid };
        var second = TestData.NewStudent("2", idInClass: 2) with { Guid = guid };
        var @class = TestData.NewClass(students: [first, second]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateStudentGuid");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRuleNames_ReturnsError()
    {
        var @class = TestData.NewClass(rules:
        [
            new GroupingRule("Duty", [], []),
            new GroupingRule("duty", [], [])
        ]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateRuleName");
    }

    [Fact]
    public async Task ValidateAsync_EmptyRuleName_ReturnsError()
    {
        var @class = TestData.NewClass(rules: [new GroupingRule(" ", [], [])]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "ruleNameEmpty");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateGroupNames_ReturnsError()
    {
        var deduplicated = new List<Group>
        {
            new("Group A", [], []),
            new("group a", [], [])
        };
        var @class = TestData.NewClass(rules: [new GroupingRule("Duty", deduplicated, [])]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateGroupName");
    }

    [Fact]
    public async Task ValidateAsync_EmptyGroupName_ReturnsError()
    {
        var @class = TestData.NewClass(
            rules: [new GroupingRule("Duty", [new Group(" ", [], [])], [])]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "groupNameEmpty");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateGroupGuid_ReturnsError()
    {
        var guid = Guid.NewGuid();
        var groups = new List<Group>
        {
            new("第一组", [], []) { Guid = guid },
            new("第二组", [], []) { Guid = guid }
        };
        var @class = TestData.NewClass(rules: [new GroupingRule("Duty", groups, [])]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "duplicateGroupGuid");
    }

    [Fact]
    public async Task ValidateAsync_GroupReferencesMissingStudent_ReturnsError()
    {
        var rule = new GroupingRule("Duty", [new Group("第一组", [Guid.NewGuid()], [])], []);
        var @class = TestData.NewClass(rules: [rule]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "studentNotFound");
    }

    [Fact]
    public async Task ValidateAsync_StudentInMultipleGroupsOfSameRule_ReturnsError()
    {
        var student = TestData.NewStudent("1", idInClass: 1);
        var groups = new List<Group>
        {
            new("第一组", [student.Guid], []),
            new("第二组", [student.Guid], [])
        };
        var @class = TestData.NewClass(
            students: [student],
            rules: [new GroupingRule("Duty", groups, [])]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "studentGroupedMoreThanOnce");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsAllErrors()
    {
        var @class = TestData.NewClass(
            nameWithoutGrade: " ",
            students:
            [
                TestData.NewStudent("", idInClass: 0),
                TestData.NewStudent("", idInClass: 0)
            ]);
        var client = new StudentService(new FakeDataSource(@class));

        var errors = await client.ValidateAsync(@class.Guid);

        Assert.Contains(errors, error => error.Code == "classNameEmpty");
        Assert.Contains(errors, error => error.Code == "studentIdEmpty");
        Assert.Contains(errors, error => error.Code == "invalidIdInClass");
        Assert.True(errors.Count >= 4);
    }
}
