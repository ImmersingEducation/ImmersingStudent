namespace ImmersingEducation.Infrastructures.ImmersingStudent.Models;

/// <summary>
/// 班级信息
/// </summary>
public sealed record Class(Grade Grade, string NameWithoutGrade, List<Student> Students, List<GroupingRule> GroupingRules, List<ExtraProperty> ExtraProperties)
{
    /// <summary>
    /// 班级 Guid
    /// </summary>
    public Guid Guid { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 年级
    /// </summary>
    public Grade Grade { get; set; } = Grade;

    /// <summary>
    /// 不含年级的班级名称
    /// </summary>
    public string NameWithoutGrade { get; set; } = NameWithoutGrade;
    
    
    /// <summary>
    /// 学生列表
    /// </summary>
    public List<Student> Students { get; set; } =  Students;
    
    /// <summary>
    /// 分组规则
    /// </summary>
    public List<GroupingRule> GroupingRules { get; set; } =  GroupingRules;

    /// <summary>
    /// 扩展属性
    /// </summary>
    public List<ExtraProperty> ExtraProperties { get; set; } = ExtraProperties;
}

/// <summary>
/// 年级枚举
/// </summary>
public enum Grade
{
    /// <summary>
    /// 一年级
    /// </summary>
    FirstGrade,
    
    /// <summary>
    /// 二年级
    /// </summary>
    SecondGrade,
    
    /// <summary>
    /// 三年级
    /// </summary>
    ThirdGrade,
    
    /// <summary>
    /// 四年级
    /// </summary>
    FourthGrade,
    
    /// <summary>
    /// 五年级
    /// </summary>
    FifthGrade,
    
    /// <summary>
    /// 六年级
    /// </summary>
    SixthGrade,
    
    /// <summary>
    /// 初一年级
    /// </summary>
    JuniorFirstGrade,
    
    /// <summary>
    /// 初二年级
    /// </summary>
    JuniorSecondGrade,
    
    /// <summary>
    /// 初三年级
    /// </summary>
    JuniorThirdGrade,
    
    /// <summary>
    /// 高一年级
    /// </summary>
    SeniorFirstGrade,
    
    /// <summary>
    /// 高二年级
    /// </summary>
    SeniorSecondGrade,
    
    /// <summary>
    /// 高三年级
    /// </summary>
    SeniorThirdGrade
}