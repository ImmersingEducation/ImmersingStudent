using ImmersingEducation.Infrastructures.ImmersingStudent.Enums;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Tests.TestSupport;

internal static class TestData
{
    public static Class NewClass(
        Grade grade = Grade.JuniorSecondGrade,
        string nameWithoutGrade = "3班",
        IEnumerable<Student>? students = null,
        IEnumerable<GroupingRule>? rules = null)
    {
        return new Class(
            grade,
            nameWithoutGrade,
            students?.ToList() ?? [],
            rules?.ToList() ?? [],
            []);
    }

    public static Student NewStudent(
        string id,
        string name = "学生",
        int idInClass = 1,
        Gender gender = Gender.Male)
    {
        return new Student(id, name, idInClass, gender, []);
    }

    /// <summary>
    /// 创建一个已保存到数据源的班级，并返回可写客户端。
    /// </summary>
    public static StudentService CreateClient(
        out FakeDataSource dataSource,
        out Class @class,
        SaveMode saveMode = SaveMode.Manual,
        Class? seed = null)
    {
        @class = seed ?? NewClass();
        dataSource = new FakeDataSource(@class);
        return new StudentService(dataSource, saveMode);
    }

    public static StudentService CreateClient(out FakeDataSource dataSource, SaveMode saveMode = SaveMode.Manual)
    {
        dataSource = new FakeDataSource();
        return new StudentService(dataSource, saveMode);
    }

    /// <summary>
    /// 创建客户端并返回数据源中的班级在缓存中的实例。
    /// </summary>
    public static async Task<(StudentService StudentService, FakeDataSource DataSource, Class Class)> CreateClientAsync(
        SaveMode saveMode = SaveMode.Manual,
        Class? seed = null)
    {
        var seedClass = seed ?? NewClass();
        var dataSource = new FakeDataSource(seedClass);
        var client = new StudentService(dataSource, saveMode);
        var cached = await client.ReadOnly.GetClassAsync(seedClass.Guid);

        return (client, dataSource, cached);
    }
}
