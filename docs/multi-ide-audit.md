# Multi-IDE lifecycle audit

## Verified defects in the Part 2 baseline

| File and symbol | Cause | Consequence | Resolution |
|---|---|---|---|
| `MainWindow.xaml`, `ExternalIdeHost` | A single host was rebound to the selected project | Changing tabs detached one IDE and reparented the next; two workspaces did not retain independent hosts | The preview creates one persistent `ExternalWindowHost` per workspace and only changes host visibility |
| `IdeWorkspaceService.AssociateAndActivate` | No reverse ownership map existed for HWND values | The same IDE window could be assigned to multiple workspaces | A process-wide HWND owner map now rejects duplicate claims |
| `IdeWorkspaceService.OpenOrActivateAsync` | Launch locking was per workspace while discovery observed all desktop windows | Concurrent workspace launches could observe and claim the same new window | The launch/discovery interval is globally serialized while other operations remain per-workspace |
| `WindowAssociationResolver.Resolve` and `IdeWorkspaceService.OpenOrActivateAsync` | One new HWND was accepted without comparing the launched process identity | An unrelated window created during polling could be claimed | Automatic association now requires PID and process-start generation to match the launch receipt; otherwise manual selection is required |
| `IdeWorkspaceService` association storage | Only an HWND was retained | Reuse of a stale HWND could target a different process | Sessions retain workspace, environment, PID, process-start ticks, HWND, generation, lifecycle state, and error |
| `MainWindowViewModel.RemoveProjectAsync` | Removing a project immediately deleted the tab record | The associated IDE was neither detached nor asked to close | The X workflow detaches first, sends `WM_CLOSE`, waits for actual closure, and removes the tab only after a safe result |
| `ExternalWindowHost.DetachExternalWindow` | Native return values were ignored and attachment state was always cleared | A failed detach could lose the information required to retry restoration | Parent, styles, and placement are checked and state is retained when restoration fails |
| `ExternalWindowHost` | Original window placement was not retained | Detaching could restore styles but not the previous desktop position | `WINDOWPLACEMENT`, parent, style, and extended style are captured and restored |
| `MainWindow.OnClosing` | Shutdown only saved JSON | Embedded windows relied on WPF destruction for detach | Shutdown explicitly detaches every known embedded host and is cancelled if a detach fails |
| `MainWindowViewModel.SetPreferredEnvironmentAsync` | Environment changes cleared associations without coordinating an active operation | An open or embedded session could be orphaned or changed during launch | Environment changes are rejected while busy or associated |
| `App.ConfigureServices` | Logging used only the debug provider | Release diagnostics were unavailable | Non-blocking local file logging was added under `%LocalAppData%\CodeDock\logs` |

## Resulting lifecycle

Runtime IDE sessions distinguish `Idle`, `Opening`, `Associated`, `Embedding`, `Embedded`, `Detaching`, `Closing`, `Closed`, and `Failed`. HWND and PID values remain runtime-only and are not persisted.

Closing a tab that owns a window performs detach, identity validation, `WM_CLOSE` delivery through `SendMessageTimeout`, asynchronous closure polling, and record removal. A timeout or a cancelled IDE save dialog keeps the tab and its association. The context menu command **Close tab only** detaches and forgets the association without closing the IDE.

## Automated verification

The infrastructure tests cover duplicate HWND claims, two concurrent workspace launches, distinct window ownership, close routing, close timeout/cancellation, already-closed windows, closing during launch, and launch-process identity mismatch. Core tests continue to cover deterministic window-difference resolution.

Native reparenting, focus, popup behavior, DPI transitions, crash behavior, and actual Visual Studio Code or Visual Studio dialogs require the manual matrix in `embedded-feasibility.md`.
