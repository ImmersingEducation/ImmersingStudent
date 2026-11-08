using ImmersingEducation.Infrastructures.ImmersingStudent.Enums;
using ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;
using ImmersingEducation.Infrastructures.ImmersingStudent.Tests.TestSupport;
using Xunit;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Tests;

public class StudentTests
{
    [Fact]
    public async Task AddStudentAsync_FirstStudentGetsIdInClassOne()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var student = await client.AddStudentAsync(@class.Guid, "2024001", "张三", Gender.Male);

        Assert.Equal(1, student.IdInClass);
        Assert.Contains(student, @class.Students);
    }

    [Fact]
    public async Task AddStudentAsync_WithoutIdInClass_UsesMaximumPlusOne()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("1", idInClass: 1),
            TestData.NewStudent("2", idInClass: 7)
        ]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        var student = await client.AddStudentAsync(@class.Guid, "3", "李四", Gender.Female);

        Assert.Equal(8, student.IdInClass);
    }

    [Fact]
    public async Task AddStudentAsync_WithExplicitIdInClass_UsesIt()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male, idInClass: 42);

        Assert.Equal(42, student.IdInClass);
    }

    [Fact]
    public async Task AddStudentAsync_DuplicateStudentIdIgnoringCase_Throws()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent("S001")]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        var exception = await Assert.ThrowsAsync<DuplicateStudentException>(
            () => client.AddStudentAsync(@class.Guid, "s001", "张三", Gender.Male));

        Assert.Equal("s001", exception.StudentId);
        Assert.Single(@class.Students);
    }

    [Theory]
    [InlineData("", "张三", "studentIdEmpty")]
    [InlineData("   ", "张三", "studentIdEmpty")]
    [InlineData("1", "", "studentNameEmpty")]
    [InlineData("1", "   ", "studentNameEmpty")]
    public async Task AddStudentAsync_InvalidTextFields_Throw(string id, string name, string expectedCode)
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.AddStudentAsync(@class.Guid, id, name, Gender.Male));

        Assert.Contains(exception.Errors, error => error.Code == expectedCode);
        Assert.Empty(@class.Students);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task AddStudentAsync_InvalidIdInClass_Throws(int idInClass)
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male, idInClass));

        Assert.Contains(exception.Errors, error => error.Code == "invalidIdInClass");
    }

    [Fact]
    public async Task AddStudentAsync_DuplicateIdInClass_Throws()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent("1", idInClass: 5)]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.AddStudentAsync(@class.Guid, "2", "李四", Gender.Female, idInClass: 5));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateIdInClass");
    }

    [Fact]
    public async Task AddStudentsAsync_AddsAllStudents()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var added = await client.AddStudentsAsync(@class.Guid,
        [
            TestData.NewStudent("1", "张三", 1),
            TestData.NewStudent("2", "李四", 2)
        ]);

        Assert.Equal(2, added.Count);
        Assert.Equal(2, @class.Students.Count);
    }

    [Fact]
    public async Task AddStudentsAsync_WithDuplicateInBatch_DoesNotWriteAnything()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<DuplicateStudentException>(
            () => client.AddStudentsAsync(@class.Guid,
            [
                TestData.NewStudent("1", "张三", 1),
                TestData.NewStudent("1", "李四", 2)
            ]));

        Assert.Empty(@class.Students);
    }

    [Fact]
    public async Task AddStudentsAsync_WithExistingId_DoesNotWriteAnything()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent("1", idInClass: 1)]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        await Assert.ThrowsAsync<DuplicateStudentException>(
            () => client.AddStudentsAsync(@class.Guid,
            [
                TestData.NewStudent("2", "李四", 2),
                TestData.NewStudent("1", "张三", 3)
            ]));

        Assert.Single(@class.Students);
    }

    [Fact]
    public async Task UpdateStudentAsync_UpdatesFields()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent("1", "张三", 1)]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;
        var student = @class.Students[0];

        await client.UpdateStudentAsync(@class.Guid, student.Guid, item =>
        {
            item.Name = "张三丰";
            item.Id = "100";
            item.IdInClass = 9;
            item.Gender = Gender.Female;
        });

        Assert.Equal("张三丰", student.Name);
        Assert.Equal("100", student.Id);
        Assert.Equal(9, student.IdInClass);
        Assert.Equal(Gender.Female, student.Gender);
    }

    [Fact]
    public async Task UpdateStudentAsync_ToDuplicateStudentId_Throws()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("1", "张三", 1),
            TestData.NewStudent("2", "李四", 2)
        ]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.UpdateStudentAsync(@class.Guid, @class.Students[1].Guid, item => item.Id = "1"));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateStudentId");
    }

    [Fact]
    public async Task UpdateStudentAsync_MissingStudent_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        var exception = await Assert.ThrowsAsync<StudentNotFoundException>(
            () => client.UpdateStudentAsync(@class.Guid, Guid.NewGuid(), _ => { }));

        Assert.Equal(@class.Guid, exception.ClassGuid);
    }

    [Fact]
    public async Task RemoveStudentAsync_RemovesStudentAndGroupMemberships()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var student = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");
        var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, group.Guid, student.Guid);

        await client.RemoveStudentAsync(@class.Guid, student.Guid);

        Assert.Empty(@class.Students);
        Assert.Empty(group.StudentGuids);
    }

    [Fact]
    public async Task RemoveStudentAsync_MissingStudent_Throws()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();

        await Assert.ThrowsAsync<StudentNotFoundException>(
            () => client.RemoveStudentAsync(@class.Guid, Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveStudentsAsync_RemovesAllAndCleansEveryRule()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var first = await client.AddStudentAsync(@class.Guid, "1", "张三", Gender.Male);
        var second = await client.AddStudentAsync(@class.Guid, "2", "李四", Gender.Female);
        var third = await client.AddStudentAsync(@class.Guid, "3", "王五", Gender.Male);

        var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日");
        var firstGroup = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");
        var secondGroup = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第二组");
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, firstGroup.Guid, first.Guid);
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, firstGroup.Guid, second.Guid);
        await client.AddStudentToGroupAsync(@class.Guid, rule.Guid, secondGroup.Guid, third.Guid);

        await client.RemoveStudentsAsync(@class.Guid, [first.Guid, second.Guid]);

        Assert.Equal([third.Guid], @class.Students.Select(student => student.Guid));
        Assert.Empty(firstGroup.StudentGuids);
        Assert.Equal([third.Guid], secondGroup.StudentGuids);
    }

    [Fact]
    public async Task RenumberStudentsAsync_OrdersByCurrentIdInClassThenStudentId()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("b", "B", 5),
            TestData.NewStudent("c", "C", 2),
            TestData.NewStudent("a", "A", 2)
        ]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        @class = cached;

        await client.RenumberStudentsAsync(@class.Guid);

        Assert.Equal(1, @class.Students.Single(student => student.Id == "a").IdInClass);
        Assert.Equal(2, @class.Students.Single(student => student.Id == "c").IdInClass);
        Assert.Equal(3, @class.Students.Single(student => student.Id == "b").IdInClass);
    }

    [Fact]
    public async Task UpdateStudentAsync_ValidationFailure_RollsBackAndKeepsObjectIdentity()
    {
        var @class = TestData.NewClass(students:
        [
            TestData.NewStudent("1", "张三", 1),
            TestData.NewStudent("2", "李四", 2)
        ]);
        var (client, dataSource, cached) = await TestData.CreateClientAsync(seed: @class);
        var first = cached.Students[0];
        var second = cached.Students[1];

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => client.UpdateStudentAsync(cached.Guid, second.Guid, student =>
            {
                student.Id = "1";
                student.Name = "改名";
                student.IdInClass = 99;
                student.Gender = Gender.Female;
            }));

        Assert.Contains(exception.Errors, error => error.Code == "duplicateStudentId");
        Assert.Same(first, cached.Students[0]);
        Assert.Same(second, cached.Students[1]);
        Assert.Equal("1", first.Id);
        Assert.Equal("2", second.Id);
        Assert.Equal("李四", second.Name);
        Assert.Equal(2, second.IdInClass);
        Assert.Equal(Gender.Male, second.Gender);
        Assert.Equal(0, dataSource.SaveClassCalls);
    }

    [Fact]
    public async Task UpdateStudentAsync_CallbackThrows_RollsBack()
    {
        var @class = TestData.NewClass(students: [TestData.NewStudent("1", "张三", 1)]);
        var (client, _, cached) = await TestData.CreateClientAsync(seed: @class);
        var student = cached.Students[0];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.UpdateStudentAsync(cached.Guid, student.Guid, item =>
            {
                item.Name = "改名";
                throw new InvalidOperationException("模拟更新回调失败。");
            }));

        Assert.Equal("张三", student.Name);
        Assert.Same(student, Assert.Single(cached.Students));
    }

    [Fact]
    public async Task AddStudentsAsync_ClonesCallerInstances()
    {
        var (client, _, @class) = await TestData.CreateClientAsync();
        var property = new ExtraProperty<int> { Id = "age", Value = 12 };
        var incoming = new Student("1", "张三", 1, Gender.Male, [property]);

        var added = await client.AddStudentsAsync(@class.Guid, [incoming]);

        incoming.Name = "外部修改";
        incoming.ExtraProperties.Clear();
        property.Value = 99;

        Assert.NotSame(incoming, added[0]);
        Assert.Same(added[0], Assert.Single(@class.Students));
        Assert.Equal("张三", @class.Students[0].Name);
        Assert.Equal(12, ReadOnlyStudentService.GetExtraProperty<int>(@class.Students[0].ExtraProperties, "age"));
    }
}
