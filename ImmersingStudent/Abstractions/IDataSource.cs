using ImmersingStudent.Models;

namespace ImmersingStudent.Abstractions;

/// <summary>
/// 数据源接口，提供给 <see cref="Client"/> 和 <see cref="ReadOnlyClient"/> 使用。
/// 以 <see cref="Class"/> 作为聚合根，学生、分组规则和小组随班级一起读写。
/// </summary>
public interface IDataSource
{
    /// <summary>
    /// 读取全部班级。
    /// </summary>
    Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取单个班级；不存在时返回 <see langword="null"/>。
    /// </summary>
    Task<Class?> GetClassAsync(Guid classGuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存班级（新增或更新）。
    /// </summary>
    Task SaveClassAsync(Class @class, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除班级。
    /// </summary>
    Task DeleteClassAsync(Guid classGuid, CancellationToken cancellationToken = default);
}
