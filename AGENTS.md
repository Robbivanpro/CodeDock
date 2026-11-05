# CodeDock agent instructions

## Project overview

CodeDock is a Windows-only desktop workspace manager built with .NET 10 and WPF. It launches, associates, embeds, activates, and closes Visual Studio Code and Visual Studio 2022 windows while persisting project and session state.

The solution is split into:

- `src/CodeDock.Core`: domain models, interfaces, validation, and application-independent services.
- `src/CodeDock.Infrastructure`: persistence, IDE discovery/launching, and Windows integration.
- `src/CodeDock.App`: WPF views, view models, dependency injection, and UI-specific services.
- `tests/CodeDock.Core.Tests` and `tests/CodeDock.Infrastructure.Tests`: xUnit tests.

Keep dependencies directed from App to Core/Infrastructure and from Infrastructure to Core. Core must remain independent of WPF and Windows UI concerns.

## Working method

1. Inspect the relevant implementation, call sites, tests, and nearby conventions before editing.
2. For non-trivial work, state a short implementation plan and then carry it through unless a decision genuinely requires user input.
3. Make the smallest coherent change that fully satisfies the request. Do not perform unrelated cleanup or broad rewrites.
4. Treat all pre-existing uncommitted and untracked files as user work. Never discard, revert, overwrite, or reformat unrelated changes.
5. Do not invent APIs, project behavior, or file contents. Verify them in the repository.
6. Implement requested changes rather than stopping at an explanation, plan, mock, or placeholder.
7. After editing, inspect the diff and run the most relevant verification commands.
8. Do not claim success unless the verification actually ran. Report failures and anything that could not be verified.

## C# and architecture conventions

- Target the SDK pinned by `global.json` and preserve the target frameworks already selected by each project.
- Nullable reference types, implicit usings, and warnings-as-errors are enabled globally. Do not suppress warnings merely to make a build pass.
- Follow the existing file-scoped namespace and naming conventions.
- Prefer clear, focused types and methods over new abstraction layers without a concrete need.
- Preserve public contracts unless the requested change explicitly requires changing them.
- Keep asynchronous operations asynchronous and propagate `CancellationToken` where the surrounding API supports cancellation.
- Add or update regression tests for behavioral changes and bug fixes.
- Avoid adding NuGet dependencies when the platform or existing packages already provide the needed behavior.

## WPF and Windows integration

- Keep business logic out of code-behind when it belongs in a view model or service. Code-behind is acceptable for view lifecycle, focus, drag-and-drop, and native-window hosting concerns.
- Preserve MVVM Toolkit patterns already used by the project.
- Ensure UI-bound state is accessed on the correct dispatcher thread.
- Treat Win32 window handles as transient. Validate handles and process identity before acting on external windows.
- Preserve explicit error handling around P/Invoke, process discovery, window association, activation, embedding, and shutdown.
- Never infer IDE identity solely from a window title when stronger process or handle information is available.
- Do not introduce behavior that can terminate an unrelated IDE process or discard unsaved work.
- Remember that automated tests may run without an interactive Windows desktop; isolate native operations behind interfaces where practical.

## Persistence and safety

- User data is stored below `%LocalAppData%\CodeDock`.
- Preserve backward compatibility of persisted JSON unless a migration is part of the task.
- Maintain the existing backup, recovery, and visible-warning behavior for damaged data.
- Tests must use isolated temporary locations and must not read, modify, or delete real user data.

## Verification

Use the narrowest relevant test first, followed by the full pipeline when the scope warrants it:

```powershell
dotnet restore CodeDock.slnx
dotnet build CodeDock.slnx --no-restore
dotnet test CodeDock.slnx --no-build
```

For a focused test run, use `dotnet test` with the appropriate test project or `--filter`. Run the WPF application manually only when UI or native-window behavior needs interactive validation:

```powershell
dotnet run --project src/CodeDock.App/CodeDock.App.csproj
```

Do not launch the GUI during routine automated verification unless the user asks for it or the change cannot otherwise be validated.

## Completion report

Keep the final response concise and include:

- what changed;
- tests/builds that were run and their result;
- any remaining risk or manual verification still needed.
