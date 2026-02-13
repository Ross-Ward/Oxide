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
    [Info("NWGTransportation", "NWG Team", "3.0.0")]
    [Description("Teleportation System: Homes, TPR/TPA, Warps, and Admin TP")]
    public class NWGTransportation : RustPlugin
    {
        #region References
        [PluginReference] private Plugin Clans;
        #endregion

        #region Configuration
        private class PluginConfig
        {
            public float TeleportTimer = 15f; // Seconds to wait before TP
            public float TeleportCooldown = 600f; // Seconds between uses
            public int HomeLimit = 3;
            public int VipHomeLimit = 5;
            public bool InterruptOnDamage = true;
            public bool AllowIceberg = true;
            public bool AllowCaves = false;
            public Dictionary<string, WarpPoint> Warps = new Dictionary<string, WarpPoint>();
        }

        private class WarpPoint
        {
            public float x, y, z;
            public string Permission;
        }
        private PluginConfig _config;
        #endregion

        #region Data
        private class PlayerData
        {
            public Dictionary<string, SerializableVector3> Homes = new Dictionary<string, SerializableVector3>();
            public float LastTeleportTime = 0f;
        }
        
        private Dictionary<ulong, PlayerData> _data;
        
        private Dictionary<ulong, Timer> _pendingTeleports = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, ulong> _pendingRequests = new Dictionary<ulong, ulong>(); // Receiver -> Sender
        #endregion

        #region Serializable Vector3
        private class SerializableVector3
        {
            public float x, y, z;
            public SerializableVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
            public Vector3 ToVector3() => new Vector3(x, y, z);
        }
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            
            try 
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>("NWG_Transportation");
            }
            catch
            {
                _data = new Dictionary<ulong, PlayerData>();
            }

            if (_data == null) _data = new Dictionary<ulong, PlayerData>();
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
            Puts("Creating new configuration file for NWG Transportation");
            _config = new PluginConfig();
            _config.Warps["outpost"] = new WarpPoint { x = 0, y = 0, z = 0 }; // Placeholder
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            if (_config == null || _config.Warps == null) return;

            // Auto-detect Outpost logic
            bool updateNeeded = false;
            if (!_config.Warps.ContainsKey("outpost"))
            {
                _config.Warps["outpost"] = new WarpPoint(); // Create if missing
                updateNeeded = true;
            }

            var wp = _config.Warps["outpost"];
            if (wp.x == 0 && wp.y == 0 && wp.z == 0)
            {
                Puts("[NWG Transportation] Searching for Outpost...");
                    
                    // Debug all monuments
                    // foreach (var m in TerrainMeta.Path.Monuments) Puts($"Monument: {m.displayPhrase.english} | {m.name}");

                    var outpost = TerrainMeta.Path.Monuments.FirstOrDefault(m => 
                        (m.displayPhrase.english != null && m.displayPhrase.english.Contains("Outpost")) || 
                        (m.name != null && (m.name.Contains("compound") || m.name.Contains("outpost"))));

                    if (outpost != null)
                    {
                        var pos = outpost.transform.position;
                        wp.x = pos.x; 
                        wp.y = pos.y + 10; // Be safe
                        wp.z = pos.z;
                        
                        Puts($"[NWG Transportation] FOUND OUTPOST at {pos}. Warp updated.");
                        SaveConfig();
                    }
                    else
                    {
                        Puts("[NWG Transportation] CRITICAL: Could not find Outpost monument! Please set manually with /setwarp outpost.");
                    }
            }
        }

        private void Unload()
        {
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Transportation", _data);
            foreach(var timer in _pendingTeleports.Values) timer?.Destroy();
        }

        private void OnServerSave()
        {
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Transportation", _data);
        }
        #endregion

        #region Helpers
        private PlayerData GetData(ulong playerId)
        {
            if (!_data.TryGetValue(playerId, out var data))
            {
                data = new PlayerData();
                _data[playerId] = data;
            }
            return data;
        }
        
        private void Msg(BasePlayer player, string msg) => player.ChatMessage($"<color=#00ccff>[TP]</color> {msg}");

        private string GetGrid(Vector3 pos)
        {
            float size = TerrainMeta.Size.x;
            float offset = size / 2;
            
            int x = Mathf.FloorToInt((pos.x + offset) / 146.3f);
            int z = Mathf.FloorToInt((size - (pos.z + offset)) / 146.3f);
            
            string letters = "";
            while (x >= 0)
            {
                letters = (char)('A' + (x % 26)) + letters;
                x = (x / 26) - 1;
            }
            
            return $"{letters}{z}";
        }
        #endregion

        #region Teleport Logic
        private void StartTeleport(BasePlayer player, Vector3 targetPos, BasePlayer targetPlayer = null)
        {
            var data = GetData(player.userID);
            float timeLeft = (data.LastTeleportTime + _config.TeleportCooldown) - Time.realtimeSinceStartup;
            
            if (!player.IsAdmin && timeLeft > 0)
            {
                Msg(player, $"Teleport Cooldown: {Mathf.CeilToInt(timeLeft)}s");
                return;
            }

            if (!player.IsAdmin && player.IsBuildingBlocked())
            {
                Msg(player, "You cannot teleport while building blocked!");
                return;
            }

            Msg(player, $"Teleporting in {_config.TeleportTimer} seconds...");

            if (_pendingTeleports.ContainsKey(player.userID))
            {
                _pendingTeleports[player.userID].Destroy();
                _pendingTeleports.Remove(player.userID);
            }

            _pendingTeleports[player.userID] = timer.Once(_config.TeleportTimer, () =>
            {
                _pendingTeleports.Remove(player.userID);
                
                if (player == null || !player.IsConnected || player.IsDead()) return;
                
                data.LastTeleportTime = Time.realtimeSinceStartup;
                
                if (targetPlayer != null && targetPlayer.IsConnected)
                {
                    DoTeleport(player, targetPlayer.transform.position);
                }
                else
                {
                    DoTeleport(player, targetPos);
                }
            });
        }

        private void DoTeleport(BasePlayer player, Vector3 pos)
        {
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "StartLoading");
            
            StartSleeping(player);
            player.MovePosition(pos);
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "ForcePositionTo", pos);
            
            if (player.net?.connection != null)
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            
            if (player.net?.connection != null)
                player.ClientRPCPlayer(null, player, "FinishedLoading");
                
            Msg(player, "Teleport Complete.");
        }

        private void StartSleeping(BasePlayer player)
        {
            if (player.IsSleeping()) return;
            player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
            if (!BasePlayer.sleepingPlayerList.Contains(player)) BasePlayer.sleepingPlayerList.Add(player);
            player.CancelInvoke("InventoryCheck");
        }
        #endregion

        #region Hooks: Interrupt
        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!_config.InterruptOnDamage) return;
            
            var player = entity as BasePlayer;
            if (player == null) return;
            
            if (_pendingTeleports.ContainsKey(player.userID))
            {
                _pendingTeleports[player.userID].Destroy();
                _pendingTeleports.Remove(player.userID);
                Msg(player, "Teleport Cancelled due to damage.");
            }
        }
        #endregion

        #region Commands: CPR/TPA
        [ChatCommand("tpr")]
        private void CmdTpr(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                Msg(player, "Usage: /tpr <player>");
                return;
            }
            
            var target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                Msg(player, "Player not found.");
                return;
            }
            
            if (target == player) return;

            _pendingRequests[target.userID] = player.userID;
            Msg(player, $"Teleport Request sent to {target.displayName}");
            Msg(target, $"<color=orange>{player.displayName}</color> requested to teleport to you. Type <color=green>/tpa</color> to accept.");
        }

        [ChatCommand("tpa")]
        private void CmdTpa(BasePlayer player, string command, string[] args)
        {
            if (!_pendingRequests.TryGetValue(player.userID, out var senderId))
            {
                Msg(player, "You have no pending requests.");
                return;
            }
            
            var sender = BasePlayer.Find(senderId.ToString());
            _pendingRequests.Remove(player.userID);
            
            if (sender == null || !sender.IsConnected)
            {
                Msg(player, "Sender is no longer available.");
                return;
            }
            
            Msg(player, "Request Accepted.");
            Msg(sender, "Request Accepted!");
            
            StartTeleport(sender, Vector3.zero, player);
        }
        #endregion

        #region Commands: Home
        [ChatCommand("home")]
        private void CmdHome(BasePlayer player, string command, string[] args)
        {
            var data = GetData(player.userID);

            if (args.Length == 0)
            {
                Msg(player, "Homes: " + string.Join(", ", data.Homes.Keys));
                return;
            }
            
            string homeName = args[0].ToLower();
            
            if (args.Length >= 2 && args[0] == "add")
            {
                string newName = args[1].ToLower();
                int limit = player.IsAdmin ? 100 : _config.HomeLimit; 
                
                if (data.Homes.Count >= limit)
                {
                    Msg(player, "Home limit reached.");
                    return;
                }
                
                if (player.GetBuildingPrivilege() == null && !player.IsAdmin)
                {
                    Msg(player, "You must be in a building privilege zone to set a home.");
                    return;
                }

                data.Homes[newName] = new SerializableVector3(player.transform.position);
                Msg(player, $"Home '{newName}' set.");
                return;
            }
            
            if (args.Length >= 2 && args[0] == "remove")
            {
                string oldName = args[1].ToLower();
                if (data.Homes.Remove(oldName))
                {
                    Msg(player, $"Home '{oldName}' removed.");
                }
                else
                {
                    Msg(player, "Home not found.");
                }
                return;
            }

            if (data.Homes.TryGetValue(homeName, out var pos))
            {
                StartTeleport(player, pos.ToVector3());
            }
            else
            {
                Msg(player, "Home not found.");
            }
        }
        
        [ChatCommand("sethome")]
        private void CmdSetHome(BasePlayer player, string command, string[] args)
        {
             if (args.Length == 0) { Msg(player, "Usage: /sethome <name>"); return; }
             CmdHome(player, "home", new string[] { "add", args[0] });
        }
        #endregion

        #region Commands: Warps
        [ChatCommand("warp")]
        private void CmdWarp(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { Msg(player, "Usage: /warp <name>"); return; }
            
            string warpName = args[0].ToLower();
            if (!_config.Warps.TryGetValue(warpName, out var wp))
            {
                Msg(player, "Warp point not found.");
                return;
            }

            if (!string.IsNullOrEmpty(wp.Permission) && !permission.UserHasPermission(player.UserIDString, wp.Permission))
            {
                Msg(player, "No permission for this warp.");
                return;
            }

            StartTeleport(player, new Vector3(wp.x, wp.y, wp.z));
        }

        [ChatCommand("setwarp")]
        private void CmdSetWarp(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length < 1) { Msg(player, "Usage: /setwarp <name> [permission]"); return; }
            
            string name = args[0].ToLower();
            string perm = args.Length > 1 ? args[1] : "";
            
            _config.Warps[name] = new WarpPoint { x = player.transform.position.x, y = player.transform.position.y, z = player.transform.position.z, Permission = perm };
            SaveConfig();
            Msg(player, $"Warp '{name}' set at your position.");
        }
        #endregion

        #region Commands: Admin
        [ChatCommand("tp")]
        private void CmdTp(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            
            if (args.Length == 0)
            {
                Msg(player, "Usage: /tp <player> or /tp <x> <y> <z>");
                return;
            }
            
            if (args.Length >= 3)
            {
                if (float.TryParse(args[0], out float x) && float.TryParse(args[1], out float y) && float.TryParse(args[2], out float z))
                {
                    DoTeleport(player, new Vector3(x, y, z));
                    Msg(player, $"Teleported to coordinates: {x}, {y}, {z}");
                    return;
                }
            }

            var target = BasePlayer.Find(args[0]);
            if (target != null)
            {
                DoTeleport(player, target.transform.position);
                Msg(player, $"Teleported to {target.displayName}");
                return;
            }
            
            Msg(player, "Player not found or invalid coordinates.");
        }

        [ChatCommand("tpc")]
        private void CmdTpc(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length < 3)
            {
                Msg(player, "Usage: /tpc <x> <y> <z>");
                return;
            }

            if (float.TryParse(args[0], out float x) && float.TryParse(args[1], out float y) && float.TryParse(args[2], out float z))
            {
                DoTeleport(player, new Vector3(x, y, z));
                Msg(player, $"Teleported to {x}, {y}, {z}");
            }
            else
            {
                Msg(player, "Invalid coordinates.");
            }
        }
        #endregion
    }
}

