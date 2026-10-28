# CodeDock

CodeDock is a Windows desktop workspace manager for developers who keep multiple repositories open in independent Visual Studio Code or Visual Studio 2022 windows.

The stable foundation provides persistent project tabs. The current Part 2 preview adds deterministic IDE discovery, launching, association, and activation without guessing from window titles.

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

For the stable workspace-local build, double-click `Start-CodeDock.cmd`. To test Part 2 separately, double-click `Start-CodeDock-Part2.cmd`.

Project and session data are saved under `%LocalAppData%\CodeDock`.

## Current capabilities

- Add existing project folders with the native Windows folder picker
- Choose Visual Studio Code or Visual Studio 2022 per project, with distinct tab badges
- Launch, associate, and recall visible IDE windows without relying on window titles
- Manually select an existing IDE window when automatic association is uncertain
- Try isolated experimental window embedding with reversible native reparenting
- Rename and remove tabs without modifying the underlying folders
- Reorder tabs with drag and drop
- Navigate with Ctrl+Tab and Ctrl+Shift+Tab
- Add a project with Ctrl+T
- Restore projects, order, selection, and preferences
- Recover project data from a backup when the primary JSON is damaged
