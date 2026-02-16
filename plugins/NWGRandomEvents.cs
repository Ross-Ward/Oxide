using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGRandomEvents", "NWG Team", "3.1.0")]
    [Description("Random PvE encounters (Zombies, Animals, Bradley, Convoy) with map markers.")]
    public class NWGRandomEvents : RustPlugin
    {
        private List<BaseEntity> _activeZombies = new List<BaseEntity>();
        private List<BaseEntity> _activeEventEntities = new List<BaseEntity>();
        private List<MapMarkerGenericRadius> _activeMarkers = new List<MapMarkerGenericRadius>();
        private Timer _hordeTimer;

#region Configuration
        private class PluginConfig
        {
            public float EventInterval = 1800f; // 30 minutes
            public Dictionary<string, string> Prefabs = new Dictionary<string, string>
            {
                ["Bradley"] = "assets/prefabs/npc/m2bradley/bradleyapc.prefab",
                ["Sedan"] = "assets/content/vehicles/sedan_a/sedantest.entity.prefab",
                ["Crate"] = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab",
                ["Zombie"] = "assets/prefabs/npc/scarecrow/scarecrow.prefab",
                ["Bear"] = "assets/rust.ai/agents/bear/bear.prefab",
                ["Wolf"] = "assets/rust.ai/agents/wolf/wolf.prefab",
                ["Boar"] = "assets/rust.ai/agents/boar/boar.prefab",
                ["MarkerGeneric"] = "assets/prefabs/tools/map/genericradiusmarker.prefab"
            };
        }
        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new configuration file for NWG Random Events");
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }
        
        protected override void SaveConfig() => Config.WriteObject(_config);
#endregion

#region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Triggered"] = "<color=#b7d092>[NWG]</color> Triggered a random encounter.",
                ["HordeParams"] = "<color=#b7d092>[NWG]</color> Spawned a zombie horde.",
                ["AnimalsParams"] = "<color=#b7d092>[NWG]</color> Spawned an animal pack.",
                ["BradleyParams"] = "<color=#b7d092>[NWG]</color> Spawned a Bradley APC.",
                ["ConvoyParams"] = "<color=#b7d092>[NWG]</color> Spawned a convoy.",
                ["Usage"] = "<color=#d9534f>[NWG]</color> Usage: /event <spawn|horde|animals|bradley|convoy>",
                
                ["Msg.Bradley"] = "<color=#b7d092>[NWG] BRADLEY APC DEPLOYED!</color>\nA Bradley has been spotted near <color=#FFA500>{0}</color>!\n<color=#aaaaaa>Check your map for an orange marker.</color>",
                ["Msg.Convoy"] = "<color=#b7d092>[NWG] HIGH-VALUE CONVOY SPOTTED!</color>\nA supply convoy is moving on the roads!\n<color=#aaaaaa>Check your map for a purple marker.</color>",
                ["Msg.Animals"] = "<color=#b7d092>[NWG] WILD ANIMAL PACK!</color>\nDangerous animals have been spotted near <color=#FFA500>{0}</color>!\n<color=#aaaaaa>Check your map for a yellow marker.</color>",
                ["Msg.Horde"] = "<color=#b7d092>[NWG] ZOMBIE HORDE!</color>\nUndead have risen near <color=#FFA500>{0}</color>!\n<color=#aaaaaa>Check your map for a green marker.</color>",
                
                ["Log.Bradley"] = "[NWG] Bradley APC has been deployed to {0}",
                ["Log.Convoy"] = "[NWG] A high-value Convoy is moving on the roads!",
                ["Log.Animals"] = "[NWG] A pack of wild animals has appeared near {0}",
                ["Log.Horde"] = "[NWG] A zombie horde has spawned at {0}"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

        private void OnServerInitialized()
        {
            _hordeTimer = timer.Every(_config.EventInterval, SpawnRandomEncounter); 
        }

        private void Unload()
        {
            _hordeTimer?.Destroy();
            foreach (var z in _activeZombies)
                if (z != null && !z.IsDestroyed) z.Kill();
            _activeZombies.Clear();

            foreach (var ent in _activeEventEntities)
                if (ent != null && !ent.IsDestroyed) ent.Kill();
            _activeEventEntities.Clear();

            CleanupMarkers();
        }

        private void CleanupMarkers()
        {
            foreach (var marker in _activeMarkers)
            {
                if (marker != null && !marker.IsDestroyed) marker.Kill();
            }
            _activeMarkers.Clear();
        }

#region Map Markers
        private MapMarkerGenericRadius SpawnMapMarker(Vector3 pos, Color primary, Color secondary, float radius)
        {
            var marker = GameManager.server.CreateEntity(_config.Prefabs["MarkerGeneric"], pos) as MapMarkerGenericRadius;
            if (marker == null) return null;

            marker.alpha = 0.75f;
            marker.color1 = primary;
            marker.color2 = secondary;
            marker.radius = radius;
            marker.Spawn();
            marker.SendUpdate();
            _activeMarkers.Add(marker);
            return marker;
        }

        private void RemoveMarkerDelayed(MapMarkerGenericRadius marker, float seconds)
        {
            if (marker == null) return;
            timer.Once(seconds, () =>
            {
                if (marker != null && !marker.IsDestroyed)
                {
                    marker.Kill();
                    _activeMarkers.Remove(marker);
                }
            });
        }
#endregion

        [ChatCommand("event")]
        private void CmdEvent(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "spawn":
                        SpawnRandomEncounter();
                        SendReply(player, GetMessage("Triggered", player.UserIDString));
                        break;
                    case "horde":
                        SpawnHorde();
                        SendReply(player, GetMessage("HordeParams", player.UserIDString));
                        break;
                    case "animals":
                        SpawnAnimals();
                        SendReply(player, GetMessage("AnimalsParams", player.UserIDString));
                        break;
                    case "bradley":
                        SpawnBradley();
                        SendReply(player, GetMessage("BradleyParams", player.UserIDString));
                        break;
                    case "convoy":
                        SpawnConvoy();
                        SendReply(player, GetMessage("ConvoyParams", player.UserIDString));
                        break;
                    default:
                        SendReply(player, GetMessage("Usage", player.UserIDString));
                        break;
                }
            }
            else
            {
                SendReply(player, GetMessage("Usage", player.UserIDString));
            }
        }

        private void SpawnRandomEncounter()
        {
            int rand = UnityEngine.Random.Range(0, 4);
            switch (rand)
            {
                case 0: SpawnHorde(); break;
                case 1: SpawnAnimals(); break;
                case 2: SpawnBradley(); break;
                case 3: SpawnConvoy(); break;
            }
        }

        private void SpawnBradley()
        {
            var monument = TerrainMeta.Path.Monuments.FirstOrDefault(m => m.displayPhrase.english.Contains("Airfield") || m.displayPhrase.english.Contains("Launch Site"));
            Vector3 pos = monument != null ? monument.transform.position : Vector3.zero;
            if (pos == Vector3.zero) return;

            var bradley = GameManager.server.CreateEntity(_config.Prefabs["Bradley"], pos) as BradleyAPC;
            if (bradley == null) return;
            bradley.Spawn();
            _activeEventEntities.Add(bradley);

            // ORANGE map marker for Bradley â€” fades after 15 min
            var marker = SpawnMapMarker(pos, new Color(1f, 0.5f, 0f, 1f), new Color(0.4f, 0.15f, 0f, 0.5f), 0.15f);
            RemoveMarkerDelayed(marker, 900f);

            PrintToChat(GetMessage("Msg.Bradley", null, monument.displayPhrase.english));
            Puts(GetMessage("Log.Bradley", null, monument.displayPhrase.english));
        }

        private void SpawnConvoy()
        {
            // Create a sedan with a hackable crate attached
            var pos = TerrainMeta.Path.Roads.GetRandom()?.Path.Points.GetRandom() ?? Vector3.zero;
            if (pos == Vector3.zero) return;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1;

            var car = GameManager.server.CreateEntity(_config.Prefabs["Sedan"], pos);
            if (car == null) return;
            car.Spawn();
            _activeEventEntities.Add(car);

            // Add a locked crate near the vehicle
            var crate = GameManager.server.CreateEntity(_config.Prefabs["Crate"], pos + Vector3.up) as HackableLockedCrate;
            if (crate == null) return;
            crate.SetParent(car);
            crate.Spawn();

            // PURPLE map marker for Convoy â€” fades after 10 min
            var marker = SpawnMapMarker(pos, new Color(0.7f, 0.2f, 1f, 1f), new Color(0.3f, 0f, 0.5f, 0.5f), 0.1f);
            RemoveMarkerDelayed(marker, 600f);

            PrintToChat(GetMessage("Msg.Convoy", null));
            Puts(GetMessage("Log.Convoy", null));
        }

        private void SpawnAnimals()
        {
            string[] prefabs = { _config.Prefabs["Bear"], _config.Prefabs["Wolf"], _config.Prefabs["Boar"] };
            var monument = TerrainMeta.Path.Monuments.GetRandom();
            if (monument == null) return;

            for (int i = 0; i < 5; i++)
            {
                var prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Length)];
                var pos = monument.transform.position + new Vector3(UnityEngine.Random.Range(-30, 30), 0, UnityEngine.Random.Range(-30, 30));
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                
                var animal = GameManager.server.CreateEntity(prefab, pos);
                if (animal != null)
                {
                    animal.Spawn();
                    _activeEventEntities.Add(animal);
                }
            }

            // YELLOW map marker for Animal Pack â€” fades after 10 min
            var marker = SpawnMapMarker(monument.transform.position, new Color(1f, 0.85f, 0f, 1f), new Color(0.4f, 0.35f, 0f, 0.5f), 0.1f);
            RemoveMarkerDelayed(marker, 600f);

            PrintToChat(GetMessage("Msg.Animals", null, monument.displayPhrase.english));
            Puts(GetMessage("Log.Animals", null, monument.displayPhrase.english));
        }

        private void SpawnHorde()
        {
            // Spawn a group of zombies at a random monument
            var monument = TerrainMeta.Path.Monuments.GetRandom();
            if (monument == null) return;

            for (int i = 0; i < 10; i++)
            {
                var pos = monument.transform.position + new Vector3(UnityEngine.Random.Range(-20, 20), 0, UnityEngine.Random.Range(-20, 20));
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                SpawnZombie(pos);
            }

            // GREEN map marker for Zombie Horde â€” fades after 10 min
            var marker = SpawnMapMarker(monument.transform.position, new Color(0.2f, 0.9f, 0.2f, 1f), new Color(0f, 0.3f, 0f, 0.5f), 0.12f);
            RemoveMarkerDelayed(marker, 600f);

            PrintToChat(GetMessage("Msg.Horde", null, monument.displayPhrase.english));
            Puts(GetMessage("Log.Horde", null, monument.displayPhrase.english));
        }

        private void SpawnZombie(Vector3 pos)
        {
            // Use 'scarecrow' prefab as it acts like a zombie (murderer.prefab no longer valid)
            var zombie = GameManager.server.CreateEntity(_config.Prefabs["Zombie"], pos) as BaseCombatEntity;
            if (zombie == null) return;

            zombie.Spawn();
            zombie.health = 200f; // Buffed health
            _activeZombies.Add(zombie);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity != null && _activeZombies.Contains(entity))
            {
                _activeZombies.Remove(entity);
            }
        }
    }
}

