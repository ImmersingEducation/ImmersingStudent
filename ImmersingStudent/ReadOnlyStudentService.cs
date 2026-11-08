using ImmersingEducation.Infrastructures.ImmersingStudent.Abstractions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Internal;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

namespace ImmersingEducation.Infrastructures.ImmersingStudent;

/// <summary>
/// 只读服务。所有查询返回缓存中的模型引用，不会修改或保存数据。
/// </summary>
public sealed class ReadOnlyStudentService : IReadOnlyStudentService
{
    private readonly ClassStore _store;

    public ReadOnlyStudentService(IDataSource dataSource)
        : this(new ClassStore(dataSource))
    {
    }

    internal ReadOnlyStudentService(ClassStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 获取全部班级。
    /// </summary>
    public async Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 查找班级；不存在时返回 <see langword="null"/>。
    /// </summary>
    public async Task<Class?> FindClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return await _store.FindClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取班级；不存在时抛出 <see cref="ClassNotFoundException"/>。
    /// </summary>
    public async Task<Class> GetClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 获取班级中的所有学生。
    /// </summary>
    public async Task<IReadOnlyList<Student>> GetStudentsAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            return @class.Students;
        }
    }

    /// <summary>
    /// 按 Guid 查找学生；不存在时返回 <see langword="null"/>。
    /// </summary>
    public async Task<Student?> FindStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            return @class.Students.FirstOrDefault(student => student.Guid == studentGuid);
        }
    }

    /// <summary>
    /// 按学号查找学生；不存在时返回 <see langword="null"/>。
    /// </summary>
    public async Task<Student?> FindStudentByStudentIdAsync(
        Guid classGuid,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentId);

        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            return @class.Students.FirstOrDefault(
                student => string.Equals(student.Id, studentId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 获取班级的所有分组规则。
    /// </summary>
    public async Task<IReadOnlyList<GroupingRule>> GetGroupingRulesAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            return @class.GroupingRules;
        }
    }

    /// <summary>
    /// 查找分组规则；不存在时返回 <see langword="null"/>。
    /// </summary>
    public async Task<GroupingRule?> FindGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            return @class.GroupingRules.FirstOrDefault(rule => rule.Guid == ruleGuid);
        }
    }

    /// <summary>
    /// 获取某个分组规则下的所有小组。
    /// </summary>
    public async Task<IReadOnlyList<Group>> GetGroupsAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            var rule = @class.GroupingRules.FirstOrDefault(item => item.Guid == ruleGuid)
                       ?? throw new GroupingRuleNotFoundException(classGuid, ruleGuid);

            return rule.Groups;
        }
    }

    /// <summary>
    /// 获取某个小组的成员。
    /// </summary>
    public async Task<IReadOnlyList<Student>> GetGroupMembersAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            var rule = @class.GroupingRules.FirstOrDefault(item => item.Guid == ruleGuid)
                       ?? throw new GroupingRuleNotFoundException(classGuid, ruleGuid);
            var group = rule.Groups.FirstOrDefault(item => item.Guid == groupGuid)
                        ?? throw new GroupNotFoundException(classGuid, groupGuid);

            var students = @class.Students.ToDictionary(student => student.Guid);
            return group.StudentGuids
                .Where(students.ContainsKey)
                .Select(studentGuid => students[studentGuid])
                .ToList();
        }
    }

    /// <summary>
    /// 获取某个分组规则下尚未被分到任何小组的学生。
    /// </summary>
    public async Task<IReadOnlyList<Student>> GetUngroupedStudentsAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            var rule = @class.GroupingRules.FirstOrDefault(item => item.Guid == ruleGuid)
                       ?? throw new GroupingRuleNotFoundException(classGuid, ruleGuid);

            var groupedStudentGuids = rule.Groups
                .SelectMany(group => group.StudentGuids)
                .ToHashSet();

            return @class.Students
                .Where(student => !groupedStudentGuids.Contains(student.Guid))
                .ToList();
        }
    }

    /// <summary>
    /// 按姓名或学号搜索学生，可同时按性别筛选。
    /// </summary>
    public async Task<IReadOnlyList<Student>> SearchStudentsAsync(
        Guid classGuid,
        string keyword,
        Gender? gender = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

            return @class.Students
                .Where(student => gender is null || student.Gender == gender)
                .Where(student => string.IsNullOrWhiteSpace(keyword)
                                  || student.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                  || student.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// 班级显示名称，例如“初二年级3班”。
    /// </summary>
    public static string GetDisplayName(Class @class)
    {
        ArgumentNullException.ThrowIfNull(@class);
        return $"{GetGradeName(@class.Grade)}{@class.NameWithoutGrade}";
    }

    /// <summary>
    /// 获取年级的中文名称。
    /// </summary>
    public static string GetGradeName(Grade grade)
    {
        return grade switch
        {
            Grade.FirstGrade => "一年级",
            Grade.SecondGrade => "二年级",
            Grade.ThirdGrade => "三年级",
            Grade.FourthGrade => "四年级",
            Grade.FifthGrade => "五年级",
            Grade.SixthGrade => "六年级",
            Grade.JuniorFirstGrade => "初一年级",
            Grade.JuniorSecondGrade => "初二年级",
            Grade.JuniorThirdGrade => "初三年级",
            Grade.SeniorFirstGrade => "高一年级",
            Grade.SeniorSecondGrade => "高二年级",
            Grade.SeniorThirdGrade => "高三年级",
            _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, "未知年级。")
        };
    }

    /// <summary>
    /// 获取指定 Id 的扩展属性值；找不到或类型不匹配时返回默认值。
    /// </summary>
    public static T? GetExtraProperty<T>(IEnumerable<ExtraProperty> properties, string id)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(id);

        var property = properties.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.Ordinal));

        return property?.Value is T value ? value : default;
    }
}
