# NWG Core – NPC Event System Development Plan

## 1. Overview

Extend **NWG_Core** so that, on the same timer as existing events (airdrop, chopper, bradley), **SpawnNPC-based events** can trigger with configurable chances. These events spawn hostile NPCs in different roles (base attackers, roaming groups, monument attackers) with optional **difficulty tiers** and **explosive/special variants**.

---

## 2. Goals

- Reuse **SpawnNPC** presets so server admins keep one place to define NPC loadouts, health, and behaviour.
- Use the **existing event loop** in NWG_Core (e.g. 15‑minute check, per‑event cooldowns).
- Add **% chance** per NPC event type so they can run alongside airdrop/chopper/bradley.
- Support **tiers** (e.g. Easy / Normal / Hard / Elite) that map to different presets or modifiers.
- Optional: **sub-variants** (e.g. % chance for “explosive” base attackers).
- **Enable/disable automatic spawning** via config and runtime toggle (master switch and per-event-type).

---

## 3. Automatic Spawning: Enable and Toggle

### 3.1 Enabling automatic spawning

- **Master switch:** A single config option controls whether **any** automatic events (including existing airdrop/chopper/bradley and future NPC events) run on the timer.
  - Example: `"AutomaticEventsEnabled": true` in the main event config. When `false`, the periodic check still runs but skips all event rolls (no airdrop, chopper, bradley, or NPC events).
- **Per-category toggles (optional):**
  - `"AutomaticVehicleEventsEnabled": true` — airdrop, chopper, bradley.
  - `"AutomaticNpcEventsEnabled": true` — base attacker, roaming group, monument attacker.
- **Per-event-type toggles:** Each event type (airdrop, chopper, bradley, baseattacker, roaming, monument) can have an `"Enabled": true/false` (or equivalent) in config so admins can turn off only e.g. bradley or only base attackers without touching chances.

Default should be **enabled** (e.g. `true`) so existing behaviour is unchanged; admins can set to `false` to disable automatic events entirely or by category/type.

### 3.2 Toggling automatic spawning at runtime

- **Chat command (admin):** e.g. `/spawnevent auto [on|off|status]` or `/nwgevents auto [on|off|status]`.
  - `on` — set automatic events enabled (master switch on).
  - `off` — set automatic events disabled (master switch off).
  - `status` — report whether automatic events are currently on or off, and optionally per-category/per-type state.
- **Persistence:** The runtime toggle can be stored in a **data file** (e.g. `NWG_Core_EventState.json`) so it survives reloads and restarts. On load, if the data file exists and contains `AutomaticEventsEnabled: false`, use that; otherwise fall back to config.
- **Config vs runtime:** Config defines the **default** (e.g. `AutomaticEventsEnabled: true`). The first time the plugin runs, it uses config. After an admin runs `/spawnevent auto off`, the plugin writes the state to data and uses it until an admin runs `auto on` or the data file is removed/reset. Optional: `/spawnevent auto reset` to clear saved state and revert to config default.
- **Console command (optional):** Same behaviour as chat command for server console: e.g. `nwgevents.automatic 1/0` or via existing admin command pattern.

### 3.3 Implementation notes

- In `CheckAndTriggerEvents()` (or equivalent), at the top: if automatic events are disabled (master switch), return immediately without rolling any event.
- When automatic spawning is off, **manual** spawns via `/spawnevent airdrop`, `/spawnevent chopper`, etc. should still work so admins can trigger one-off events.
- Document in README or in-game: “Automatic events can be turned off with `/spawnevent auto off` and back on with `/spawnevent auto on`.”

---

## 4. SpawnNPC Integration

### 4.1 Current behaviour

- SpawnNPC holds **presets** (name, health, weapons, loot, scientist vs scarecrow, etc.) and **spawn rules** (monument, biome).
- Spawning is done via `config.GetNPCPreset(presetName).Spawn(point)` with no public API for other plugins.

### 4.2 Required integration

**Option A (recommended): Add API to SpawnNPC**

- Add a public/callable API so NWG_Core can spawn by preset name at an arbitrary position, e.g.:
  - `bool SpawnPresetAt(Vector3 position, string presetName, bool shouldRespawn = false)`
  - Or: `BaseCombatEntity SpawnPresetAt(Vector3 position, string presetName, bool shouldRespawn = false)` to return the spawned entity for markers/tracking.
- NWG_Core would call this for every NPC in an event (base attackers, roaming group, monument attackers).
- Presets used (e.g. `nwg_base_attacker_t1`, `nwg_roaming_t2`, `nwg_monument_attacker_elite`) would be defined in SpawnNPC’s config; NWG_Core only references them by name and tier.

**Option B: No SpawnNPC API**

- NWG_Core could spawn scientists/scarecrows via `GameManager.server.CreateEntity` and apply gear/health from its own config, effectively duplicating a subset of SpawnNPC logic. Not ideal for maintainability.

**Recommendation:** Implement Option A in SpawnNPC first (single method that looks up preset and calls existing `preset.Spawn(point)`), then have NWG_Core depend on it.

---

## 5. Event Types (SpawnNPC-Based)

### 5.1 Base attackers

- **Concept:** A group of NPCs spawns near a **player base** (e.g. building privilege or TC) and acts as “raiders.”
- **Spawn logic:**
  - Pick one or more bases (e.g. by building privilege holder count, or random among recently active bases).
  - Spawn point: offset from base position (e.g. 20–50 m), on NavMesh.
- **Variants (configurable %):**
  - Standard: preset(s) for tier (e.g. melee + rifle).
  - **Explosive:** % chance that some NPCs use presets that include satchel/rocket/C4 (or grenades) so they can damage structures.
- **Tiers:** Easy (weaker preset), Normal, Hard, Elite (stronger presets, more NPCs or more explosives chance).

### 5.2 Roaming NPC groups

- **Concept:** A group of NPCs spawns in the **open world** (random or biome-based) and roams; no specific target.
- **Spawn logic:**
  - Random position on map (or filtered by biome if desired), valid NavMesh, away from spawn.
  - Spawn N NPCs in a small cluster (e.g. 3–8) using tier presets.
- **Variants:** Optionally a small % for “elite patrol” (better weapons/health).
- **Tiers:** Easy (fewer, weaker), Normal, Hard (more, stronger), Elite (small group, very strong).

### 5.3 Monument attackers

- **Concept:** NPCs spawn **at or near a monument** and “attack” it (occupy it, attack players who come).
- **Spawn logic:**
  - Use `TerrainMeta.Path.Monuments` (or SpawnNPC’s monument list if exposed) to pick a random monument.
  - Spawn at monument position + radius (similar to SpawnNPC monument rules).
- **Variants:** Same tier system; optional “heavy” variant for certain monuments (e.g. Launch Site = harder preset).
- **Tiers:** Easy (e.g. tier 1 monuments, weaker presets), Normal, Hard, Elite (major monuments, strong presets).

---

## 6. Difficulty Tiers

- **Tier 1 – Easy:** Fewer NPCs, presets with lower health/weaker weapons (e.g. pistol, melee).
- **Tier 2 – Normal:** Default count and “normal” presets (e.g. rifle, SMG).
- **Tier 3 – Hard:** More NPCs and/or presets with better weapons and health.
- **Tier 4 – Elite:** Fewer but very strong NPCs (e.g. L96, M249), optional explosives.

Tier can be chosen:
- **Random** per event (weighted by config), or
- **Time-based** (e.g. more elite at peak hours), or
- **Fixed** in config.

Config should allow:
- Per-event-type tier weights (e.g. base attackers: 40% Easy, 35% Normal, 20% Hard, 5% Elite).
- Per-tier: preset names (one or list to pick randomly), min/max NPC count, and for base attackers the “explosive” % and preset.

---

## 7. Configuration (NWG_Core)

### 7.1 Automatic spawning (enable / default state)

- Add to main event config (or top-level NWG_Core config):
  - `"AutomaticEventsEnabled": true` — master switch for all automatic events (vehicle + NPC).
  - Optional: `"AutomaticVehicleEventsEnabled": true`, `"AutomaticNpcEventsEnabled": true` for category-level control.
- Per-event-type `"Enabled"` flags can live inside each event block (e.g. AirdropChance and `"AirdropEnabled": true`).

### 7.2 Event chances (same timer as existing events)

- Reuse existing `CheckInterval` (e.g. 900 s) and `EventCooldown` (e.g. 7200 s) per event type.
- Add to `EventConfig` (or equivalent):
  - `BaseAttackerChance` (0–1)
  - `RoamingGroupChance` (0–1)
  - `MonumentAttackerChance` (0–1)
- In `CheckAndTriggerEvents()`:
  - After/before existing airdrop/chopper/bradley checks, roll for each NPC event type (with its own cooldown).
  - If roll succeeds and cooldown expired, trigger that event (and set cooldown).

### 7.3 Per-event cooldowns

- Add cooldowns: `LastBaseAttacker`, `LastRoamingGroup`, `LastMonumentAttacker` (same pattern as airdrop/chopper/bradley).
- Optional: shared “NPC event” cooldown so only one NPC event can fire per check (simpler balance).

### 7.4 NPC event config block (example structure)

Top-level automatic spawning (add to main event config):

- `"AutomaticEventsEnabled": true` — master switch; when `false`, no automatic events run (manual `/spawnevent airdrop` etc. still work).
- `"AutomaticVehicleEventsEnabled": true` — optional; enable/disable airdrop, chopper, bradley.
- `"AutomaticNpcEventsEnabled": true` — optional; enable/disable NPC events (base attacker, roaming, monument).

Example combined structure:

```json
{
  "CheckInterval": 900,
  "EventCooldown": 7200,
  "AutomaticEventsEnabled": true,
  "AutomaticVehicleEventsEnabled": true,
  "AutomaticNpcEventsEnabled": true,
  "AirdropChance": 0.25,
  "ChopperChance": 0.20,
  "BradleyChance": 0.15,
  "NpcEvents": {
  "BaseAttacker": {
    "Chance": 0.15,
    "Cooldown": 7200,
    "ExplosiveVariantChance": 0.25,
    "TierWeights": { "Easy": 0.4, "Normal": 0.35, "Hard": 0.2, "Elite": 0.05 },
    "Tiers": {
      "Easy":   { "Presets": ["nwg_ba_easy"],   "MinCount": 2, "MaxCount": 4 },
      "Normal": { "Presets": ["nwg_ba_norm"],   "MinCount": 3, "MaxCount": 6 },
      "Hard":   { "Presets": ["nwg_ba_hard"],   "MinCount": 4, "MaxCount": 8 },
      "Elite":  { "Presets": ["nwg_ba_elite"],  "MinCount": 3, "MaxCount": 6, "ExplosivePresets": ["nwg_ba_elite_expl"] }
    }
  },
  "RoamingGroup": {
    "Chance": 0.20,
    "Cooldown": 5400,
    "TierWeights": { "Easy": 0.35, "Normal": 0.40, "Hard": 0.20, "Elite": 0.05 },
    "Tiers": {
      "Easy":   { "Presets": ["nwg_roam_easy"],   "MinCount": 3, "MaxCount": 5 },
      "Normal": { "Presets": ["nwg_roam_norm"],   "MinCount": 4, "MaxCount": 7 },
      "Hard":   { "Presets": ["nwg_roam_hard"],   "MinCount": 5, "MaxCount": 9 },
      "Elite":  { "Presets": ["nwg_roam_elite"],  "MinCount": 4, "MaxCount": 6 }
    }
  },
  "MonumentAttacker": {
    "Chance": 0.18,
    "Cooldown": 7200,
    "TierWeights": { "Easy": 0.30, "Normal": 0.45, "Hard": 0.20, "Elite": 0.05 },
    "MonumentTierOverrides": {
      "launch_site_1": "Elite",
      "military_tunnel_1": "Hard"
    },
    "Tiers": {
      "Easy":   { "Presets": ["nwg_mon_easy"],   "MinCount": 2, "MaxCount": 4 },
      "Normal": { "Presets": ["nwg_mon_norm"],   "MinCount": 3, "MaxCount": 6 },
      "Hard":   { "Presets": ["nwg_mon_hard"],   "MinCount": 4, "MaxCount": 8 },
      "Elite":  { "Presets": ["nwg_mon_elite"],  "MinCount": 4, "MaxCount": 8 }
    }
  }
  }
}
```

- Preset names (e.g. `nwg_ba_easy`) would be created in **SpawnNPC** config by the server admin; NWG_Core only references them.

---

## 8. Implementation Phases

### Phase 0 – Automatic spawning enable/toggle (applies to existing events)

1. **Config:** Add `AutomaticEventsEnabled` (default `true`) to event config. In `CheckAndTriggerEvents()`, if `false`, return without rolling any event. Manual `/spawnevent airdrop` etc. still work.
2. **Optional:** Add `AutomaticVehicleEventsEnabled` and/or per-event `Enabled` so only vehicle events or only specific types can be turned off in config.
3. **Runtime toggle:** Add `/spawnevent auto [on|off|status]` (admin-only). When set to `off`/`on`, save state to a data file (e.g. `data/NWG_Core_EventState.json`: `{ "AutomaticEventsEnabled": false }`). On load, read this file; if present, it overrides config for the master switch. Add `auto reset` to clear saved state and use config default again.
4. **Status:** In `spawnevent status`, show “Automatic events: On/Off” (and optionally per-category if implemented).

### Phase 1 – SpawnNPC API and config (prerequisite)

1. **SpawnNPC:** Add API, e.g. `SpawnPresetAt(Vector3 position, string presetName, bool shouldRespawn = false)` (and optionally return entity/list for markers).
2. **NWG_Core:** Add `[PluginReference] Plugin SpawnNPC`, default config section for NPC events (chances, cooldowns, tier weights, preset names per tier). No spawning yet; just config load and validation (warn if SpawnNPC missing or preset names missing).

### Phase 2 – Roaming NPC groups (simplest)

1. Implement **roaming group** event only: on trigger, pick tier from weights, get min/max count and presets, choose random map position (with simple NavMesh/terrain check), call SpawnNPC API N times in a small radius.
2. Add cooldown and chance roll in `CheckAndTriggerEvents()`.
3. Optional: MarkerManager marker for “Roaming patrol” at centre of group (or skip markers for groups).
4. Broadcast: e.g. “Roaming hostile patrol spotted!” with rough location or no location.

### Phase 3 – Monument attackers

1. Use monument list (from SpawnNPC or `TerrainMeta.Path.Monuments`) to pick a random monument; optionally filter by `MonumentTierOverrides` to force tier (e.g. Launch Site = Elite).
2. Spawn N NPCs at monument position + random radius (reuse SpawnNPC-style radius logic if available, else fixed 30–80 m).
3. Cooldown, chance, tier weights, presets from config.
4. Optional: event marker at monument; broadcast “Monument under attack: [MonumentName].”

### Phase 4 – Base attackers

1. **Base selection:** Decide how to pick a “base” (e.g. list of buildings with building privilege, or TC entities). Option: random player with building blocks in a radius, then get a building position; or use a hook/API from a base-related plugin if present.
2. Spawn point: offset from base (e.g. 25–45 m), on NavMesh; spawn group of NPCs via SpawnNPC API.
3. **Explosive variant:** When rolling event, roll again for “explosive”; for that spawn use `ExplosivePresets` or a separate preset list (e.g. NPCs with satchel/beancan in belt) from SpawnNPC config.
4. Cooldown, chance, tier weights, presets.
5. Optional: marker at raid zone; broadcast “Hostile raiders approaching a base!” (no need to expose which base).

### Phase 5 – Polish and admin tools

1. **/spawnevent** extensions: e.g. `spawnevent baseattacker`, `spawnevent roaming`, `spawnevent monument`, `spawnevent npcstatus` (show cooldowns for NPC events).
2. **/spawnevent auto:** Ensure `spawnevent auto on|off|status|reset` is documented and visible in help.
3. **/spawnevent status:** Include automatic-events state (On/Off), NPC event cooldowns, and maybe last triggered time.
4. Optional: per-tier or per-event lang keys for broadcast messages.
5. Document required SpawnNPC presets (names and suggested loadouts) in README or separate doc.

---

## 9. Markers and UX

- **Airdrop/chopper/bradley:** Keep current behaviour (markers on crate, chopper, bradley).
- **Roaming group:** Optional single marker at spawn centre (short duration, e.g. 5 min) or no marker.
- **Monument attacker:** Optional marker at monument (duration until event “ends” or a timeout).
- **Base attacker:** Optional marker at spawn cluster (short duration); avoid revealing exact base.

Use same MarkerManager integration and naming (e.g. `nwg_roaming_event`, `nwg_monument_event`, `nwg_baseattack_event`) and remove on timeout or when all event NPCs are dead (optional).

---

## 10. Dependencies and Compatibility

- **SpawnNPC:** Required for NPC events; NWG_Core should check `SpawnNPC != null && SpawnNPC.IsLoaded` before rolling NPC events and log a warning if missing.
- **MarkerManager:** Already used; optional markers for new events only if enabled in config.
- **Kits (SpawnNPC):** If presets use kits, no change; SpawnNPC handles that.
- **TruePVE / RaidableBases / etc.:** Consider whether base-attacker logic should respect PVE or no-raid zones (config flag to enable/disable base attacker in those zones).

---

## 11. Summary

| Item | Action |
|------|--------|
| **Events** | Base attackers (% explosive), roaming groups, monument attackers |
| **Timer** | Same as existing (e.g. 15 min check); each type has its own % chance and cooldown |
| **Automatic spawning** | Config: `AutomaticEventsEnabled` (and optional per-category/per-type). Runtime: `/spawnevent auto on\|off\|status\|reset`; state persisted in data file so it survives reloads |
| **Tiers** | Easy / Normal / Hard / Elite with weights and preset names per tier |
| **SpawnNPC** | Add `SpawnPresetAt(position, presetName, shouldRespawn)` (or equivalent) and reference presets by name in NWG config |
| **Config** | Master/category enable flags; NpcEvents block with chance, cooldown, tier weights, presets per tier; optional explosive % and preset lists for base attackers |
| **Phases** | 0 = Auto enable/toggle, 1 = API + config, 2 = Roaming, 3 = Monument, 4 = Base attackers, 5 = Commands and polish |

This plan keeps all NPC definition in SpawnNPC, uses the existing NWG event loop and cooldown model, adds enable/toggle for automatic spawning (config + runtime), and adds clear tiers and optional explosive variants for base attackers.
