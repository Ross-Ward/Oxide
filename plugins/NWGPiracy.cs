// Forced Recompile: 2026-02-12 01:38
using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NWGPiracy", "NWG Team", "1.1.0")]
    [Description("High-seas piracy features: Tugboat Raiders and Deep Sea Salvage with map markers.")]
    public class NWGPiracy : RustPlugin
    {
        private List<BaseEntity> _activePirateEntities = new List<BaseEntity>();
        private List<MapMarkerGenericRadius> _activeMarkers = new List<MapMarkerGenericRadius>();
        private Timer _piracyTimer;

        #region Lifecycle
        private void OnServerInitialized()
        {
            _piracyTimer = timer.Every(3600, SpawnPiracyEvent); // Every hour
        }

        private void Unload()
        {
            CleanupPirates();
            CleanupMarkers();
        }

        private void CleanupPirates()
        {
            foreach (var ent in _activePirateEntities.ToList())
            {
                if (ent == null || ent.IsDestroyed) continue;
                
                // Unparent to ensure safe destruction of children vs parents
                if (ent.GetParentEntity() != null) ent.SetParent(null);
                
                ent.Kill();
            }
            _activePirateEntities.Clear();
        }

        private void CleanupMarkers()
        {
            foreach (var marker in _activeMarkers)
            {
                if (marker != null && !marker.IsDestroyed) marker.Kill();
            }
            _activeMarkers.Clear();
        }
        #endregion

        #region Map Markers
        private void SpawnMapMarker(Vector3 pos, Color primary, Color secondary, float radius, string label)
        {
            var marker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", pos) as MapMarkerGenericRadius;
            if (marker == null) return;

            marker.alpha = 0.75f;
            marker.color1 = primary;
            marker.color2 = secondary;
            marker.radius = radius;
            marker.Spawn();
            marker.SendUpdate();
            _activeMarkers.Add(marker);
            _activePirateEntities.Add(marker); // Also track for global cleanup
        }
        #endregion

        #region Events
        private void SpawnPiracyEvent()
        {
            int rand = UnityEngine.Random.Range(0, 2);
            if (rand == 0) SpawnTugboatRaider();
            else SpawnDeepSeaSalvage();
        }

        [ChatCommand("spawnpiracy")]
        private void CmdPiracySpawn(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length > 0 && args[0].ToLower() == "salvage")
            {
                SpawnDeepSeaSalvage();
                player.ChatMessage("Deep Sea Salvage spawned!");
            }
            else
            {
                SpawnTugboatRaider();
                player.ChatMessage("Pirate Tugboat spawned!");
            }
        }

        private void SpawnTugboatRaider()
        {
            // Find a spot in the ocean
            Vector3 pos = FindOceanPosition();
            if (pos == Vector3.zero) return;

            var tugboat = GameManager.server.CreateEntity("assets/content/vehicles/boats/tugboat/tugboat.prefab", pos) as Tugboat;
            if (tugboat == null) return;
            tugboat.Spawn();
            _activePirateEntities.Add(tugboat);

            // Add a hackable crate to the deck
            var crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", tugboat.transform.position + (Vector3.up * 3)) as HackableLockedCrate;
            if (crate != null)
            {
                crate.SetParent(tugboat);
                crate.Spawn();
            }

            // Spawn some scientists as pirates
            for (int i = 0; i < 3; i++)
            {
                var pirate = GameManager.server.CreateEntity("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab", tugboat.transform.position + Vector3.up) as HumanNPC;
                if (pirate != null)
                {
                    pirate.SetParent(tugboat);
                    pirate.Spawn();
                }
            }

            // Spawn a BLUE map marker for the tugboat raider
            SpawnMapMarker(pos, new Color(0.2f, 0.4f, 1f, 1f), new Color(0f, 0.1f, 0.4f, 0.5f), 0.12f, "Pirate Tugboat");

            PrintToChat("<color=#4488FF>\u2693 PIRATE TUGBOAT SPOTTED!</color>\nA hostile tugboat with high-value cargo has been spotted at sea!\n<color=#AAAAAA>Check your map for a blue marker.</color>");
            Puts($"[NWG Piracy] A Pirate Tugboat with high-value cargo has been spotted at {pos}!");
        }

        private void SpawnDeepSeaSalvage()
        {
            Vector3 pos = FindOceanPosition();
            if (pos == Vector3.zero) return;

            Vector3 markerPos = pos; // Save surface position for marker
            // Anchor it to the sea floor
            pos.y = TerrainMeta.WaterMap.GetHeight(pos) - 20; // Deepish

            var crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab", pos) as HackableLockedCrate;
            if (crate == null) return;
            crate.Spawn();
            _activePirateEntities.Add(crate);

            // Spawn a CYAN map marker for deep sea salvage
            SpawnMapMarker(markerPos, new Color(0f, 0.9f, 0.9f, 1f), new Color(0f, 0.3f, 0.5f, 0.5f), 0.08f, "Deep Sea Salvage");

            PrintToChat("<color=#00DDDD>\u2693 DEEP SEA SALVAGE DETECTED!</color>\nA sunken crate with valuable cargo has been located!\n<color=#AAAAAA>Check your map for a cyan marker. Bring diving gear!</color>");
            Puts($"[NWG Piracy] Deep sea salvage detected at {markerPos}!");
        }

        private Vector3 FindOceanPosition()
        {
            for (int i = 0; i < 10; i++)
            {
                float x = UnityEngine.Random.Range(-TerrainMeta.Size.x / 2, TerrainMeta.Size.x / 2);
                float z = UnityEngine.Random.Range(-TerrainMeta.Size.z / 2, TerrainMeta.Size.z / 2);
                Vector3 pos = new Vector3(x, 0, z);
                
                if ((TerrainMeta.TopologyMap.GetTopology(pos) & (int)TerrainTopology.Enum.Ocean) != 0)
                {
                    pos.y = TerrainMeta.WaterMap.GetHeight(pos);
                    return pos;
                }
            }
            return Vector3.zero;
        }
        #endregion
    }
}

