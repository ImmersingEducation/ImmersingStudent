using ImmersingStudent.Models;

namespace ImmersingStudent.Internal;

/// <summary>
/// 模型深拷贝与原地恢复工具。用于在修改失败时回滚，同时尽量保留调用方已持有的对象引用。
/// </summary>
internal static class ModelCloner
{
    public static Class Clone(Class source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Class(
            source.Grade,
            source.NameWithoutGrade,
            source.Students.Select(Clone).ToList(),
            source.GroupingRules.Select(Clone).ToList(),
            source.ExtraProperties.Select(Clone).ToList())
        {
            Guid = source.Guid
        };
    }

    public static Student Clone(Student source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Student(
            source.Id,
            source.Name,
            source.IdInClass,
            source.Gender,
            source.ExtraProperties.Select(Clone).ToList())
        {
            Guid = source.Guid
        };
    }

    public static GroupingRule Clone(GroupingRule source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new GroupingRule(
            source.Name,
            source.Groups.Select(Clone).ToList(),
            source.ExtraProperties.Select(Clone).ToList())
        {
            Guid = source.Guid
        };
    }

    public static Group Clone(Group source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new Group(
            source.Name,
            [.. source.StudentGuids],
            source.ExtraProperties.Select(Clone).ToList())
        {
            Guid = source.Guid
        };
    }

    public static ExtraProperty Clone(ExtraProperty source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var type = source.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ExtraProperty<>))
        {
            var clone = (ExtraProperty)Activator.CreateInstance(type)!;
            clone.Guid = source.Guid;
            clone.Id = source.Id;
            clone.Value = source.Value;
            return clone;
        }

        return new ExtraProperty
        {
            Guid = source.Guid,
            Id = source.Id,
            Value = source.Value
        };
    }

    /// <summary>
    /// 将快照内容复制回目标对象。已有对象按 Guid 匹配并原地更新，避免破坏调用方持有的引用。
    /// </summary>
    public static void Restore(Class target, Class snapshot)
    {
        target.Grade = snapshot.Grade;
        target.NameWithoutGrade = snapshot.NameWithoutGrade;
        RestoreStudents(target.Students, snapshot.Students);
        RestoreGroupingRules(target.GroupingRules, snapshot.GroupingRules);
        RestoreExtraProperties(target.ExtraProperties, snapshot.ExtraProperties);
    }

    private static void RestoreStudents(List<Student> target, List<Student> snapshot)
    {
        var existing = new Dictionary<Guid, Student>();
        foreach (var student in target)
        {
            existing.TryAdd(student.Guid, student);
        }

        var restored = new List<Student>(snapshot.Count);

        foreach (var source in snapshot)
        {
            if (existing.Remove(source.Guid, out var current))
            {
                current.Id = source.Id;
                current.Name = source.Name;
                current.IdInClass = source.IdInClass;
                current.Gender = source.Gender;
                RestoreExtraProperties(current.ExtraProperties, source.ExtraProperties);
                restored.Add(current);
            }
            else
            {
                restored.Add(Clone(source));
            }
        }

        target.Clear();
        target.AddRange(restored);
    }

    private static void RestoreGroupingRules(List<GroupingRule> target, List<GroupingRule> snapshot)
    {
        var existing = new Dictionary<Guid, GroupingRule>();
        foreach (var rule in target)
        {
            existing.TryAdd(rule.Guid, rule);
        }

        var restored = new List<GroupingRule>(snapshot.Count);

        foreach (var source in snapshot)
        {
            if (existing.Remove(source.Guid, out var current))
            {
                current.Name = source.Name;
                RestoreGroups(current.Groups, source.Groups);
                RestoreExtraProperties(current.ExtraProperties, source.ExtraProperties);
                restored.Add(current);
            }
            else
            {
                restored.Add(Clone(source));
            }
        }

        target.Clear();
        target.AddRange(restored);
    }

    private static void RestoreGroups(List<Group> target, List<Group> snapshot)
    {
        var existing = new Dictionary<Guid, Group>();
        foreach (var group in target)
        {
            existing.TryAdd(group.Guid, group);
        }

        var restored = new List<Group>(snapshot.Count);

        foreach (var source in snapshot)
        {
            if (existing.Remove(source.Guid, out var current))
            {
                current.Name = source.Name;
                current.StudentGuids.Clear();
                current.StudentGuids.AddRange(source.StudentGuids);
                RestoreExtraProperties(current.ExtraProperties, source.ExtraProperties);
                restored.Add(current);
            }
            else
            {
                restored.Add(Clone(source));
            }
        }

        target.Clear();
        target.AddRange(restored);
    }

    private static void RestoreExtraProperties(List<ExtraProperty> target, List<ExtraProperty> snapshot)
    {
        var existing = new Dictionary<Guid, ExtraProperty>();
        foreach (var property in target)
        {
            existing.TryAdd(property.Guid, property);
        }

        var restored = new List<ExtraProperty>(snapshot.Count);

        foreach (var source in snapshot)
        {
            if (existing.Remove(source.Guid, out var current))
            {
                current.Id = source.Id;
                current.Value = source.Value;
                restored.Add(current);
            }
            else
            {
                restored.Add(Clone(source));
            }
        }

        target.Clear();
        target.AddRange(restored);
    }
}
