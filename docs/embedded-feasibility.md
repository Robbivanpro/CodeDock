# Embedded workspace feasibility

The production-safe workflow remains Window Manager mode: CodeDock launches or associates an IDE window and asks Windows to activate it. Association is stored only as runtime state and is never serialized.

An experimental `HwndHost` adapter is available for controlled testing. It reparents an associated top-level IDE window into a native child host, removes top-level window styles while attached, resizes it with the WPF host, and restores the original parent and styles on detach.

This mechanism is not supported by Visual Studio Code or Visual Studio and must not be treated as production-ready. The current implementation has compiled successfully but has not yet passed the manual acceptance matrix.

## Manual acceptance matrix

| Scenario | Status |
|---|---|
| Window opens and is visible in the host | Pending manual verification |
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
