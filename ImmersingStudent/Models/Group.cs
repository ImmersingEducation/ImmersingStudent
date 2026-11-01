namespace ImmersingStudent.Models;

/// <summary>
/// 小组信息
/// </summary>
public sealed record Group(string Name, List<Guid> StudentGuids, List<ExtraProperty>  ExtraProperties)
{
    /// <summary>
    /// 小组 Guid
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 小组名称
    /// </summary>
    public required string Name { get; set; } = Name;
    
    /// <summary>
    /// 小组成员的 Guid
    /// </summary>
    public required List<Guid> StudentGuids { get; set; } = StudentGuids;
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public required List<ExtraProperty> ExtraProperties { get; set; } = ExtraProperties;
}