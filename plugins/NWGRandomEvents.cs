using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG Random Events", "NWG Team", "3.0.0")]
    [Description("Random PvE encounters (Zombies, Animals, Bradley, Convoy).")]
    public class NWGRandomEvents : RustPlugin
    {
        private List<BaseEntity> _activeZombies = new List<BaseEntity>();
        private Timer _hordeTimer;

        private void OnServerInitialized()
        {
            _hordeTimer = timer.Every(1800, SpawnRandomEncounter); // Every 30 minutes
        }

        [ChatCommand("event")]
        private void CmdEvent(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length > 0 && args[0].ToLower() == "spawn")
            {
                SpawnRandomEncounter();
                player.ChatMessage("Triggered a random encounter.");
            }
            else
            {
                player.ChatMessage("Usage: /event spawn");
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

            var bradley = GameManager.server.CreateEntity("assets/prefabs/npc/m2bradley/bradleyapc.prefab", pos) as BradleyAPC;
            if (bradley == null) return;
            bradley.Spawn();
            Puts($"[NWG Random Events] Bradley APC has been deployed to {monument.displayPhrase.english}");
        }

        private void SpawnConvoy()
        {
            // Create a modular car with a crate attached
            var pos = TerrainMeta.Path.Roads.GetRandom()?.Path.Points.GetRandom() ?? Vector3.zero;
            if (pos == Vector3.zero) return;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1;

            var car = GameManager.server.CreateEntity("assets/content/vehicles/modularcar/2module_car.prefab", pos) as ModularCar;
            if (car == null) return;
            car.Spawn();

            // Add a locked crate to the back (simulated via attachment or proximity)
            var crate = GameManager.server.CreateEntity("assets/prefabs/deployable/chinookcrate/codelockedhackablecrate.prefab", pos + Vector3.up) as HackableLockedCrate;
            if (crate == null) return;
            crate.SetParent(car);
            crate.Spawn();

            Puts("[NWG Random Events] A high-value Convoy is moving on the roads!");
        }

        private void SpawnAnimals()
        {
            string[] prefabs = { "assets/rust.ai/agents/bear/bear.prefab", "assets/rust.ai/agents/wolf/wolf.prefab", "assets/rust.ai/agents/boar/boar.prefab" };
            var monument = TerrainMeta.Path.Monuments.GetRandom();
            if (monument == null) return;

            for (int i = 0; i < 5; i++)
            {
                var prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Length)];
                var pos = monument.transform.position + new Vector3(UnityEngine.Random.Range(-30, 30), 0, UnityEngine.Random.Range(-30, 30));
                pos.y = TerrainMeta.HeightMap.GetHeight(pos);
                
                var animal = GameManager.server.CreateEntity(prefab, pos);
                animal.Spawn();
            }
            Puts($"[NWG Zombies] A pack of wild animals has appeared near {monument.displayPhrase.english}");
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
            Puts($"[NWG Zombies] A horde has spawned at {monument.displayPhrase.english}");
        }

        private void SpawnZombie(Vector3 pos)
        {
            // Use 'murderer' prefab as it acts like a zombie
            var zombie = GameManager.server.CreateEntity("assets/prefabs/npc/murderer/murderer.prefab", pos) as BaseCombatEntity;
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
