using ImmersingStudent.Abstractions;
using ImmersingStudent.Enums;
using ImmersingStudent.Exceptions;
using ImmersingStudent.Internal;
using ImmersingStudent.Models;

namespace ImmersingStudent;

/// <summary>
/// 可写客户端，负责数据修改、校验和保存。
/// </summary>
/// <remarks>
/// 查询返回的是缓存中的模型引用。通过本类方法修改会自动校验（并在 <see cref="SaveMode.Auto"/> 下保存），
/// 但直接修改返回的模型（例如 <c>student.Name = "..."</c>）不会被感知，需要手动调用
/// <see cref="SaveAsync(CancellationToken)"/>。
/// </remarks>
public sealed class Client
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ClassStore _store;
    private readonly SaveMode _saveMode;

    public Client(IDataSource dataSource, SaveMode saveMode = SaveMode.Manual)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        _store = new ClassStore(dataSource);
        _saveMode = saveMode;
        ReadOnly = new ReadOnlyClient(_store);
    }

    /// <summary>
    /// 与本客户端共享缓存的只读视图。
    /// </summary>
    public ReadOnlyClient ReadOnly { get; }

    #region 班级

    /// <summary>
    /// 新建班级。
    /// </summary>
    public async Task<Class> CreateClassAsync(
        Grade grade,
        string nameWithoutGrade,
        CancellationToken cancellationToken = default)
    {
        ValidateClassName(nameWithoutGrade);

        // 班级重名检查跨越所有班级，必须和创建动作一起串行化。
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
            EnsureClassNameAvailable(classes, grade, nameWithoutGrade);

            var @class = new Class(grade, nameWithoutGrade, [], [], []);
            await _store.AddAsync(@class, cancellationToken).ConfigureAwait(false);
            await PersistAsync(@class.Guid, cancellationToken).ConfigureAwait(false);

            return @class;
        }
    }

    /// <summary>
    /// 修改班级名称。
    /// </summary>
    public async Task RenameClassAsync(
        Guid classGuid,
        string nameWithoutGrade,
        CancellationToken cancellationToken = default)
    {
        ValidateClassName(nameWithoutGrade);

        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
            var @class = classes.FirstOrDefault(item => item.Guid == classGuid)
                         ?? throw new ClassNotFoundException(classGuid);
            EnsureClassNameAvailable(classes, @class.Grade, nameWithoutGrade, classGuid);

            await MutateCoreAsync(
                    classGuid,
                    item => item.NameWithoutGrade = nameWithoutGrade,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 修改班级年级。
    /// </summary>
    public async Task SetGradeAsync(
        Guid classGuid,
        Grade grade,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
            var @class = classes.FirstOrDefault(item => item.Guid == classGuid)
                         ?? throw new ClassNotFoundException(classGuid);
            EnsureClassNameAvailable(classes, grade, @class.NameWithoutGrade, classGuid);

            await MutateCoreAsync(classGuid, item => item.Grade = grade, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 删除班级。手动保存模式下会等到 <see cref="SaveAsync(CancellationToken)"/> 才真正删除。
    /// </summary>
    public async Task DeleteClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            // 先确保班级已加载，这样未提前查询时也能正确删除。
            await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);

            await _store.DeleteClassAsync(
                    classGuid,
                    immediate: _saveMode == SaveMode.Auto,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    #endregion

    #region 学生

    /// <summary>
    /// 添加学生。未指定班内学号时自动取当前最大值加一。
    /// </summary>
    public Task<Student> AddStudentAsync(
        Guid classGuid,
        string studentId,
        string name,
        Gender gender,
        int? idInClass = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentId);
        ArgumentNullException.ThrowIfNull(name);

        return MutateAsync(
            classGuid,
            @class =>
            {
                if (string.IsNullOrWhiteSpace(studentId))
                {
                    throw new ValidationException(
                        [new ValidationError("studentIdEmpty", "学号不能为空。")]);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ValidationException(
                        [new ValidationError("studentNameEmpty", "学生姓名不能为空。")]);
                }

                if (@class.Students.Any(
                        student => string.Equals(student.Id, studentId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new DuplicateStudentException(studentId);
                }

                var nextIdInClass = idInClass
                                    ?? (@class.Students.Count == 0
                                        ? 1
                                        : @class.Students.Max(student => student.IdInClass) + 1);

                if (nextIdInClass <= 0)
                {
                    throw new ValidationException(
                        [new ValidationError("invalidIdInClass", $"班内学号 {nextIdInClass} 不合法。")]);
                }

                if (@class.Students.Any(student => student.IdInClass == nextIdInClass))
                {
                    throw new ValidationException(
                        [new ValidationError("duplicateIdInClass", $"班内学号 {nextIdInClass} 已存在。")]);
                }

                var student = new Student(studentId, name, nextIdInClass, gender, []);
                @class.Students.Add(student);
                return student;
            },
            cancellationToken);
    }

    /// <summary>
    /// 批量添加学生。任意一个学生不合法时整批不会写入。
    /// </summary>
    public Task<IReadOnlyList<Student>> AddStudentsAsync(
        Guid classGuid,
        IEnumerable<Student> students,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(students);

        // 复制调用方传入的实例，避免调用方后续修改绕过客户端校验和脏标记。
        var incoming = students.Select(ModelCloner.Clone).ToList();

        return MutateAsync<IReadOnlyList<Student>>(
            classGuid,
            @class =>
            {
                var studentIds = new HashSet<string>(
                    @class.Students.Select(student => student.Id),
                    IdComparer);
                var idsInClass = new HashSet<int>(
                    @class.Students.Select(student => student.IdInClass));
                var studentGuids = new HashSet<Guid>(
                    @class.Students.Select(student => student.Guid));

                foreach (var student in incoming)
                {
                    if (string.IsNullOrWhiteSpace(student.Id))
                    {
                        throw new ValidationException(
                            [new ValidationError("studentIdEmpty", "学号不能为空。")]);
                    }

                    if (string.IsNullOrWhiteSpace(student.Name))
                    {
                        throw new ValidationException(
                            [new ValidationError("studentNameEmpty", "学生姓名不能为空。")]);
                    }

                    if (!studentIds.Add(student.Id))
                    {
                        throw new DuplicateStudentException(student.Id);
                    }

                    if (student.IdInClass <= 0)
                    {
                        throw new ValidationException(
                            [new ValidationError("invalidIdInClass", $"班内学号 {student.IdInClass} 不合法。")]);
                    }

                    if (!idsInClass.Add(student.IdInClass))
                    {
                        throw new ValidationException(
                            [new ValidationError("duplicateIdInClass", $"班内学号 {student.IdInClass} 已存在。")]);
                    }

                    if (!studentGuids.Add(student.Guid))
                    {
                        throw new ValidationException(
                            [new ValidationError("duplicateStudentGuid", "学生 Guid 重复。", student.Guid)]);
                    }
                }

                @class.Students.AddRange(incoming);
                return incoming;
            },
            cancellationToken);
    }

    /// <summary>
    /// 修改学生。修改后会自动校验学号、班内学号等约束。
    /// </summary>
    public Task UpdateStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        Action<Student> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        return MutateAsync(
            classGuid,
            @class =>
            {
                var student = FindStudent(@class, classGuid, studentGuid);
                update(student);
            },
            cancellationToken);
    }

    /// <summary>
    /// 删除学生，并同步从所有分组规则的小组中移除。
    /// </summary>
    public Task RemoveStudentAsync(
        Guid classGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                FindStudent(@class, classGuid, studentGuid);
                @class.Students.RemoveAll(student => student.Guid == studentGuid);
                RemoveStudentFromAllGroups(@class, studentGuid);
            },
            cancellationToken);
    }

    /// <summary>
    /// 批量删除学生，并同步从所有分组规则的小组中移除。
    /// </summary>
    public Task RemoveStudentsAsync(
        Guid classGuid,
        IEnumerable<Guid> studentGuids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(studentGuids);

        var guids = studentGuids.ToHashSet();

        return MutateAsync(
            classGuid,
            @class =>
            {
                @class.Students.RemoveAll(student => guids.Contains(student.Guid));

                foreach (var studentGuid in guids)
                {
                    RemoveStudentFromAllGroups(@class, studentGuid);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// 按当前班内学号重新编号为 1..N。
    /// </summary>
    public Task RenumberStudentsAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                var ordered = @class.Students
                    .OrderBy(student => student.IdInClass)
                    .ThenBy(student => student.Id, IdComparer)
                    .ToList();

                for (var index = 0; index < ordered.Count; index++)
                {
                    ordered[index].IdInClass = index + 1;
                }
            },
            cancellationToken);
    }

    #endregion

    #region 分组规则

    /// <summary>
    /// 新建分组规则。
    /// </summary>
    public Task<GroupingRule> CreateGroupingRuleAsync(
        Guid classGuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateRuleName(name);

        return MutateAsync(
            classGuid,
            @class =>
            {
                EnsureRuleNameAvailable(@class, name);

                var rule = new GroupingRule(name, [], []);
                @class.GroupingRules.Add(rule);
                return rule;
            },
            cancellationToken);
    }

    /// <summary>
    /// 重命名分组规则。
    /// </summary>
    public Task RenameGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateRuleName(name);

        return MutateAsync(
            classGuid,
            @class =>
            {
                var rule = FindRule(@class, classGuid, ruleGuid);
                EnsureRuleNameAvailable(@class, name, ruleGuid);
                rule.Name = name;
            },
            cancellationToken);
    }

    /// <summary>
    /// 删除分组规则及其下所有小组。
    /// </summary>
    public Task DeleteGroupingRuleAsync(
        Guid classGuid,
        Guid ruleGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                var rule = FindRule(@class, classGuid, ruleGuid);
                @class.GroupingRules.Remove(rule);
            },
            cancellationToken);
    }

    #endregion

    #region 小组

    /// <summary>
    /// 新建小组。
    /// </summary>
    public Task<Group> CreateGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateGroupName(name);

        return MutateAsync(
            classGuid,
            @class =>
            {
                var rule = FindRule(@class, classGuid, ruleGuid);
                EnsureGroupNameAvailable(rule, name);

                var group = new Group(name, [], []);
                rule.Groups.Add(group);
                return group;
            },
            cancellationToken);
    }

    /// <summary>
    /// 重命名小组。
    /// </summary>
    public Task RenameGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        string name,
        CancellationToken cancellationToken = default)
    {
        ValidateGroupName(name);

        return MutateAsync(
            classGuid,
            @class =>
            {
                var rule = FindRule(@class, classGuid, ruleGuid);
                var group = FindGroup(rule, classGuid, groupGuid);
                EnsureGroupNameAvailable(rule, name, groupGuid);
                group.Name = name;
            },
            cancellationToken);
    }

    /// <summary>
    /// 删除小组。
    /// </summary>
    public Task DeleteGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                var rule = FindRule(@class, classGuid, ruleGuid);
                var group = FindGroup(rule, classGuid, groupGuid);
                rule.Groups.Remove(group);
            },
            cancellationToken);
    }

    /// <summary>
    /// 把学生加入小组。学生已在该组时不作处理，已在同规则其他组时抛出异常。
    /// </summary>
    public Task AddStudentToGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                FindStudent(@class, classGuid, studentGuid);
                var rule = FindRule(@class, classGuid, ruleGuid);
                var group = FindGroup(rule, classGuid, groupGuid);

                if (group.StudentGuids.Contains(studentGuid))
                {
                    return;
                }

                var existingGroup = rule.Groups.FirstOrDefault(
                    item => item.Guid != groupGuid && item.StudentGuids.Contains(studentGuid));

                if (existingGroup is not null)
                {
                    throw new ValidationException(
                        [new ValidationError(
                            "studentAlreadyGrouped",
                            $"学生已在小组“{existingGroup.Name}”中。",
                            studentGuid)]);
                }

                group.StudentGuids.Add(studentGuid);
            },
            cancellationToken);
    }

    /// <summary>
    /// 把学生移出小组。学生本来不在该组时不作处理。
    /// </summary>
    public Task RemoveStudentFromGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid groupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                FindStudent(@class, classGuid, studentGuid);
                var rule = FindRule(@class, classGuid, ruleGuid);
                var group = FindGroup(rule, classGuid, groupGuid);
                group.StudentGuids.Remove(studentGuid);
            },
            cancellationToken);
    }

    /// <summary>
    /// 把学生从一个小组移动到另一个小组。
    /// </summary>
    public Task MoveStudentToGroupAsync(
        Guid classGuid,
        Guid ruleGuid,
        Guid fromGroupGuid,
        Guid toGroupGuid,
        Guid studentGuid,
        CancellationToken cancellationToken = default)
    {
        return MutateAsync(
            classGuid,
            @class =>
            {
                FindStudent(@class, classGuid, studentGuid);
                var rule = FindRule(@class, classGuid, ruleGuid);
                var fromGroup = FindGroup(rule, classGuid, fromGroupGuid);
                var toGroup = FindGroup(rule, classGuid, toGroupGuid);

                if (!fromGroup.StudentGuids.Contains(studentGuid))
                {
                    throw new ValidationException(
                        [new ValidationError(
                            "studentNotInSourceGroup",
                            $"学生不在小组“{fromGroup.Name}”中。",
                            studentGuid)]);
                }

                var otherGroup = rule.Groups.FirstOrDefault(
                    item => item.Guid != fromGroupGuid
                            && item.Guid != toGroupGuid
                            && item.StudentGuids.Contains(studentGuid));

                if (otherGroup is not null)
                {
                    throw new ValidationException(
                        [new ValidationError(
                            "studentAlreadyGrouped",
                            $"学生已在小组“{otherGroup.Name}”中。",
                            studentGuid)]);
                }

                fromGroup.StudentGuids.Remove(studentGuid);

                if (!toGroup.StudentGuids.Contains(studentGuid))
                {
                    toGroup.StudentGuids.Add(studentGuid);
                }
            },
            cancellationToken);
    }

    #endregion

    #region 扩展属性

    /// <summary>
    /// 设置实体的扩展属性，不存在则新建。
    /// </summary>
    public Task SetExtraPropertyAsync(
        Guid classGuid,
        Guid ownerGuid,
        ExtraPropertyOwner ownerKind,
        string id,
        object value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("扩展属性 Id 不能为空。", nameof(id));
        }

        return MutateAsync(
            classGuid,
            @class =>
            {
                var properties = ownerKind switch
                {
                    ExtraPropertyOwner.Class => ownerGuid == classGuid
                        ? @class.ExtraProperties
                        : throw new ValidationException(
                            [new ValidationError(
                                "invalidOwnerGuid",
                                "班级扩展属性的 ownerGuid 必须等于 classGuid。")]),

                    ExtraPropertyOwner.Student => FindStudent(@class, classGuid, ownerGuid).ExtraProperties,
                    ExtraPropertyOwner.GroupingRule => FindRule(@class, classGuid, ownerGuid).ExtraProperties,
                    ExtraPropertyOwner.Group => FindGroup(@class, classGuid, ownerGuid).ExtraProperties,
                    _ => throw new ArgumentOutOfRangeException(nameof(ownerKind), ownerKind, "未知的扩展属性所属类型。")
                };

                var property = properties.FirstOrDefault(
                    item => string.Equals(item.Id, id, StringComparison.Ordinal));

                if (property is null)
                {
                    properties.Add(new ExtraProperty { Id = id, Value = value });
                }
                else
                {
                    property.Value = value;
                }
            },
            cancellationToken);
    }

    #endregion

    #region 校验、保存与刷新

    /// <summary>
    /// 校验指定班级，返回所有错误；没有错误时返回空列表。
    /// </summary>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        Guid classGuid,
        CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
            var @class = classes.FirstOrDefault(item => item.Guid == classGuid)
                         ?? throw new ClassNotFoundException(classGuid);

            return ValidateClass(@class, classes);
        }
    }

    /// <summary>
    /// 保存所有未保存的修改和删除。保存前会校验当前缓存中的班级。
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_store.HasDirtyClasses)
            {
                var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
                EnsureAllSavable(classes);
            }

            await _store.SaveDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 保存指定班级。保存前会校验该班级。
    /// </summary>
    public async Task SaveAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        using (await _store.LockReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            var classes = await _store.GetClassesAsync(cancellationToken).ConfigureAwait(false);
            EnsureSavable(@class, classes);

            await _store.SaveClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从数据源重新加载全部班级。已返回给调用方的旧引用将不再与缓存同步。
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            await _store.ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 从数据源重新加载指定班级。已返回给调用方的旧引用将不再与缓存同步。
    /// </summary>
    public async Task ReloadAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            await _store.ReloadAsync(classGuid, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region 内部辅助

    private async Task<T> MutateAsync<T>(
        Guid classGuid,
        Func<Class, T> mutate,
        CancellationToken cancellationToken)
    {
        using (await _store.LockWriteAsync(cancellationToken).ConfigureAwait(false))
        {
            return await MutateCoreAsync(classGuid, mutate, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<T> MutateCoreAsync<T>(
        Guid classGuid,
        Func<Class, T> mutate,
        CancellationToken cancellationToken)
    {
        using (await _store.LockClassAsync(classGuid, cancellationToken).ConfigureAwait(false))
        {
            var @class = await _store.GetClassAsync(classGuid, cancellationToken).ConfigureAwait(false);
            var snapshot = ModelCloner.Clone(@class);

            T result;
            try
            {
                result = mutate(@class);
                EnsureValid(@class);
            }
            catch
            {
                // 校验或用户回调失败时恢复原状态，避免把非法数据留在缓存里。
                ModelCloner.Restore(@class, snapshot);
                throw;
            }

            await PersistAsync(classGuid, cancellationToken).ConfigureAwait(false);

            return result;
        }
    }

    private async Task MutateAsync(
        Guid classGuid,
        Action<Class> mutate,
        CancellationToken cancellationToken)
    {
        await MutateAsync<object?>(
                classGuid,
                @class =>
                {
                    mutate(@class);
                    return null;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PersistAsync(Guid classGuid, CancellationToken cancellationToken)
    {
        if (_saveMode == SaveMode.Auto)
        {
            try
            {
                await _store.SaveClassCoreAsync(classGuid, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 保存失败时保留脏标记，方便调用方稍后重试 SaveAsync。
                _store.MarkDirty(classGuid);
                throw;
            }
        }
        else
        {
            _store.MarkDirty(classGuid);
        }
    }

    private static void EnsureValid(Class @class)
    {
        var errors = ValidateClass(@class);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void EnsureSavable(Class @class, IReadOnlyCollection<Class> allClasses)
    {
        var errors = ValidateClass(@class, allClasses);

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void EnsureAllSavable(IReadOnlyCollection<Class> classes)
    {
        var errors = new List<ValidationError>();

        foreach (var @class in classes)
        {
            errors.AddRange(ValidateClass(@class, classes));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static List<ValidationError> ValidateClass(
        Class @class,
        IReadOnlyCollection<Class>? allClasses = null)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(@class.NameWithoutGrade))
        {
            errors.Add(new ValidationError("classNameEmpty", "班级名称不能为空。", @class.Guid));
        }
        else if (allClasses is not null
                 && allClasses.Any(other => other.Guid != @class.Guid
                                            && other.Grade == @class.Grade
                                            && string.Equals(
                                                other.NameWithoutGrade,
                                                @class.NameWithoutGrade,
                                                StringComparison.Ordinal)))
        {
            errors.Add(new ValidationError(
                "duplicateClassName",
                $"同年级已存在名为“{@class.NameWithoutGrade}”的班级。",
                @class.Guid));
        }

        var studentGuids = new HashSet<Guid>();
        var studentIds = new HashSet<string>(IdComparer);
        var idsInClass = new HashSet<int>();

        foreach (var student in @class.Students)
        {
            if (!studentGuids.Add(student.Guid))
            {
                errors.Add(new ValidationError("duplicateStudentGuid", "学生 Guid 重复。", student.Guid));
            }

            if (string.IsNullOrWhiteSpace(student.Id))
            {
                errors.Add(new ValidationError("studentIdEmpty", "学号不能为空。", student.Guid));
            }
            else if (!studentIds.Add(student.Id))
            {
                errors.Add(new ValidationError("duplicateStudentId", $"学号 {student.Id} 重复。", student.Guid));
            }

            if (student.IdInClass <= 0)
            {
                errors.Add(new ValidationError(
                    "invalidIdInClass",
                    $"班内学号 {student.IdInClass} 不合法。",
                    student.Guid));
            }
            else if (!idsInClass.Add(student.IdInClass))
            {
                errors.Add(new ValidationError(
                    "duplicateIdInClass",
                    $"班内学号 {student.IdInClass} 重复。",
                    student.Guid));
            }
        }

        var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupGuids = new HashSet<Guid>();

        foreach (var rule in @class.GroupingRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add(new ValidationError("ruleNameEmpty", "分组规则名称不能为空。", rule.Guid));
            }
            else if (!ruleNames.Add(rule.Name))
            {
                errors.Add(new ValidationError("duplicateRuleName", $"分组规则名称“{rule.Name}”重复。", rule.Guid));
            }

            var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupedStudentGuids = new HashSet<Guid>();

            foreach (var group in rule.Groups)
            {
                if (!groupGuids.Add(group.Guid))
                {
                    errors.Add(new ValidationError("duplicateGroupGuid", "小组 Guid 重复。", group.Guid));
                }

                if (string.IsNullOrWhiteSpace(group.Name))
                {
                    errors.Add(new ValidationError("groupNameEmpty", "小组名称不能为空。", group.Guid));
                }
                else if (!groupNames.Add(group.Name))
                {
                    errors.Add(new ValidationError("duplicateGroupName", $"小组名称“{group.Name}”重复。", group.Guid));
                }

                foreach (var studentGuid in group.StudentGuids)
                {
                    if (!studentGuids.Contains(studentGuid))
                    {
                        errors.Add(new ValidationError(
                            "studentNotFound",
                            $"小组“{group.Name}”引用了不存在的学生。",
                            studentGuid));
                    }
                    else if (!groupedStudentGuids.Add(studentGuid))
                    {
                        errors.Add(new ValidationError(
                            "studentGroupedMoreThanOnce",
                            $"同一学生在规则“{rule.Name}”中被分到了多个小组。",
                            studentGuid));
                    }
                }
            }
        }

        return errors;
    }

    private static void EnsureClassNameAvailable(
        IReadOnlyCollection<Class> classes,
        Grade grade,
        string nameWithoutGrade,
        Guid? excludeClassGuid = null)
    {
        var exists = classes.Any(@class => @class.Guid != excludeClassGuid
                                          && @class.Grade == grade
                                          && string.Equals(
                                              @class.NameWithoutGrade,
                                              nameWithoutGrade,
                                              StringComparison.Ordinal));

        if (exists)
        {
            throw new ValidationException(
                [new ValidationError(
                    "duplicateClassName",
                    $"同年级已存在名为“{nameWithoutGrade}”的班级。")]);
        }
    }

    private static void EnsureRuleNameAvailable(
        Class @class,
        string name,
        Guid? excludeRuleGuid = null)
    {
        var exists = @class.GroupingRules.Any(
            rule => rule.Guid != excludeRuleGuid
                    && string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            throw new ValidationException(
                [new ValidationError("duplicateRuleName", $"分组规则名称“{name}”已存在。")]);
        }
    }

    private static void EnsureGroupNameAvailable(
        GroupingRule rule,
        string name,
        Guid? excludeGroupGuid = null)
    {
        var exists = rule.Groups.Any(
            group => group.Guid != excludeGroupGuid
                     && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            throw new ValidationException(
                [new ValidationError("duplicateGroupName", $"小组名称“{name}”已存在。")]);
        }
    }

    private static void ValidateClassName(string nameWithoutGrade)
    {
        if (string.IsNullOrWhiteSpace(nameWithoutGrade))
        {
            throw new ValidationException(
                [new ValidationError("classNameEmpty", "班级名称不能为空。")]);
        }
    }

    private static void ValidateRuleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(
                [new ValidationError("ruleNameEmpty", "分组规则名称不能为空。")]);
        }
    }

    private static void ValidateGroupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(
                [new ValidationError("groupNameEmpty", "小组名称不能为空。")]);
        }
    }

    private static Student FindStudent(Class @class, Guid classGuid, Guid studentGuid)
    {
        return @class.Students.FirstOrDefault(student => student.Guid == studentGuid)
               ?? throw new StudentNotFoundException(classGuid, studentGuid);
    }

    private static GroupingRule FindRule(Class @class, Guid classGuid, Guid ruleGuid)
    {
        return @class.GroupingRules.FirstOrDefault(rule => rule.Guid == ruleGuid)
               ?? throw new GroupingRuleNotFoundException(classGuid, ruleGuid);
    }

    private static Group FindGroup(GroupingRule rule, Guid classGuid, Guid groupGuid)
    {
        return rule.Groups.FirstOrDefault(group => group.Guid == groupGuid)
               ?? throw new GroupNotFoundException(classGuid, groupGuid);
    }

    private static Group FindGroup(Class @class, Guid classGuid, Guid groupGuid)
    {
        foreach (var rule in @class.GroupingRules)
        {
            var group = rule.Groups.FirstOrDefault(item => item.Guid == groupGuid);
            if (group is not null)
            {
                return group;
            }
        }

        throw new GroupNotFoundException(classGuid, groupGuid);
    }

    private static void RemoveStudentFromAllGroups(Class @class, Guid studentGuid)
    {
        foreach (var rule in @class.GroupingRules)
        {
            foreach (var group in rule.Groups)
            {
                group.StudentGuids.Remove(studentGuid);
            }
        }
    }

    #endregion
}
