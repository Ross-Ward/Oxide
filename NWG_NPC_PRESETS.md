# NWG Core – Required SpawnNPC Presets

NWG Core’s NPC events (roaming, monument attacker, base attacker) use **SpawnNPC** presets by name. Add these presets in **SpawnNPC** config (`config/SpawnNPC.json`) or change the preset names in **NWG_Core** config (`config/NWG_Core.json` → `NpcEvents`) to match presets you already have.

---

## Default preset names (from NWG_Core config)

### Roaming group
| Preset name      | Tier  | Suggested use                          |
|------------------|-------|----------------------------------------|
| `nwg_roam_easy`  | Easy  | Fewer, weaker (e.g. pistol, melee)      |
| `nwg_roam_norm`  | Normal| Rifle, SMG                              |
| `nwg_roam_hard`  | Hard  | Better weapons, more health             |
| `nwg_roam_elite` | Elite | Strong (e.g. L96, M249), fewer count   |

### Monument attacker
| Preset name     | Tier  | Suggested use                     |
|-----------------|-------|-----------------------------------|
| `nwg_mon_easy`  | Easy  | Weaker, small monuments          |
| `nwg_mon_norm`  | Normal| Standard monument defenders       |
| `nwg_mon_hard`  | Hard  | Military tunnel, etc.             |
| `nwg_mon_elite` | Elite | Launch site, major monuments     |

### Base attacker
| Preset name        | Tier  | Suggested use                          |
|--------------------|-------|----------------------------------------|
| `nwg_ba_easy`      | Easy  | Melee + weak guns                      |
| `nwg_ba_norm`      | Normal| Rifle, SMG                             |
| `nwg_ba_hard`      | Hard  | Better weapons, more health           |
| `nwg_ba_elite`     | Elite | Strong weapons                        |
| `nwg_ba_elite_expl`| Elite | Explosive variant (satchel, beancan, rocket) |

---

## Quick setup

1. Open `config/SpawnNPC.json`.
2. In the **"Preset for the NPC"** array, add one entry per preset name above (copy an existing preset and change **"Preset name"** and loadout).
3. For base attacker explosive variant, give `nwg_ba_elite_expl` belt items like satchel charges, beancan grenades, or rockets so they can damage structures.

You can also point NWG_Core at existing SpawnNPC presets: edit `config/NWG_Core.json` → `NpcEvents` → each event’s `Tiers` and set `Presets` (and `ExplosivePresets` for base attacker) to your preset names.

---

## Reference

- **NWG_Core** calls SpawnNPC via: `SpawnPresetAt(position, presetName, shouldRespawn: false)`.
- Presets are defined only in SpawnNPC; NWG_Core only references them by name.
- If a preset name is missing in SpawnNPC, that spawn is skipped and a warning is logged.
