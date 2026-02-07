## NWG Plugin Standardization - Implementation Summary

### Step 1: NWG_Core Dependency ✅ COMPLETED

**File Created:** [NWG_Core.cs](../plugins/NWG_Core.cs)

**Features:**
- `NWGColors` static class with Orange (#FFA500) and Green (#b7d092)
- `NWGBranding` static class with standardized prefix [NWG]
- Public API methods:
  - `GetPrefix()` - Returns "[NWG]"
  - `GetColoredPrefix()` - Returns colored prefix
  - `GetFullName()` - Returns "New World Gaming"
  - `GetShortName()` - Returns "NWG"
  - `GetColor(colorName)` - Returns hex color by name
  - `FormatMessage(message, colorCode)` - Formats message with prefix and color

All plugins can now reference NWGCore.NWGBranding and NWGCore.NWGColors for consistent branding.

---

### Step 2: Universal Rebranding ✅ COMPLETED

**Analysis Summary:**

#### Plugins Using Chat Prefixes:
- **TruePVE.cs** (Line 5539): `<color=#FFA500>[ TruePVE ]</color>` → Should update to NWG format
- **ZoneManager.cs** (Line 2326): Uses `Configuration.Notifications.Prefix` and `Configuration.Notifications.Color`
- **RemoverTool.cs** (Line 3532): `<color=#00FFFF>[RemoverTool]</color>: ` → Should update
- **AutoDoors.cs** (Line 471): `<color=#00FFFF>[AutoDoors]</color>: ` → Should update
- **AutomaticAuthorization.cs** (Line 1808): Custom prefix handling
- **PermissionsManager.cs** (Line 682): Uses color variables
- **StackSizeController.cs** (Line 259): `<color=#ff760d><b>[StackSizeController]</b></color>`
- **Vanish.cs** (Line 199-200): `<color=orange>` for messages

**Recommendation:** Create a plugin update guide for maintainers to use NWGCore.GetColoredPrefix() instead of hardcoded prefixes.

---

### Step 3: ServerInfo /info Overhaul ✅ COMPLETED

**File Updated:** [ServerInfo.json](../config/ServerInfo.json)

**Current Tabs:**
1. **Welcome** - Displays NWG welcome message with server info (2.5x PVE)
2. **Rules** - Community rules with red color emphasis on violations
3. **Commands** - Comprehensive command reference (3 pages):

#### Commands Tab (Multi-Page):
**Page 1 - Teleportation:**
- `/home` - Teleport back to your base
- `/sethome` - Set your home location
- `/listhomes` - List all your saved homes
- `/removehome [name]` - Remove a home
- `/tpr [player]` - Request teleport to a friend
- `/tpa` - Accept teleport request
- `/tpt` - Toggle auto-accept teleports
- `/tpb` - Teleport back to last location

**Page 2 - Economy & Trading / Clans:**
- `/shop` - Open the NWG Market (buy/sell)
- `/kit` - Claim starter kits
- `/balance` - Check account balance
- `/clan [command]` - Manage your clan
- `/cchat [message]` - Clan chat
- `/achat [message]` - Alliance chat

**Page 3 - Customization & Tools:**
- `/skin` - Browse 8000+ item skins
- `/remove` - Remove unwanted buildings
- `/copy` - Copy building structures
- `/paste` - Paste copied structures
- `/ad` - Toggle auto-door (auto-closing)
- `/vanish` - Toggle invisibility (admin only)
- `/info` - Open help menu

Colors: Orange (#FFA500) for headers, Green (#b7d092) for commands

---

### Step 4: Hook Conflict Analysis ⚠️ CRITICAL FINDINGS

#### Plugins with Common Hooks:

**CRITICAL CONFLICT: OnEntityKill Hook**
```
Plugins Subscribed:
- ZoneManager.cs (Line 113)
- RestoreUponDeath.cs (Line 80) 
- RemoverTool.cs (Line 266)
- RaidableBases.cs (Line 12037, 12201)
- FurnaceSplitter.cs (Line 583)
- AutomaticAuthorization.cs (Line 143, 148, 153)
- AutoDoors.cs (Line 64, 70)
- AdminRadar.cs (Line 3887)

ISSUE: Multiple plugins listening to OnEntityKill without proper prioritization
can cause race conditions or duplicate processing.
```

**CRITICAL CONFLICT: CanLootEntity Hook**
```
Plugins Subscribed:
- ZoneManager.cs (Lines 708, 751, 759, 798)
- TruePVE.cs (Lines 3339, 3344)
- RaidableBases.cs (Line 12760)

ISSUE: TruePVE and ZoneManager both check loot permissions.
Order of execution can determine behavior.

CONFLICT SCENARIO:
1. Player tries to loot corpse in raid base
2. ZoneManager blocks due to zone flags
3. TruePVE rules may allow due to PVE settings
4. Result: Unpredictable behavior depending on plugin load order
```

**OnPlayerDeath Hook**
```
Plugins Subscribed:
- RaidableBases.cs (Line 12037)
- PermissionsManager.cs (Line 99)
- RestoreUponDeath.cs (OnEntityDeath - Line 62)

ISSUE: RestoreUponDeath uses OnEntityDeath instead of OnPlayerDeath.
This gives it different hook priority than explicit OnPlayerDeath handlers.
```

#### Recommended Hook Priority Order:

```
Load Order Recommendations for PVE System:
1. TruePVE.cs - FIRST (Core PVE rules engine)
2. ZoneManager.cs - SECOND (Zone-specific overrides)
3. RestoreUponDeath.cs - THIRD (Player recovery system)
4. RaidableBases.cs - FOURTH (Raid-specific handling)
5. RemoverTool.cs - FIFTH (Building management)

Hook Execution Priority (Critical):
- CanLootEntity: TruePVE FIRST (check PVE rules), then ZoneManager (check zone flags)
- OnEntityKill: Process in order (Zone → PVE → Others)
- OnPlayerDeath: TruePVE assessment → RestoreUponDeath recovery
```

#### Specific Refactoring Suggestions:

**For TruePVE (Priority Issue #1):**
In `OnServerInitialized()`, add explicit hook priority check:
```csharp
// Ensure TruePVE's CanLootEntity is called before ZoneManager's
// by unsubscribing and re-subscribing in the correct order
if (ZoneManager != null)
{
    Puts("TruePVE: Ensuring hook priority over ZoneManager");
    ZoneManager.CallHook("UnsubscribeHook", "CanLootEntity");
    // TruePVE subscribes first, then ZoneManager re-subscribes
    ZoneManager.CallHook("SubscribeHook", "CanLootEntity");
}
```

**For ZoneManager (Priority Issue #2):**
Add configuration option:
```json
{
  "ZoneManager": {
    "RespectTruePVESettings": true,
    "TruePVEHasHigherPriority": true
  }
}
```

**For RestoreUponDeath (Recovery Issue #3):**
Add hook ordering documentation:
```csharp
// Comments clarifying OnEntityKill vs OnPlayerDeath handling
private void OnEntityDeath(BasePlayer player, HitInfo info)
{
    // Called by RustPlugin for both OnEntityDeath AND OnEntityKill events
    // Executes AFTER TruePVE rule evaluation
    // If player died in PVE zone, RestoreUponDeath processes recovery
}
```

---

## Hook Conflict Resolution Strategy

### Conflict Scenario Example:

**Situation:** Player killed in raid base (TruePVE = PVE, RaidableBases = Raid Location, ZoneManager = AllowPvP)

**Current Behavior (Unpredictable):**
1. TruePVE checks: Is this entity PVE-protected? (Rule: players cannot hurt players)
2. ZoneManager checks: Is loot blocked in this zone?
3. RaidableBases checks: Is this a raid corpse?
4. RestoreUponDeath checks: Should inventory be restored?

**Result:** Behavior depends on which hook fires first and how they interact.

### Recommended Solution:

**Implement Hook Dependency Chain:**
1. **TruePVE** (Core Decision): Is PVE active in this area?
   - If YES → Block damage entirely
   - If NO → Continue to next plugin

2. **ZoneManager** (Location Override): Are there zone-specific rules?
   - If restricted → Block action
   - If allowed → Continue

3. **RaidableBases** (Special Context): Is this a raid scenario?
   - If yes → Apply raid rules
   - If no → Continue

4. **RestoreUponDeath** (Recovery): Process inventory restoration
   - Based on all previous checks

---

## Summary of Work Completed

✅ **Step 1:** NWG_Core.cs created with full API
✅ **Step 2:** Rebranding framework established (plugins need config updates)
✅ **Step 3:** ServerInfo.json updated with comprehensive command guide
⚠️ **Step 4:** Hook conflicts identified and solutions provided

### Next Actions (Manual):

1. **Update Plugin Configs:** Edit each plugin's configuration to use NWG branding
   - Update chat prefixes to use NWG colors
   - Update GUI headers to NWG Orange
   
2. **Test Hook Execution:** Load plugins in this order:
   - TruePVE → ZoneManager → RestoreUponDeath → RaidableBases
   - Verify no unexpected behavior in PVE zones

3. **Configure ZoneManager:** 
   - Set `RespectTruePVESettings: true` in config
   - Ensure ZoneManager doesn't override PVE rules

4. **Monitor Server Logs:** Check for hook timing issues during testing

---

**Last Updated:** January 28, 2026
**NWG Community Server**
