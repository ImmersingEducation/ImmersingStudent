using ImmersingStudent.Abstractions;
using ImmersingStudent.Models;

namespace ImmersingStudent.Tests.TestSupport;

/// <summary>
/// 内存数据源，保存的是快照副本，便于验证客户端是否真的落盘。
/// </summary>
internal sealed class FakeDataSource : IDataSource
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Class> _classes = [];

    public int GetClassesCalls { get; private set; }

    public int GetClassCalls { get; private set; }

    public int SaveClassCalls { get; private set; }

    public int DeleteClassCalls { get; private set; }

    public List<Class> SavedClasses { get; } = [];

    public List<Guid> DeletedClassGuids { get; } = [];

    /// <summary>
    /// 下一次保存时抛出异常。
    /// </summary>
    public bool FailNextSave { get; set; }

    /// <summary>
    /// 下一次读取全部班级时抛出异常。
    /// </summary>
    public bool FailNextGetClasses { get; set; }

    public FakeDataSource(params Class[] seed)
    {
        foreach (var @class in seed)
        {
            _classes[@class.Guid] = Clone(@class);
        }
    }

    public Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextGetClasses)
        {
            FailNextGetClasses = false;
            throw new InvalidOperationException("模拟读取失败。");
        }

        lock (_sync)
        {
            GetClassesCalls++;
            IReadOnlyList<Class> result = _classes.Values.Select(Clone).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<Class?> GetClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            GetClassCalls++;
            return Task.FromResult(_classes.TryGetValue(classGuid, out var @class) ? Clone(@class) : null);
        }
    }

    public Task SaveClassAsync(Class @class, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("模拟保存失败。");
        }

        lock (_sync)
        {
            SaveClassCalls++;
            var snapshot = Clone(@class);
            SavedClasses.Add(snapshot);
            _classes[@class.Guid] = snapshot;
        }

        return Task.CompletedTask;
    }

    public Task DeleteClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            DeleteClassCalls++;
            DeletedClassGuids.Add(classGuid);
            _classes.Remove(classGuid);
        }

        return Task.CompletedTask;
    }

    public Class? GetSavedClass(Guid classGuid)
    {
        lock (_sync)
        {
            return _classes.TryGetValue(classGuid, out var @class) ? @class : null;
        }
    }

    public bool Contains(Guid classGuid)
    {
        lock (_sync)
        {
            return _classes.ContainsKey(classGuid);
        }
    }

    public static Class Clone(Class @class)
    {
        return new Class(
            @class.Grade,
            @class.NameWithoutGrade,
            @class.Students.Select(Clone).ToList(),
            @class.GroupingRules.Select(Clone).ToList(),
            @class.ExtraProperties.Select(Clone).ToList())
        {
            Guid = @class.Guid
        };
    }

    private static Student Clone(Student student)
    {
        return new Student(
            student.Id,
            student.Name,
            student.IdInClass,
            student.Gender,
            student.ExtraProperties.Select(Clone).ToList())
        {
            Guid = student.Guid
        };
    }

    private static GroupingRule Clone(GroupingRule rule)
    {
        return new GroupingRule(
            rule.Name,
            rule.Groups.Select(Clone).ToList(),
            rule.ExtraProperties.Select(Clone).ToList())
        {
            Guid = rule.Guid
        };
    }

    private static Group Clone(Group group)
    {
        return new Group(
            group.Name,
            group.StudentGuids.ToList(),
            group.ExtraProperties.Select(Clone).ToList())
        {
            Guid = group.Guid
        };
    }

    private static ExtraProperty Clone(ExtraProperty property)
    {
        return property switch
        {
            ExtraProperty<int> value => new ExtraProperty<int>
            {
                Guid = property.Guid,
                Id = property.Id,
                Value = value.Value
            },
            ExtraProperty<string> value => new ExtraProperty<string>
            {
                Guid = property.Guid,
                Id = property.Id,
                Value = value.Value
            },
            _ => new ExtraProperty
            {
                Guid = property.Guid,
                Id = property.Id,
                Value = property.Value
            }
        };
    }
}
