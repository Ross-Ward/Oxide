using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGBaseRaid", "NWG Team", "3.0.0")]
    [Description("Key-based base raiding system.")]
    public class NWGBaseRaid : RustPlugin
    {
        private Dictionary<ulong, RaidWindow> _activeRaids = new Dictionary<ulong, RaidWindow>();

        private class RaidWindow
        {
            public float EndTime;
            public ulong RaiderId;
            public Vector3 BasePos;
        }

        #region Player Death Drop
        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            var attacker = info?.Initiator as BasePlayer;
            if (attacker == null || attacker == victim) return;
            if (victim.IsNpc || !victim.userID.IsSteamId()) return; // Prevent NPCs/Bots from dropping keys

            if (UnityEngine.Random.Range(0f, 100f) <= 25f)
            {
                // Find potential base location (nearest TC authorized)
                Vector3 basePos = victim.transform.position;
                var tc = FindPrimaryTC(victim);
                if (tc != null) basePos = tc.transform.position;

                DropRaidItems(victim.transform.position, victim.displayName, basePos);
                attacker.ChatMessage("<color=#FF6B6B>LUCKY KILL!</color> The victim dropped a Base Raid Key and Map!");
            }
        }

        private void DropRaidItems(Vector3 pos, string victimName, Vector3 basePos)
        {
            // Drop a map (Note)
            var map = ItemManager.CreateByName("note", 1);
            map.name = $"Raid Map: {victimName}'s Base";
            map.text = $"Base Location: {Math.Round(basePos.x)}, {Math.Round(basePos.z)}";
            map.Drop(pos + new Vector3(0, 1, 0), Vector3.up);

            // Drop a key (Customized item)
            var key = ItemManager.CreateByName("keycard_red", 1);
            key.name = "Base Raid Key";
            key.Drop(pos + new Vector3(0, 1.1f, 0), Vector3.up);
        }

        private BuildingPrivlidge FindPrimaryTC(BasePlayer player)
        {
            // Simple check: Find any TC where the player is authorized
            return UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>()
                .FirstOrDefault(tc => tc.authorizedPlayers.Any(p => p == player.userID));
        }
        #endregion

        #region Raid Activation
        [ChatCommand("startraid")]
        private void CmdRaidStart(BasePlayer player)
        {
            var key = player.GetActiveItem();
            if (key == null || key.info.shortname != "keycard_red" || key.name != "Base Raid Key")
            {
                player.ChatMessage("You must be holding a 'Base Raid Key' to start a raid.");
                return;
            }

            // Check if near a building
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 10f, LayerMask.GetMask("Construction", "Deployed")))
            {
                var ent = hit.GetEntity();
                var tc = ent.GetBuildingPrivilege();
                if (tc == null) { player.ChatMessage("Look at a building to start the raid."); return; }

                ulong baseId = tc.net.ID.Value;
                if (_activeRaids.ContainsKey(baseId)) { player.ChatMessage("This base is already being raided!"); return; }

                _activeRaids[baseId] = new RaidWindow {
                    EndTime = Time.realtimeSinceStartup + 1800f, // 30 minutes
                    RaiderId = player.userID,
                    BasePos = tc.transform.position
                };

                key.UseItem(1);
                player.ChatMessage("<color=#FF6B6B>RAID STARTED!</color> You have 30 minutes to breach this base.");
                Puts($"[NWG Raid Logic] Raid started on base {baseId} by {player.displayName}");
            }
        }
        #endregion

        #region Damage Control
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (!(entity is BuildingBlock) && !(entity is Door)) return null;

            var tc = entity.GetBuildingPrivilege();
            if (tc == null) return null;

            ulong baseId = tc.net.ID.Value;
            if (_activeRaids.TryGetValue(baseId, out var raid))
            {
                if (Time.realtimeSinceStartup < raid.EndTime)
                    return null; // Allow damage
                
                _activeRaids.Remove(baseId);
            }

            // If no active raid, block damage
            if (info.Initiator is BasePlayer)
            {
                return true; // Block damage
            }

            return null;
        }
        #endregion
    }
}

