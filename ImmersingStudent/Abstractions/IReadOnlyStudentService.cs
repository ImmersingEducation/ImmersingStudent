using ImmersingEducation.Infrastructures.ImmersingStudent.Enums;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Abstractions;

/// <summary>
/// 只读学生数据服务。所有查询返回缓存中的模型引用，不会修改或保存数据。
/// </summary>
public interface IReadOnlyStudentService
{
    /// <summary>
    /// 获取全部班级。
    /// </summary>
    Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找班级；不存在时返回 <see langword="null"/>。
    /// </summary>
    Task<Class?> FindClassAsync(Guid classGuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取班级；不存在时抛出异常。
    /// </summary>
    Task<Class> GetClassAsync(Guid classGuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取班级中的所有学生。
    /// </summary>
    Task<IReadOnlyList<Student>> GetStudentsAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 Guid 查找学生；不存在时返回 <see langword="null"/>。
    /// </summary>
    Task<Student?> FindStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按学号查找学生；不存在时返回 <see langword="null"/>。
    /// </summary>
    Task<Student?> FindStudentByStudentIdAsync(
        Guid classGuid,
        string studentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取班级的所有分组规则。
    /// </summary>
    Task<IReadOnlyList<GroupingRule>> GetGroupingRulesAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找分组规则；不存在时返回 <see langword="null"/>。
    /// </summary>
    Task<GroupingRule?> FindGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取某个分组规则下的所有小组。
    /// </summary>
    Task<IReadOnlyList<Group>> GetGroupsAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取某个小组的成员。
    /// </summary>
    Task<IReadOnlyList<Student>> GetGroupMembersAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取某个分组规则下尚未被分到任何小组的学生。
    /// </summary>
    Task<IReadOnlyList<Student>> GetUngroupedStudentsAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按姓名或学号搜索学生，可同时按性别筛选。
    /// </summary>
    Task<IReadOnlyList<Student>> SearchStudentsAsync(
        Guid classGuid,
        string keyword,
        Gender? gender = null,
        CancellationToken cancellationToken = default);
}
