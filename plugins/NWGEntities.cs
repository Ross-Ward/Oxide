using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using Rust;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWG Entities", "NWG Team", "1.0.0")]
    [Description("Manages Entity Spawning and Player Death Restoration.")]
    public class NWGEntities : RustPlugin
    {
        #region References
        [PluginReference] private Plugin NWGCore;
        #endregion

        #region Config
        private class PluginConfig
        {
            public bool EnableRestoration = true;
            public bool EnableSpawning = true;
            public Dictionary<string, int> MonumentSpawnCounts = new Dictionary<string, int>
            {
                ["Airfield"] = 5,
                ["Dome"] = 3,
                ["Trainyard"] = 5
            };
            public string NpcPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab";
            public float RespawnTimer = 600f; // 10 minutes
        }

        private PluginConfig _config;
        #endregion

        #region Data (Restoration)
        private class RestoreData
        {
             public Dictionary<ulong, PlayerInventoryData> PlayerData = new Dictionary<ulong, PlayerInventoryData>();
        }

        private class PlayerInventoryData
        {
            public List<ItemData> Main = new List<ItemData>();
            public List<ItemData> Wear = new List<ItemData>();
            public List<ItemData> Belt = new List<ItemData>();
        }

        private class ItemData
        {
            public int Id;
            public int Amount;
            public ulong Skin;
            public int Position;
            public float Condition;
            public int Ammo;
            public string AmmoType;
            public List<ItemData> Contents;
        }

        private RestoreData _restoreData;
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            
            _restoreData = Interface.Oxide.DataFileSystem.ReadObject<RestoreData>(Name);
            if (_restoreData == null) _restoreData = new RestoreData();

            if (_config.EnableSpawning)
            {
                timer.Every(_config.RespawnTimer, RespawnLoop);
            }
        }

        private void LoadConfigVariables()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new configuration file for NWG Entities");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            if (_config.EnableSpawning)
            {
                SpawnMonumentNPCs();
            }
        }

        private void Unload()
        {
            // Optional: Kill spawned NPCs? Or leave them.
            // For now, let's leave them to persist or decay naturally.
            // If we tracked them, we could kill them.
        }
        #endregion

        #region Restoration Logic
        private void OnEntityDeath(BasePlayer player, HitInfo info)
        {
            if (!_config.EnableRestoration) return;
            if (player == null || player.IsNpc) return;
            
            // Permission check can be added here
            if (!permission.UserHasPermission(player.UserIDString, "nwg_entities.restore")) return;

            SaveInventory(player);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (!_config.EnableRestoration) return;
            if (_restoreData.PlayerData.ContainsKey(player.userID))
            {
                RestoreInventory(player);
            }
        }

        private void SaveInventory(BasePlayer player)
        {
            var data = new PlayerInventoryData();
            data.Main = SaveContainer(player.inventory.containerMain);
            data.Wear = SaveContainer(player.inventory.containerWear);
            data.Belt = SaveContainer(player.inventory.containerBelt);

            _restoreData.PlayerData[player.userID] = data;
            Interface.Oxide.DataFileSystem.WriteObject(Name, _restoreData);
        }

        private List<ItemData> SaveContainer(ItemContainer container)
        {
            var list = new List<ItemData>();
            foreach (var item in container.itemList)
            {
                var d = new ItemData
                {
                    Id = item.info.itemid,
                    Amount = item.amount,
                    Skin = item.skin,
                    Position = item.position,
                    Condition = item.condition
                };
                
                var weapon = item.GetHeldEntity() as BaseProjectile;
                if (weapon != null)
                {
                    d.Ammo = weapon.primaryMagazine.contents;
                    d.AmmoType = weapon.primaryMagazine.ammoType.shortname;
                }

                if (item.contents != null)
                {
                    d.Contents = SaveContainer(item.contents);
                }

                list.Add(d);
            }
            return list;
        }

        private void RestoreInventory(BasePlayer player)
        {
            if (!_restoreData.PlayerData.TryGetValue(player.userID, out var data)) return;

            player.inventory.Strip();
            
            RestoreContainer(player.inventory.containerMain, data.Main);
            RestoreContainer(player.inventory.containerWear, data.Wear);
            RestoreContainer(player.inventory.containerBelt, data.Belt);

            // Remove data after restore
            _restoreData.PlayerData.Remove(player.userID);
            Interface.Oxide.DataFileSystem.WriteObject(Name, _restoreData);
            
            player.ChatMessage("Your inventory has been restored.");
        }

        private void RestoreContainer(ItemContainer container, List<ItemData> items)
        {
            foreach (var d in items)
            {
                var item = ItemManager.CreateByItemID(d.Id, d.Amount, d.Skin);
                if (item != null)
                {
                    item.condition = d.Condition;
                    var weapon = item.GetHeldEntity() as BaseProjectile;
                    if (weapon != null && !string.IsNullOrEmpty(d.AmmoType))
                    {
                         weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(d.AmmoType);
                         weapon.primaryMagazine.contents = d.Ammo;
                    }

                    if (d.Contents != null && item.contents != null)
                    {
                         RestoreContainer(item.contents, d.Contents);
                    }

                    item.MoveToContainer(container, d.Position);
                }
            }
        }
        #endregion

        #region Spawning Logic
        // Simple tracked list of NPCs to prevent overpopulation
        private List<BaseCombatEntity> _spawnedNpcs = new List<BaseCombatEntity>();

        private void SpawnMonumentNPCs()
        {
            // Cleanup dead
            _spawnedNpcs.RemoveAll(x => x == null || x.IsDestroyed);

            var monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>();
            foreach (var monument in monuments)
            {
                // Simple name match
                string name = monument.name; 
                // Monument names are often full paths, e.g., "assets/bundled/prefabs/autospawn/monument/large/airfield.prefab"
                // We check if any key in config is contained in name
                
                var configKey = _config.MonumentSpawnCounts.Keys.FirstOrDefault(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
                
                if (configKey != null)
                {
                    int targetCount = _config.MonumentSpawnCounts[configKey];
                    // Count how many we already have here? 
                    // For simplicity, we just spawn if global count is low, or we could track per monument.
                    // Let's just spawn a few based on a simple radius check around the monument.
                    
                    int currentHere = CountNpcsInRadius(monument.transform.position, 100f);
                    int toSpawn = targetCount - currentHere;

                    for(int i=0; i<toSpawn; i++)
                    {
                        SpawnNpcAt(monument.transform.position, 50f);
                    }
                }
            }
        }

        private int CountNpcsInRadius(Vector3 pos, float radius)
        {
            int count = 0;
            foreach(var col in Physics.OverlapSphere(pos, radius))
            {
                if (col.GetComponentInParent<BaseNpc>() != null || col.GetComponentInParent<ScientistNPC>() != null)
                {
                    count++;
                }
            }
            return count;
        }

        private void SpawnNpcAt(Vector3 origin, float radius)
        {
             Vector3 pos = origin + (UnityEngine.Random.insideUnitSphere * radius);
             pos.y = TerrainMeta.HeightMap.GetHeight(pos); // Snap to ground
             
             // Check Spawn
             var entity = GameManager.server.CreateEntity(_config.NpcPrefab, pos, Quaternion.identity);
             if (entity != null)
             {
                 entity.Spawn();
                 _spawnedNpcs.Add(entity as BaseCombatEntity);
                 
                 // Add a simple component to make them roam origin?
                 // Standard ScientistNPC roams automatically if enabled.
             }
        }

        private void RespawnLoop()
        {
            SpawnMonumentNPCs();
        }
        #endregion
    }
}
