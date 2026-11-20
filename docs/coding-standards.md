# C#/.NET coding standards

These rules apply to every `.cs` and `.razor` file in `src/` and `tests/`. They are enforced where possible by [.editorconfig](../.editorconfig), [.globalconfig](../.globalconfig), analyzers, `TreatWarningsAsErrors`, `dotnet format`, and the convention tests under `tests/Pyxie.Tests/Conventions/`.

## Files, folders, and namespaces

- Declare exactly one type per file: each class, record, enum, interface, and struct has its own file named after the type. Nested helper types are not allowed; extract them to their own files. A generic type appends its type parameter names, so `ServiceResult<T>` lives in `ServiceResultT.cs`.
- A Razor component and its code-behind are one type: `Board.razor` with `Board.razor.cs`.
- Folders and namespaces mirror each other exactly: namespace = project root namespace + folder path. A file in `src/Pyxie.Data/Services/Results/` declares `namespace Pyxie.Data.Services.Results;`.
- Organize folders by responsibility (for example `Enums/`, `Entities/`, `Persistence/`, `Persistence/Configuration/`, `Persistence/Converters/`, `Services/`, `Services/Results/`, `Services/Inputs/`, `Limits/`, `DependencyInjection/`), not by type kind alone. Add a folder when a responsibility has more than a couple of types.
- EF Core migrations are generator output: they keep the generated several-types-per-file layout and are exempt from this rule, from `CS1591`, and from style analysis through `generated_code` in `.editorconfig`. Never hand-edit the generated schema operations. Pyxie has no deployed data yet, so migrations are regenerated as a single `InitialCreate` after a model change until the first real deployment.
- `Program.cs` is a short top-level file. Composition-root logic lives in `Pyxie.Web/Hosting/` as one extension class per file.
- `OneTypePerFileTests` and `NamespaceMirrorsFolderTests` enforce this over `src/` and `tests/`.

## Formatting

- Use UTF-8 and LF line endings. Use tabs for C# and Razor indentation at four columns, and Allman braces. There is no line-length limit. Use braces for every control-flow body, including single statements. Project files, props, JSON, and Markdown use two spaces.
- Keep statements, signatures, and calls on one line; break a long expression at a logical boundary only where that reads better. Do not vertically align code with extra spaces.
- Separate members and logical blocks with one blank line, including after a guard clause. Keep tightly related declarations together.
- Use file-scoped namespaces. Place `using` directives outside the namespace, `System` first, then other namespaces in separated groups. Remove unused directives (`IDE0005` is an error).
- Use `#region` only when a large type needs navigational grouping; it is not a substitute for splitting responsibilities.
- `dotnet format` owns whitespace and style. Run `dotnet format --verify-no-changes` before handoff. Do not weaken rules to make checks pass.

## Naming and types

- Use descriptive names. One- or two-character names are reserved for conventional loop counters and generic parameters; prefer descriptive generic names such as `TItem`.
- Use PascalCase for namespaces, types, methods, properties, events, enum members, and constants. Prefix interfaces with `I` and generic type parameters with `T`.
- Use camelCase for parameters and locals, `_camelCase` for private instance fields, and `s_camelCase` for private static fields.
- Give asynchronous methods an `Async` suffix unless implementing a framework-defined signature.
- Prefer explicit types when the type is not clear from the expression; use `var` when it is apparent. `var` does not express immutability: use `const`, `readonly`, and immutable values for that.
- Nullable reference types are enabled everywhere. Model optional values explicitly and validate at boundaries. Do not use `dynamic`, unchecked casts, or the null-forgiving operator to evade the type checker.
- Use explicit access modifiers and the narrowest useful visibility. Delete dead code instead of keeping speculative extension points.

## Constants, configuration, and state

- Each shared constant or limit has one owning type; group related constants by responsibility (see `PyxieLimits`). Avoid magic numbers and catch-all constants classes. Place private constants and readonly fields at the top of their type.
- Use `const` for compile-time values and `static readonly` or instance `readonly` for values set at initialization. A readonly reference does not make its collection immutable; expose a read-only view.
- Read configuration at the composition root (`Pyxie.Web/Hosting/`). Do not scatter configuration reads or machine-specific paths through services or components. Keep secrets, including the Blazorise product token, out of source and logs.
- Give mutable state a clear owner. Avoid mutable globals, service locators, and static singleton state.
- Initialize locals at declaration and objects into valid states. Supply dependencies through constructors. The one exception is a Blazor component, which the framework creates without constructor injection: declare its services as `[Inject] public required IService Service { get; set; }`, never `{ get; set; } = default!;`. Represent meaningful states explicitly rather than with contradictory booleans or unexplained nulls.

## Design

- Follow KISS, YAGNI, DRY, single responsibility, and least astonishment. Fix the underlying cause. Extract a duplicated rule when its second copy appears; keep unrelated concepts separate.
- `Pyxie.Data` depends on EF Core only, never on a provider or on the web project. Provider-specific code and migrations live in a provider assembly such as `Pyxie.Data.Sqlite`.
- Keep the model provider-agnostic: no provider-specific collations, types, or SQL in `Pyxie.Data`.
- Store timestamps as UTC `DateTime`. Route time and randomness through explicit dependencies so tests can control them.
- Release disposable resources with `using` / `await using`, or `try`/`finally` when needed.

## Concurrency and asynchronous work

- Use async APIs for I/O. Avoid `.Result`, `.Wait()`, and `Thread.Sleep`.
- Await every task or give it an explicit owner that observes completion and exceptions. No unowned fire-and-forget work.
- Use `Task`/`Task<T>` for async methods. `async void` is not allowed, including in Blazor components; use `async Task` handlers.
- Propagate `CancellationToken` through every operation that can outlive its caller, as the last parameter.
- Never hold a `lock` across `await`. Protect shared mutable data by ownership, immutable snapshots, or appropriate synchronization.

## Comments and error handling

- Write XML documentation for public types and members, including meaningful parameter and return descriptions; use `<inheritdoc/>` for implementations. `CS1591` is a warning and warnings are errors. Explain ownership, non-obvious invariants, and reasons; do not paraphrase the implementation.
- Documentation enforcement applies to authored APIs. `.globalconfig` exempts source-generator output (including Razor component output), `.editorconfig` exempts EF migrations and the xUnit project (`tests/*.Tests/`), from `CS1591` only. Keep both configurations together. Never replace this with a project-wide `NoWarn` or disabled documentation generation.
- Exceptions represent exceptional failures. Expected outcomes, such as a duplicate name, an invalid assignment, or deleting a category that cards still use, return a typed result (`ServiceResult`, `ServiceResult<T>`) with a user-presentable message, not an exception.
- Validate external boundaries: configuration, user input, and files. Use typed invariants within the system rather than repeating defensive checks everywhere.
- Catch only where an error can be handled, translated, or reported meaningfully. Never swallow errors; preserve the stack with `throw;`. Treat expected cancellation separately from faults.
- Use appropriate .NET exception types with actionable context, such as the configuration key that is wrong. Log at the responsible boundary instead of logging and rethrowing at every layer.
- Keep compiler and analyzer output free of warnings. Narrowly scoped suppressions require a reason; do not blanket-disable nullable analysis or rules.
- Do not add tool credits, attribution, or generated-by text to code, comments, or documents.

## Blazor

- Components are thin: markup, local UI state, and calls into services. Business rules, validation, ordering, and persistence belong in `Pyxie.Data` services.
- Blazor Server circuits are long-lived. Never inject or hold a circuit-scoped `DbContext`. Use services that create a short-lived context per operation from `IDbContextFactory<ProjectDbContext>` (the open project) or `IDbContextFactory<MachineDbContext>`.
- Components handle `ServiceResult` failures by showing the message; they do not catch exceptions for control flow.
- A handler that writes checks on the server that its form can still act: the dialog is open and nothing is running, the item is still listed, the form is not disabled. What the browser showed decides nothing. The circuit handles a click when it gets to it, so a second click on Save made while a save held the circuit, as every save over SQLite does, is handled after that save has ended. A Blazorise `Button` checks `Disabled` only then, when it is enabled again, and runs the `Clicked` it has then. A closed Blazorise modal keeps its content, so its buttons still take the clicks the browser sent before it hid them. Only the browser applies a plain element's `disabled`, and a form's submit reaches the server whatever the form shows. The handler of an element that a render removed or replaced keeps running, with what it was drawn with, until the browser has acknowledged that render, unless the component that rendered it is gone.
- A control whose action follows the state, such as Open and Close, acts on what it showed when it was pressed. Its handler sits on an element around the Blazorise button and takes the state from the render that drew it, and the button stays the same, so it keeps the keyboard focus. The handler ignores a press when the state has changed since, and a press the browser counts as part of a double click (`MouseEventArgs.Detail` above 1); so does an action in a list that removes its item at once, such as Close on a card, because the next item slides under the pointer before the second click. Keyboard presses carry no count, so a held Enter repeats.
- A dialog acts on what it was opened for, never on a parameter that changed while it was open. A Blazorise modal reports `VisibleChanged` only after its 150 ms animation, so it also repeats, late, the opening and closing its parent asked for: take its report only as a close the person made while the dialog is open.
- Dispose event subscriptions, timers, and `CancellationTokenSource` instances by implementing `IDisposable`/`IAsyncDisposable`. Pass the component's cancellation token to service calls where an operation can outlive the component.
- Event handlers are `async Task`, never `async void`.
- Use Blazorise v2 Ant Design components for layout and controls. Keep markup tidy and use tabs.
- The visual design is [docs/design](design/README.md): its tokens are the `--pyx-*` custom properties in `wwwroot/app.css`, which also maps them onto Ant's `--ant-*` variables. Component CSS reads tokens and never hardcodes a color; a color that is data, such as a category's, reaches CSS only as a custom property written from a `HexColor`. The design's secondary button is `Color.Default` (`Color.Secondary` renders solid gray in Ant), its primary is `Color.Primary`, and its dark button is `Color.Dark`. Blazorise's scoped-CSS attribute does not reach the root of a Blazorise component, so style those through `::deep` under an element the component owns.
- Blazorise components accept unknown attributes, so a parameter that v2 renamed or removed (such as `Check` `Checked`, now `Value`) compiles and then does nothing. `RazorComponentParameterTests` fails when markup names a parameter its component does not declare; look parameters up in the Blazorise v2 docs.
- Follow the UI restraint rules in [AGENTS.md](../AGENTS.md).

## Testing

- Write automated tests with xUnit v3 under `tests/`. Name test files `<Type>Tests.cs` after the type or behavior under test, and give methods names that state the expected behavior. Split a large test class by concern (for example `CardServiceCloseTests`).
- Test services against a per-test in-memory SQLite database from `TestDatabase`. Do not mock away the database or the rules under test.
- Keep a specific failure message on every assertion that does not explain itself, as in `Assert.True(condition, message)`. Turn independent cases into `[Theory]` rows so each reports separately.
- Tests must be parallel-safe: no shared mutable statics, shared databases, or fixed output paths. Pass `TestContext.Current.CancellationToken` to async calls.
- Control time and randomness in tests. Avoid sleeps and duplicate-implementation tests.
- A passing build or unit run is not proof of the user interface working; check the running application for UI changes and report what was and was not verified.
- Pin dependencies in `Directory.Packages.props` and keep restore lock files (`packages.lock.json`). Do not upgrade unrelated packages or weaken analyzers to get a build through.
