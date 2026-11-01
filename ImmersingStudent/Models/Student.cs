namespace ImmersingStudent.Models;

/// <summary>
/// 学生信息
/// </summary>
public sealed record Student(string Id, string Name, int IdInClass, Gender Gender)
{
    /// <summary>
    /// 学生 Guid
    /// </summary>
    public Guid Guid { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// 学生学号
    /// </summary>
    public required string Id { get; set; } = Id;

    /// <summary>
    /// 学生姓名
    /// </summary>
    public required string Name { get; set; } = Name;

    /// <summary>
    /// 学生在班内的学号
    /// </summary>
    public required int IdInClass { get; set; } = IdInClass;

    /// <summary>
    /// 学生性别
    /// </summary>
    public required Gender Gender { get; set; } = Gender;
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