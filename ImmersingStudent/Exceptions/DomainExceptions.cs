namespace ImmersingEducation.Infrastructures.ImmersingStudent.Exceptions;

/// <summary>
/// 本库所有业务异常的基类。
/// </summary>
public class ImmersingStudentException : Exception
{
    public ImmersingStudentException(string message) : base(message)
    {
    }

    public ImmersingStudentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// 找不到班级。
/// </summary>
public sealed class ClassNotFoundException(Guid classGuid)
    : ImmersingStudentException($"找不到 Guid 为 {classGuid} 的班级。")
{
    public Guid ClassGuid { get; } = classGuid;
}

/// <summary>
/// 找不到学生。
/// </summary>
public sealed class StudentNotFoundException(Guid classGuid, Guid studentGuid)
    : ImmersingStudentException($"班级 {classGuid} 中找不到 Guid 为 {studentGuid} 的学生。")
{
    public Guid ClassGuid { get; } = classGuid;
    public Guid StudentGuid { get; } = studentGuid;
}

/// <summary>
/// 找不到分组规则。
/// </summary>
public sealed class GroupingRuleNotFoundException(Guid classGuid, Guid ruleGuid)
    : ImmersingStudentException($"班级 {classGuid} 中找不到 Guid 为 {ruleGuid} 的分组规则。")
{
    public Guid ClassGuid { get; } = classGuid;
    public Guid RuleGuid { get; } = ruleGuid;
}

/// <summary>
/// 找不到小组。
/// </summary>
public sealed class GroupNotFoundException(Guid classGuid, Guid groupGuid)
    : ImmersingStudentException($"班级 {classGuid} 中找不到 Guid 为 {groupGuid} 的小组。")
{
    public Guid ClassGuid { get; } = classGuid;
    public Guid GroupGuid { get; } = groupGuid;
}

/// <summary>
/// 学号重复。
/// </summary>
public sealed class DuplicateStudentException(string studentId)
    : ImmersingStudentException($"学号 {studentId} 已存在。")
{
    public string StudentId { get; } = studentId;
}

/// <summary>
/// 数据校验失败。
/// </summary>
public sealed class ValidationException : ImmersingStudentException
{
    public ValidationException(IReadOnlyList<ValidationError> errors)
        : base("数据校验失败：" + string.Join("；", errors.Select(error => error.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<ValidationError> Errors { get; }
}
