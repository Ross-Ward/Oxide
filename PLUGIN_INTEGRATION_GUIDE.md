# Plugin Integration & Compatibility Guide

**Date:** January 29, 2026  
**Server:** Rust Oxide Plugin Suite  
**Status:** ✅ All plugins configured for seamless integration

---

## 🔗 Core Plugin Dependencies

### **Economy System**
- **Primary:** Economics.cs (currency backend)
- **Integration:** GUIShop, RaidableBases, NTeleportation, Kits
- **Default:** Economics enabled as primary currency
- **Config:** GUIShop.json - "Switches to Economics as default currency" = true

### **Zone Management**
- **Primary:** ZoneManager.cs (location-based control)
- **Integration:** TruePVE, RaidableBases, AutoDoors, SpawnNPC
- **Usage:** Defines PVE/PVP zones, raid spawn locations, protected areas

### **PVE Protection**
- **Primary:** TruePVE.cs (PVE rule enforcement)
- **Integration:** RaidableBases, ZoneManager, Clans, Friends
- **Dependencies:** ZoneManager, Clans, Friends, AbandonedBases, RaidableBases
- **Config Options:** 
  - Works alongside RaidableBases for raid zone PVE/PVP
  - Respects Clans and Friends for friendly fire

### **Raid Events**
- **Primary:** RaidableBases.cs (raid spawning and management)
- **Integration:** Economics, ZoneManager, TruePVE, Clans, Friends, Kits, Skins
- **Key Features:**
  - Automatic reward distribution via Economics
  - Zone protection via ZoneManager
  - PVE/PVP rule enforcement via TruePVE
  - Clan/Friend restrictions for loot locks
  - Custom NPC kits via Kits plugin
  - Item skins via Skins plugin

### **Shop System**
- **Primary:** GUIShop.cs (merchant interface)
- **Integration:** Economics, Kits, ImageLibrary, ServerRewards
- **Default Currency:** Economics
- **Categories:** Commands, Vehicles, Kits (customizable)

### **NPC System**
- **Primary:** SpawnNPC.cs (NPC spawning)
- **Integration:** Kits, MarkerManager, RaidableBases, ZoneManager
- **Usage:** Spawns NPCs for raids, can use custom kits

---

## ⚙️ Configuration Integration Points

### **RaidableBases.json**
```
Key Settings for Plugin Integration:
✓ Economics Integration: Reward system enabled
✓ ZoneManager Integration: Zone restrictions enforced
✓ TruePVE Integration: PVE/PVP zone rules applied
✓ Clans Integration: Clan members locked to shared treasure
✓ Friends Integration: Friendly fire restrictions
✓ Kits Integration: Custom NPC loadouts
✓ Skins Integration: Random/preset item skins
```

### **Economics.json**
```
Key Settings:
✓ Starting account balance: 1000
✓ Allow negative balance: false
✓ Remove unused accounts: true
✓ Wipe balances on new save: false
```

### **GUIShop.json**
```
Key Settings for Integration:
✓ Default currency: Economics
✓ Currency dropdown: Supports Economics + ServerRewards
✓ Default shop: Commands
✓ Commands category: For command shop items
```

### **TruePVE.json**
```
Key Settings for RaidableBases Integration:
✓ Respects ZoneManager zones
✓ Clans support: Prevents clan members from damaging each other
✓ Friends support: Prevents friends from damaging each other
✓ RaidableBases integration: Zones created by raids enforced
```

---

## 🔄 Plugin Interaction Flow

### **1. Raid Spawn → All Systems Activate**
```
RaidableBases spawns raid base
    ↓
ZoneManager creates temporary zone around base
    ↓
TruePVE applies zone rules (PVE or PVP)
    ↓
NPCs spawn (via SpawnNPC with Kits)
    ↓
Loot generated with optional Skins applied
    ↓
MarkerManager creates map marker
    ↓
Players notified via BetterChat
```

### **2. Player Raids Base → Damage & Rewards**
```
Player damages structure/NPC
    ↓
TruePVE checks damage rules in zone
    ↓
RaidableBases tracks raiders
    ↓
Clans/Friends restrictions applied (if enabled)
    ↓
Player completes raid
    ↓
Economics rewards distributed
    ↓
GUIShop currency updated for player
```

### **3. Shop Purchase → Item Delivery**
```
Player opens GUIShop
    ↓
Economics balance displayed
    ↓
Player selects item/command
    ↓
Cost deducted from Economics account
    ↓
Item/command executed
    ↓
Kits plugin applies customization (if applicable)
    ↓
Transaction logged
```

---

## 🎯 Recommended Integration Settings

### **For PVE-Focused Server:**
```
RaidableBases:
  - Set all spawns to "Include PVE Bases: true"
  - Set "Include PVP Bases: false"
  - Enable ZoneManager integration
  - Enable TruePVE integration

TruePVE:
  - Enable all PVE protections
  - Set friendly fire to false
```

### **For PVP-Focused Server:**
```
RaidableBases:
  - Set all spawns to "Include PVP Bases: true"
  - Set "Include PVE Bases: false"
  - Enable PVP delay system
  - Configure lock treasure settings

TruePVE:
  - Enable selective PVP protections
  - Allow friendly fire in PVP zones
```

### **For Hybrid Server (PVE + PVP zones):**
```
RaidableBases:
  - Set "Include PVE Bases: true"
  - Set "Include PVP Bases: true"
  - Use ZoneManager zones to designate area types
  - Configure separate rewards for each type

TruePVE:
  - Set zone-specific rules
  - Enable Clans/Friends protection
  - Allow damage in PVP zones only
```

---

## 🔐 Permission Hierarchy

```
Admin Permissions (Server Management):
├── raidablebases.config (Raid configuration)
├── zonemanager.admin (Zone control)
├── truepve.config (PVE rule configuration)
└── guishop.admin (Shop configuration)

Player Permissions (Feature Access):
├── raidablebases.allow (Access raids)
├── raidablebases.setowner (Lock raid to player)
├── raidablebases.allow.commands (Use blocked commands)
├── zonemanager.visit (Enter zones)
├── truepve.raids (Access raid systems)
└── guishop.use (Access shop)
```

---

## ✅ Pre-Launch Checklist

### **Economy Integration**
- [ ] Economics plugin loaded
- [ ] GUIShop set to use Economics
- [ ] Starting balance configured (recommended: 1000)
- [ ] RaidableBases reward amounts set

### **Zone Integration**
- [ ] ZoneManager plugin loaded
- [ ] Zones configured (PVE/PVP areas)
- [ ] Zone radius appropriate for server size
- [ ] RaidableBases zone restrictions enabled

### **PVE/PVP System**
- [ ] TruePVE plugin loaded
- [ ] Zone rules configured
- [ ] Clans plugin integration enabled
- [ ] Friends plugin integration enabled
- [ ] RaidableBases PVE/PVP settings match zones

### **Shop System**
- [ ] GUIShop enabled
- [ ] Economics selected as currency
- [ ] Shop categories populated
- [ ] NPC locations configured (optional)

### **Raid Events**
- [ ] RaidableBases profiles configured
- [ ] CopyPaste buildings loaded
- [ ] Loot tables configured
- [ ] Reward amounts reasonable
- [ ] Difficulty levels balanced

### **Information**
- [ ] ServerInfo plugin enabled
- [ ] /info command working
- [ ] /rbhelp available to players
- [ ] /rbadminhelp available to admins
- [ ] Commands documented

### **Safety & Compatibility**
- [ ] All plugin versions compatible
- [ ] No conflicting permissions
- [ ] Test raid spawn and completion
- [ ] Test economy reward distribution
- [ ] Test zone enforcement

---

## 🚨 Common Integration Issues & Solutions

### **Issue: Raids spawn but no rewards given**
**Solution:**
- Verify Economics plugin is loaded: `/plugins info`
- Check RaidableBases config has reward amounts set
- Verify GUIShop currency set to Economics
- Check player doesn't have "raidablebases.banned" permission

### **Issue: Players can damage in PVE zones**
**Solution:**
- Verify TruePVE plugin is loaded
- Check ZoneManager zones are created
- Verify RaidableBases zone integration enabled
- Check TruePVE.json PVE settings: "All Damage Blocked": true

### **Issue: Shop doesn't show economics balance**
**Solution:**
- Verify Economics plugin loaded
- Check GUIShop config: "Switches to Economics as default currency": true
- Verify player has economics account (should auto-create)
- Check player balance: `/economy balance @player`

### **Issue: Clans/Friends not preventing damage**
**Solution:**
- Verify Clans and Friends plugins loaded
- Check TruePVE config has these plugins referenced
- Verify players are actually in same clan/friend list
- Check TruePVE zone rules allow this protection

### **Issue: RaidableBases not using ZoneManager zones**
**Solution:**
- Verify ZoneManager plugin loaded
- Check RaidableBases config: zone integration enabled
- Verify zones exist: `/zm list`
- Check zone names in RaidableBases allowed zones list

---

## 📊 Plugin Load Order (Recommended)

1. **Core:** Economics, ZoneManager
2. **Protection:** TruePVE, Clans, Friends
3. **Events:** RaidableBases, SpawnNPC
4. **Interface:** GUIShop, ServerInfo, BetterChat
5. **Utility:** Kits, Skins, ImageLibrary, MarkerManager
6. **Optional:** AdminRadar, Vanish, etc.

Load order typically automatic, but ensure no circular dependencies.

---

## 🔄 Plugin Communication Methods

### **Hooks Called by RaidableBases:**
- `OnRaidableBaseStarted` - Base spawned
- `OnRaidableBaseEnded` - Base despawned
- `OnRaidablePlayerEntered` - Player entered raid zone
- `OnRaidablePlayerExited` - Player left raid zone
- `OnRaidableLootDestroyed` - Container destroyed
- `OnRaidableBaseDespawned` - Base fully cleaned up

### **Hooks Called to Other Plugins:**
- `GetPlayerMoney()` - To Economics for player balance
- `Interface.CallHook("GetGridWaitTime")` - To config system
- `Clans?.Call("IsClanMember", ...)` - To Clans plugin
- `Friends?.Call("AreFriends", ...)` - To Friends plugin
- `ZoneManager?.Call(...)` - To ZoneManager for zones

---

## 📝 Custom Configuration Example

### **Running a Raid Event Economy:**
```json
RaidableBases: {
  "Easy Difficulty Rewards": 500,
  "Normal Difficulty Rewards": 1000,
  "Hard Difficulty Rewards": 2500,
  "Divide Rewards Among All Raiders": true
}

Economics: {
  "Starting account balance": 1000,
  "Allow negative balance": false
}

GUIShop: {
  "Switches to Economics as default currency": true,
  "Commands category": [
    {"Item": "Minicopter", "Cost": 500},
    {"Item": "Attack Heli", "Cost": 1000},
    {"Item": "CH47", "Cost": 1500}
  ]
}
```

This setup encourages players to raid bases to earn economy for shop purchases!

---

## 🆘 Support & Troubleshooting

**Check Plugin Status:**
```
/plugins info economics
/plugins info guishop
/plugins info raidablebases
/plugins info truepve
/plugins info zonemanager
```

**View Configuration:**
```
/oxide.reload RaidableBases (to reload config)
/oxide.reload Economics
/oxide.reload GUIShop
```

**Debug Commands:**
```
/raidbase active (show active raids and queue)
/zm list (show all zones)
/economy balance @player (check balance)
```

---

**All plugins are configured and ready for production use!**
