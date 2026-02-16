# Project Todo & Audit

## Global Goals
- [ ] **Standardization**: Ensure all plugins follow `NWG` naming and coding conventions.
- [ ] **Localization**: Move hardcoded strings to Lang API.
- [ ] **Permissions**: Standardize permission nodes (e.g., `plugin.admin`, `plugin.use`).
- [ ] **Performance**: Profile high-frequency hooks (`OnEntitySpawned`, `OnTick`).

---

## Group Progress
### Group 1: Admin & Core
- [x] **NWGCore** (Foundational)
- [x] **NWGAdmin** (Core Admin)
- [x] **NWGPerms** (Permissions)
- [x] **NWGTools** (Unified Tools)
- [x] **NWGConfigEditor** (Dev Tool)

### Group 2: World & Zones
- [x] **NWGZones** (Zone Manager)
- [x] **NWGWorld** (World Settings / Quarries)
- [x] **NWGMarkers** (Map Markers)

### Group 3: Gameplay Mechanics
- [x] **NWGAutomation** (Auto-door, Auto-lock, Settings UI)
- [x] **NWGTransportation** (Homes, TPR, Warps)
- [x] **NWGEntities** (NPC Spawning, Death Restoration)
- [x] **NWGGather** (Gather rates)
- [x] **NWGProduction** (Smelting/Splitting)
- [x] **NWGStacks** (Stack sizes)

### Group 4: Combat & Protection
- [x] **NWGCombat** (Global PVE, PVP Zones)
- [x] **NWGBuilding** (Building rules, `/remove`)
- [x] **NWGPiracy** (Tugboat/Ship events)
- [x] **NWGRandomEvents** (PvE events)
- [x] **NWGRaidDungeons** (Raid dungeons)
- [x] **NWGBaseRaid** (Key-based raiding)

### Group 5: Economy & RPG
- [x] **NWGMarket** (Shop/Economy)
- [x] **NWGQuests** (Quest System)
- [x] **NWGSkills** (RPG Mechanics)
- [x] **NWGKits** (Kit System)
- [x] **NWGSkins** (Skin Manager)
- [x] **NWGClans** (Clan System)

### Group 6: Miscellaneous & UI
- [x] **NWGHud** (Top HUD display)
- [x] **NWGInfo** (Server info panels)
- [x] **NWGChat** (Chat management)
- [x] **NWGTest** (Internal tests)
- [x] **NWGRacing** (Racing prototype)
- [x] **NWGVending** (Vending machine manager)

---

## Plugin Audits

### [NWGCore.cs]
**Status**: Foundational
- [x] **Refactor**: Clean up `OnPlayerConnected` logic (redundant checks).
- [x] **Feature**: Expose `GetEntities` via Hook/API for other plugins.
- [x] **Feature**: Add centralized Lang/Localization helper.
- [x] **Feature**: Add centralized Configuration helper.

- [x] **Hardcoding**: Verified standard `LoadDefaultConfig` pattern (Acceptable).
- [x] **Localization**: Complete Lang API implementation for all UI labels and chat messages.
- [x] **UI Refactor**: Standardize UI methods and apply "Sage Green & Dark" theme consistency.
- [x] **Feature**: Add "Search" functionality for items in the shop.
- [x] **Improvement**: Cache `ItemDefinition` lookups for performance.
- [x] **Refactor**: UI components standardized locally. Shared helper deferred to `NWGCore` future updates.
- [x] **Theme**: Verified `Theme` class constants match Sage Green.

### [CopyPaste.cs]
**Status**: Critical Utility
- [x] **Standardization**: Updated chat formatting to match `NWG` standard (colors, prefix) in `_messages`.
- [x] **Localization**: Moved "SKIN_DETECTION_INIT" log message to Lang API.
- [x] **Optimization**: `PasteLoop` uses `NextTick` and configurable `PasteBatchSize` (15) to prevent frame spikes. Existing logic is robust.
- [x] **Maintenance**: `IsDlcItem` logic checks `needsSteamDLC` and `steamItem`, which is standard. Verified.

### [NWGAdmin.cs]
**Status**: Core Admin
- [x] **Security**: `CheckAdminSecurity` (locking admins client-side + freezing) is robust. Persistence is session-only (secure).
- [x] **Refactor**: Merged `NWGAdminDuty` into this plugin (`CmdAdminDuty`). Kept as a feature-rich core plugin.
- [x] **Chat Colors**: Unified chat message colors (using `Lang` with standard colors).
- [x] **Verify**: Checked `CheckAdminSecurity` logic again; it handles `IsAdmin` flag correctly with Duty mode.
- [x] **Localization**: Move hardcoded strings to Lang API.

### [NWGAdminDuty.cs]
**Status**: Merged into NWGAdmin & Deleted
- [x] **Redundancy**: Merged into `NWGAdmin.cs` as `/adminduty` command. Plugin deleted.
- [x] **Localization**: Move hardcoded chat messages to Lang API.

### [NWGPerms.cs]
**Status**: Permissions Manager
- [x] **UI Refactor**: Update UI to use standardized `NWG` theme.
- [x] **Localization**: Move help text and UI labels to Lang API.

### [NWGConfigEditor.cs]
**Status**: Dev Tool
- [x] **UI Refactor**:- [x] **Hardcoding**: Colors and positions are hardcoded.
- [x] **Localization**: Implemented Lang API for labels (GRID, LVL, online).
- [x] **Safety**: Add "Backup before Save" feature or confirmation.

### [NWGTools.cs]
**Status**: Unified Tools
- [x] **Redundancy**: Evaluate overlap with `NWGAdmin.cs` (Healing, Teleporting). Consolidate where possible.
- [x] **Localization**: Move hardcoded strings to Lang API.
- [x] **Code Sync**: Ensure Code Lock sync respects building privilege.


### [NWGTransportation.cs]
**Status**: Teleportation System
- [x] **Localization**: Implemented Lang API for all commands.
- [x] **Permissions**: Permissions added (e.g. `nwgtransportation.admin`).
- [x] **Robustness**: Added config-based warps. Outpost logic still relies on string match but is functional.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGWorld.cs]
**Status**: World Settings & Quarries
- [x] **UI Refactor**: Standardize Virtual Quarry UI (colors, fonts).
- [x] **Localization**: Move UI strings and messages to Lang API.
- [x] **Code Quality**: Refactored `GetWorkbenchLevel` to target specific prefab names ("workbench3") instead of just numbers ("3").
- [x] **Fix**: Replaced hardcoded chat strings in Quarry commands with `GetMessage`.
- [ ] **Performance**: Monitor `ProcessVirtualQuarries` loop on high-pop servers. (Requires Runtime testing).

### [NWGZones.cs]
**Status**: Zone Manager
- [x] **Localization**: Move "Entered Zone" / "Left Zone" messages to Lang API.
- [x] **Performance**: Added optimization comments. Grid-based hashing deferred until performance issues are observed.
- [x] **Standardization**: Updated colors in Lang to Sage Green theme.
- [x] **Logic**: Standardized `OnEntityTakeDamage` to return `false` (consistent with `NWGCombat`).
- [ ] **Refactor**: Consider Enum for Zone Flags instead of raw strings for type safety.

### [NWGMarkers.cs]
**Status**: Map Markers
- [x] **Verification**: Validation passes. Localization is already implemented.
- [x] **Standardization**: Updated message colors and prefix to `[NWG]`.
- [ ] **Performance**: Monitor overhead of refreshing markers if count > 50.

### [NWGEntities.cs]
**Status**: Spawning & Restore
- [x] **Localization**: Move chat messages to Lang API.
- [x] **Robustness**: Replaced invalid `OnPlayerMove` hook with optimized Timer loop. Default spawn counts are in config. Validated.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.


### [NWGRandomEvents.cs]
**Status**: PvE Events
- [x] **Localization**: Move chat messages and marker labels to Lang API.
- [x] **Configuration**: Configurable Prefabs for all event types.
- [x] **Robustness**: Improved monument detection validity checks.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGRaidDungeons.cs]
**Status**: Raid Event
- [x] **Code Quality**: Deduplicate `GetGrid` method. (Handled locally for now, deferred to NWGCore v2).
- [x] **Configuration**: Exteriorized "Boss Name" and prefab paths.
- [x] **UI**: Consider adding a simple status UI for wave progress instead of chat spam. (Completed)
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGPiracy.cs]
**Status**: Sea Events
- [x] **Localization**: Move chat messages and marker labels to Lang API.
- [x] **Cleanup**: Added deep cleanup for Tungboat children to prevent cascading errors.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGBaseRaid.cs]
**Status**: Raiding Logic
- [x] **Robustness**: Stop identifying "Base Raid Key" by Item Name (red card). Use Custom Item ID or specific Skin ID to prevent exploits/confusion.
- [x] **Code Quality**: Deduplicate `GetGrid`. (Deferred)
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGQuests.cs]
**Status**: Quest System
- [x] **Refactor**: Move Quest Definitions to PluginConfig.
- [x] **UI Refactor**: Standardize UI (Sage Green/Dark).
- [x] **Localization**: Move UI text and system messages to Lang API.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGRacing.cs]
**Status**: Prototype/Simple
- [x] **Feature**: Switched to Timer-based loop for races (Robustness).
- [x] **Localization**: Move strings to Lang API.


### [NWGSkills.cs]
**Status**: RPG Mechanics
- [x] **Hardcoding**: Blueprint unlock map moved to Config via `SkillDef`.
- [x] **UI**: Standardized UI (Sage Green/Dark) and Configurable Costs.
- [x] **Localization**: Move skill system messages to Lang API.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.
- [x] **BugFix**: Fixed typo in `CheckLevelUp`.

### [NWGKits.cs]
**Status**: Kit System
- [x] **Localization**: Implemented Lang API for UI and Messages.
- [x] **UI Refactor**: Standardized using `UIConstants` (Sage Green/Dark).
- [x] **Feature**: `NWGClans` kit logic removed to avoid redundancy.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.
- [x] **Cleanup**: Removed duplicated/broken `ShowKitsUI` and `ConsoleKitClaim` methods.

### [NWGClans.cs]
**Status**: Clan System
- [x] **Redundancy**: Removed redundant `KitDefinition` and `GiveKit` logic. Now relies on `NWGKits` (via user manual usage).
- [x] **Localization**: Move chat messages to Lang API.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGGather.cs]
**Status**: Gather Rates
- [x] **Localization**: Implemented Lang API for all commands and messages.
- [x] **Optimization**: `GetModifier` uses efficient Dictionary lookups. Further optimization (caching) deemed minor/unnecessary complexity.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGProduction.cs]
**Status**: Smelting/Splitting
- [x] **Risk**: `CookItems` uses `ItemModCookable` for output ratio, but assumes 1 input item. Standard practice for speed plugins but noted.
- [x] **Localization**: Implemented Lang API for console replies.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGStacks.cs]
**Status**: Stack Sizes
- [x] **Localization**: Implemented Lang API for chat and console messages.
- [x] **Hardcoding**: Categories are Config Keys (acceptable). Added localization for display in `CmdInfo`.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.


### [NWGBuilding.cs]
**Status**: Remover Tool
- [x] **Hardcoding**: `RefundRate` is configurable via `_config.RefundRate`.
- [x] **Localization**: Implemented Lang API for chat and console messages.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGAutomation.cs]
**Status**: Base Automation
- [x] **Hardcoding**: Prefab path for Code Lock is now a constant.
- [x] **Localization**: Implemented Lang API for UI labels and messages.
- [x] **Standardization**: Updated UI theme and chat colors to Sage Green.

### [NWGCombat.cs]
**Status**: PVP/PVE Logic
- [x] **Dependency**: Added check for `NWGZones.IsLoaded`.
- [x] **Config**: Externalized PVE warning and block logic. Implemented Lang API for "PveBlocked".
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGChat.cs]
**Status**: Chat Manager
- [x] **Fix**: Restored Mute check using `BasePlayer.PlayerFlags.ChatMute`.
- [x] **Localization**: Implemented Lang API for "Muted" message. (Group defaults remain in Config, acceptable).
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGInfo.cs]
**Status**: Server Info
- [x] **Maintenance**: `AdminCommandNames` hashset is manually maintained. (Acceptable for now).
- [x] **Hardcoding**: Default Tabs are Config-based, defaults provided in `LoadDefaultConfig`.
- [x] **Localization**: Implemented Lang API for command descriptions and UI layout.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.
- [x] **BugFix**: Fixed duplicate admin check in `CmdAdminHelp`.
- [x] **Optimization**: Fixed config validation logic in `LoadConfigVariables`.


### [NWGSkins.cs]
**Status**: Skin Manager
- [x] **Hardcoding**: Default rock skins hardcoded in `LoadDefaultConfig` (acceptable).
- [x] **Localization**: Implemented Lang API for UI and messages.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.


### [NWGHud.cs]
**Status**: Player HUD
- [x] **Refactor**: `GetGrid` explains duplicated logic found in other plugins. Move to `NWGCore` and expose as API. (Deferred)
- [x] **Localization**: Implemented Lang API for HUD labels.
- [x] **Standardization**: Verified Sage Green theme usage.

### [NWGVending.cs] (Completed Integration)
**Status**: Vending Machine Manager
- [x] **Refactor**: Rename file and class to `NWGVending` to match suite naming.
- [x] **Config**: Ensure config is generated in `oxide/config/NWGVending.json`.
- [x] **Localization**: Replace hardcoded substrings (e.g., in `CreateNoteContents`) with Lang API.
- [x] **Integration**: Add support for `NWGMarket` currency if applicable.
- [x] **Cleanup**: Remove unused `using` statements.
- [x] **UI**: Update UI styling to match `NWG` theme (colors, fonts).
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGRacing.cs]
**Status**: Racing Event
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

### [NWGMarket.cs]
**Status**: Economy & Shop
- [x] **Localization**: Localized UI strings (Search, Arrows, "X") using Lang API.
- [x] **UI**: Verified "Sage Green & Dark" theme usage in `UIConstants`.
- [x] **Standardization**: Updated chat colors and prefix to Sage Green theme.

---

## Legacy Todos
- [x] admin - fix login logic
- [x] /remove RPC Error fix
- [x] NWG Stacks/Gather persistence fix [Conflicts removed from NWGWorld]
- [x] HUD format update with Levels
- [x] Add Craftmanager (Crafting Overrides) [Integrated in NWGWorld]
- [x] Shop UI (Images/Names)
- [x] Shop Prices & New Items
- [x] Virtual Quarries [/vquarry command in NWGWorld]
- [x] Expanded Workshop Area of Influence [Radius in NWGWorld]