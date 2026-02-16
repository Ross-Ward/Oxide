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

#region Localization
        public static class Lang
        {
            public const string Prefix = "Prefix";
            public const string TeleportCooldown = "TeleportCooldown";
            public const string TeleportBlocked = "TeleportBlocked";
            public const string TeleportPending = "TeleportPending";
            public const string TeleportComplete = "TeleportComplete";
            public const string TeleportCancelledDamage = "TeleportCancelledDamage";
            public const string SenderNotAvailable = "SenderNotAvailable";
            public const string RequestAccepted = "RequestAccepted";
            public const string RequestAcceptedTarget = "RequestAcceptedTarget";
            public const string NoPendingRequests = "NoPendingRequests";
            public const string RequestSent = "RequestSent";
            public const string RequestReceived = "RequestReceived";
            public const string PlayerNotFound = "PlayerNotFound";
            public const string UsageTpr = "UsageTpr";
            public const string HomesList = "HomesList";
            public const string HomeLimit = "HomeLimit";
            public const string HomeSet = "HomeSet";
            public const string HomeRemoved = "HomeRemoved";
            public const string HomeNotFound = "HomeNotFound";
            public const string HomeNoPrivilege = "HomeNoPrivilege";
            public const string UsageSetHome = "UsageSetHome";
            public const string WarpNotFound = "WarpNotFound";
            public const string WarpNoPermission = "WarpNoPermission";
            public const string WarpSet = "WarpSet";
            public const string UsageWarp = "UsageWarp";
            public const string UsageSetWarp = "UsageSetWarp";
            public const string UsageTp = "UsageTp";
            public const string UsageTpc = "UsageTpc";
            public const string TeleportedToCoords = "TeleportedToCoords";
            public const string TeleportedToPlayer = "TeleportedToPlayer";
            public const string InvalidCoords = "InvalidCoords";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Prefix] = "<color=#b7d092>[NWG]</color>",
                [Lang.TeleportCooldown] = "Teleport Cooldown: <color=#FFA500>{0}s</color>",
                [Lang.TeleportBlocked] = "<color=#d9534f>You cannot teleport while building blocked!</color>",
                [Lang.TeleportPending] = "Teleporting in <color=#FFA500>{0}</color> seconds...",
                [Lang.TeleportComplete] = "Teleport Complete.",
                [Lang.TeleportCancelledDamage] = "<color=#d9534f>Teleport Cancelled due to damage.</color>",
                [Lang.SenderNotAvailable] = "<color=#d9534f>Sender is no longer available.</color>",
                [Lang.RequestAccepted] = "Request Accepted.",
                [Lang.RequestAcceptedTarget] = "Request Accepted!",
                [Lang.NoPendingRequests] = "You have no pending requests.",
                [Lang.RequestSent] = "Teleport Request sent to <color=#FFA500>{0}</color>",
                [Lang.RequestReceived] = "<color=#FFA500>{0}</color> requested to teleport to you. Type <color=#b7d092>/tpa</color> to accept.",
                [Lang.PlayerNotFound] = "<color=#d9534f>Player not found.</color>",
                [Lang.UsageTpr] = "Usage: <color=#FFA500>/tpr <player></color>",
                [Lang.HomesList] = "Homes: <color=#b7d092>{0}</color>",
                [Lang.HomeLimit] = "<color=#d9534f>Home limit reached.</color>",
                [Lang.HomeSet] = "Home '<color=#FFA500>{0}</color>' set.",
                [Lang.HomeRemoved] = "Home '<color=#d9534f>{0}</color>' removed.",
                [Lang.HomeNotFound] = "<color=#d9534f>Home not found.</color>",
                [Lang.HomeNoPrivilege] = "<color=#d9534f>You must be in a building privilege zone to set a home.</color>",
                [Lang.UsageSetHome] = "Usage: <color=#FFA500>/sethome <name></color>",
                [Lang.WarpNotFound] = "<color=#d9534f>Warp point not found.</color>",
                [Lang.WarpNoPermission] = "<color=#d9534f>No permission for this warp.</color>",
                [Lang.WarpSet] = "Warp '<color=#FFA500>{0}</color>' set at your position.",
                [Lang.UsageWarp] = "Usage: <color=#FFA500>/warp <name></color>",
                [Lang.UsageSetWarp] = "Usage: <color=#FFA500>/setwarp <name> [permission]</color>",
                [Lang.UsageTp] = "Usage: <color=#FFA500>/tp <player></color> or <color=#FFA500>/tp <x> <y> <z></color>",
                [Lang.UsageTpc] = "Usage: <color=#FFA500>/tpc <x> <y> <z></color>",
                [Lang.TeleportedToCoords] = "Teleported to coordinates: <color=#b7d092>{0}, {1}, {2}</color>",
                [Lang.TeleportedToPlayer] = "Teleported to <color=#b7d092>{0}</color>",
                [Lang.InvalidCoords] = "<color=#d9534f>Invalid coordinates.</color>"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

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
        
        private void Msg(BasePlayer player, string key, params object[] args)
        {
            string message = GetMessage(key, player.UserIDString, args);
            player.ChatMessage($"{GetMessage(Lang.Prefix, player.UserIDString)} {message}");
        }


#endregion

#region Teleport Logic
        private void StartTeleport(BasePlayer player, Vector3 targetPos, BasePlayer targetPlayer = null)
        {
            var data = GetData(player.userID);
            float timeLeft = (data.LastTeleportTime + _config.TeleportCooldown) - Time.realtimeSinceStartup;
            
            if (!player.IsAdmin && timeLeft > 0)
            {
                Msg(player, Lang.TeleportCooldown, Mathf.CeilToInt(timeLeft));
                return;
            }

            if (!player.IsAdmin && player.IsBuildingBlocked())
            {
                Msg(player, Lang.TeleportBlocked);
                return;
            }

            Msg(player, Lang.TeleportPending, _config.TeleportTimer);

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
                
            Msg(player, Lang.TeleportComplete);
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
                Msg(player, Lang.TeleportCancelledDamage);
            }
        }
#endregion

#region Commands: CPR/TPA
        [ChatCommand("tpr")]
        private void CmdTpr(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                Msg(player, Lang.UsageTpr);
                return;
            }
            
            var target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                Msg(player, Lang.PlayerNotFound);
                return;
            }
            
            if (target == player) return;

            _pendingRequests[target.userID] = player.userID;
            Msg(player, Lang.RequestSent, target.displayName);
            Msg(target, Lang.RequestReceived, player.displayName);
        }

        [ChatCommand("tpa")]
        private void CmdTpa(BasePlayer player, string command, string[] args)
        {
            if (!_pendingRequests.TryGetValue(player.userID, out var senderId))
            {
                Msg(player, Lang.NoPendingRequests);
                return;
            }
            
            var sender = BasePlayer.Find(senderId.ToString());
            _pendingRequests.Remove(player.userID);
            
            if (sender == null || !sender.IsConnected)
            {
                Msg(player, Lang.SenderNotAvailable);
                return;
            }
            
            Msg(player, Lang.RequestAccepted);
            Msg(sender, Lang.RequestAcceptedTarget);
            
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
                Msg(player, Lang.HomesList, string.Join(", ", data.Homes.Keys));
                return;
            }
            
            string homeName = args[0].ToLower();
            
            if (args.Length >= 2 && args[0] == "add")
            {
                string newName = args[1].ToLower();
                int limit = player.IsAdmin ? 100 : _config.HomeLimit; 
                
                if (data.Homes.Count >= limit)
                {
                    Msg(player, Lang.HomeLimit);
                    return;
                }
                
                if (player.GetBuildingPrivilege() == null && !player.IsAdmin)
                {
                    Msg(player, Lang.HomeNoPrivilege);
                    return;
                }

                data.Homes[newName] = new SerializableVector3(player.transform.position);
                Msg(player, Lang.HomeSet, newName);
                return;
            }
            
            if (args.Length >= 2 && args[0] == "remove")
            {
                string oldName = args[1].ToLower();
                if (data.Homes.Remove(oldName))
                {
                    Msg(player, Lang.HomeRemoved, oldName);
                }
                else
                {
                    Msg(player, Lang.HomeNotFound);
                }
                return;
            }

            if (data.Homes.TryGetValue(homeName, out var pos))
            {
                StartTeleport(player, pos.ToVector3());
            }
            else
            {
                Msg(player, Lang.HomeNotFound);
            }
        }
        
        [ChatCommand("sethome")]
        private void CmdSetHome(BasePlayer player, string command, string[] args)
        {
             if (args.Length == 0) { Msg(player, Lang.UsageSetHome); return; }
             CmdHome(player, "home", new string[] { "add", args[0] });
        }
#endregion

#region Commands: Warps
        [ChatCommand("warp")]
        private void CmdWarp(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { Msg(player, Lang.UsageWarp); return; }
            
            string warpName = args[0].ToLower();
            if (!_config.Warps.TryGetValue(warpName, out var wp))
            {
                Msg(player, Lang.WarpNotFound);
                return;
            }

            if (!string.IsNullOrEmpty(wp.Permission) && !permission.UserHasPermission(player.UserIDString, wp.Permission))
            {
                Msg(player, Lang.WarpNoPermission);
                return;
            }

            StartTeleport(player, new Vector3(wp.x, wp.y, wp.z));
        }

        [ChatCommand("setwarp")]
        private void CmdSetWarp(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length < 1) { Msg(player, Lang.UsageSetWarp); return; }
            
            string name = args[0].ToLower();
            string perm = args.Length > 1 ? args[1] : "";
            
            _config.Warps[name] = new WarpPoint { x = player.transform.position.x, y = player.transform.position.y, z = player.transform.position.z, Permission = perm };
            SaveConfig();
            Msg(player, Lang.WarpSet, name);
        }
#endregion

#region Commands: Admin
        [ChatCommand("tp")]
        private void CmdTp(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            
            if (args.Length == 0)
            {
                Msg(player, Lang.UsageTp);
                return;
            }
            
            if (args.Length >= 3)
            {
                if (float.TryParse(args[0], out float x) && float.TryParse(args[1], out float y) && float.TryParse(args[2], out float z))
                {
                    DoTeleport(player, new Vector3(x, y, z));
                    Msg(player, Lang.TeleportedToCoords, x, y, z);
                    return;
                }
            }

            var target = BasePlayer.Find(args[0]);
            if (target != null)
            {
                DoTeleport(player, target.transform.position);
                Msg(player, Lang.TeleportedToPlayer, target.displayName);
                return;
            }
            
            Msg(player, Lang.PlayerNotFound);
        }

        [ChatCommand("tpc")]
        private void CmdTpc(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length < 3)
            {
                Msg(player, Lang.UsageTpc);
                return;
            }

            if (float.TryParse(args[0], out float x) && float.TryParse(args[1], out float y) && float.TryParse(args[2], out float z))
            {
                DoTeleport(player, new Vector3(x, y, z));
                Msg(player, Lang.TeleportedToCoords, x, y, z);
            }
            else
            {
                Msg(player, Lang.InvalidCoords);
            }
        }
#endregion
    }
}

