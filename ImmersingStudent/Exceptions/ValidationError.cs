namespace ImmersingStudent.Exceptions;

/// <summary>
/// 一条数据校验错误，作为 <see cref="ValidationException"/> 的载荷。
/// </summary>
/// <param name="Code">错误代码，便于程序判断。</param>
/// <param name="Message">面向用户的错误信息。</param>
/// <param name="EntityGuid">相关实体的 Guid（如果适用）。</param>
public sealed record ValidationError(string Code, string Message, Guid? EntityGuid = null);
