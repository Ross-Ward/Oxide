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
    [Info("NWGRaidDungeons", "NWG Team", "3.1.2")]
    [Description("Dungeon events and Boss fights (Private/Group/Global).")]
    public class NWGRaidDungeons : RustPlugin
    {
        [PluginReference] private Plugin NWGCore, NWGClans;

        private class PluginConfig
        {
            public float EventIntervalHours = 3.0f;
            public Vector3 BasePosition = new Vector3(3000, 500, 3000);
            public float DungeonSpacing = 500f;
            public float DungeonHackingTime = 300f;
            public string ExitPortalPrefab = "assets/prefabs/deployable/single door/door.hinged.metal.prefab";
            public string WallPrefab = "assets/prefabs/building core/wall/wall.prefab";
            public string DoorwayPrefab = "assets/prefabs/building core/wall.doorway/wall.doorway.prefab";
            public string FloorPrefab = "assets/prefabs/building core/floor/floor.prefab";
            public List<DungeonTheme> Themes = new List<DungeonTheme>
            {
                new DungeonTheme { Name = "The Safari", NpcPrefabs = new List<string> { "assets/rust.ai/agents/wolf/wolf.prefab", "assets/rust.ai/agents/bear/bear.prefab" } },
                new DungeonTheme { Name = "The Graveyard", NpcPrefabs = new List<string> { "assets/prefabs/npc/murderer/murderer.prefab", "assets/prefabs/npc/scarecrow/scarecrow.prefab" } }
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
            public float MobDensityMultiplier = 1.0f;
            public float HealthMultiplier = 1.0f;
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
        }

        private List<ActiveDungeon> _activeDungeons = new List<ActiveDungeon>();
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
            DungeonSize size = DungeonSize.Medium;
            Vector3 pos = _config.BasePosition + new Vector3(_activeDungeons.Count * _config.DungeonSpacing, 0, 0);

            var dungeon = new ActiveDungeon { Type = type, Size = size, Theme = theme, Position = pos, OwnerId = initiator?.userID ?? 0, StartTime = DateTime.Now };
            dungeon.ExpiryTimer = timer.Once((size == DungeonSize.Small ? 15 : 30) * 60, () => HandleDungeonExpiry(dungeon));

            BuildDungeon(dungeon);
            _activeDungeons.Add(dungeon);

            if (type == DungeonType.Global) SetupGlobalEntrance(dungeon);
            else if (initiator != null) TeleportToDungeon(initiator, dungeon);
        }

        private void BuildDungeon(ActiveDungeon dungeon)
        {
            int gridSide = 8;
            float blockSize = 4f;
            for (int x = 0; x < gridSide; x++)
            {
                for (int z = 0; z < gridSide; z++)
                {
                    Vector3 blockPos = dungeon.Position + new Vector3(x * blockSize, 0, z * blockSize);
                    SpawnStructure(_config.FloorPrefab, blockPos, Quaternion.identity, dungeon);
                }
            }
        }

        private void SpawnStructure(string prefab, Vector3 pos, Quaternion rot, ActiveDungeon dungeon)
        {
            var ent = GameManager.server.CreateEntity(prefab, pos, rot);
            if (ent == null) return;
            ent.Spawn();
            dungeon.Entities.Add(ent);
        }

        private void CheckDungeonsProgress()
        {
            foreach (var dungeon in _activeDungeons.ToList())
            {
                if (dungeon.IsCleared) continue;
                dungeon.Mobs.RemoveAll(m => m == null || m.IsDead());
                if (dungeon.Mobs.Count == 0) ClearDungeon(dungeon);
            }
        }

        private void ClearDungeon(ActiveDungeon dungeon)
        {
            dungeon.IsCleared = true;
            foreach (var p in BasePlayer.activePlayerList) if (Vector3.Distance(p.transform.position, dungeon.Position) < 100f) p.ChatMessage("Dungeon Cleared!");
        }

        private void HandleDungeonExpiry(ActiveDungeon dungeon)
        {
            if (!_activeDungeons.Contains(dungeon)) return;
            StopDungeon(dungeon);
            _activeDungeons.Remove(dungeon);
        }

        private void TeleportToDungeon(BasePlayer player, ActiveDungeon dungeon)
        {
            dungeon.ParticipantReturnPoints[player.userID] = player.transform.position;
            player.Teleport(dungeon.Position + new Vector3(2, 2, 2));
        }

        private void ReturnPlayer(BasePlayer player, ActiveDungeon dungeon)
        {
            if (dungeon.ParticipantReturnPoints.TryGetValue(player.userID, out Vector3 retPos)) player.Teleport(retPos);
        }

        private void CheckEntranceProximity()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                foreach (var d in _activeDungeons)
                {
                    if (d.Type == DungeonType.Global && Vector3.Distance(p.transform.position, d.EntrancePos) < 5f) TeleportToDungeon(p, d);
                }
            }
        }

        private void SetupGlobalEntrance(ActiveDungeon dungeon)
        {
            dungeon.EntrancePos = new Vector3(0, 0, 0); // Placeholder
        }

        private void StopDungeon(ActiveDungeon dungeon)
        {
            dungeon.ExpiryTimer?.Destroy();
            foreach (var ent in dungeon.Entities) if (ent != null) ent.Kill();
        }

        [ChatCommand("dungeon")]
        private void CmdDungeon(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0 && args[0] == "start") StartDungeon(DungeonType.Global);
        }
    }
}
