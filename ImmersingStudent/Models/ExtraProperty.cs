namespace ImmersingEducation.Infrastructures.ImmersingStudent.Models;

public record ExtraProperty
{
    public Guid Guid { get; set; } = Guid.NewGuid();
    public required string Id { get; set; }
    public object Value { get; set; } = null!;
}

public sealed record ExtraProperty<T> : ExtraProperty
{
    public new required T Value
    {
        get => (T)base.Value;
        set => base.Value = value!;
    }
}