namespace ImmersingStudent.Models;

/// <summary>
/// 分组规则
/// </summary>
public sealed record GroupingRule(string Name, List<Group> Groups)
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
}