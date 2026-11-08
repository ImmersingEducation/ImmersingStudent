using ImmersingStudent.Abstractions;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Models;
using ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingStudent.Tests;

public class ReadOnlyStudentServiceTests
{
    private sealed record Fixture(
        FakeDataSource DataSource,
        ReadOnlyStudentService StudentService,
        Class Class,
        Student First,
        Student Second,
        Student Third,
        GroupingRule Rule,
        Group FirstGroup,
        Group GroupWithMissingStudent);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var first = TestData.NewStudent("S001", "张三", 1, Gender.Male);
        var second = TestData.NewStudent("S002", "李四", 2, Gender.Female);
        var third = TestData.NewStudent("S003", "王五", 3, Gender.Male);

        var firstGroup = new Group("第一组", [first.Guid, second.Guid], []);
        var groupWithMissingStudent = new Group("第二组", [first.Guid, Guid.NewGuid()], []);
        var rule = new GroupingRule("值日", [firstGroup, groupWithMissingStudent], []);
        var @class = TestData.NewClass(
            students: [first, second, third],
            rules: [rule]);

        var dataSource = new FakeDataSource(@class);
        var client = new ReadOnlyStudentService(dataSource);
        var cached = await client.GetClassAsync(@class.Guid);
        var cachedRule = Assert.Single(cached.GroupingRules);
        return new Fixture(
            dataSource,
            client,
            cached,
            cached.Students[0],
            cached.Students[1],
            cached.Students[2],
            cachedRule,
            cachedRule.Groups[0],
            cachedRule.Groups[1]);
    }

    [Fact]
    public void Constructor_NullDataSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyStudentService((IDataSource)null!));
    }

    [Fact]
    public async Task GetClassesAsync_ReturnsAllClasses()
    {
        var fixture = await CreateFixtureAsync();

        var classes = await fixture.StudentService.GetClassesAsync();

        Assert.Equal(fixture.Class.Guid, Assert.Single(classes).Guid);
    }

    [Fact]
    public async Task FindClassAsync_ReturnsClassOrNull()
    {
        var fixture = await CreateFixtureAsync();

        var found = await fixture.StudentService.FindClassAsync(fixture.Class.Guid);
        var missing = await fixture.StudentService.FindClassAsync(Guid.NewGuid());

        Assert.Equal(fixture.Class.Guid, found?.Guid);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetClassAsync_MissingClass_Throws()
    {
        var fixture = await CreateFixtureAsync();
        var missing = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<ClassNotFoundException>(
            () => fixture.StudentService.GetClassAsync(missing));

        Assert.Equal(missing, exception.ClassGuid);
    }

    [Fact]
    public async Task GetStudentsAsync_ReturnsAllStudents()
    {
        var fixture = await CreateFixtureAsync();

        var students = await fixture.StudentService.GetStudentsAsync(fixture.Class.Guid);

        Assert.Equal(3, students.Count);
    }

    [Fact]
    public async Task FindStudentAsync_ReturnsStudentOrNull()
    {
        var fixture = await CreateFixtureAsync();

        var found = await fixture.StudentService.FindStudentAsync(fixture.Class.Guid, fixture.Second.Guid);
        var missing = await fixture.StudentService.FindStudentAsync(fixture.Class.Guid, Guid.NewGuid());

        Assert.Same(fixture.Second, found);
        Assert.Null(missing);
    }

    [Fact]
    public async Task FindStudentByStudentIdAsync_IgnoresCase()
    {
        var fixture = await CreateFixtureAsync();

        var found = await fixture.StudentService.FindStudentByStudentIdAsync(fixture.Class.Guid, "s002");
        var missing = await fixture.StudentService.FindStudentByStudentIdAsync(fixture.Class.Guid, "nope");

        Assert.Same(fixture.Second, found);
        Assert.Null(missing);
    }

    [Fact]
    public async Task FindStudentByStudentIdAsync_NullId_Throws()
    {
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.StudentService.FindStudentByStudentIdAsync(fixture.Class.Guid, null!));
    }

    [Fact]
    public async Task GetGroupingRulesAsync_ReturnsRules()
    {
        var fixture = await CreateFixtureAsync();

        var rules = await fixture.StudentService.GetGroupingRulesAsync(fixture.Class.Guid);

        Assert.Same(fixture.Rule, Assert.Single(rules));
    }

    [Fact]
    public async Task FindGroupingRuleAsync_ReturnsRuleOrNull()
    {
        var fixture = await CreateFixtureAsync();

        var found = await fixture.StudentService.FindGroupingRuleAsync(fixture.Class.Guid, fixture.Rule.Guid);
        var missing = await fixture.StudentService.FindGroupingRuleAsync(fixture.Class.Guid, Guid.NewGuid());

        Assert.Same(fixture.Rule, found);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetGroupsAsync_ReturnsGroups()
    {
        var fixture = await CreateFixtureAsync();

        var groups = await fixture.StudentService.GetGroupsAsync(fixture.Class.Guid, fixture.Rule.Guid);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public async Task GetGroupsAsync_MissingRule_Throws()
    {
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => fixture.StudentService.GetGroupsAsync(fixture.Class.Guid, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsMemberStudents()
    {
        var fixture = await CreateFixtureAsync();

        var members = await fixture.StudentService.GetGroupMembersAsync(
            fixture.Class.Guid,
            fixture.Rule.Guid,
            fixture.FirstGroup.Guid);

        Assert.Equal([fixture.First.Guid, fixture.Second.Guid], members.Select(student => student.Guid));
    }

    [Fact]
    public async Task GetGroupMembersAsync_IgnoresMissingStudents()
    {
        var fixture = await CreateFixtureAsync();

        var members = await fixture.StudentService.GetGroupMembersAsync(
            fixture.Class.Guid,
            fixture.Rule.Guid,
            fixture.GroupWithMissingStudent.Guid);

        Assert.Same(fixture.First, Assert.Single(members));
    }

    [Fact]
    public async Task GetGroupMembersAsync_MissingGroup_Throws()
    {
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<GroupNotFoundException>(
            () => fixture.StudentService.GetGroupMembersAsync(
                fixture.Class.Guid,
                fixture.Rule.Guid,
                Guid.NewGuid()));
    }

    [Fact]
    public async Task GetUngroupedStudentsAsync_ReturnsStudentsNotInAnyGroup()
    {
        var fixture = await CreateFixtureAsync();

        var ungrouped = await fixture.StudentService.GetUngroupedStudentsAsync(
            fixture.Class.Guid,
            fixture.Rule.Guid);

        Assert.Same(fixture.Third, Assert.Single(ungrouped));
    }

    [Fact]
    public async Task GetUngroupedStudentsAsync_MissingRule_Throws()
    {
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<GroupingRuleNotFoundException>(
            () => fixture.StudentService.GetUngroupedStudentsAsync(fixture.Class.Guid, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("张", null, 1)]
    [InlineData("李", null, 1)]
    [InlineData("S00", null, 3)]
    [InlineData("", null, 3)]
    [InlineData("   ", null, 3)]
    [InlineData("王", null, 1)]
    public async Task SearchStudentsAsync_MatchesNameOrId(string keyword, Gender? gender, int expected)
    {
        var fixture = await CreateFixtureAsync();

        var students = await fixture.StudentService.SearchStudentsAsync(
            fixture.Class.Guid,
            keyword,
            gender);

        Assert.Equal(expected, students.Count);
    }

    [Fact]
    public async Task SearchStudentsAsync_FiltersByGender()
    {
        var fixture = await CreateFixtureAsync();

        var students = await fixture.StudentService.SearchStudentsAsync(
            fixture.Class.Guid,
            "",
            Gender.Female);

        Assert.Same(fixture.Second, Assert.Single(students));
    }

    [Fact]
    public async Task SearchStudentsAsync_KeywordAndGenderMustBothMatch()
    {
        var fixture = await CreateFixtureAsync();

        var students = await fixture.StudentService.SearchStudentsAsync(
            fixture.Class.Guid,
            "李",
            Gender.Male);

        Assert.Empty(students);
    }

    [Fact]
    public async Task SearchStudentsAsync_NullKeyword_Throws()
    {
        var fixture = await CreateFixtureAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.StudentService.SearchStudentsAsync(fixture.Class.Guid, null!));
    }

    [Fact]
    public void GetDisplayName_CombinesGradeAndClassName()
    {
        var @class = TestData.NewClass(Grade.JuniorSecondGrade, "3班");

        var displayName = ReadOnlyStudentService.GetDisplayName(@class);

        Assert.Equal("初二年级3班", displayName);
    }

    [Fact]
    public void GetDisplayName_NullClass_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ReadOnlyStudentService.GetDisplayName(null!));
    }

    [Theory]
    [InlineData(Grade.FirstGrade, "一年级")]
    [InlineData(Grade.SecondGrade, "二年级")]
    [InlineData(Grade.ThirdGrade, "三年级")]
    [InlineData(Grade.FourthGrade, "四年级")]
    [InlineData(Grade.FifthGrade, "五年级")]
    [InlineData(Grade.SixthGrade, "六年级")]
    [InlineData(Grade.JuniorFirstGrade, "初一年级")]
    [InlineData(Grade.JuniorSecondGrade, "初二年级")]
    [InlineData(Grade.JuniorThirdGrade, "初三年级")]
    [InlineData(Grade.SeniorFirstGrade, "高一年级")]
    [InlineData(Grade.SeniorSecondGrade, "高二年级")]
    [InlineData(Grade.SeniorThirdGrade, "高三年级")]
    public void GetGradeName_ReturnsChineseName(Grade grade, string expected)
    {
        Assert.Equal(expected, ReadOnlyStudentService.GetGradeName(grade));
    }

    [Fact]
    public void GetGradeName_UnknownGrade_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReadOnlyStudentService.GetGradeName((Grade)99));
    }

    [Fact]
    public async Task Queries_DoNotWriteToDataSource()
    {
        var fixture = await CreateFixtureAsync();

        await fixture.StudentService.GetClassesAsync();
        await fixture.StudentService.GetStudentsAsync(fixture.Class.Guid);
        await fixture.StudentService.GetGroupMembersAsync(
            fixture.Class.Guid,
            fixture.Rule.Guid,
            fixture.FirstGroup.Guid);

        Assert.Equal(0, fixture.DataSource.SaveClassCalls);
        Assert.Equal(0, fixture.DataSource.DeleteClassCalls);
    }
}
