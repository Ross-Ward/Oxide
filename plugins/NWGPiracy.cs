// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NWG Piracy", "NWG Team", "1.0.0")]
    [Description("High-seas piracy features: Tugboat Raiders and Deep Sea Salvage.")]
    public class NWGPiracy : RustPlugin
    {
        private List<BaseEntity> _activePirateEntities = new List<BaseEntity>();
        private Timer _piracyTimer;

        #region Lifecycle
        private void OnServerInitialized()
        {
            _piracyTimer = timer.Every(3600, SpawnPiracyEvent); // Every hour
        }

        private void Unload()
        {
            CleanupPirates();
        }

        private void CleanupPirates()
        {
            foreach (var ent in _activePirateEntities)
            {
                if (ent != null && !ent.IsDestroyed) ent.Kill();
            }
            _activePirateEntities.Clear();
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
            SpawnTugboatRaider();
            player.ChatMessage("Pirate Tugboat spawned!");
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
            var crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinookcrate/codelockedhackablecrate.prefab", tugboat.transform.position + (Vector3.up * 3)) as HackableLockedCrate;
            if (crate != null)
            {
                crate.SetParent(tugboat);
                crate.Spawn();
            }

            // Spawn some scientists as pirates
            for (int i = 0; i < 3; i++)
            {
                var pirate = GameManager.server.CreateEntity("assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistfull_heavy.prefab", tugboat.transform.position + Vector3.up) as HumanNPC;
                if (pirate != null)
                {
                    pirate.SetParent(tugboat);
                    pirate.Spawn();
                }
            }

            Puts($"[NWG Piracy] A Pirate Tugboat with high-value cargo has been spotted at {pos}!");
        }

        private void SpawnDeepSeaSalvage()
        {
            Vector3 pos = FindOceanPosition();
            if (pos == Vector3.zero) return;

            // Anchor it to the sea floor
            pos.y = TerrainMeta.WaterMap.GetHeight(pos) - 20; // Deepish

            var crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinookcrate/codelockedhackablecrate.prefab", pos) as HackableLockedCrate;
            if (crate == null) return;
            crate.Spawn();
            _activePirateEntities.Add(crate);

            Puts($"[NWG Piracy] Deep sea salvage detected! Check your maps for signals.");
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
