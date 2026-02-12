using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("NWGCombat", "NWG Team", "1.0.0")]
    [Description("Unified Damage Control and PVP System.")]
    public class NWGCombat : RustPlugin
    {
        #region References
        [PluginReference] private Plugin NWGZones;
        #endregion

        #region Config
        private class PluginConfig
        {
            public bool GlobalPVE = true;
            public bool AllowRaiding = false; // If true, building damage allowed even in PVE
            public bool UsePVPIndicator = true;
            public string PVPIndicatorUrl = "https://i.imgur.com/JWrsJqI.jpg";
        }

        private PluginConfig _config;
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
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
            Puts("Creating new configuration file for NWG Combat");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "NWG_Combat_PVP");
            }
        }
        #endregion

        #region Damage Logic
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (!_config.GlobalPVE) return null; // Logic is purely for PVE enforcement

            var attacker = info.InitiatorPlayer;
            var victimPlayer = entity as BasePlayer;

            // Admin bypass
            if (attacker != null && (attacker.IsAdmin || permission.UserHasPermission(attacker.UserIDString, "nwgcore.admin"))) return null;

            // Environment damage is always fine
            if (attacker == null && info.Initiator == null) return null; 

            // Check for PVP Zones
            bool attackerInPvp = attacker != null && IsInPvpZone(attacker.userID);
            bool victimInPvp = victimPlayer != null && IsInPvpZone(victimPlayer.userID);

            // PVP Logic
            if (attacker != null && victimPlayer != null)
            {
                // If both in PVP zone, allow
                if (attackerInPvp && victimInPvp) return null;
                
                // Otherwise, block
                return false; 
            }

            // Neutral/World Object Logic (Barrels, Road Loot, etc.)
            // If it has no owner, it's a world object and should be breakable.
            if (entity.OwnerID == 0) return null;

            // Raiding Logic (Player vs Building)
            if (attacker != null && !(entity is BasePlayer) && !(entity is BaseNpc))
            {
                // If raiding disabled, block damage to buildings unless owner
                if (!_config.AllowRaiding)
                {
                    // Allow if authorized
                    if (entity.OwnerID == attacker.userID) return null;
                    if (IsAuthed(entity, attacker)) return null;

                    return false;
                }
            }

            return null;
        }

        private bool IsAuthed(BaseEntity entity, BasePlayer player)
        {
            // Simple check for TC auth or Lock auth would go here
            // For now assuming stricter PVE
            return false;
        }

        private bool IsInPvpZone(ulong playerId)
        {
            if (NWGZones == null) return false;
            // Assuming NWG_Zones checks flags. 
            // Since NWG_Zones handles "HasFlag", we'd need to know the zone logic.
            // Or simpler: Check if NWG_Zones returns "PVP" for this player.
            // Let's assume NWG_Zones exposes a hook or method.
            // Hook: IsPlayerInZone(string flag, ulong id)
            
            var result = NWGZones.Call("HasPlayerFlag", playerId, "PVP");
            if (result is bool b) return b;
            return false;
        }
        #endregion

        #region UI
        [HookMethod("OnEnterPvpZone")]
        public void OnEnterPvpZone(BasePlayer player)
        {
            if (_config.UsePVPIndicator)
            {
                CreatePVPUI(player);
            }
        }

        [HookMethod("OnExitPvpZone")]
        public void OnExitPvpZone(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "NWG_Combat_PVP");
        }

        private void CreatePVPUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "NWG_Combat_PVP");
            var elements = new CuiElementContainer();
            elements.Add(new CuiElement
            {
                Name = "NWG_Combat_PVP",
                Components =
                {
                    new CuiRawImageComponent { Url = _config.PVPIndicatorUrl, Color = "1 1 1 0.8" },
                    new CuiRectTransformComponent { AnchorMin = "0.01 0.01", AnchorMax = "0.06 0.06" }
                }
            });
            CuiHelper.AddUi(player, elements);
        }
        #endregion
    }
}

