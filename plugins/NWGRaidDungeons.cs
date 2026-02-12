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
    [Info("NWGRaidDungeons", "NWG Team", "3.1.0")]
    [Description("Dungeon events and Boss fights (Private/Group/Global).")]
    public class NWGRaidDungeons : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public float EventIntervalHours = 3.0f;
            public Vector3 DungeonPosition = new Vector3(2000, 200, 2000);
            public string ScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";
            public string TurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
            public string CratePrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            public string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
            public string BossPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
            public float DomeRadius = 50f; // Visual dome size at entrance
            public float EntranceTeleportRadius = 5f; // How close players must be to enter
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

        protected override void SaveConfig() => Config.WriteObject(_config);
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
        private List<SphereEntity> _activeEventDomes = new List<SphereEntity>();
        private MapMarkerGenericRadius _activeMapMarker;
        private Vector3 _globalEntrancePos = Vector3.zero;
        private ActiveDungeon _globalDungeon;
        private Timer _eventTimer;
        private Timer _waveCheckTimer;
        private Timer _entranceCheckTimer;

        private string GetGrid(Vector3 pos)
        {
            float size = TerrainMeta.Size.x;
            float offset = size / 2;
            int x = Mathf.FloorToInt((pos.x + offset) / 146.3f);
            int z = Mathf.FloorToInt((size - (pos.z + offset)) / 146.3f);
            string letters = "";
            while (x >= 0) { letters = (char)('A' + (x % 26)) + letters; x = (x / 26) - 1; }
            return $"{letters}{z}";
        }
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
            _entranceCheckTimer = timer.Every(2f, CheckEntranceProximity);
        }

        private void Unload()
        {
            _eventTimer?.Destroy();
            _waveCheckTimer?.Destroy();
            _entranceCheckTimer?.Destroy();

            foreach (var dungeon in _activeDungeons)
                StopDungeon(dungeon);
            _activeDungeons.Clear();

            CleanupGlobalEntrance();
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
                _globalEntrancePos = entrance;
                _globalDungeon = dungeon;

                // Spawn 3 overlapping dome spheres for better visibility / opacity
                for (int i = 0; i < 3; i++)
                {
                    var dome = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", entrance) as SphereEntity;
                    if (dome != null)
                    {
                        dome.currentRadius = _config.DomeRadius - (i * 2); // Slightly different sizes
                        dome.lerpRadius = _config.DomeRadius - (i * 2);
                        dome.Spawn();
                        _activeEventDomes.Add(dome);
                        dungeon.Entities.Add(dome);
                    }
                }

                // Spawn a map marker so players can find the event
                _activeMapMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", entrance) as MapMarkerGenericRadius;
                if (_activeMapMarker != null)
                {
                    _activeMapMarker.alpha = 0.8f;
                    _activeMapMarker.color1 = new Color(1f, 0.1f, 0.1f, 1f); // Bright red
                    _activeMapMarker.color2 = new Color(0.3f, 0f, 0f, 0.5f);
                    _activeMapMarker.radius = 0.2f;
                    _activeMapMarker.Spawn();
                    _activeMapMarker.SendUpdate();
                    dungeon.Entities.Add(_activeMapMarker);
                }

                // Spawn a fire pit as a visible ground marker
                var fire = GameManager.server.CreateEntity("assets/prefabs/deployable/campfire/campfire.prefab", entrance);
                if (fire != null)
                {
                    fire.Spawn();
                    fire.SetFlag(BaseEntity.Flags.On, true); // Light it up
                    dungeon.Entities.Add(fire);
                }

                // Spawn a large sign to label the entrance
                var sign = GameManager.server.CreateEntity("assets/prefabs/deployable/signs/sign.post.double.prefab", entrance + new Vector3(2, 0, 0));
                if (sign != null)
                {
                    sign.Spawn();
                    dungeon.Entities.Add(sign);
                }
                
                PrintToChat($"<color=#FF6B6B>⚔ GLOBAL RAID EVENT STARTED! ⚔</color>\nA dungeon entrance has appeared at <color=yellow>{GetGrid(entrance)}</color>! Check your map for a <color=#FF4444>red marker</color>.\n<color=#AAAAAA>Walk into the dome to enter the dungeon.</color>");
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
             int floorCount = 0;
             for (float x = -size; x <= size; x += 4f)
             {
                 for (float z = -size; z <= size; z += 4f)
                 {
                     var floor = GameManager.server.CreateEntity(_config.FloorPrefab, dungeon.Position + new Vector3(x, 0, z));
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
                         floorCount++;
                     }
                 }
             }

             if (floorCount == 0)
             {
                 PrintWarning("[NWG Raid Dungeons] No floor tiles spawned — check config FloorPrefab path.");
             }

             SpawnWave(dungeon, 1);

             var turret = GameManager.server.CreateEntity(_config.TurretPrefab, dungeon.Position + new Vector3(10, 0, 10)) as AutoTurret;
             if (turret != null)
             {
                 turret.Spawn();
                 dungeon.Entities.Add(turret);
             }
             else
             {
                 PrintWarning("[NWG Raid Dungeons] Failed to spawn turret — check config TurretPrefab.");
             }

             var crate = GameManager.server.CreateEntity(_config.CratePrefab, dungeon.Position) as HackableLockedCrate;
             if (crate != null)
             {
                 crate.Spawn();
                 dungeon.Entities.Add(crate);
             }
             else
             {
                 PrintWarning("[NWG Raid Dungeons] Failed to spawn reward crate — check config CratePrefab.");
             }
        }

        private void SpawnWave(ActiveDungeon dungeon, int wave)
        {
            int count = 4 + (wave * 2);
            int spawned = 0;
            for(int i=0; i<count; i++)
            {
                var scientist = GameManager.server.CreateEntity(_config.ScientistPrefab, dungeon.Position + new Vector3(UnityEngine.Random.Range(-10, 10), 0.5f, UnityEngine.Random.Range(-10, 10))) as ScientistNPC;
                if (scientist != null)
                {
                    scientist.Spawn();
                    dungeon.Npcs.Add(scientist);
                    dungeon.Entities.Add(scientist);
                    spawned++;
                }
            }

            if (spawned == 0)
            {
                PrintWarning($"[NWG Raid Dungeons] Wave {wave}: No NPCs spawned — check config ScientistPrefab path.");
            }
            else
            {
                Puts($"[NWG Raid Dungeons] Wave {wave}: Spawned {spawned}/{count} NPCs.");
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
            
            var boss = GameManager.server.CreateEntity(_config.BossPrefab, dungeon.Position) as ScientistNPC;
            if (boss == null)
            {
                PrintWarning("[NWG Raid Dungeons] Failed to spawn boss — check config BossPrefab path.");
                return;
            }
            boss.displayName = "DUNGEON OVERSEER";
            boss.Spawn();
            dungeon.Npcs.Add(boss);
            dungeon.Entities.Add(boss);
        }

        private void StopAllDungeons()
        {
            foreach (var d in _activeDungeons) StopDungeon(d);
            _activeDungeons.Clear();
            CleanupGlobalEntrance();
        }

        private void CleanupGlobalEntrance()
        {
            foreach (var dome in _activeEventDomes)
            {
                if (dome != null && !dome.IsDestroyed) dome.Kill();
            }
            _activeEventDomes.Clear();

            if (_activeMapMarker != null && !_activeMapMarker.IsDestroyed)
                _activeMapMarker.Kill();
            _activeMapMarker = null;

            _globalEntrancePos = Vector3.zero;
            _globalDungeon = null;
        }

        private void CheckEntranceProximity()
        {
            if (_globalDungeon == null || _globalEntrancePos == Vector3.zero) return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.IsDead() || !player.IsConnected) continue;
                if (Vector3.Distance(player.transform.position, _globalEntrancePos) <= _config.EntranceTeleportRadius)
                {
                    // Teleport player into the dungeon
                    player.Teleport(_globalDungeon.Position + new Vector3(0, 2, 0));
                    player.ChatMessage("<color=#FF6B6B>⚔ You have entered the RAID DUNGEON! ⚔</color>\n<color=#AAAAAA>Defeat all waves and the Overseer to claim the loot!</color>");
                }
            }
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

