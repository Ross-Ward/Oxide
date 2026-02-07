# 🎮 New World Gaming (NWG) - Server Master Guide
**Last Updated:** February 7, 2026

Welcome to the unified guide for the NWG Rust Server. This document consolidates all commands, development plans, and server status information into one place.

---

## 📊 1. Server Status & Integration
**Current Status:** ✅ ALL PLUGINS STABLE & INTEGRATED

### Core Plugin Matrix
| System | Primary Plugin | Status |
|--------|----------------|--------|
| **Economy** | Economics | ✓ Integrated with Shop & Raids |
| **PVE/PVP** | TruePVE | ✓ Zone-based enforcement |
| **Zones** | ZoneManager | ✓ Linked with Raids & Building |
| **Raids** | RaidableBases | ✓ Multi-tier with rewards |
| **Shop** | GUIShop | ✓ Uses Economics |
| **HUD** | NWGHud | ✓ Real-time balance & stats |

---

## 🛠️ 2. Command Reference
All commands support both `/` and `!` prefixes.

### 👤 Player Commands
| Command | Description |
|---------|-------------|
| `/info` | Open server information menu |
| `/home` | List or teleport to your saved homes |
| `/tpr <name>` | Request teleport to another player |
| `/shop` | Open the Market to buy/sell items |
| `/kit` | View and claim available kits |
| `/balance` | Check your current bank balance |
| `/clan` | Access clan management menu |
| `/remove` | Toggle building removal tool (if authorized) |
| `/skin` | Open skin workshop for held item |
| `/rates` | View current server gather rates |

### 👑 Admin & Staff Commands
| Command | Description |
|---------|-------------|
| `/vanish` | Toggle invisibility mode |
| `/radar` | Toggle admin ESP/Radar |
| `/god` | Toggle global invulnerability (Blocked on NPCs) |
| `/tp <name>` | Teleport to a specific player |
| `/bring <name>` | Bring a player to your location |
| `/heal <name>` | Restore player health/food/water |
| `/kick/ban` | Punish troublesome players |
| `/dungeon start global` | Start a GLOBAL Raid Event (Sphere Tank) |
| `/dungeon start <private/group>` | Start a Personal/Group Raid Instance |
| `/dungeon stopall` | Stop ALL active Raid Dungeons |
| `/event spawn` | Manually trigger a random PvE encounter |
| `/perms` | Manage player/group permissions visually |

---

## 📝 3. Active Tasks & Development Plan
### ✅ Completed Recent Fixes
- **Block Breaking:** Fixed `NWGCombat` to allow damaging barrels/signs.
- **God Mode:** Consolidated logic into `NWGAdmin`, removed conflicts.
- **Command Standardization:** Converted all `nwg.x` console commands to `/x` chat format.
- **Git Prep:** Repository initialized with `.gitignore` for logs/data.

### 🚀 Planned Features (NPC Event System)
We are extending the NPC system to include:
1. **Base Attackers:** Hostile NPCs that RAID player bases (Explosive variants).
2. **Roaming Patrols:** Groups of NPCs moving between monuments.
3. **Difficulty Tiers:** Weighted tiers from "Easy" to "Elite" with different loadouts.

---

## 🧟 4. NPC Preset Reference (SpawnNPC)
If adding new events, ensure these presets exist in `SpawnNPC.json`:

| Preset Name | Tier | Recommended Loadout |
|-------------|------|---------------------|
| `nwg_ba_easy` | Easy | Melee / Revolver |
| `nwg_ba_hard` | Hard | AK47 / Heavy Plate |
| `nwg_ba_elite_expl` | Elite | M249 + Satchels (Raid variant) |
| `nwg_roam_norm` | Normal | SMG / Hazmat |

---

## 📋 5. Admin Maintenance Checklist
**Daily Tasks:**
- [ ] Check plugin load status: `/plugins`
- [ ] Monitor active raids: `/dungeon status`
- [ ] Verify economy flow: `/economy balance @player`
- [ ] Check for startup errors in `oxide/logs/`

**Troubleshooting:**
- **Plugin Issues:** `/oxide.reload <PluginName>`
- **Lag:** `/raidbase despawn` (Clears active raid entities)
- **Permissions:** `/oxide.show perms <PlayerName>`

---

*New World Gaming - Stability, Performance, Action.*
