using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("NWGZones", "NWG Team", "3.0.0")]
    [Description("Zone Management for NWG. PVP/PVE/Safe zones.")]
    public class NWGZones : RustPlugin
    {
#region Config
        private class PluginConfig
        {
            public bool UseSafeZoneTrigger = true;
            public float CheckInterval = 1.0f;
        }
        private PluginConfig _config;
#endregion

#region Data
        private class ZoneData
        {
            public string ID;
            public string Name;
            public SerializableVector3 Location;
            public float Radius;
            public HashSet<string> Flags = new HashSet<string>();
        }
        
        private Dictionary<string, ZoneData> _zones;
#endregion

#region Fields
        private Timer _checkTimer;
        private Dictionary<ulong, HashSet<string>> _playerZones = new Dictionary<ulong, HashSet<string>>();
#endregion

        private class SerializableVector3
        {
            public float x, y, z;
            public SerializableVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
            public Vector3 ToVector3() => new Vector3(x, y, z);
        }

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();

            try
            {
                _zones = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, ZoneData>>("NWG_Zones");
            }
            catch
            {
                _zones = new Dictionary<string, ZoneData>();
            }
            
            if (_zones == null) _zones = new Dictionary<string, ZoneData>();
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
            Puts("Creating new configuration file for NWG Zones");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            _checkTimer = timer.Every(_config.CheckInterval, CheckZones);
        }

        private void Unload()
        {
            _checkTimer?.Destroy();
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Zones", _zones);
        }
        
        private void OnServerSave()
        {
             Interface.Oxide.DataFileSystem.WriteObject("NWG_Zones", _zones);
        }
#endregion

#region Zone Logic
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["EnteredZone"] = "<color=#b7d092>Entered Zone: {0}</color>",
                ["LeftZone"] = "<color=#d9534f>Left Zone: {0}</color>",
                ["ZoneCreated"] = "Zone <color=#FFA500>{0}</color> created at your position.",
                ["FlagRemoved"] = "Flag <color=#d9534f>{0}</color> removed from {1}.",
                ["FlagAdded"] = "Flag <color=#b7d092>{0}</color> added to {1}.",
                ["ZoneRemoved"] = "Zone <color=#FFA500>{0}</color> removed.",
                ["ZoneList"] = "<color=#FFA500>ID: {0}</color>, Name: {1}, R: {2}, Flags: {3}",
                ["Usage"] = "<color=#b7d092>[NWG]</color> Usage: /zone <add|remove|list|flags> [id] [flag]"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);

        private void CheckZones()
        {
            // Optimization: Grid-based hashing or spatial partitioning would be better for high counts.
            // For now, simple optimization: sqrMagnitude checks.
            foreach(var player in BasePlayer.activePlayerList)
            {
                UpdatePlayerZones(player);
            }
        }

        private void UpdatePlayerZones(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            var pos = player.transform.position;
            var currentZones = new HashSet<string>();

            foreach(var zone in _zones.Values)
            {
                if (Vector3.Distance(pos, zone.Location.ToVector3()) <= zone.Radius)
                {
                    currentZones.Add(zone.ID);
                }
            }

            if (!_playerZones.TryGetValue(player.userID, out var oldZones))
            {
                oldZones = new HashSet<string>();
                _playerZones[player.userID] = oldZones;
            }

            foreach(var id in currentZones)
            {
                if (!oldZones.Contains(id))
                {
                    OnEnterZone(player, _zones[id]);
                }
            }

            foreach(var id in oldZones)
            {
                if (!currentZones.Contains(id))
                {
                    if (_zones.ContainsKey(id))
                        OnLeaveZone(player, _zones[id]);
                }
            }
            
            _playerZones[player.userID] = currentZones;
        }

        private void OnEnterZone(BasePlayer player, ZoneData zone)
        {
            if (zone.Flags.Contains("msg_enter"))
            {
                SendReply(player, GetMessage("EnteredZone", player.UserIDString, zone.Name));
            }
            if (zone.Flags.Contains("safe"))
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);
            }
            
            Interface.CallHook("OnEnterZone", zone.ID, player);
        }

        private void OnLeaveZone(BasePlayer player, ZoneData zone)
        {
             if (zone.Flags.Contains("msg_leave"))
            {
                SendReply(player, GetMessage("LeftZone", player.UserIDString, zone.Name));
            }
             if (zone.Flags.Contains("safe"))
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
            }
             
             Interface.CallHook("OnExitZone", zone.ID, player);
        }
#endregion

#region Flags / Hooks
        [HookMethod("HasPlayerFlag")]
        public bool HasPlayerFlag(BasePlayer player, string flag)
        {
            if (!_playerZones.TryGetValue(player.userID, out var zones)) return false;
            foreach(var id in zones)
            {
                if (_zones.TryGetValue(id, out var zone) && zone.Flags.Contains(flag)) return true;
            }
            return false;
        }
        
        private bool HasFlag(Vector3 pos, string flag)
        {
            foreach(var zone in _zones.Values)
            {
                if (Vector3.Distance(pos, zone.Location.ToVector3()) <= zone.Radius)
                {
                    if (zone.Flags.Contains(flag)) return true;
                }
            }
            return false;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is BasePlayer player)
            {
                if (HasPlayerFlag(player, "nopvp") || HasPlayerFlag(player, "god"))
                {
                    if (info.damageTypes.GetMajorityDamageType() == DamageType.Suicide) return null;
                    return false; 
                }
            }
            else
            {
                if (HasFlag(entity.transform.position, "pve") || HasFlag(entity.transform.position, "undestr"))
                {
                    return false;
                }
            }

            return null;
        }

        private object CanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            var player = plan.GetOwnerPlayer();
            if (player != null && HasFlag(player.transform.position, "nobuild"))
            {
                return false;
            }
            return null;
        }
        
        private object OnEntityDecay(BaseEntity entity, DecayEntity decayEntity)
        {
            if (HasFlag(entity.transform.position, "nodecay"))
            {
                return false;
            }
            return null;
        }

#endregion

#region Commands
        [ChatCommand("zone")]
        private void CmdZone(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (args.Length == 0)
            {
                SendReply(player, GetMessage("Usage", player.UserIDString));
                return;
            }

            string op = args[0].ToLower();
            
            if (op == "add")
            {
                string id = Guid.NewGuid().ToString().Substring(0, 8);
                if (args.Length > 1) id = args[1];
                
                _zones[id] = new ZoneData
                {
                    ID = id,
                    Name = id,
                    Location = new SerializableVector3(player.transform.position),
                    Radius = 20f
                };
                SendReply(player, GetMessage("ZoneCreated", player.UserIDString, id));
            }
            else if (op == "flags")
            {
                if (args.Length < 3) return;
                string id = args[1];
                string flag = args[2].ToLower();
                
                if (_zones.TryGetValue(id, out var zone))
                {
                    if (zone.Flags.Contains(flag))
                    {
                        zone.Flags.Remove(flag);
                        SendReply(player, GetMessage("FlagRemoved", player.UserIDString, flag, id));
                    }
                    else
                    {
                        zone.Flags.Add(flag);
                        SendReply(player, GetMessage("FlagAdded", player.UserIDString, flag, id));
                    }
                }
            }
            else if (op == "remove")
            {
                 if (args.Length < 2) return;
                 string id = args[1];
                 if (_zones.Remove(id)) SendReply(player, GetMessage("ZoneRemoved", player.UserIDString, id));
            }
            else if (op == "list")
            {
                foreach(var z in _zones.Values)
                {
                    SendReply(player, GetMessage("ZoneList", player.UserIDString, z.ID, z.Name, z.Radius, string.Join(",", z.Flags)));
                }
            }
        }
#endregion
    }
}

