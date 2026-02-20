using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using Newtonsoft.Json;
using Rust;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("NWGRaidDungeons", "NWG Team", "3.1.2")]
    [Description("Dungeon events and Boss fights (Private/Group/Global). Fixed compiler timeout.")]
    public class NWGRaidDungeons : RustPlugin
    {
        [PluginReference] private Plugin NWGCore, NWGClans, NWGMarkers;

        private class PluginConfig
        {
            public float EventIntervalHours = 3.0f;
            public Vector3 BasePosition = new Vector3(2500, 5, 2500); // Moved to Oceanic Remote to avoid FlyHack
            public float DungeonSpacing = 500f;
            public float DungeonHackingTime = 300f;
            public string ExitPortalPrefab = "assets/prefabs/misc/monuments/tunnel_entrance/entrance_a.prefab";
            public string WallPrefab = "assets/prefabs/building core/wall/wall.prefab";
            public string DoorwayPrefab = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
            public string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
            public string GatewayPrefab = "assets/prefabs/building core/wall.frame/wall.frame.prefab";
            public List<DungeonTheme> Themes = new List<DungeonTheme>
            {
                new DungeonTheme { Name = "The Graveyard", NpcPrefabs = new List<string> { "assets/prefabs/npc/scarecrow/scarecrow.prefab", "assets/prefabs/npc/scientistloadout/scientist_full_any.prefab" } },
                new DungeonTheme { Name = "The Shadows", NpcPrefabs = new List<string> { "assets/prefabs/npc/scarecrow/scarecrow.prefab", "assets/prefabs/npc/scientistloadout/scientist_full_any.prefab" } }
            };
            public float DomeRadius = 50f;
            public float EntranceTeleportRadius = 5f;
            public int CompletionTimeoutSmall = 15;
            public int CompletionTimeoutMedium = 25;
            public int CompletionTimeoutLarge = 40;
        }

        private class DungeonTheme
        {
            public string Name;
            public List<string> NpcPrefabs;
            public List<string> DecorationPrefabs = new List<string>();
            public float MobDensityMultiplier = 1.0f;
            public float HealthMultiplier = 1.0f;
        }

        private PluginConfig _config;

        private void LoadConfigVariables()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null) LoadDefaultConfig();

            // Force fix legacy/invalid settings from old config files that users don't manually delete
            bool changed = false;
            // Use wall.doorway as a safe fallback since it's guaranteed to exist and we set it to stone anyway
            if (string.IsNullOrEmpty(_config.ExitPortalPrefab) || _config.ExitPortalPrefab.Contains("monuments") || _config.ExitPortalPrefab.Contains("door.hinged.metal")) 
            { 
                _config.ExitPortalPrefab = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab"; 
                changed = true; 
            }
            if (_config.BasePosition.y > 1000) 
            { 
                _config.BasePosition = new Vector3(2500, 5, 2500); 
                changed = true; 
            }
            if (string.IsNullOrEmpty(_config.GatewayPrefab) || _config.GatewayPrefab.Contains("single door")) 
            { 
                _config.GatewayPrefab = "assets/prefabs/building core/wall.frame/wall.frame.prefab"; 
                changed = true; 
            }

            // Force add Halloween theme if it doesn't exist
            if (!_config.Themes.Any(t => t.Name.Contains("Halloween")))
            {
                _config.Themes.Add(new DungeonTheme 
                { 
                    Name = "Halloween Horrors", 
                    NpcPrefabs = new List<string> { 
                        "assets/prefabs/npc/mummy/mummy.prefab", 
                        "assets/prefabs/npc/scarecrow/scarecrow_dungeon.prefab",
                        "assets/prefabs/npc/murderer/murderer.prefab"
                    },
                    DecorationPrefabs = new List<string> {
                        "assets/prefabs/misc/halloween/scarecrow/scarecrow.deployed.prefab",
                        "assets/prefabs/misc/halloween/deployablegravestone/gravestone.stone.deployed.prefab",
                        "assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab",
                        "assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab",
                        "assets/prefabs/misc/halloween/spookyspeaker/spookyspeaker.prefab",
                        "assets/prefabs/missions/portal/proceduraldungeon/webs/webs.prefab",
                        "assets/prefabs/misc/halloween/coffin/coffin.prefab",
                        "assets/prefabs/misc/halloween/skull spikes/skullspikes.deployed.prefab",
                        "assets/prefabs/misc/halloween/trophy skulls/skulltrophy.deployed.prefab"
                    }
                });
                changed = true;
            }

            if (changed) SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private enum DungeonType { Private, Group, Global }
        private enum DungeonSize { Small, Medium, Large }

        private class ActiveDungeon
        {
            public string Id = Guid.NewGuid().ToString();
            public DungeonType Type;
            public DungeonSize Size;
            public DungeonTheme Theme;
            public ulong OwnerId;
            public Vector3 Position;
            public Vector3 EntrancePos;
            public List<BaseEntity> Entities = new List<BaseEntity>();
            public List<BaseCombatEntity> Mobs = new List<BaseCombatEntity>();
            public List<LootContainer> LootNodes = new List<LootContainer>();
            public Dictionary<ulong, Vector3> ParticipantReturnPoints = new Dictionary<ulong, Vector3>();
            public bool IsCleared = false;
            public DateTime StartTime;
            public BaseEntity ExitPortal;
            public Timer ExpiryTimer;
            public List<SphereEntity> DomeSpheres = new List<SphereEntity>();
            public MapMarkerGenericRadius MapMarker;
            public bool IsBuilding = false;
        }

        private List<ActiveDungeon> _activeDungeons = new List<ActiveDungeon>();
        private int _dungeonCounter = 0;
        private Timer _eventTimer;
        private Timer _statusTimer;
        private Timer _entranceCheckTimer;

        private const string PermAdmin = "nwgraiddungeons.admin";
        private const string PermPrivate = "nwgraiddungeons.private";
        private const string PermGroup = "nwgraiddungeons.group";

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermPrivate, this);
            permission.RegisterPermission(PermGroup, this);
            LoadConfigVariables();
        }

        private void OnServerInitialized()
        {
            _eventTimer = timer.Every(_config.EventIntervalHours * 3600, () => StartDungeon(DungeonType.Global));
            _statusTimer = timer.Every(5f, CheckDungeonsProgress);
            _entranceCheckTimer = timer.Every(2f, CheckEntranceProximity);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Usage"] = "<color=#d9534f>[NWG]</color> Usage: /dungeon start <global|private|group>",
                ["GlobalEvent"] = "<color=#b7d092>[NWG] GLOBAL RAID EVENT STARTED!</color>",
                ["DungeonCleared"] = "<color=#b7d092>[NWG] DUNGEON CLEARED!</color>",
                ["DungeonFailed"] = "<color=#d9534f>[NWG] DUNGEON EXPIRED!</color>",
                ["Returned"] = "<color=#b7d092>[NWG]</color> You have returned."
            }, this);
        }

        private void Unload()
        {
            _eventTimer?.Destroy();
            _statusTimer?.Destroy();
            _entranceCheckTimer?.Destroy();
            foreach (var d in _activeDungeons.ToList()) StopDungeon(d);
            _activeDungeons.Clear();
        }

        private void StartDungeon(DungeonType type, BasePlayer initiator = null)
        {
            var theme = _config.Themes[UnityEngine.Random.Range(0, _config.Themes.Count)];
            DungeonSize size = (DungeonSize)UnityEngine.Random.Range(0, 3);
            // Spacing them out in the ocean at Y=5 - Using fixed counter to avoid overlap
            Vector3 pos = _config.BasePosition + new Vector3(_dungeonCounter * _config.DungeonSpacing, 0, 0);
            _dungeonCounter++;

            var dungeon = new ActiveDungeon { Type = type, Size = size, Theme = theme, Position = pos, OwnerId = initiator?.userID ?? 0, StartTime = DateTime.Now };
            dungeon.ExpiryTimer = timer.Once((size == DungeonSize.Small ? 15 : 45) * 60, () => HandleDungeonExpiry(dungeon));

            _activeDungeons.Add(dungeon);
            BuildDungeon(dungeon); 
            
            Puts($"Dungeon {dungeon.Id} ({size}) created at {pos} (Oceanic Surface).");

            if (type == DungeonType.Global) 
            {
                SetupGlobalEntrance(dungeon);
                MessageGlobal(lang.GetMessage("GlobalEvent", this, null));
                NWGMarkers?.Call("API_CreateMarker", dungeon.EntrancePos, $"dungeon_{dungeon.Id}", 0, 30f, 0.4f, "GLOBAL DUNGEON", "FF0000", "FF0000", 0.8f);
            }
            else if (initiator != null) 
            {
                if (type == DungeonType.Private)
                {
                    // platform logic
                    bool onPlatform = false;
                    Vector3 entrancePos = initiator.transform.position;
                    RaycastHit hit;
                    if (Physics.Raycast(initiator.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 3.0f, LayerMask.GetMask("Construction")))
                    {
                        entrancePos = hit.point;
                        onPlatform = true;
                        Puts($"[NWG] Private dungeon entrance placing on existing platform at {entrancePos}");
                    }
                    else
                    {
                        entrancePos = FindSuitableSpawnPos(initiator.transform.position);
                    }

                    dungeon.EntrancePos = entrancePos;
                    SpawnEntrancePortal(dungeon, onPlatform, initiator);
                    
                    string grid = GetGrid(entrancePos);
                    Puts($"Private dungeon entrance spawned at {entrancePos} (Grid: {grid}).");
                    initiator.ChatMessage($"<color=#b7d092>[NWG]</color> Private dungeon opened {(onPlatform ? "ON your platform!" : "nearby!")} Find the gate at Grid <color=#FFA500>{grid}</color>");
                    
                    // Private Map Marker
                    NWGMarkers?.Call("API_CreateMarker", dungeon.EntrancePos, $"dungeon_{dungeon.Id}", 0, 30f, 0.3f, "PRIVATE DUNGEON", "b7d092", "b7d092", 0.7f);
                }
                else
                {
                    TeleportToDungeon(initiator, dungeon);
                }
            }
        }

        [ChatCommand("tpgrd")]
        private void CmdTPGrd(BasePlayer player)
        {
            if (player.net.connection.authLevel < 1) return;
            var dungeon = _activeDungeons.OrderByDescending(d => d.StartTime).FirstOrDefault(d => d.OwnerId == player.userID || player.IsAdmin);
            if (dungeon == null)
            {
                player.ChatMessage("No active dungeons found to teleport to.");
                return;
            }
            player.Teleport(dungeon.EntrancePos);
            player.ChatMessage($"<color=#b7d092>[NWG]</color> Teleported to dungeon entrance at <color=#FFA500>{dungeon.EntrancePos}</color>");
        }

        private void SpawnEntrancePortal(ActiveDungeon dungeon, bool onPlatform = false, BasePlayer initiator = null)
        {
            Vector3 spawnPos = dungeon.EntrancePos + Vector3.up * 0.1f;
            Quaternion spawnRot = initiator != null ? Quaternion.Euler(0, initiator.eyes.rotation.eulerAngles.y, 0) : Quaternion.identity;
            
            bool isHalloween = dungeon.Theme.Name.Contains("Halloween");
            string framePrefab = isHalloween ? "assets/prefabs/missions/portal/halloweenportalart.prefab" : _config.GatewayPrefab;
            string portalPrefab = isHalloween ? "assets/prefabs/missions/portal/halloweenportalentry.prefab" : _config.ExitPortalPrefab;

            // MASSIVE Entrance Marker (Sphere) - 40m radius
            var sphereMarker = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", spawnPos + Vector3.up * 5f) as SphereEntity;
            if (sphereMarker != null)
            {
                sphereMarker.Spawn();
                sphereMarker.SetFlag(BaseEntity.Flags.Reserved1, true);
                sphereMarker.currentRadius = 40f; 
                sphereMarker.lerpSpeed = 0f;
                sphereMarker.SendNetworkUpdate();
                dungeon.Entities.Add(sphereMarker);
            }

            // 0. Base Foundation at entrance for stability and reachability (only if not on platform and not Halloween)
            if (!onPlatform && !isHalloween)
            {
                var foundation = GameManager.server.CreateEntity("assets/prefabs/building core/foundation/foundation.prefab", spawnPos, Quaternion.identity);
                if (foundation != null)
                {
                    foundation.Spawn();
                    var block = foundation as BuildingBlock;
                    if (block != null) { block.SetGrade(BuildingGrade.Enum.Stone); block.health = block.MaxHealth(); block.SendNetworkUpdate(); }
                    dungeon.Entities.Add(foundation);
                }
            }

            // 1. The Frame / Portal Art
            var frame = GameManager.server.CreateEntity(framePrefab, spawnPos + Vector3.up * 0.1f, spawnRot);
            if (frame != null)
            {
                frame.Spawn();
                if (!isHalloween)
                {
                    var block = frame as BuildingBlock;
                    if (block != null) { block.SetGrade(BuildingGrade.Enum.Stone); block.health = block.MaxHealth(); block.SendNetworkUpdate(); }
                }
                dungeon.Entities.Add(frame);
            }

            // 2. The Doorway / Actual Portal
            var ent = GameManager.server.CreateEntity(portalPrefab, spawnPos + Vector3.up * 0.1f, spawnRot);
            if (ent != null)
            {
                ent.SetFlag(BaseEntity.Flags.Reserved1, true); 
                ent.Spawn();
                if (!isHalloween)
                {
                    var block = ent as BuildingBlock;
                    if (block != null) { block.SetGrade(BuildingGrade.Enum.Stone); block.health = block.MaxHealth(); block.SendNetworkUpdate(); }
                }
                dungeon.Entities.Add(ent);
            }
        }

        private void MessageGlobal(string msg)
        {
            foreach (var player in BasePlayer.activePlayerList) player.ChatMessage(msg);
        }

        private void BuildDungeon(ActiveDungeon dungeon)
        {
            int roomsSide = 6; // Stable Medium
            if (dungeon.Size == DungeonSize.Small) roomsSide = 3;
            if (dungeon.Size == DungeonSize.Large) roomsSide = 12;

            // Updated: Rust building blocks are 3m wide. 
            float foundationSize = 3.0f; 
            int foundationsPerRoom = 3; // 9x9m 
            float roomSize = foundationSize * foundationsPerRoom;
            int fSide = roomsSide * foundationsPerRoom;
            
            // ELEVATE THE ENTIRE DUNGEON TO 50M
            dungeon.Position = new Vector3(dungeon.Position.x, 50f, dungeon.Position.z);
            dungeon.IsCleared = false; 
            dungeon.IsBuilding = true;
            
            Puts($"[Stage 1] Foundations - Grid: {fSide}x{fSide} at Height: {dungeon.Position.y}");

            for (int x = 0; x < fSide; x++)
            {
                for (int z = 0; z < fSide; z++)
                {
                    Vector3 fPos = dungeon.Position + new Vector3(x * foundationSize - (fSide * foundationSize / 2f), 0, z * foundationSize - (fSide * foundationSize / 2f));
                    SpawnStructure("assets/prefabs/building core/foundation/foundation.prefab", fPos, Quaternion.identity, dungeon);
                }
            }

            timer.Once(6.0f, () => {
                if (!_activeDungeons.Contains(dungeon)) return;
                BuildDungeonStage2(dungeon, roomsSide, roomSize, fSide);
            });
        }

        private void BuildDungeonStage2(ActiveDungeon dungeon, int roomsSide, float roomSize, int fSide)
        {
            Puts("[Stage 2] Placing Structural Walls...");
            float foundationSize = 3.0f;
            Vector3 startPos = dungeon.Position - new Vector3((fSide * foundationSize) / 2f, 0, (fSide * foundationSize) / 2f);

            // Perimeter
            for (int i = 0; i < fSide; i++)
            {
                Vector3 x0 = startPos + new Vector3(-1.5f, 0, i * foundationSize);
                Vector3 xMax = startPos + new Vector3((fSide - 1) * foundationSize + 1.5f, 0, i * foundationSize);
                Vector3 z0 = startPos + new Vector3(i * foundationSize, 0, -1.5f);
                Vector3 zMax = startPos + new Vector3(i * foundationSize, 0, (fSide - 1) * foundationSize + 1.5f);

                SpawnStructure(_config.WallPrefab, x0, Quaternion.Euler(0, 90, 0), dungeon);
                SpawnStructure(_config.WallPrefab, xMax, Quaternion.Euler(0, 90, 0), dungeon);
                SpawnStructure(_config.WallPrefab, z0, Quaternion.identity, dungeon);
                SpawnStructure(_config.WallPrefab, zMax, Quaternion.identity, dungeon);
            }

            // Grid Walls (9m room boundaries)
            for (int rx = 0; rx < roomsSide; rx++)
            {
                for (int rz = 0; rz < roomsSide; rz++)
                {
                    Vector3 roomBase = startPos + new Vector3(rx * roomSize, 0, rz * roomSize);
                    
                    if (rx < roomsSide - 1)
                    {
                        float wallX = (rx + 1) * roomSize - 1.5f;
                        int doorI = UnityEngine.Random.Range(0, 3);
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 pos = startPos + new Vector3(wallX, 0, rz * roomSize + (i * 3.0f));
                            if (i == doorI) {
                                bool isGarage = UnityEngine.Random.value > 0.5f;
                                string p = isGarage ? "assets/prefabs/building core/wall.frame/wall.frame.prefab" : "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
                                var ent = SpawnStructure(p, pos, Quaternion.Euler(0, 90, 0), dungeon);
                                SpawnDoor(ent, pos, Quaternion.Euler(0, 90, 0), dungeon, isGarage);
                            } else SpawnStructure(_config.WallPrefab, pos, Quaternion.Euler(0, 90, 0), dungeon);
                        }
                    }

                    if (rz < roomsSide - 1)
                    {
                        float wallZ = (rz + 1) * roomSize - 1.5f;
                        int doorI = UnityEngine.Random.Range(0, 3);
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 pos = startPos + new Vector3(rx * roomSize + (i * 3.0f), 0, wallZ);
                            if (i == doorI) {
                                bool isGarage = UnityEngine.Random.value > 0.5f;
                                string p = isGarage ? "assets/prefabs/building core/wall.frame/wall.frame.prefab" : "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
                                var ent = SpawnStructure(p, pos, Quaternion.identity, dungeon);
                                SpawnDoor(ent, pos, Quaternion.identity, dungeon, isGarage);
                            } else SpawnStructure(_config.WallPrefab, pos, Quaternion.identity, dungeon);
                        }
                    }
                }
            }

            timer.Once(6.0f, () => {
                if (!_activeDungeons.Contains(dungeon)) return;
                BuildDungeonStage3(dungeon, roomsSide, roomSize, fSide);
            });
        }

        private void BuildDungeonStage3(ActiveDungeon dungeon, int roomsSide, float roomSize, int fSide)
        {
            Puts("[Stage 3] Spawning Loot and Security Forces...");
            float foundationSize = 3.0f;
            Vector3 startPos = dungeon.Position - new Vector3((fSide * foundationSize) / 2f, 0, (fSide * foundationSize) / 2f);

            for (int rx = 0; rx < roomsSide; rx++)
            {
                for (int rz = 0; rz < roomsSide; rz++)
                {
                    Vector3 cellCenter = startPos + new Vector3(rx * roomSize + 4.5f, 0.5f, rz * roomSize + 4.5f);
                    bool isBossRoom = (rx == roomsSide / 2 && rz == roomsSide / 2);

                    if (isBossRoom)
                    {
                        for (int i = 0; i < 15; i++) SpawnNPC(dungeon, cellCenter + new Vector3(UnityEngine.Random.Range(-3,3), 0, UnityEngine.Random.Range(-3,3)));
                        var trophy = SpawnLoot(dungeon, cellCenter, true) as StorageContainer;
                        if (trophy != null) { ItemManager.CreateByItemID(-1540203525, 1000)?.MoveToContainer(trophy.inventory); }
                    }
                    else
                    {
                        if (UnityEngine.Random.Range(0, 100) < 70)
                        {
                            int count = UnityEngine.Random.Range(4, 10);
                            for (int i = 0; i < count; i++) SpawnNPC(dungeon, cellCenter + new Vector3(UnityEngine.Random.Range(-3,3), 0, UnityEngine.Random.Range(-3,3)));
                        }
                        if (UnityEngine.Random.Range(0, 100) < 60) SpawnLoot(dungeon, cellCenter + new Vector3(UnityEngine.Random.Range(-2,2), 0, UnityEngine.Random.Range(-2,2)));
                    }

                    // Theme Decorations
                    if (dungeon.Theme.DecorationPrefabs != null && dungeon.Theme.DecorationPrefabs.Count > 0)
                    {
                        int decorCount = UnityEngine.Random.Range(1, 4);
                        for (int i = 0; i < decorCount; i++)
                        {
                            Vector3 decorPos = cellCenter + new Vector3(UnityEngine.Random.Range(-4f, 4f), 0, UnityEngine.Random.Range(-4f, 4f));
                            string prefab = dungeon.Theme.DecorationPrefabs[UnityEngine.Random.Range(0, dungeon.Theme.DecorationPrefabs.Count)];
                            var decor = GameManager.server.CreateEntity(prefab, decorPos, Quaternion.identity);
                            if (decor != null)
                            {
                                decor.Spawn();
                                dungeon.Entities.Add(decor);
                            }
                        }
                    }
                }
            }

            timer.Once(6.0f, () => {
                if (!_activeDungeons.Contains(dungeon)) return;
                BuildDungeonFinal(dungeon, fSide);
            });
        }

        private void BuildDungeonFinal(ActiveDungeon dungeon, int fSide)
        {
            Puts("[Stage 4] Finishing Roof and Systems...");
            float foundationSize = 3.0f;
            Vector3 startPos = dungeon.Position - new Vector3((fSide * foundationSize) / 2f, 0, (fSide * foundationSize) / 2f);

            for (int x = 0; x < fSide; x++)
            {
                for (int z = 0; z < fSide; z++)
                {
                    Vector3 rPos = startPos + new Vector3(x * foundationSize, 3.0f, z * foundationSize);
                    SpawnStructure("assets/prefabs/building core/floor/floor.prefab", rPos, Quaternion.identity, dungeon);
                }
            }

            // Visual Dome
            float radius = (fSide * foundationSize) + 50f;
            for (int i = 0; i < 2; i++)
            {
                var sphere = GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", dungeon.Position + Vector3.up * 5) as SphereEntity;
                if (sphere != null)
                {
                    sphere.currentRadius = radius + (i * 20f);
                    sphere.Spawn();
                    dungeon.DomeSpheres.Add(sphere);
                    dungeon.Entities.Add(sphere);
                }
            }

            // Exit Portal
            bool isHalloween = dungeon.Theme.Name.Contains("Halloween");
            string exitPortalPrefab = isHalloween ? "assets/prefabs/missions/portal/halloweenportalexit.prefab" : _config.ExitPortalPrefab;
            
            var exitPortal = GameManager.server.CreateEntity(exitPortalPrefab, dungeon.Position + Vector3.up * 0.1f, Quaternion.identity);
            if (exitPortal != null)
            {
                exitPortal.Spawn();
                dungeon.ExitPortal = exitPortal;
                dungeon.Entities.Add(exitPortal);
            }

            dungeon.IsBuilding = false;
            Puts($"[NWG] Dungeon {dungeon.Id} Generation Complete.");
        }

        private void SpawnDoor(BaseEntity frame, Vector3 pos, Quaternion rot, ActiveDungeon dungeon, bool garage = false)
        {
            string prefab = garage ? "assets/prefabs/deployable/doors/garage.door.metal/garage.door.metal.prefab" : "assets/prefabs/deployable/doors/door.hinged.metal/door.hinged.metal.prefab";
            timer.Once(0.5f, () => {
                var door = GameManager.server.CreateEntity(prefab, pos, rot);
                if (door != null)
                {
                    door.Spawn();
                    dungeon.Entities.Add(door);
                }
            });
        }

        private BaseEntity SpawnLoot(ActiveDungeon dungeon, Vector3 pos, bool elite = false)
        {
            bool isHalloween = dungeon.Theme.Name.Contains("Halloween");
            string prefab = elite ? "assets/bundled/prefabs/radtown/crate_elite.prefab" : "assets/bundled/prefabs/radtown/crate_normal.prefab";
            
            if (isHalloween)
            {
                // Use a Coffin for Halloween loot
                prefab = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";
            }

            var ent = GameManager.server.CreateEntity(prefab, pos, Quaternion.identity);
            if (ent != null)
            {
                ent.Spawn();
                dungeon.Entities.Add(ent);
            }
            return ent;
        }

        private void SpawnNPC(ActiveDungeon dungeon, Vector3 pos)
        {
             var npcPrefab = dungeon.Theme.NpcPrefabs[UnityEngine.Random.Range(0, dungeon.Theme.NpcPrefabs.Count)];
             var npc = GameManager.server.CreateEntity(npcPrefab, pos, Quaternion.identity) as BaseCombatEntity;
             if (npc != null)
             {
                 npc.Spawn();
                 dungeon.Mobs.Add(npc);
                 dungeon.Entities.Add(npc);
             }
        }

        private BaseEntity SpawnStructure(string prefab, Vector3 pos, Quaternion rot, ActiveDungeon dungeon)
        {
            var ent = GameManager.server.CreateEntity(prefab, pos, rot);
            if (ent == null) return null;
            
            ent.Spawn(); 

            var block = ent as BuildingBlock;
            if (block != null)
            {
                block.SetGrade(BuildingGrade.Enum.Stone);
                block.health = block.MaxHealth();
                block.SendNetworkUpdate();
            }

            dungeon.Entities.Add(ent);
            return ent;
        }

        private void CheckDungeonsProgress()
        {
            foreach (var dungeon in _activeDungeons.ToList())
            {
                if (dungeon.IsCleared || dungeon.IsBuilding) continue; 
                dungeon.Mobs.RemoveAll(m => m == null || m.IsDead());
                if (dungeon.Mobs.Count == 0) ClearDungeon(dungeon);
            }
        }

        private void ClearDungeon(ActiveDungeon dungeon)
        {
            dungeon.IsCleared = true;
            foreach (var p in BasePlayer.activePlayerList) 
            {
                if (Vector3.Distance(p.transform.position, dungeon.Position) < 200f) 
                {
                    p.ChatMessage("<color=#b7d092>[NWG]</color> DUNGEON COMPLETED! Teleporting home in 30 seconds...");
                    timer.Once(30f, () => { if (p != null) ReturnPlayer(p, dungeon); });
                }
            }

            // Shorten expiry timer to 2 minutes after clear
            dungeon.ExpiryTimer?.Destroy();
            dungeon.ExpiryTimer = timer.Once(120f, () => HandleDungeonExpiry(dungeon));
        }

        private void HandleDungeonExpiry(ActiveDungeon dungeon)
        {
            if (!_activeDungeons.Contains(dungeon)) return;
            StopDungeon(dungeon);
            _activeDungeons.Remove(dungeon);
        }

        private void TeleportToDungeon(BasePlayer player, ActiveDungeon dungeon)
        {
            if (dungeon.ParticipantReturnPoints.ContainsKey(player.userID)) return; // Already inside

            dungeon.ParticipantReturnPoints[player.userID] = player.transform.position;
            ShowLoadingUI(player);
            
            // Teleport to floor level (Safe 1.5m height to avoid roof clipping and ensure ground contact)
            Vector3 targetPos = dungeon.Position + Vector3.up * 1.5f;
            player.Teleport(targetPos);

            // Re-teleport and Un-sleep after 25s
            timer.Once(25.0f, () => {
                if (player != null && player.IsConnected)
                {
                    player.Teleport(targetPos);
                    player.EndSleeping();
                    CuiHelper.DestroyUi(player, "DungeonLoading");
                    player.ChatMessage("<color=#b7d092>[NWG]</color> Welcome to the Dungeon! Survive and escape.");
                }
            });
        }

        private void ShowLoadingUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "DungeonLoading");
            var container = new CuiElementContainer();
            
            // Premium Dark Blur Background
            container.Add(new CuiPanel { 
                Image = { Color = "0.01 0.01 0.01 0.98", Material = "assets/content/ui/uibackgroundblur-builtinv2.mat" }, 
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, 
                CursorEnabled = false 
            }, "Overlay", "DungeonLoading");

            // Main Title
            container.Add(new CuiLabel { 
                Text = { Text = "NWG MEG DUNGEON", FontSize = 45, Align = TextAnchor.MiddleCenter, Color = "0.718 0.816 0.573 1", Font = "robotocondensed-bold.ttf" }, 
                RectTransform = { AnchorMin = "0 0.55", AnchorMax = "1 0.75" } 
            }, "DungeonLoading");

            // Sub-status text
            container.Add(new CuiLabel { 
                Text = { Text = "INITIALIZING STABILITY MATRIX & SEEDING ELITE FORCES", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.4", Font = "robotocondensed-regular.ttf" }, 
                RectTransform = { AnchorMin = "0 0.45", AnchorMax = "1 0.55" } 
            }, "DungeonLoading");

            // Progress Bar background
            container.Add(new CuiPanel { 
                Image = { Color = "1 1 1 0.05" }, 
                RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.41" } 
            }, "DungeonLoading", "ProgressBar");

            CuiHelper.AddUi(player, container);
        }

        private void ReturnPlayer(BasePlayer player, ActiveDungeon dungeon)
        {
            if (dungeon.ParticipantReturnPoints.TryGetValue(player.userID, out Vector3 retPos)) player.Teleport(retPos);
        }

        private object OnStructureStabilityUpdate(BuildingBlock block)
        {
            if (block.transform.position.y > 45) return false;
            return null;
        }

        private object OnEntityGroundMissing(BaseEntity entity)
        {
            if (entity.transform.position.y > 45) return false;
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity.transform.position.y > 45 && (info.damageTypes.Has(Rust.DamageType.Decay) || info.damageTypes.Has(Rust.DamageType.Suicide)))
                return false;
            return null;
        }

        private void CheckEntranceProximity()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                foreach (var d in _activeDungeons)
                {
                    // Entrance Logic - Increased radius to 5m for easier entry
                    float dist = Vector3.Distance(p.transform.position, d.EntrancePos);
                    if (dist < 5.0f) 
                    {
                        if (d.Type == DungeonType.Global) TeleportToDungeon(p, d);
                        else if (d.Type == DungeonType.Private && IsInGroup(p, d.OwnerId)) TeleportToDungeon(p, d);
                    }
                    else if (dist < 35.0f && p.IsAdmin) // Show debug for admins near the sphere
                    {
                         // p.SendConsoleCommand("ddraw.text", 0.5f, Color.green, d.EntrancePos + Vector3.up * 2, "DUNGEON ENTRANCE HERE");
                    }

                    // Exit Logic
                    if (d.ExitPortal != null && Vector3.Distance(p.transform.position, d.ExitPortal.transform.position) < 2.5f)
                    {
                        ReturnPlayer(p, d);
                    }
                }
            }
        }

        private bool IsInGroup(BasePlayer player, ulong ownerId)
        {
            if (player.userID == ownerId) return true;
            if (NWGClans == null) return false;
            string tag1 = NWGClans.Call<string>("GetClanTag", player.userID);
            string tag2 = NWGClans.Call<string>("GetClanTag", ownerId);
            return !string.IsNullOrEmpty(tag1) && tag1 == tag2;
        }

        private string GetGrid(Vector3 pos)
        {
            float worldSize = ConVar.Server.worldsize;
            float offset = worldSize / 2f;
            const float cellSize = 150f; // Standard NWG cellSize
            
            int xIdx = Mathf.FloorToInt((pos.x + offset) / cellSize);
            int zIdx = Mathf.FloorToInt((worldSize - (pos.z + offset)) / cellSize); 
            
            string col = "";
            int cx = xIdx;
            do
            {
                col = (char)('A' + (cx % 26)) + col;
                cx = (cx / 26) - 1;
            } while (cx >= 0);
            
            return $"{col}{zIdx}";
        }

        private bool IsNearBase(BasePlayer player)
        {
            var priv = player.GetBuildingPrivilege();
            return priv != null && priv.IsAuthed(player);
        }

        private Vector3 FindSuitableSpawnPos(Vector3 center)
        {
            for (int i = 0; i < 40; i++)
            {
                float angle = UnityEngine.Random.Range(0, 360) * Mathf.Deg2Rad;
                float dist = UnityEngine.Random.Range(40f, 60f); 
                // Start raycast from 500m up to handle high mountains
                Vector3 pos = center + new Vector3(Mathf.Cos(angle) * dist, 500, Mathf.Sin(angle) * dist);
                
                RaycastHit hit;
                if (Physics.Raycast(pos, Vector3.down, out hit, 800, LayerMask.GetMask("Terrain", "World", "Construction")))
                {
                    if (hit.point.y > WaterLevel.GetWaterSurface(hit.point, true, true) + 1f)
                    {
                        return hit.point;
                    }
                }
            }
            return center + new Vector3(8, 2, 8); 
        }

        private void SetupGlobalEntrance(ActiveDungeon dungeon)
        {
            // Find a random monument or safe spot on the map for the global entrance
            Vector3 randomPos = Vector3.zero;
            for (int i = 0; i < 50; i++)
            {
                float worldSize = ConVar.Server.worldsize;
                randomPos = new Vector3(UnityEngine.Random.Range(-worldSize / 2f, worldSize / 2f), 500, UnityEngine.Random.Range(-worldSize / 2f, worldSize / 2f));
                
                RaycastHit hit;
                if (Physics.Raycast(randomPos, Vector3.down, out hit, 800, LayerMask.GetMask("Terrain", "World")))
                {
                    if (hit.point.y > WaterLevel.GetWaterSurface(hit.point, true, true) + 2f)
                    {
                        dungeon.EntrancePos = hit.point;
                        SpawnEntrancePortal(dungeon);
                        return;
                    }
                }
            }
            // Fallback to center of map but not 0,0,0
            dungeon.EntrancePos = new Vector3(10, 10, 10); 
            SpawnEntrancePortal(dungeon);
        }

        private void StopDungeon(ActiveDungeon dungeon)
        {
            NWGMarkers?.Call("API_RemoveMarker", $"dungeon_{dungeon.Id}");
            dungeon.ExpiryTimer?.Destroy();
            
            // Return players before killing entities
            foreach (var participant in dungeon.ParticipantReturnPoints)
            {
                BasePlayer p = BasePlayer.FindByID(participant.Key);
                if (p != null && p.IsConnected)
                {
                    ReturnPlayer(p, dungeon);
                }
            }

            foreach (var ent in dungeon.Entities) if (ent != null) ent.Kill();
        }

        [ChatCommand("startdungeon")]
        private void CmdStartDungeon(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.ChatMessage("<color=#d9534f>[NWG]</color> This command is for administrators only. Use <color=#FFA500>/dungeon start private</color> with a Keycard.");
                return;
            }
            CmdDungeon(player, command, new[] { "start", "global" });
        }

        [ChatCommand("dungeon")]
        private void CmdDungeon(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage(lang.GetMessage("Usage", this, player.UserIDString));
                return;
            }

            if (args[0] == "start")
            {
                DungeonType type = DungeonType.Global;
                if (args.Length > 1)
                {
                    if (args[1].ToLower() == "private") type = DungeonType.Private;
                    else if (args[1].ToLower() == "group") type = DungeonType.Group;
                }

                if (type == DungeonType.Private)
                {
                    if (!IsNearBase(player))
                    {
                        player.ChatMessage("<color=#d9534f>[NWG]</color> You must be inside your <color=#FFA500>Building Privilege</color> zone to use a Keycard!");
                        return;
                    }

                    var activeItem = player.GetActiveItem();
                    if (activeItem == null || activeItem.info.shortname != "keycard_red" || activeItem.name != "Dungeon Keycard")
                    {
                        player.ChatMessage("<color=#d9534f>[NWG]</color> You must be holding a <color=#FFA500>Dungeon Keycard</color> in your hand!");
                        return;
                    }
                    activeItem.UseItem(1);
                    StartDungeon(type, player);
                    return;
                }

                // Admin-only check for Global/Group (if not otherwise permitted)
                if (type == DungeonType.Global && !permission.UserHasPermission(player.UserIDString, PermAdmin))
                {
                    player.ChatMessage("<color=#d9534f>[NWG]</color> Only administrators can start Global dungeon events.");
                    return;
                }

                if (type == DungeonType.Group && !permission.UserHasPermission(player.UserIDString, PermGroup) && !permission.UserHasPermission(player.UserIDString, PermAdmin))
                {
                    player.ChatMessage("<color=#d9534f>[NWG]</color> You do not have permission to start group dungeons.");
                    return;
                }

                StartDungeon(type, player);
            }
        }

        [ConsoleCommand("nwgrd.fixconfig")]
        private void CCFixConfig(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !permission.UserHasPermission(arg.Player().UserIDString, PermAdmin)) return;
            
            _config.BasePosition = new Vector3(2500, 5, 2500);
            _config.ExitPortalPrefab = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
            _config.GatewayPrefab = "assets/prefabs/building core/wall.frame/wall.frame.prefab";
            SaveConfig();
            Puts("NWG Raid Dungeons: Config forced to oceanic surface (2500, 5, 2500).");
            if (arg.Player() != null) arg.Player().ChatMessage("Dungeon Config Forced to Remote Ocean (No FlyHack Errors)!");
        }
    }
}
