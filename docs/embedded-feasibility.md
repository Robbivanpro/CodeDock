# Embedded workspace feasibility

The production-safe workflow remains Window Manager mode: CodeDock launches or associates an IDE window and asks Windows to activate it. Association is stored only as runtime state and is never serialized.

An experimental `HwndHost` adapter is available for controlled testing. Each workspace now owns a persistent host, so switching tabs changes visibility instead of repeatedly moving one HWND between a shared host. The adapter reparents an associated top-level IDE window into a native child host, removes top-level window styles while attached, resizes it with the WPF host, and restores the original parent, styles, and placement on detach.

This mechanism is not supported by Visual Studio Code or Visual Studio and must not be treated as production-ready. The current implementation has compiled successfully but has not yet passed the manual acceptance matrix.

## Manual acceptance matrix

| Scenario | Status |
|---|---|
| Two independent VS Code windows open in two persistent hosts | Pending manual verification |
| Repeated tab switching without detach | Pending manual verification |
| Keyboard editing | Pending manual verification |
| IDE shortcuts | Pending manual verification |
| Integrated terminal | Pending manual verification |
| File dialogs and secondary windows | Pending manual verification |
| Resize | Pending manual verification |
| Switching between IDE instances | Pending manual verification |
| Detach and reopen | Pending manual verification |
| IDE crash recovery | Pending manual verification |
| Mixed-DPI monitors | Pending manual verification |

If any test leaves the external IDE window unusable, close CodeDock normally so the host can restore the original parent and styles. Do not terminate IDE processes as a recovery mechanism.

A forced CodeDock process crash bypasses normal detach and restoration. Automatic crash recovery is not claimed because HWND and process identifiers are deliberately not persisted as durable identities. Use Window Manager mode for production-sensitive work until the complete matrix passes.
