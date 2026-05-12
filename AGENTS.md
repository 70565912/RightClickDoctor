# AGENTS.md

## Project Goal

RightClickDoctor is a Windows desktop diagnostic tool for slow File Explorer right-click menus. It enumerates static verbs and COM-based ShellEx context menu handlers, probes dynamic handlers in an isolated helper process, highlights slow or failed handlers, and lets the user safely block or restore selected shell extensions.

## Tech Stack

- Language: C# on .NET Windows Desktop.
- UI: Windows Forms for a small, dependency-light desktop utility.
- Target framework: `net8.0-windows` or newer compatible Windows Desktop SDK.
- Platform: x64 Windows. Shell extensions are bitness-sensitive, so prefer x64 builds for normal Explorer diagnostics.
- External dependencies: avoid NuGet dependencies unless they materially reduce risk. Prefer built-in .NET APIs, Win32/COM interop, and registry APIs.

## Architecture

- Keep UI, registry discovery, probing, and remediation in separate namespaces.
- The main UI process must not load third-party shell extensions directly.
- COM timing probes must run in a child process with a timeout so a crashing or hanging handler cannot kill the UI.
- Treat registry changes as reversible:
  - Disable COM shell extensions by writing their CLSID to `HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked`.
  - Re-enable by removing that HKCU blocked value.
  - Avoid deleting third-party registration keys.
- Restarting Explorer is a user-triggered action only.

## Safety Rules

- Never automatically disable Microsoft-signed handlers.
- Never delete registry keys as a remediation path.
- Show the exact registry path, CLSID, DLL path, publisher, and measured timings before offering remediation.
- Persist reports as JSON and CSV so findings can be reviewed before changes.
- Any operation that may require admin rights should fail gracefully with a clear message.

## Coding Conventions

- Prefer clear, boring C# over clever abstractions.
- Keep COM interop definitions isolated under `Interop`.
- Use `CancellationToken` and process timeouts for long-running diagnostics.
- Keep comments short and only where COM, registry virtualization, or safety behavior needs explanation.
- Use nullable reference types and `ImplicitUsings`.

## Verification

- Build with `dotnet build`.
- For non-UI verification, support command-line modes for scan/probe/report generation.
- Do not require elevation to scan or to block extensions through HKCU.

## References To Respect

- Microsoft Shell context menu handler documentation.
- Microsoft Shell extension handler registration documentation.
- Similar GitHub projects used as design references:
  - `moudey/Shell`
  - `oleg-shilo/shell-x`
  - `yanxijian/ShellExtContextMenuHandler`
