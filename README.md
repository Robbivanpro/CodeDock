# CodeDock

CodeDock is a Windows desktop workspace manager for developers who keep multiple repositories open in independent Visual Studio Code or Visual Studio 2022 windows.

Milestones 1 and 2 provide the WPF foundation and persistent project tabs. VS Code launching and window association are intentionally reserved for later milestones so the production path never guesses which external window belongs to a project.

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK

## Build and run

```powershell
dotnet restore CodeDock.slnx
dotnet build CodeDock.slnx --no-restore
dotnet test CodeDock.slnx --no-build
dotnet run --project src/CodeDock.App/CodeDock.App.csproj
```

Project and session data are saved under `%LocalAppData%\CodeDock`.

## Current capabilities

- Add existing project folders with the native Windows folder picker
- Choose Visual Studio Code or Visual Studio 2022 per project, with distinct tab badges
- Rename and remove tabs without modifying the underlying folders
- Reorder tabs with drag and drop
- Navigate with Ctrl+Tab and Ctrl+Shift+Tab
- Add a project with Ctrl+T
- Restore projects, order, selection, and preferences
- Recover project data from a backup when the primary JSON is damaged
