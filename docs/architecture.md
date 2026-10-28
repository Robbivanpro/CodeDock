# Architecture

## Project responsibilities

`CodeDock.Core` contains persistent models, contracts, validation, and project registry behavior. It has no dependency on WPF, Win32, or infrastructure packages.

`CodeDock.Infrastructure` implements versioned JSON project and session stores. Writes use a temporary file in the destination directory, force buffered data to disk, and replace the destination while retaining a backup. Invalid primary project data is quarantined rather than overwritten, and a valid backup is recovered when available.

`CodeDock.App` is the WPF composition root. It owns views, view models, native dialogs, visual styles, and dependency injection. Code-behind is limited to focus, keyboard, window-closing, and drag/drop behavior that belongs to WPF itself.

The test projects exercise framework-independent registry behavior and concrete filesystem persistence independently.

## Data boundaries

`ProjectWorkspace` is persistent configuration, including the preferred development environment for each project. Process IDs, native window handles, launch progress, association confidence, and transient errors will be introduced as separate runtime state in the window-management milestones and will never be serialized in the project registry.

The project and session documents each carry a schema version. Unknown versions are rejected explicitly so future migrations cannot silently reinterpret data.

## Reliability choices

Project mutations are saved immediately and also saved during orderly shutdown. Session selection is persisted after changes and during shutdown. Folder availability is evaluated without removing missing projects, allowing removable drives and temporarily unavailable network paths to recover.

The UI does not launch or terminate VS Code in the current milestones. Removing a CodeDock tab only removes its registry entry.
