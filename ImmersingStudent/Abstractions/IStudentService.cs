using ImmersingEducation.Infrastructures.ImmersingStudent.Enums;
using ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

namespace ImmersingEducation.Infrastructures.ImmersingStudent.Abstractions;

/// <summary>
/// 可写学生数据服务，负责数据修改、校验和保存。
/// </summary>
/// <remarks>
/// 查询返回的是缓存中的模型引用。通过本接口方法修改会自动校验（并在 <see cref="SaveMode.Auto"/> 下保存），
/// 但直接修改返回的模型（例如 <c>student.Name = "..."</c>）不会被感知，需要手动调用
/// <see cref="SaveAsync(CancellationToken)"/>。
/// </remarks>
public interface IStudentService
{
    /// <summary>
    /// 与当前服务共享缓存的只读视图。
    /// </summary>
    IReadOnlyStudentService ReadOnly { get; }

    #region 班级

    /// <summary>
    /// 新建班级。
    /// </summary>
    Task<Class> CreateClassAsync(
        Grade grade,
        string nameWithoutGrade,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改班级名称。
    /// </summary>
    Task RenameClassAsync(
        Guid classGuid,
        string nameWithoutGrade,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改班级年级。
    /// </summary>
    Task SetGradeAsync(
        Guid classGuid,
        Grade grade,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除班级。手动保存模式下会等到 <see cref="SaveAsync(CancellationToken)"/> 才真正删除。
    /// </summary>
    Task DeleteClassAsync(Guid classGuid, CancellationToken cancellationToken = default);

    #endregion

    #region 学生

    /// <summary>
    /// 添加学生。未指定班内学号时自动取当前最大值加一。
    /// </summary>
    Task<Student> AddStudentAsync(
        Guid classGuid,
        string studentId,
        string name,
        Gender gender,
        int? idInClass = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量添加学生。任意一个学生不合法时整批不会写入。
    /// </summary>
    Task<IReadOnlyList<Student>> AddStudentsAsync(
        Guid classGuid,
        IEnumerable<Student> students,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改学生。修改后会自动校验学号、班内学号等约束。
    /// </summary>
    Task UpdateStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        Action<Student> update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除学生，并同步从所有分组规则的小组中移除。
    /// </summary>
    Task RemoveStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除学生，并同步从所有分组规则的小组中移除。
    /// </summary>
    Task RemoveStudentsAsync(
        Guid classGuid,
        IEnumerable<Guid> studentGuids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按当前班内学号重新编号为 1..N。
    /// </summary>
    Task RenumberStudentsAsync(Guid classGuid, CancellationToken cancellationToken = default);

    #endregion

    #region 分组规则

    /// <summary>
    /// 新建分组规则。
    /// </summary>
    Task<GroupingRule> CreateGroupingRuleAsync(
        Guid classGuid,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重命名分组规则。
    /// </summary>
    Task RenameGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除分组规则及其下所有小组。
    /// </summary>
    Task DeleteGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default);

    #endregion

    #region 小组

    /// <summary>
    /// 新建小组。
    /// </summary>
    Task<Group> CreateGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重命名小组。
    /// </summary>
    Task RenameGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除小组。
    /// </summary>
    Task DeleteGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把学生加入小组。学生已在该组时不作处理，已在同规则其他组时抛出异常。
    /// </summary>
    Task AddStudentToGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把学生移出小组。学生本来不在该组时不作处理。
    /// </summary>
    Task RemoveStudentFromGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把学生从一个小组移动到另一个小组。
    /// </summary>
    Task MoveStudentToGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid fromGroupGuid,
        Guid toGroupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default);

    #endregion

    #region 扩展属性

    /// <summary>
    /// 设置实体的扩展属性，不存在则新建。
    /// </summary>
    Task SetExtraPropertyAsync(
        Guid classGuid,
        Guid ownerGuid,
        ExtraPropertyOwner ownerKind,
        string id,
        object value,
        CancellationToken cancellationToken = default);

    #endregion

    #region 校验、保存与刷新

    /// <summary>
    /// 校验指定班级，返回所有错误；没有错误时返回空列表。
    /// </summary>
    Task<IReadOnlyList<ValidationError>> ValidateAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存所有未保存的修改和删除。保存前会校验当前缓存中的班级。
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存指定班级。保存前会校验该班级。
    /// </summary>
    Task SaveAsync(Guid classGuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从数据源重新加载全部班级。已返回给调用方的旧引用将不再与缓存同步。
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 从数据源重新加载指定班级。已返回给调用方的旧引用将不再与缓存同步。
    /// </summary>
    Task ReloadAsync(Guid classGuid, CancellationToken cancellationToken = default);

    #endregion
}
