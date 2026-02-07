// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG Admin", "NWG Team", "3.0.0")]
    [Description("Essential Admin Tools: Radar (ESP) and Vanish.")]
    public class NWGAdmin : RustPlugin
    {
        #region Configuration
        private class PluginConfig
        {
            public bool RadarShowBox = true;
            public bool RadarShowLoot = true;
            public bool RadarShowPlayers = true;
            public bool RadarShowTc = true;
            public float RadarDistance = 150f;
            public float RadarUpdateRate = 0.5f;

            public bool VanishUnlockLocks = true;
            public bool VanishNoDamage = true;
            public bool VanishInfiniteRun = true;
        }
        private PluginConfig _config;
        #endregion

        #region State
        private readonly HashSet<ulong> _vanishedPlayers = new HashSet<ulong>();
        private readonly HashSet<ulong> _radarUsers = new HashSet<ulong>();
        private readonly HashSet<ulong> _godPlayers = new HashSet<ulong>();
        private Timer _radarTimer;
        private int _playerLayerMask;
        private const string PermUse = "nwgcore.admin";
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            _playerLayerMask = LayerMask.GetMask("Player (Server)", "Construction", "Deployed", "Loot");
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
            Puts("Creating new configuration file for NWG Admin");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            _radarTimer = timer.Every(_config.RadarUpdateRate, RadarLoop);
        }

        private void Unload()
        {
            _radarTimer?.Destroy();
            
            // Reappear everyone on unload
            foreach (var uid in _vanishedPlayers.ToList())
            {
                var p = BasePlayer.FindByID(uid);
                if (p != null) Reappear(p);
            }
        }
        #endregion

        #region Commands
        [ChatCommand("admin")]
        private void CmdAdmin(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse)) return;

            if (args.Length == 0)
            {
                player.ChatMessage("<color=#FFA500>[NWG Admin]</color> Commands:\n/vanish - Toggle Invisible\n/radar - Toggle ESP");
                return;
            }
        }

        [ChatCommand("vanish")]
        private void CmdVanish(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse)) return;

            if (_vanishedPlayers.Contains(player.userID))
                Reappear(player);
            else
                Disappear(player);
        }

        [ChatCommand("radar")]
        private void CmdRadar(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse)) return;

            if (_radarUsers.Contains(player.userID))
            {
                _radarUsers.Remove(player.userID);
                player.ChatMessage("<color=#FFA500>[NWG]</color> Radar OFF");
            }
            else
            {
                _radarUsers.Add(player.userID);
                player.ChatMessage("<color=#51CF66>[NWG]</color> Radar ON");
            }
        }

        [ChatCommand("god")]
        private void CmdGod(BasePlayer player, string msg, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse)) return;

            if (_godPlayers.Contains(player.userID))
            {
                _godPlayers.Remove(player.userID);
                player.ChatMessage("God Mode: <color=#FF6B6B>Disabled</color>");
            }
            else
            {
                _godPlayers.Add(player.userID);
                player.ChatMessage("God Mode: <color=#51CF66>Enabled</color>");
            }
        }

        #endregion

        #region Vanish Logic
        private void Disappear(BasePlayer player)
        {
            if (_vanishedPlayers.Contains(player.userID)) return;

            _vanishedPlayers.Add(player.userID);
            player.ChatMessage("Vanish: <color=#51CF66>Enabled</color>");
            
            // Network Hiding logic
            // We force the player to "leave" the network group of everyone else
            var connections = new List<Network.Connection>();
            foreach (var conn in Network.Net.sv.connections)
            {
                if (conn.player is BasePlayer p && p != player)
                    connections.Add(conn);
            }
            player.OnNetworkSubscribersLeave(connections);
            
            // Stats
            player.limitNetworking = true;
            // player.SetPlayerFlag(BasePlayer.PlayerFlags.Muted, true);
            
            Interface.CallHook("OnVanishDisappear", player);
        }

        private void Reappear(BasePlayer player)
        {
            if (!_vanishedPlayers.Contains(player.userID)) return;

            _vanishedPlayers.Remove(player.userID);
            SendReply(player, "Vanish: <color=#FF6B6B>Disabled</color>");
            
            player.limitNetworking = false;
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            
            Interface.CallHook("OnVanishReappear", player);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is BasePlayer victim && ( _godPlayers.Contains(victim.userID) || (_config.VanishNoDamage && _vanishedPlayers.Contains(victim.userID)) ))
            {
                return true; // Block damage
            }
            
            if (info?.Initiator is BasePlayer attacker && _vanishedPlayers.Contains(attacker.userID) && _config.VanishNoDamage)
            {
                return true; // Block damage DEALING while vanished
            }
            
            return null;
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (_config.VanishUnlockLocks && _vanishedPlayers.Contains(player.userID)) return true;
            return null;
        }
        #endregion

        #region Radar Logic
        private void RadarLoop()
        {
            if (_radarUsers.Count == 0) return;

            foreach (var uid in _radarUsers)
            {
                var player = BasePlayer.FindByID(uid);
                if (player == null || !player.IsConnected) continue;

                DoRadarScan(player);
            }
        }

        private void DoRadarScan(BasePlayer player)
        {
            // PRO TIP: OverlapSphereNonAlloc is better for GC, but for admin tools simple OverlapSphere is simpler code.
            // Since we are creating a Premium plugin, we use NonAlloc.
            
            var colliders = new Collider[50];
            int count = Physics.OverlapSphereNonAlloc(player.transform.position, _config.RadarDistance, colliders, _playerLayerMask);

            for (int i = 0; i < count; i++)
            {
                var col = colliders[i];
                var entity = col.ToBaseEntity();
                if (entity == null || entity == player) continue;

                Color color = Color.white;
                string label = "";

                if (entity is BasePlayer target)
                {
                    if (!_config.RadarShowPlayers) continue;
                    color = target.IsSleeping() ? Color.gray : Color.red;
                    label = target.displayName;
                }
                else if (entity is BuildingPrivlidge)
                {
                    if (!_config.RadarShowTc) continue;
                    color = Color.green;
                    label = "TC";
                }
                else if (entity is StorageContainer box)
                {
                    if (box is LootContainer)
                    {
                         if (!_config.RadarShowLoot) continue;
                         color = Color.yellow;
                    }
                    else
                    {
                        if (!_config.RadarShowBox) continue;
                        color = Color.cyan;
                    }
                }
                else
                {
                    continue; // Skip irrelevant stuff
                }

                // DDraw (Debug Draw) - Lines in the world
                // Draws a box around the entity for 0.5 sec (until next update)
                player.SendConsoleCommand("ddraw.box", _config.RadarUpdateRate + 0.1f, color, entity.transform.position, 1f);
                
                // Advanced: Draw Text (needs CUI or DDraw text if supported, DDraw text is messy in Rust)
                // We'll stick to boxes for v1 "Clean Radar"
                if (!string.IsNullOrEmpty(label))
                    player.SendConsoleCommand("ddraw.text", _config.RadarUpdateRate + 0.1f, color, entity.transform.position + Vector3.up, label);
            }
        }
        #endregion
    }
}
