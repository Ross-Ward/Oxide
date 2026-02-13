# NWG Plugin Enhancement Implementation Plan

## Phase 1: Fix NWGPerms Critical Issues ⚠️

### Issues Identified
1. **UI closes when clicking permissions** - CursorEnabled issue
2. **Permissions not being granted/revoked** - Console command logic bug
3. **UI not updating after changes** - Missing refresh calls
4. **Missing command documentation** - Need help system

### Fixes Required

#### 1.1 Fix UI Cursor Issues
- **Problem**: Clicking anywhere closes the UI
- **Solution**: Ensure `CursorEnabled = true` on all interactive panels
- **Files**: `NWGPerms.cs` - All `DrawXXXUI` methods
- **Priority**: CRITICAL

#### 1.2 Fix Permission Grant/Revoke Logic
- **Problem**: Console commands not properly applying permissions
- **Solution**: Debug `CCPermsList` console command handler
- **Files**: `NWGPerms.cs` - Line ~500-550
- **Priority**: CRITICAL

#### 1.3 Add UI Refresh After Actions
- **Problem**: UI doesn't update after granting/revoking
- **Solution**: Call `CloseUI()` then redraw current screen after each action
- **Files**: `NWGPerms.cs` - All console command handlers
- **Priority**: HIGH

#### 1.4 Add Command Help System
- **Problem**: No in-game documentation
- **Solution**: Add `/perms help` command with full documentation
- **Files**: `NWGPerms.cs` - New `CmdPermsHelp` method
- **Priority**: MEDIUM

---

## Phase 2: Create NWGConfigEditor Plugin 🎛️

### Overview
A universal in-game config editor that can modify any NWG plugin's configuration through a GUI.

### Core Features
1. **Plugin Selection Menu** - List all NWG plugins with configs
2. **Config Field Editor** - Edit strings, numbers, booleans, lists
3. **Live Preview** - Show current vs new values
4. **Validation** - Prevent invalid config values
5. **Apply & Reload** - Save config and reload plugin

### Architecture

```
NWGConfigEditor
├── UI System
│   ├── Main Menu (Plugin List)
│   ├── Config Editor (Field List)
│   ├── Field Editor (Type-specific inputs)
│   └── Confirmation Dialog
├── Config Parser
│   ├── JSON Reader
│   ├── Type Detector (string/int/float/bool/array)
│   └── Validator
└── Config Writer
    ├── JSON Writer
    └── Plugin Reloader
```

### Implementation Steps

#### 2.1 Create Base Plugin Structure
**File**: `NWGConfigEditor.cs`
```csharp
- Permission: nwgconfig.use
- Commands: /config, /cfg
- Data: Track editing sessions
```

#### 2.2 Build Plugin Scanner
```csharp
- Scan oxide/config/ for NWG*.json files
- Parse plugin name from filename
- Load config into memory
- Detect field types
```

#### 2.3 Create Main Menu UI
```csharp
- List all NWG plugins (paginated)
- Show plugin version/description
- "Edit Config" button per plugin
- Search/filter functionality
```

#### 2.4 Create Config Editor UI
```csharp
- Display all config fields
- Group by category (if nested JSON)
- Show current value
- Input field for new value
- Type-specific editors:
  - String: Text input
  - Number: Number input with validation
  - Boolean: Toggle button
  - Array: Multi-line with add/remove
```

#### 2.5 Implement Field Editors
```csharp
- Text Input: CuiInputFieldComponent
- Number Input: CuiInputFieldComponent with validation
- Boolean Toggle: CuiButton with color change
- Array Editor: Dynamic list with +/- buttons
- Color Picker: Hex input with preview
```

#### 2.6 Add Validation System
```csharp
- Min/Max for numbers
- Regex for strings (e.g., hex colors)
- Required fields
- Custom validators per plugin
```

#### 2.7 Implement Save & Reload
```csharp
- Write modified JSON to config file
- Call plugin.LoadConfig() via reflection
- Show success/error message
- Return to main menu
```

---

## Phase 3: Integrate Config Editor with Existing Plugins 🔌

### Plugins to Support (Priority Order)

#### High Priority
1. **NWGGather** - Complex config with many rates
2. **NWGStacks** - Item overrides and multipliers
3. **NWGProduction** - Furnace/smelter settings
4. **NWGAdmin** - Radar/vanish settings

#### Medium Priority
5. **NWGMarkers** - Default marker settings
6. **NWGPerms** - UI colors and behavior
7. **NWGTransportation** - Warp settings
8. **NWGChat** - Chat formatting

#### Low Priority
9. **NWGRandomEvents** - Event frequencies
10. **NWGPiracy** - Piracy mechanics

### Per-Plugin Enhancements

#### 3.1 Add Config Metadata
Add comments/descriptions to config classes:
```csharp
[JsonProperty("Gather Rate Multiplier")]
[ConfigDescription("Global multiplier for all gather rates (1.0 = vanilla)")]
[ConfigRange(0.1, 100.0)]
public float GlobalMultiplier = 2.0f;
```

#### 3.2 Add Config Validation
Implement `ValidateConfig()` method in each plugin:
```csharp
private bool ValidateConfig()
{
    if (_config.GlobalMultiplier < 0.1f) return false;
    // ... more validation
    return true;
}
```

#### 3.3 Add Reload Hooks
Implement `ReloadConfig()` method:
```csharp
[ConsoleCommand("nwggather.reload")]
private void ReloadConfigCmd(ConsoleSystem.Arg arg)
{
    LoadConfigVars();
    ApplyConfig();
    SendReply(arg, "Config reloaded successfully.");
}
```

---

## Phase 4: Advanced Features 🚀

### 4.1 Config Templates
- Save/load config presets
- Share configs between servers
- Import/export functionality

### 4.2 Change History
- Track config changes
- Show who changed what and when
- Rollback functionality

### 4.3 Bulk Operations
- Apply same change to multiple plugins
- Global search/replace
- Batch enable/disable features

### 4.4 Config Profiles
- Different configs for different scenarios
- Quick-switch between profiles
- Scheduled profile changes (e.g., 2x weekends)

---

## Implementation Timeline

### Week 1: Critical Fixes
- [ ] Day 1-2: Fix NWGPerms UI issues
- [ ] Day 3-4: Fix permission grant/revoke logic
- [ ] Day 5-7: Add UI refresh and help system

### Week 2: Config Editor Foundation
- [ ] Day 1-2: Create NWGConfigEditor plugin structure
- [ ] Day 3-4: Build plugin scanner and config parser
- [ ] Day 5-7: Create main menu UI

### Week 3: Config Editor Features
- [ ] Day 1-3: Build config editor UI
- [ ] Day 4-5: Implement field editors
- [ ] Day 6-7: Add validation and save/reload

### Week 4: Integration & Polish
- [ ] Day 1-3: Integrate with high-priority plugins
- [ ] Day 4-5: Add metadata and validation to plugins
- [ ] Day 6-7: Testing and bug fixes

---

## Testing Checklist

### NWGPerms
- [ ] UI stays open when clicking permissions
- [ ] Permissions are granted correctly
- [ ] Permissions are revoked correctly
- [ ] UI updates after changes
- [ ] Help command shows all commands
- [ ] Works for both players and groups
- [ ] Inherited permissions show correctly

### NWGConfigEditor
- [ ] All NWG plugins are detected
- [ ] Config files load correctly
- [ ] All field types display correctly
- [ ] String inputs work
- [ ] Number inputs validate
- [ ] Boolean toggles work
- [ ] Array editors work
- [ ] Changes save to file
- [ ] Plugins reload after save
- [ ] Invalid values are rejected
- [ ] UI is responsive and clear

### Integration
- [ ] Each plugin's config is editable
- [ ] Validation prevents bad values
- [ ] Reload commands work
- [ ] No conflicts between plugins
- [ ] Performance is acceptable

---

## Success Criteria

### Must Have
✅ NWGPerms works without UI closing issues
✅ Permissions can be granted/revoked via UI
✅ Config editor can modify at least 5 plugins
✅ Changes persist after server restart
✅ No data corruption or crashes

### Should Have
✅ Config editor supports all field types
✅ Validation prevents invalid configs
✅ UI is intuitive and well-designed
✅ Help documentation is complete

### Nice to Have
✅ Config templates and presets
✅ Change history tracking
✅ Bulk operations
✅ Config profiles

---

## Risk Assessment

### High Risk
- **Config corruption**: Mitigate with validation and backups
- **Plugin crashes**: Mitigate with try/catch and safe defaults
- **Permission issues**: Mitigate with thorough testing

### Medium Risk
- **UI complexity**: Mitigate with clear design and user testing
- **Performance**: Mitigate with efficient code and caching
- **Compatibility**: Mitigate with version checks

### Low Risk
- **User confusion**: Mitigate with help text and tooltips
- **Edge cases**: Mitigate with comprehensive testing

---

## Next Steps

1. **Review this plan** - Confirm approach and priorities
2. **Start Phase 1** - Fix critical NWGPerms issues
3. **Prototype Phase 2** - Build basic config editor
4. **Iterate** - Test, refine, and expand

---

## Notes

- All UI should follow NWG design standards (colors, spacing, etc.)
- All plugins should have consistent command structure
- Config editor should be extensible for future plugins
- Documentation should be generated automatically where possible
- Consider adding `/nwg` master command that opens a hub UI for all NWG tools
