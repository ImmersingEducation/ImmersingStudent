namespace ImmersingStudent.Models;

/// <summary>
/// 分组规则
/// </summary>
public sealed record GroupingRule(string Name, List<Group> Groups, List<ExtraProperty> ExtraProperties)
{
    /// <summary>
    /// 规则 Guid
    /// </summary>
    public Guid Guid { get; init; } =  Guid.NewGuid();

    /// <summary>
    /// 规则名称
    /// </summary>
    public required string Name { get; set; } = Name;

    /// <summary>
    /// 规则内小组
    /// </summary>
    public required List<Group> Groups { get; set; } = Groups;
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public required List<ExtraProperty> ExtraProperties { get; set; } = ExtraProperties;
}