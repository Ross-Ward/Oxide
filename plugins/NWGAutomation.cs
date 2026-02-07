using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG Automation", "NWG Team", "3.0.0")]
    [Description("Automated Base Management: Doors, Locks, Codes, and Authorization.")]
    public class NWGAutomation : RustPlugin
    {
        #region References
        [PluginReference] private Plugin Clans; 
        #endregion

        #region Configuration
        private class PluginConfig
        {
            public bool EnableAutoDoor = true;
            public float AutoDoorDelay = 5.0f;
            public bool EnableAutoCode = true;
            public bool EnableAutoLock = true; // Auto deploy lock
            public bool EnableAutoAuth = true; // Auto authorize team/clan
            
            public bool ShareWithTeam = true;
            public bool ShareWithClan = true;
        }
        private PluginConfig _config;
        #endregion

        #region Data
        // Player preferences
        private class PlayerData
        {
            public string Code = "";
            public bool AutoDoorEnabled = true;
            public float AutoDoorDelayOverride = -1f;
        }
        private Dictionary<ulong, PlayerData> _data;
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            
            try 
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>("NWG_Automation");
            } 
            catch { _data = new Dictionary<ulong, PlayerData>(); }

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
            Puts("Creating new configuration file for NWG Automation");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerSave()
        {
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Automation", _data);
        }
        #endregion

        #region Auto Door Logic
        // Dictionary to track open doors for closing
        private Dictionary<ulong, Timer> _doorTimers = new Dictionary<ulong, Timer>();

        private void OnDoorOpened(Door door, BasePlayer player)
        {
            if (!_config.EnableAutoDoor) return;
            if (door == null || player == null) return;
            
            // Check player pref
            var delay = _config.AutoDoorDelay;
            if (_data.TryGetValue(player.userID, out var prefs))
            {
                if (!prefs.AutoDoorEnabled) return;
                if (prefs.AutoDoorDelayOverride > 0) delay = prefs.AutoDoorDelayOverride;
            }

            // Schedule Close
            if (_doorTimers.ContainsKey(door.net.ID.Value))
            {
                _doorTimers[door.net.ID.Value].Destroy();
                _doorTimers.Remove(door.net.ID.Value);
            }

            _doorTimers[door.net.ID.Value] = timer.Once(delay, () => CloseDoor(door));
        }

        private void OnDoorClosed(Door door, BasePlayer player)
        {
            if (door == null) return;
            if (_doorTimers.ContainsKey(door.net.ID.Value))
            {
                _doorTimers[door.net.ID.Value].Destroy();
                _doorTimers.Remove(door.net.ID.Value);
            }
        }

        private void CloseDoor(Door door)
        {
            if (door == null || door.IsDestroyed || !door.IsOpen()) return;
            
            // Check for players in doorway? (Optional complexity, skipping for perf/simplicity)
            door.SetFlag(BaseEntity.Flags.Open, false);
            door.SendNetworkUpdateImmediate();
        }
        #endregion

        #region Auto Lock & Code Logic
        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var entity = go.ToBaseEntity();
            if (entity == null) return;
            var player = planner?.GetOwnerPlayer();
            if (player == null) return;

            // 1. Auto Door Lock Deployment
            if (_config.EnableAutoLock && entity is Door door && door.GetSlot(BaseEntity.Slot.Lock) == null)
            {
                // Try to deploy a lock if player has one
                // Check inventory for lock
                var lockItem = player.inventory.FindItemByItemID(ItemManager.FindItemDefinition("lock.code").itemid);
                if (lockItem != null)
                {
                    // Deploy lock logic is complex to simulate perfectly without "CanBuild" checks
                    // Simplification: Wait for user to place lock, OR just handle code setting.
                    // Most "AutoLock" plugins actually CONSUME a lock from inventory and spawn it.
                    
                    // We will focus on AutoCODE first (when lock is placed). AutoDeployLock is risky without precise checks.
                }
            }
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity entity, BaseEntity instance)
        {
            // Called when an item is deployed (e.g. Code Lock)
            if (entity is CodeLock codeLock && _config.EnableAutoCode)
            {
                var player = deployer.GetOwnerPlayer();
                if (player == null) return;

                if (_data.TryGetValue(player.userID, out var prefs) && !string.IsNullOrEmpty(prefs.Code))
                {
                    // Set Code
                    codeLock.code = prefs.Code;
                    codeLock.whitelistPlayers.Add(player.userID);
                    codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                    
                    // Auto Auth Friends/Teams
                    if (_config.EnableAutoAuth)
                    {
                        TryAuthorizeOthers(codeLock, player);
                    }
                    
                    SendReply(player, $"<color=#aaffaa>AutoCode</color>: Lock set to {prefs.Code}");
                }
            }
        }
        #endregion

        #region Auto Authorization Logic
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            // Auto Auth for Cupboards / Turrets
            if (_config.EnableAutoAuth)
            {
                if (entity is BuildingPrivlidge priv)
                {
                     // Get owner
                     var ownerId = priv.OwnerID;
                     if (ownerId == 0) return;
                     // We can't easily get the player instance here if they rely on "OnEntityBuilt" context, 
                     // but we have OwnerID. 
                     
                     // We need to look up owner's friends/team.
                     NextTick(() => {
                         if (priv == null || priv.IsDestroyed) return;
                         AuthorizeTeam(priv, ownerId);
                     });
                }
                else if (entity is AutoTurret turret)
                {
                    // Same for turrets
                    var ownerId = turret.OwnerID;
                    if (ownerId == 0) return;
                    
                    NextTick(() => {
                        if (turret == null || turret.IsDestroyed) return;
                        AuthorizeTeam(turret, ownerId);
                    });
                }
            }
        }

        private void AuthorizeTeam(BaseEntity entity, ulong ownerId)
        {
            List<ulong> toAuth = new List<ulong>();
            
            // 1. Team
            if (_config.ShareWithTeam && RelationshipManager.ServerInstance != null)
            {
                var team = RelationshipManager.ServerInstance.FindPlayersTeam(ownerId);
                if (team != null)
                {
                    toAuth.AddRange(team.members);
                }
            }

            // 2. Clan
            if (_config.ShareWithClan && Clans != null)
            {
                 // Call Clans API
                 // var clanMembers = Clans.Call("GetClanMembers", ownerId) as List<string>;
                 // Simplified: If user uses Clans, we assume they have the plugin.
            }
            
            // Apply
            if (entity is BuildingPrivlidge priv)
            {
                foreach(var id in toAuth) 
                {
                    priv.authorizedPlayers.Add(id);
                }
                priv.SendNetworkUpdate();
            }
            else if (entity is AutoTurret turret)
            {
                foreach(var id in toAuth) 
                {
                    turret.authorizedPlayers.Add(id);
                }
                turret.SendNetworkUpdate();
            }
            else if (entity is CodeLock codeLock)
            {
                 foreach(var id in toAuth) 
                 {
                     if (!codeLock.whitelistPlayers.Contains(id))
                         codeLock.whitelistPlayers.Add(id);
                 }
                 codeLock.SendNetworkUpdate();
            }
        }
        
        private void TryAuthorizeOthers(CodeLock codeLock, BasePlayer owner)
        {
            AuthorizeTeam(codeLock, owner.userID);
        }
        #endregion

        #region Commands
        [ChatCommand("autocode")]
        private void CmdAutoCode(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /autocode <code>");
                return;
            }
            
            string code = args[0];
            if (code.Length != 4 || !int.TryParse(code, out _))
            {
                player.ChatMessage("Code must be 4 digits.");
                return;
            }

            if (!_data.ContainsKey(player.userID)) _data[player.userID] = new PlayerData();
            _data[player.userID].Code = code;
            player.ChatMessage($"AutoCode set to: {code}");
        }
        #endregion
    }
}
