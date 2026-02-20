# Prompt for Next Chat (Rust Oxide Log Triage + Fixes)

Use this workspace and continue log triage/fixing from the latest checkpoint only.

## Workspace
- Root: `f:\Software Developement\gameservers\rust\nwgserver\oxide`
- Main log: `logs/2026-02-19_T2311.log`
- Plugins: `plugins/*.cs`

## Hard Rules
1. Triage **only newly appended lines** after the checkpoint below.
2. Prefer **root-cause fixes** in plugin code/config migration logic.
3. Before declaring a dependency missing, search workspace for local equivalent types/hooks.
4. Keep edits minimal and targeted.
5. After edits, run diagnostics on edited files and report exact remaining blockers.

## Checkpoint
- Last reviewed line: **3100**
- Next read should start at: **3101**

## What was already fixed
- `plugins/NWGAimTrain.cs`
  - Resolved UI alias conflict by using `ATUI` alias.
  - Normalized permission registration/checks toward `nwgaimtrain.*` with legacy fallback.
- `plugins/NWGBots.cs`
  - Fixed API drift for `BecomeWounded` call.
  - Fixed `SignalBroadcast(..., string.Empty, null)` to float arg usage.
- `plugins/NWGArena.cs`
  - Normalized admin permission to `nwgarena.admin` and kept legacy fallback checks.
- `plugins/NWGKits.cs`
  - Guarded `CmdKitsConsole` editing dictionaries with `TryGetValue` to avoid `KeyNotFoundException`.
- `plugins/NWGAnticheat.cs`
  - Fixed nested namespace/main-class load issue.
  - Normalized permission checks to `nwganticheat.admin` with legacy fallback.
- `plugins/NWGSkills.cs`
  - Reworked horse speed API accesses using reflection-safe getters/setters.
  - Fixed stale symbol in XP popup (`NWGXPEventModifiedCol`).

## Known external/data blockers (not code-local)
- Missing extension deps:
  - `Oxide.Ext.Chaos` (affects `NWGClans`)
  - `Oxide.Ext.ChaosNPC` (affects `NWGZombies`)
- Image host failures:
  - HTTP 526 from `https://img.rustspain.com/...`
- Arena content availability:
  - Missing copypaste files for `ATER` / `ONEVSONE`

## What to do next
1. Read `logs/2026-02-19_T2311.log` from line `3101` onward.
2. Extract only fresh warnings/errors.
3. Patch locally fixable issues in `plugins/*.cs`.
4. Validate edited files via diagnostics.
5. Report:
   - Fixed items
   - Files changed
   - Remaining true blockers
   - New checkpoint line

## Suggested output format for user
- `Fixed:` bullet list
- `Changed files:` bullet list
- `Still blocked:` bullet list
- `Next checkpoint:` single line
