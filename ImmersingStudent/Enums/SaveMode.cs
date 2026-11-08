namespace ImmersingStudent.Enums;

/// <summary>
/// 保存策略。
/// </summary>
public enum SaveMode
{
    /// <summary>
    /// 改动只写入内存缓存，调用 <see cref="StudentService.SaveAsync(CancellationToken)"/> 时才落盘。
    /// </summary>
    Manual,

    /// <summary>
    /// 每次通过 <see cref="StudentService"/> 完成的修改都会在方法返回前自动保存。
    /// </summary>
    Auto
}
