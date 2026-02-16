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
        [PluginReference] private Plugin NWGCore;

        private Dictionary<Vector3, RaidWindow> _activeRaids = new Dictionary<Vector3, RaidWindow>();



        private class RaidWindow
        {
            public float EndTime;
            public ulong RaiderId;
            public Vector3 BasePos;
        }

        private const ulong RaidKeySkinID = 123456789; // TODO: Replace with actual custom skin ID or specific workshop skin
        
#region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LuckyKill"] = "<color=#b7d092>[NWG] LUCKY KILL!</color> The victim dropped a Base Raid Key and Map!",
                ["KeyRequired"] = "<color=#d9534f>[NWG]</color> You must be holding a <color=#FFA500>'Base Raid Key'</color> to start a raid.",
                ["LookAtBase"] = "<color=#d9534f>[NWG]</color> Look at a building to start the raid.",
                ["RaidActive"] = "<color=#d9534f>[NWG]</color> This base area is already being raided!",
                ["RaidStarted"] = "<color=#b7d092>[NWG] RAID STARTED!</color> You have 30 minutes to breach the base at <color=#FFA500>{0}</color>.",
                ["LogRaidStart"] = "[NWG] Raid started on base at {0} (Grid: {1}) by {2}"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

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
                attacker.ChatMessage(GetMessage("LuckyKill", attacker.UserIDString));
            }
        }

        private void DropRaidItems(Vector3 pos, string victimName, Vector3 basePos)
        {
            // Drop a map (Note)
            var map = ItemManager.CreateByName("note", 1);
            map.name = $"Raid Map: {victimName}'s Base";
            string grid = NWGCore?.Call<string>("GetGrid", basePos) ?? "???";
            map.text = $"Base Location: {grid} ({Math.Round(basePos.x)}, {Math.Round(basePos.z)})";
            map.Drop(pos + new Vector3(0, 1, 0), Vector3.up);

            // Drop a key (Customized item)
            var key = ItemManager.CreateByName("keycard_red", 1, RaidKeySkinID);
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
            // Use SkinID for robust checking
            if (key == null || key.info.shortname != "keycard_red" || key.skin != RaidKeySkinID)
            {
                player.ChatMessage(GetMessage("KeyRequired", player.UserIDString));
                return;
            }

            // Check if near a building
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 10f, LayerMask.GetMask("Construction", "Deployed")))
            {
                var ent = hit.GetEntity();
                var tc = ent.GetBuildingPrivilege();
                if (tc == null) { player.ChatMessage(GetMessage("LookAtBase", player.UserIDString)); return; }

                Vector3 basePos = tc.transform.position;
                if (_activeRaids.Keys.Any(p => Vector3.Distance(p, basePos) < 50f)) 
                { 
                    player.ChatMessage(GetMessage("RaidActive", player.UserIDString)); 
                    return; 
                }

                _activeRaids[basePos] = new RaidWindow {
                    EndTime = Time.realtimeSinceStartup + 1800f, // 30 minutes
                    RaiderId = player.userID,
                    BasePos = basePos
                };

                key.UseItem(1);
                string gridRef = NWGCore?.Call<string>("GetGrid", basePos) ?? "???";
                player.ChatMessage(GetMessage("RaidStarted", player.UserIDString, gridRef));
                Puts(GetMessage("LogRaidStart", null, basePos, gridRef, player.displayName));
            }
        }
#endregion

#region Damage Control
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (!(entity is BuildingBlock) && !(entity is Door)) return null;

            var tc = entity.GetBuildingPrivilege();
            Vector3 targetPos = tc != null ? tc.transform.position : entity.transform.position;

            foreach (var kvp in _activeRaids.ToList())
            {
                if (Vector3.Distance(kvp.Key, targetPos) < 100f) // 100m radius for raids
                {
                    if (Time.realtimeSinceStartup < kvp.Value.EndTime)
                        return null; // Allow damage
                    
                    _activeRaids.Remove(kvp.Key);
                }
            }

            // If no active raid found nearby, block damage from players
            if (info.Initiator is BasePlayer)
            {
                return true; // Block damage
            }

            return null;
        }
#endregion
    }
}

