# ImmersingStudent

[English](README.md) | [简体中文](README.zh-CN.md)

A .NET class library for managing classes, students, and groupings.

Built around the `Class` aggregate root, it provides student CRUD, grouping rules, group organization, extra properties, validation, caching, and save strategies. The core library has no third-party dependencies.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

## Features

- **Class management**: create, rename, change grade, and delete classes, with unique class names per grade.
- **Student management**: add students individually or in batches, update, remove, and renumber by in-class number, with automatic validation of student IDs and in-class numbers.
- **Grouping rules**: each class can have multiple grouping rules, and each rule can contain multiple groups.
- **Group organization**: add, remove, and move students; a student can belong to only one group per rule.
- **Extra properties**: classes, students, grouping rules, and groups can all carry arbitrary typed extra properties.
- **Read-only view**: `ReadOnlyStudentService` offers read-only queries and shares the cache with the writable client.
- **Save modes**: manual save (`SaveMode.Manual`) or automatic save (`SaveMode.Auto`).
- **Concurrency safety**: reads run concurrently, writes are serialized per class, and cached instances stay consistent.
- **Pluggable storage**: implement `IDataSource` to connect a database, file storage, or an in-memory store.

## Requirements

- .NET SDK 10.0 or later
- Core library target framework: `net10.0`

## Installation

The project has not been published to NuGet yet. Use it via a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\ImmersingStudent\ImmersingStudent.csproj" />
</ItemGroup>
```

Alternatively, build it and reference the generated DLL directly:

```bash
dotnet build ImmersingStudent/ImmersingStudent.csproj -c Release
```

## Quick Start

### 1. Implement a Data Source

The library does not handle persistence itself. Implement `IDataSource` for your storage. Here is a minimal in-memory example:

```csharp
using ImmersingStudent.Abstractions;
using ImmersingStudent.Models;

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

> For production, persist serialized data or deep-copied snapshots. Model instances in the client cache may still be mutated by later operations, so a data source should not retain a long-lived reference to the same mutable instance.

### 2. Create a StudentService and Work with Data

```csharp
using ImmersingStudent;
using ImmersingStudent.Enums;
using ImmersingStudent.Models;

var dataSource = new InMemoryDataSource();
var client = new StudentService(dataSource, SaveMode.Auto);

// Create a class
var @class = await client.CreateClassAsync(Grade.JuniorSecondGrade, "3班");

// Add students (the in-class number defaults to the current maximum + 1)
var zhang = await client.AddStudentAsync(
    @class.Guid, "20260001", "张三", Gender.Male);

await client.AddStudentAsync(
    @class.Guid, "20260002", "李四", Gender.Female);

// Create a grouping rule and a group
var rule = await client.CreateGroupingRuleAsync(@class.Guid, "值日分组");
var group = await client.CreateGroupAsync(@class.Guid, rule.Guid, "第一组");

// Add a student to the group
await client.AddStudentToGroupAsync(
    @class.Guid, rule.Guid, group.Guid, zhang.Guid);

Console.WriteLine(ReadOnlyStudentService.GetDisplayName(@class)); // 初二年级3班
```

With `SaveMode.Auto`, every change made through `StudentService` is saved before the method returns. With `SaveMode.Manual`, call `SaveAsync` explicitly.

#### Dependency Injection

`StudentService` implements `IStudentService`, and its `ReadOnly` view implements `IReadOnlyStudentService`:

```csharp
services.AddSingleton<IDataSource, InMemoryDataSource>();
services.AddSingleton<IStudentService>(sp =>
    new StudentService(sp.GetRequiredService<IDataSource>(), SaveMode.Auto));

// Reuse the same instance so the read-only view shares the cache.
services.AddSingleton<IReadOnlyStudentService>(sp =>
    sp.GetRequiredService<IStudentService>().ReadOnly);
```

### 3. Query Data

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

`ReadOnlyStudentService` can also be used on its own:

```csharp
var reader = new ReadOnlyStudentService(dataSource);
var allClasses = await reader.GetClassesAsync();
```

## Core Concepts

### Model Structure

```text
Class
├── Students
│   └── ExtraProperties
├── GroupingRules
│   └── Groups
│       └── StudentGuids (member GUIDs)
└── ExtraProperties
```

Every entity has a stable `Guid`. Groups reference students through `StudentGuids`, and removing a student automatically removes them from all groups.

### Save Modes

| Mode | Behavior |
| --- | --- |
| `SaveMode.Manual` (default) | Changes are written to the in-memory cache only; data is written to the data source when `SaveAsync()` is called |
| `SaveMode.Auto` | Every change made through `StudentService` is saved before the method returns |

In manual mode, deleting a class only marks it as pending deletion. It is actually removed from the data source when `SaveAsync()` is called; before that, queries no longer return the class.

### Validation

All mutation methods validate before writing. On failure they throw `ValidationException` with a structured list of errors:

```csharp
try
{
    await client.AddStudentAsync(
        @class.Guid, "20260001", "Duplicate ID", Gender.Male);
}
catch (ValidationException ex)
{
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }
}
```

You can also validate a whole class explicitly:

```csharp
IReadOnlyList<ValidationError> errors = await client.ValidateAsync(@class.Guid);
```

Common error codes:

| Code | Meaning |
| --- | --- |
| `classNameEmpty` / `duplicateClassName` | Empty class name / duplicate class name in the same grade |
| `studentIdEmpty` / `duplicateStudentId` | Empty student ID / duplicate student ID |
| `invalidIdInClass` / `duplicateIdInClass` | Invalid in-class number / duplicate in-class number |
| `ruleNameEmpty` / `duplicateRuleName` | Empty grouping rule name / duplicate rule name |
| `groupNameEmpty` / `duplicateGroupName` | Empty group name / duplicate group name |
| `studentAlreadyGrouped` / `studentNotInSourceGroup` | Student is already in another group / not in the source group |
| `studentGroupedMoreThanOnce` | The same student is assigned to multiple groups in one rule |

If validation or a user callback fails, the in-memory class is rolled back to the snapshot taken before the change. If an automatic save fails, the change stays in the cache and is marked as unsaved, so `SaveAsync()` can be retried later.

### Extra Properties

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

`ExtraPropertyOwner` supports `Class`, `Student`, `GroupingRule`, and `Group`. Reads return `default` when the property is missing or has a different type.

## API Overview

### `StudentService`

| Area | Methods |
| --- | --- |
| Classes | `CreateClassAsync`, `RenameClassAsync`, `SetGradeAsync`, `DeleteClassAsync` |
| Students | `AddStudentAsync`, `AddStudentsAsync`, `UpdateStudentAsync`, `RemoveStudentAsync`, `RemoveStudentsAsync`, `RenumberStudentsAsync` |
| Grouping rules | `CreateGroupingRuleAsync`, `RenameGroupingRuleAsync`, `DeleteGroupingRuleAsync` |
| Groups | `CreateGroupAsync`, `RenameGroupAsync`, `DeleteGroupAsync` |
| Members | `AddStudentToGroupAsync`, `RemoveStudentFromGroupAsync`, `MoveStudentToGroupAsync` |
| Extra properties | `SetExtraPropertyAsync` |
| Validation and saving | `ValidateAsync`, `SaveAsync`, `ReloadAsync` |

### `ReadOnlyStudentService`

| Method | Description |
| --- | --- |
| `GetClassesAsync` | Get all classes |
| `FindClassAsync` / `GetClassAsync` | Find a class (returns `null` / throws) |
| `GetStudentsAsync` | Get the students in a class |
| `FindStudentAsync` / `FindStudentByStudentIdAsync` | Find a student by GUID / student ID |
| `GetGroupingRulesAsync` / `FindGroupingRuleAsync` | Get or find grouping rules |
| `GetGroupsAsync` | Get the groups in a rule |
| `GetGroupMembersAsync` | Get the members of a group |
| `GetUngroupedStudentsAsync` | Get students not assigned to any group |
| `SearchStudentsAsync` | Search by name/student ID, optionally filtered by gender |
| `GetDisplayName` / `GetGradeName` | Class display name / Chinese grade name |
| `GetExtraProperty<T>` | Read an extra property by ID |

## Notes

1. **Queries return cached references.** Directly mutating a model (for example, `student.Name = "..."`) does not trigger validation or automatic saving. Even with `SaveMode.Auto`, call `SaveAsync()` manually.
2. **`ReloadAsync` replaces cached instances.** Model references obtained before a reload are no longer synchronized with the client.
3. **Batch operations are atomic.** If any student in `AddStudentsAsync` is invalid, the whole batch is rejected.
4. **Concurrency safety.** `StudentService` and `ReadOnlyStudentService` can be used concurrently from multiple threads; reads run in parallel, writes lock per class, and changes to the same class do not overwrite each other.
5. **Class naming rules.** Duplicate class names are checked within the same grade and are case-sensitive. Student IDs, rule names, and group names are checked case-insensitively.

## Project Structure

```text
ImmersingStudent/
├── ImmersingStudent/                 # Core library
│   ├── Abstractions/                 # IDataSource, IStudentService, IReadOnlyStudentService
│   ├── StudentService.cs             # Writable service
│   ├── ReadOnlyStudentService.cs     # Read-only service
│   ├── Enums/                        # SaveMode, ExtraPropertyOwner
│   ├── Exceptions/                   # Domain exceptions and validation errors
│   ├── Internal/                     # Cache, cloning, async reader-writer lock
│   └── Models/                       # Class, Student, Group, and other models
├── ImmersingStudent.Tests/           # xUnit tests
└── ImmersingStudent.sln
```

## Development and Testing

```bash
dotnet build
dotnet test
```

The tests cover student operations, grouping rules, extra properties, validation, persistence, concurrency, and the read-only view.

## License

This project is licensed under the [MIT License](LICENSE).
