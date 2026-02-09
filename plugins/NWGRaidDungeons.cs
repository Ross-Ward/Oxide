using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using Newtonsoft.Json;
using Rust;

namespace Oxide.Plugins
{
    [Info("NWG Raid Dungeons", "NWG Team", "3.0.0")]
    [Description("Dungeon events and Boss fights (Private/Group/Global).")]
    public class NWGRaidDungeons : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public float EventIntervalHours = 3.0f;
            public Vector3 DungeonPosition = new Vector3(2000, 200, 2000);
            public string ScientistPrefab = "assets/prefabs/npc/scientist/scientist.prefab";
            public string TurretPrefab = "assets/prefabs/deployable/tier 2 turtle turret/turret.deployed.prefab";
            public string CratePrefab = "assets/prefabs/deployable/chinookinventory/chinooklockedcrate.prefab";
        }

        private PluginConfig _config;

        private void LoadConfigVariables()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null) LoadDefaultConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }
        #endregion

        #region Internal State
        private enum DungeonType { Private, Group, Global }
        private class ActiveDungeon
        {
            public DungeonType Type;
            public ulong OwnerId;
            public ulong ClanId;
            public Vector3 Position;
            public List<BaseEntity> Entities = new List<BaseEntity>();
            public List<ScientistNPC> Npcs = new List<ScientistNPC>();
            public int Wave = 1;
        }

        private List<ActiveDungeon> _activeDungeons = new List<ActiveDungeon>();
        private BaseEntity _activeEventDome;
        private Timer _eventTimer;
        private Timer _waveCheckTimer;
        #endregion

        #region Lifecycle
        private const string PermAdmin = "nwgraiddungeons.admin";
        private const string PermPrivate = "nwgraiddungeons.private";
        private const string PermGroup = "nwgraiddungeons.group";

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermPrivate, this);
            permission.RegisterPermission(PermGroup, this);

            LoadConfigVariables();
            _eventTimer = timer.Every(_config.EventIntervalHours * 3600, () => StartDungeon(DungeonType.Global));
            _waveCheckTimer = timer.Every(5f, CheckDungeonsProgress);
        }

        [ChatCommand("dungeon")]
        private void CmdDungeon(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            
            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /dungeon start <global|private|group> or /dungeon stopall");
                return;
            }

            string action = args[0].ToLower();

            if (action == "start")
            {
                if (args.Length < 2) { player.ChatMessage("Usage: /dungeon start <type>"); return; }
                string typeStr = args[1].ToLower();

                if (typeStr == "global")
                {
                    if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) { player.ChatMessage("No permission."); return; }
                    StartDungeon(DungeonType.Global);
                    player.ChatMessage("Started GLOBAL dungeon.");
                }
                else if (typeStr == "private")
                {
                    if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermPrivate)) { player.ChatMessage("No permission for Private Dungeon."); return; }
                    StartDungeon(DungeonType.Private, player);
                }
                else if (typeStr == "group")
                {
                    if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermGroup)) { player.ChatMessage("No permission for Group Dungeon."); return; }
                    StartDungeon(DungeonType.Group, player);
                }
            }
            else if (action == "stopall")
            {
                if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin)) { player.ChatMessage("No permission."); return; }
                StopAllDungeons();
                player.ChatMessage("Stopped all dungeons.");
            }
        }

        [ConsoleCommand("nwg.dungeon")]
        private void ConsoleDungeon(ConsoleSystem.Arg arg)
        {
            // Usage: nwg.dungeon start private <steamid>
            if (!arg.IsAdmin && arg.Connection != null) return;

            string action = arg.GetString(0).ToLower();
            if (action == "start" && arg.Args.Length >= 3)
            {
                string typeStr = arg.GetString(1).ToLower();
                ulong targetId = arg.GetULong(2);
                BasePlayer target = BasePlayer.Find(targetId.ToString());

                if (target == null) { Puts($"Player {targetId} not found for dungeon start."); return; }

                if (typeStr == "private")
                {
                    StartDungeon(DungeonType.Private, target);
                    target.ChatMessage("Your purchased Private Dungeon is ready!");
                }
                else if (typeStr == "group")
                {
                    StartDungeon(DungeonType.Group, target);
                    target.ChatMessage("Your purchased Group Dungeon is ready!");
                }
            }
        }
        #endregion

        #region Event Logic
        private void StartDungeon(DungeonType type, BasePlayer initiator = null)
        {
            Vector3 dungeonPos = _config.DungeonPosition + new Vector3(_activeDungeons.Count * 200, 0, 0); // Offset for "instancing"
            
            dungeonPos.y = 500f; // Force high altitude

            var dungeon = new ActiveDungeon {
                Type = type,
                Position = dungeonPos,
                OwnerId = initiator?.userID ?? 0
            };

            // Spawn entrance for Global
            if (type == DungeonType.Global)
            {
                var entrance = GetRandomLocation();
                _activeEventDome = GameManager.server.CreateEntity("assets/prefabs/monuments/sphere_tank/sphere_tank.prefab", entrance);
                _activeEventDome.Spawn();
                
                var portal = GameManager.server.CreateEntity("assets/prefabs/misc/portal/portal.prefab", entrance + new Vector3(0, 1, 10));
                portal.Spawn();
                dungeon.Entities.Add(portal);
                
                PrintToChat("<color=#FF6B6B>GLOBAL RAID EVENT STARTED!</color> A dungeon has appeared at the Sphere Tank!");
            }

            BuildDungeon(dungeon);
            _activeDungeons.Add(dungeon);

            if (initiator != null && type != DungeonType.Global)
            {
                // Teleport initiator to private/group dungeon AFTER build
                timer.Once(1f, () => {
                    if (initiator != null && initiator.IsConnected)
                    {
                        initiator.Teleport(dungeonPos + new Vector3(0, 2, 0));
                        initiator.ChatMessage($"<color=#5BC0DE>{type.ToString().ToUpper()} DUNGEON STARTED!</color> Welcome to your private challenge.");
                    }
                });
            }
        }

        private void BuildDungeon(ActiveDungeon dungeon)
        {
             // Spawn Arena
             float size = 20f;
             for (float x = -size; x <= size; x += 4f)
             {
                 for (float z = -size; z <= size; z += 4f)
                 {
                     var floor = GameManager.server.CreateEntity("assets/prefabs/building/floor/floor.prefab", dungeon.Position + new Vector3(x, 0, z));
                     if (floor != null)
                     {
                         var block = floor as BuildingBlock;
                         if (block != null)
                         {
                             block.grounded = true;
                             block.grade = BuildingGrade.Enum.Metal;
                         }
                         floor.Spawn();
                         dungeon.Entities.Add(floor);
                     }
                 }
             }

             SpawnWave(dungeon, 1);

             var turret = GameManager.server.CreateEntity(_config.TurretPrefab, dungeon.Position + new Vector3(10, 0, 10)) as AutoTurret;
             turret.Spawn();
             dungeon.Entities.Add(turret);

             var crate = GameManager.server.CreateEntity(_config.CratePrefab, dungeon.Position) as HackableLockedCrate;
             crate.Spawn();
             dungeon.Entities.Add(crate);
        }

        private void SpawnWave(ActiveDungeon dungeon, int wave)
        {
            int count = 4 + (wave * 2);
            for(int i=0; i<count; i++)
            {
                var scientist = GameManager.server.CreateEntity(_config.ScientistPrefab, dungeon.Position + new Vector3(UnityEngine.Random.Range(-10, 10), 0.5f, UnityEngine.Random.Range(-10, 10))) as ScientistNPC;
                scientist.Spawn();
                dungeon.Npcs.Add(scientist);
                dungeon.Entities.Add(scientist);
            }
        }

        private void CheckDungeonsProgress()
        {
            foreach (var dungeon in _activeDungeons.ToList())
            {
                dungeon.Npcs.RemoveAll(n => n == null || n.IsDestroyed || n.IsDead());
                if (dungeon.Npcs.Count == 0)
                {
                    dungeon.Wave++;
                    if (dungeon.Wave <= 3) SpawnWave(dungeon, dungeon.Wave);
                    else SpawnBoss(dungeon);
                }
            }
        }

        private void SpawnBoss(ActiveDungeon dungeon)
        {
            if (dungeon.Npcs.Any(n => (n as BasePlayer)?.displayName == "DUNGEON OVERSEER")) return;
            
            var boss = GameManager.server.CreateEntity("assets/prefabs/npc/m249_scientist/scientist.m249.prefab", dungeon.Position) as ScientistNPC;
            boss.displayName = "DUNGEON OVERSEER";
            boss.Spawn();
            dungeon.Npcs.Add(boss);
            dungeon.Entities.Add(boss);
        }

        private void StopAllDungeons()
        {
            foreach (var d in _activeDungeons) StopDungeon(d);
            _activeDungeons.Clear();
            if (_activeEventDome != null) _activeEventDome.Kill();
        }

        private void StopDungeon(ActiveDungeon dungeon)
        {
            foreach (var ent in dungeon.Entities)
            {
                if (ent != null && !ent.IsDestroyed) ent.Kill();
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (_activeDungeons.Count == 0 || entity == null) return;
            
            if (entity is BasePlayer player && player.displayName == "DUNGEON OVERSEER")
            {
                PrintToChat("<color=#FF6B6B>RAID EVENT:</color> The Overseer has been defeated! The loot room is now vulnerable.");
                // Optionally force unlock the crate or just let naturally unlock
            }
        }

        private Vector3 GetRandomLocation()
        {
            float size = TerrainMeta.Size.x / 2 - 200;
            Vector3 pos = new Vector3(UnityEngine.Random.Range(-size, size), 0, UnityEngine.Random.Range(-size, size));
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            return pos;
        }

        // Portal entry logic skipped or handled via separate timer/command
        #endregion
    }
}
