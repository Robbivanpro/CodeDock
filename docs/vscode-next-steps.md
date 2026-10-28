# IDE integration next steps

## Milestone 3

Introduce `IVSCodeLocator`, `IProcessRunner`, and `IVSCodeLauncher` in Core with Windows implementations in Infrastructure. Detection should consider an explicit user setting first, then documented per-user and system installation locations, and finally the `code` command when resolvable. Arguments must be passed through `ProcessStartInfo.ArgumentList`, with launch de-duplication, cancellation, timeout handling, and structured errors.

Add a separate Visual Studio adapter that discovers supported Visual Studio 2022 installations through the documented Setup Configuration API or `vswhere`, launches `.sln` and `.slnx` files through `devenv`, and retains its own window-detection rules. The two IDE integrations share process-runner and association concepts but not executable discovery assumptions.

Launching remains distinct from discovering the eventual top-level window. The CLI process ID must not be treated as proof of ownership.

## Milestone 4

Introduce an isolated Win32 adapter for enumeration, liveness checks, placement, restoration, and activation. Association should compare visible VS Code windows before and after a launch, gather process and timing evidence, and expose confidence and ambiguity rather than selecting an arbitrary candidate. Existing ambiguous windows require explicit user selection.

Activation must report failure because Windows foreground restrictions can prevent `SetForegroundWindow` from succeeding. No path inference based only on a window title should be represented as certain.

## Embedded feasibility

Reparenting an Electron window is not a supported VS Code integration contract. It remains a separate spike after the production Window Manager is proven and must be judged against the acceptance matrix in the product specification before any production inclusion.
