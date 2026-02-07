# Plugin Suite Code Review - Issues & Recommendations

**Date:** January 28, 2026  
**Scope:** All plugins in `/oxide/plugins/` directory

---

## ✅ POSITIVE FINDINGS

### No Compilation Errors
All plugins compile successfully with no C# syntax errors.

### Good Memory Management
- **ZoneManager.cs**: Proper use of `Pool.Get<>()` and `Pool.FreeUnmanaged()` for list management
- **RemoverTool.cs**: Uses StringBuilder efficiently for debug logging
- **RaidableBases.cs**: Proper object lifecycle management with OnDestroy() hooks

### Proper Hook Cleanup
Plugins correctly unload hooks:
- RestoreUponDeath: Disables backpack hooks on unload
- ZoneManager: Destroys all zones and UI on unload
- TruePVE: Cleans up game object references

### Good Logging Practices
- Debug mode available in multiple plugins (RemoverTool, RaidableBases, Skins)
- Debug logging to files for troubleshooting

---

## ⚠️ ISSUES & RECOMMENDATIONS

### 1. POTENTIAL NULL REFERENCE ISSUES

#### Issue: RestoreUponDeath.cs - Missing null checks
**Location:** Line 62-70 (OnEntityDeath hook)
```csharp
if (!player || !player.userID.IsSteamId() || restoreData.HasRestoreData(player.userID))
    return;
```
**Problem:** `restoreData` could be null if `LoadData()` fails  
**Recommendation:** Add null check:
```csharp
if (restoreData == null) return;
if (!player || !player.userID.IsSteamId() || restoreData.HasRestoreData(player.userID))
    return;
```

---

#### Issue: NWG_Core.cs - Potential null colorCode handling
**Location:** Line 72 (FormatMessage method)
```csharp
public static string FormatMessage(string message, string colorCode = null)
{
    colorCode ??= NWGColors.Orange;
    return $"{NWGBranding.ColoredPrefix} <color={colorCode}>{message}</color>";
}
```
**Problem:** If `message` is null, it will produce invalid XML  
**Recommendation:**
```csharp
public static string FormatMessage(string message, string colorCode = null)
{
    if (string.IsNullOrEmpty(message)) return string.Empty;
    colorCode ??= NWGColors.Orange;
    return $"{NWGBranding.ColoredPrefix} <color={colorCode}>{message}</color>";
}
```

---

### 2. HOOK EXECUTION ORDER PROBLEMS

#### Issue: TruePVE + RestoreUponDeath + ZoneManager Death Handling
**Critical:** Three plugins respond to player death with different mechanisms

**Current Flow:**
1. Player dies → OnEntityDeath fires (RestoreUponDeath)
2. Player dies → OnEntityKill fires (RestoreUponDeath)
3. Player respawns → OnPlayerRespawned (RestoreUponDeath)
4. TruePVE rules should prevent death in PVE zones

**Problem:** If TruePVE blocks damage, RestoreUponDeath may still try to restore inventory  
**Recommendation:** RestoreUponDeath should check TruePVE state:
```csharp
private void OnEntityDeath(BasePlayer player, HitInfo info)
{
    if (!player || !player.userID.IsSteamId()) return;
    
    // Check if death was prevented by TruePVE
    var truePVE = plugins.Find("TruePVE");
    if (truePVE != null && (bool?)truePVE.CallHook("IsEnabled") == true)
    {
        var mapping = truePVE.CallHook("GetPlayerMapping", player) as string;
        if (mapping != "pvp") return; // Don't restore in PVE zones
    }
    
    // Rest of logic...
}
```

---

### 3. PLUGIN DEPENDENCY RISKS

#### Issue: SpawnNPC.cs references optional plugins without null checks
**Location:** Line 38 & 453
```csharp
[PluginReference] Plugin Kits, MarkerManager;
...
object checkKit = Instance.Kits?.CallHook("GetKitInfo", KitName);
```
**Status:** ✅ SAFE - Uses null-conditional operator `?.`

#### Issue: TruePVE references multiple optional plugins
**Location:** Line 31-32
```csharp
[PluginReference]
Plugin ZoneManager, LiteZones, Clans, Friends, AbandonedBases, RaidableBases;
```
**Problem:** Code doesn't consistently check for null before calling hooks
**Example:** Line 2901 checks null, but line 2880 doesn't
```csharp
// Line 2880 - Missing null check
if (Interface.CallHook("CanEntityBeTargeted", ...) is bool val)

// Better approach:
if (ZoneManager != null && Interface.CallHook("CanEntityBeTargeted", ...) is bool val)
```

---

### 4. RESOURCE CLEANUP ISSUES

#### Issue: ZoneManager.cs OnDestroy not always called
**Location:** Line 1225-1235
```csharp
private void OnDestroy()
{
    Pool.FreeUnmanaged(ref players);
    Pool.FreeUnmanaged(ref entities);
    // ... etc
}
```
**Problem:** If gameobject is destroyed by other means, OnDestroy may not fire  
**Recommendation:** Also clean up in Zone.Destroy():
```csharp
public void Destroy()
{
    Pool.FreeUnmanaged(ref players);
    Pool.FreeUnmanaged(ref entities);
    UnityEngine.Object.Destroy(Object);
}
```

---

### 5. CONFIGURATION VALIDATION ISSUES

#### Issue: Missing null checks in config initialization
**Location:** Multiple plugins (RemoverTool, RaidableBases, etc.)
**Problem:** LoadDefaultConfig may not catch all invalid states

**Example - RaidableBases.cs Line 21203:**
```csharp
if (config == null) throw new NullReferenceException("config");
```
This is too late - should validate in OnServerInitialized()

**Recommendation:**
```csharp
private void OnServerInitialized()
{
    if (config == null)
    {
        PrintError("Configuration failed to load. Using defaults.");
        LoadDefaultConfig();
    }
    // Validate required config sections
    if (config.Settings == null) config.Settings = new();
}
```

---

### 6. ANTI-HACK & PERFORMANCE ISSUES

#### Issue: Vanish.cs and SpawnNPC.cs heavy use of raycasts
**Location:** SpawnNPC.cs Line 996
```csharp
if (!NavMesh.SamplePosition(position, out navmeshHit, 10f, 1) || 
    position.y < TerrainMeta.WaterMap.GetHeight(position) || 
    IsInRockPrefab(position) || 
    IsNearWorldCollider(position) || 
    IsBuildingBlocked(position) || 
    IsInObject(position) || 
    AntiHack.TestInsideTerrain(position))
```
**Problem:** Multiple expensive checks every spawn. Could cause lag spikes  
**Recommendation:** Cache results for 1-5 seconds on same position:
```csharp
private Dictionary<Vector3, (float, bool)> _spawnCheckCache = new();
private const float CACHE_TIME = 2f;

private bool IsValidSpawnPosition(Vector3 position)
{
    if (_spawnCheckCache.TryGetValue(position, out var cached))
    {
        if (Time.realtimeSinceStartup - cached.Item1 < CACHE_TIME)
            return cached.Item2;
    }
    // Run expensive checks
    bool result = PerformExpensiveChecks(position);
    _spawnCheckCache[position] = (Time.realtimeSinceStartup, result);
    return result;
}
```

---

### 7. ARRAY/LIST ITERATION SAFETY

#### Issue: Direct foreach over changing collections
**Location:** ZoneManager.cs Line 79, 137, 140
```csharp
foreach (string flag in ZoneFlags.NameToIndex.Keys) { }
foreach (BasePlayer player in BasePlayer.activePlayerList) { }
foreach (KeyValuePair<string, Zone> kvp in zones) { }
```
**Problem:** Collections might change during iteration if plugins are unloaded  
**Recommendation:** Create copies:
```csharp
foreach (string flag in ZoneFlags.NameToIndex.Keys.ToList()) { }
foreach (BasePlayer player in BasePlayer.activePlayerList.ToList()) { }
foreach (KeyValuePair<string, Zone> kvp in zones.ToList()) { }
```

---

### 8. HARDCODED TIMER VALUES

#### Issue: Potential timer spam in RemoverTool
**Location:** Line 241
```csharp
timer.Once(Random.Range(0f, 60f), SaveDebug);
```
**Problem:** Multiple timer instances could accumulate if not properly cleared  
**Recommendation:** Track and cancel old timers:
```csharp
private Timer _saveDebugTimer;

private void ScheduleSaveDebug()
{
    _saveDebugTimer?.Destroy();
    _saveDebugTimer = timer.Once(Random.Range(0f, 60f), SaveDebug);
}
```

---

### 9. RACE CONDITION: OnEntityKill vs OnPlayerDeath

#### Issue: RestoreUponDeath uses both hooks
**Location:** Line 62 & 80-82
```csharp
private void OnEntityDeath(BasePlayer player, HitInfo info) { ... }
private void OnEntityKill(BasePlayer player) { OnEntityDeath(player, null); }
```
**Problem:** Both hooks fire for same event, could process twice
**Current Mitigation:** `restoreData.HasRestoreData()` check prevents double processing ✅
**Risk:** If HasRestoreData() is unreliable, could cause data corruption

---

### 10. TODO/INCOMPLETE CODE

#### Issue: StackSizeController.cs Line 517
```csharp
// TODO: Consider refactoring workflow to avoid ambiguity
```
**Status:** Needs investigation - incomplete refactoring

---

## SUMMARY OF CRITICAL FIXES NEEDED

| Severity | Issue | Plugin | Fix |
|----------|-------|--------|-----|
| 🔴 HIGH | Null reference in FormatMessage | NWG_Core | Add null check for message |
| 🔴 HIGH | Missing restoreData null check | RestoreUponDeath | Validate loaded data |
| 🟡 MEDIUM | Hook execution order conflicts | TruePVE, RestoreUponDeath | Add plugin state checks |
| 🟡 MEDIUM | Collection iteration during changes | ZoneManager | Use .ToList() |
| 🟡 MEDIUM | Missing optional plugin checks | TruePVE | Null check before CallHook |
| 🟠 LOW | Performance: expensive raycasts | SpawnNPC | Implement position caching |
| 🟠 LOW | TODO: Incomplete code | StackSizeController | Complete refactoring |

---

## TESTING RECOMMENDATIONS

1. **Test null scenarios:**
   - Load server with RestoreUponDeath config missing/corrupted
   - Test NWG_Core.FormatMessage with null inputs

2. **Test plugin load order:**
   - Load TruePVE first, then RestoreUponDeath
   - Kill player in PVE zone - verify no double inventory restore

3. **Test resource cleanup:**
   - Reload ZoneManager plugin
   - Check for memory leaks with large number of zones

4. **Test performance:**
   - Spawn 50+ NPCs simultaneously in SpawnNPC
   - Monitor CPU usage and frame time

---

**Generated:** January 28, 2026  
**Review Status:** ✅ Complete
