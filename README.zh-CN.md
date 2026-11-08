# ImmersingStudent

[English](README.md) | [简体中文](README.zh-CN.md)

一个面向班级、学生与分组管理的 .NET 类库。

以班级（`Class`）为聚合根，提供学生增删改查、分组规则、小组编排、扩展属性、数据校验、缓存与保存策略等功能。主类库无第三方依赖。

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

## 特性

- **班级管理**：创建、重命名、调整年级、删除班级，同年级班级名唯一。
- **学生管理**：单个/批量添加、更新、删除、按班内学号重新编号，学号与班内学号自动校验。
- **分组规则**：一个班级可有多套分组规则；每条规则下可有多个小组。
- **小组编排**：添加、移除、移动学生；同一学生在同一规则中只能属于一个小组。
- **扩展属性**：班级、学生、分组规则、小组均可挂载任意类型的扩展属性。
- **查询视图**：`ReadOnlyStudentService` 提供只读查询，与可写客户端共享缓存。
- **保存策略**：手动保存（`SaveMode.Manual`）或自动保存（`SaveMode.Auto`）。
- **并发安全**：读操作可并发，写操作按班级串行化，缓存实例保持一致。
- **可插拔存储**：实现 `IDataSource` 即可接入数据库、文件或内存存储。

## 环境要求

- .NET SDK 10.0 或更高版本
- 主类库目标框架：`net10.0`

## 安装

项目目前未发布 NuGet 包，可通过项目引用使用：

```xml
<ItemGroup>
  <ProjectReference Include="..\ImmersingStudent\ImmersingStudent.csproj" />
</ItemGroup>
```

也可以先构建后直接引用生成的 DLL：

```bash
dotnet build ImmersingStudent/ImmersingStudent.csproj -c Release
```

## 快速开始

### 1. 实现数据源

类库不负责具体持久化，业务方需要实现 `IDataSource`。下面是内存实现的最小示例：

```csharp
using ImmersingEducation.Infrastructures.ImmersingStudent.Abstractions;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

public sealed class InMemoryDataSource : IDataSource
{
    private readonly Dictionary<Guid, Class> _classes = [];

    public Task<IReadOnlyList<Class>> GetClassesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Class>>(_classes.Values.ToList());

    public Task<Class?> GetClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
        => Task.FromResult(_classes.GetValueOrDefault(classGuid));

    public Task SaveClassAsync(Class @class, CancellationToken cancellationToken = default)
    {
        _classes[@class.Guid] = @class;
        return Task.CompletedTask;
    }

    public Task DeleteClassAsync(Guid classGuid, CancellationToken cancellationToken = default)
    {
        _classes.Remove(classGuid);
        return Task.CompletedTask;
    }
}
```

> 生产环境的数据源建议保存序列化结果或深拷贝快照。客户端缓存中的模型实例仍可能被后续操作修改，数据源不应长期持有同一个可变实例。

### 2. 创建客户端并操作数据

```csharp
using ImmersingEducation.Infrastructures.ImmersingStudent;
using ImmersingEducation.Infrastructures.ImmersingStudent.Enums;
using ImmersingEducation.Infrastructures.ImmersingStudent.Models;

var dataSource = new InMemoryDataSource();
var client = new StudentService(dataSource, SaveMode.Auto);

// 新建班级
var @class = await client.CreateClassAsync(Grade.JuniorSecondGrade, "3班");

// 添加学生（不传班内学号时自动取当前最大值 + 1）
var zhang = await client.AddStudentAsync(
    @class.Guid, "20260001", "张三", Gender.Male);

await client.AddStudentAsync(
    @class.Guid, "20260002", "李四", Gender.Female);

// 新建分组规则和小组
var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日分组");
var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

// 把学生加入小组
await client.AddStudentToGroupAsync(
    @class.Guid, rule.Guid, group.Guid, zhang.Guid);

Console.WriteLine(ReadOnlyStudentService.GetDisplayName(@class)); // 初二年级3班
```

`SaveMode.Auto` 下，每次通过 `StudentService` 完成的修改都会在方法返回前自动保存；`SaveMode.Manual` 下需要显式调用 `SaveAsync`。

#### 依赖注入

`StudentService` 实现 `IStudentService`，其 `ReadOnly` 视图实现 `IReadOnlyStudentService`：

```csharp
services.AddSingleton<IDataSource, InMemoryDataSource>();
services.AddSingleton<IStudentService>(sp =>
    new StudentService(sp.GetRequiredService<IDataSource>(), SaveMode.Auto));

// 复用同一个实例，确保只读视图共享缓存。
services.AddSingleton<IReadOnlyStudentService>(sp =>
    sp.GetRequiredService<IStudentService>().ReadOnly);
```

### 3. 查询

```csharp
var classes = await client.ReadOnly.GetClassesAsync();

var cached = await client.ReadOnly.GetClassAsync(@class.Guid);

var student = await client.ReadOnly.FindStudentByStudentIdAsync(
    @class.Guid, "20260001");

var ungrouped = await client.ReadOnly.GetUngroupedStudentsAsync(
    @class.Guid, rule.Guid);

var boys = await client.ReadOnly.SearchStudentsAsync(
    @class.Guid, keyword: "", gender: Gender.Male);
```

`ReadOnlyStudentService` 也可以单独使用：

```csharp
var reader = new ReadOnlyStudentService(dataSource);
var allClasses = await reader.GetClassesAsync();
```

## 核心概念

### 模型结构

```text
Class（班级）
├── Students（学生）
│   └── ExtraProperties（扩展属性）
├── GroupingRules（分组规则）
│   └── Groups（小组）
│       └── StudentGuids（成员 Guid）
└── ExtraProperties（扩展属性）
```

所有实体都有稳定的 `Guid`。小组通过 `StudentGuids` 引用学生，删除学生会自动从所有小组中移除。

### 保存模式

| 模式 | 行为 |
| --- | --- |
| `SaveMode.Manual`（默认） | 修改只写入内存缓存，调用 `SaveAsync()` 时才写入数据源 |
| `SaveMode.Auto` | 每次通过 `StudentService` 完成的修改在方法返回前自动保存 |

手动模式下删除班级只会先标记为待删除，调用 `SaveAsync()` 后才真正从数据源删除；在保存前，查询已经看不到该班级。

### 校验

所有修改方法在写入前都会校验，失败时抛出 `ValidationException`，并携带结构化的错误列表：

```csharp
try
{
    await client.AddStudentAsync(
        @class.Guid, "20260001", "重复学号", Gender.Male);
}
catch (ValidationException ex)
{
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }
}
```

也可以主动校验整个班级：

```csharp
IReadOnlyList<ValidationError> errors = await client.ValidateAsync(@class.Guid);
```

常见错误代码：

| 代码 | 含义 |
| --- | --- |
| `classNameEmpty` / `duplicateClassName` | 班级名称为空 / 同年级班级重名 |
| `studentIdEmpty` / `duplicateStudentId` | 学号为空 / 学号重复 |
| `invalidIdInClass` / `duplicateIdInClass` | 班内学号非法 / 重复 |
| `ruleNameEmpty` / `duplicateRuleName` | 分组规则名称为空 / 重复 |
| `groupNameEmpty` / `duplicateGroupName` | 小组名称为空 / 重复 |
| `studentAlreadyGrouped` / `studentNotInSourceGroup` | 学生已在其他小组 / 不在源小组 |
| `studentGroupedMoreThanOnce` | 同一学生在同一规则中被分到多个小组 |

校验或用户回调失败时，内存中的班级会回滚到修改前的快照；自动保存失败时，改动会保留在缓存中并标记为未保存，可稍后重试 `SaveAsync()`。

### 扩展属性

```csharp
await client.SetExtraPropertyAsync(
    @class.Guid,
    zhang.Guid,
    ExtraPropertyOwner.Student,
    "nickname",
    "小张");

var nickname = ReadOnlyStudentService.GetExtraProperty<string>(
    zhang.ExtraProperties, "nickname");
```

`ExtraPropertyOwner` 支持 `Class`、`Student`、`GroupingRule`、`Group`。读取时类型不匹配或不存在会返回 `default`。

## API 概览

### `StudentService`

| 分类 | 方法 |
| --- | --- |
| 班级 | `CreateClassAsync`、`RenameClassAsync`、`SetGradeAsync`、`DeleteClassAsync` |
| 学生 | `AddStudentAsync`、`AddStudentsAsync`、`UpdateStudentAsync`、`RemoveStudentAsync`、`RemoveStudentsAsync`、`RenumberStudentsAsync` |
| 分组规则 | `CreateGroupingRuleAsync`、`RenameGroupingRuleAsync`、`DeleteGroupingRuleAsync` |
| 小组 | `CreateGroupAsync`、`RenameGroupAsync`、`DeleteGroupAsync` |
| 成员 | `AddStudentToGroupAsync`、`RemoveStudentFromGroupAsync`、`MoveStudentToGroupAsync` |
| 扩展属性 | `SetExtraPropertyAsync` |
| 校验与保存 | `ValidateAsync`、`SaveAsync`、`ReloadAsync` |

### `ReadOnlyStudentService`

| 方法 | 说明 |
| --- | --- |
| `GetClassesAsync` | 获取全部班级 |
| `FindClassAsync` / `GetClassAsync` | 查找班级（返回 `null` / 抛异常） |
| `GetStudentsAsync` | 获取班级学生 |
| `FindStudentAsync` / `FindStudentByStudentIdAsync` | 按 Guid / 学号查找学生 |
| `GetGroupingRulesAsync` / `FindGroupingRuleAsync` | 获取或查找分组规则 |
| `GetGroupsAsync` | 获取规则下的小组 |
| `GetGroupMembersAsync` | 获取小组成员 |
| `GetUngroupedStudentsAsync` | 获取尚未分组的学生 |
| `SearchStudentsAsync` | 按姓名/学号搜索，可按性别筛选 |
| `GetDisplayName` / `GetGradeName` | 班级显示名称、年级中文名称 |
| `GetExtraProperty<T>` | 读取指定 Id 的扩展属性 |

## 注意事项

1. **查询返回的是缓存引用。** 直接修改模型（例如 `student.Name = "..."`）不会触发校验和自动保存；即使使用 `SaveMode.Auto`，也需要手动调用 `SaveAsync()`。
2. **`ReloadAsync` 会替换缓存实例。** 之前查询得到的旧模型引用将不再与客户端同步。
3. **批量操作是原子的。** `AddStudentsAsync` 中任意一个学生不合法时，整批都不会写入。
4. **并发安全。** `StudentService` 和 `ReadOnlyStudentService` 可在多线程下并发使用；读操作并行，写操作按班级加锁，同一班级的修改不会互相覆盖。
5. **班级重名规则。** 班级重名检查作用于同年级，班级名称本身区分大小写；学号、规则名、小组名的检查不区分大小写。

## 项目结构

```text
ImmersingStudent/
├── ImmersingStudent/                 # 主类库
│   ├── Abstractions/                 # IDataSource、IStudentService、IReadOnlyStudentService
│   ├── StudentService.cs             # 可写服务
│   ├── ReadOnlyStudentService.cs     # 只读服务
│   ├── Enums/                        # SaveMode、ExtraPropertyOwner
│   ├── Exceptions/                   # 领域异常与校验错误
│   ├── Internal/                     # 缓存、克隆、异步读写锁
│   └── Models/                       # Class、Student、Group 等模型
├── ImmersingStudent.Tests/           # xUnit 测试
└── ImmersingStudent.sln
```

## 开发与测试

```bash
dotnet build
dotnet test
```

测试覆盖学生操作、分组规则、扩展属性、校验、持久化、并发与只读视图等场景。

## 许可证

本项目采用 [MIT License](LICENSE)。
