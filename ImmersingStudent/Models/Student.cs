namespace ImmersingEducation.Infrastructures.ImmersingStudent.Models;

/// <summary>
/// 学生信息
/// </summary>
public sealed record Student(string Id, string Name, int IdInClass, Gender Gender, List<ExtraProperty> ExtraProperties)
{
    /// <summary>
    /// 学生 Guid
    /// </summary>
    public Guid Guid { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// 学生学号
    /// </summary>
    public string Id { get; set; } = Id;

    /// <summary>
    /// 学生姓名
    /// </summary>
    public string Name { get; set; } = Name;

    /// <summary>
    /// 学生在班内的学号
    /// </summary>
    public int IdInClass { get; set; } = IdInClass;

    /// <summary>
    /// 学生性别
    /// </summary>
    public Gender Gender { get; set; } = Gender;
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public List<ExtraProperty>  ExtraProperties { get; set; } = ExtraProperties;
}


/// <summary>
/// 学生性别枚举
/// </summary>
public enum Gender
{
    /// <summary>
    /// 男
    /// </summary>
    Male,
    
    /// <summary>
    /// 女
    /// </summary>
    Female
}