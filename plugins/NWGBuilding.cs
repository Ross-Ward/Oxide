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
    [Info("NWGBuilding", "NWG Team", "3.1.0")]
    [Description("Building Utilities: /remove tool with hammer hit detection and refunds.")]
    public class NWGBuilding : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public float RefundRate = 0.5f; // 50% refund
            public float RemoveTime = 30f; // 30 seconds to remove
            public bool UseToolCupboard = true;
            public bool UseEntityOwner = true;
        }
        private PluginConfig _config;

        private void LoadConfigVariables()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) LoadDefaultConfig();
            }
            catch { LoadDefaultConfig(); }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region State
        private HashSet<ulong> _removerMode = new HashSet<ulong>();
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
        }

        private void Unload()
        {
            _removerMode.Clear();
        }
        #endregion

        #region Commands
        [ChatCommand("remove")]
        private void CmdRemove(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (_removerMode.Contains(player.userID))
            {
                _removerMode.Remove(player.userID);
                player.ChatMessage("<color=#ffaa00>Remover Tool Disabled.</color>");
            }
            else
            {
                _removerMode.Add(player.userID);
                player.ChatMessage($"<color=#aaffaa>Remover Tool Enabled for {_config.RemoveTime}s.</color>\nHit entities with your <color=#ffcc00>Hammer</color> to remove them.");
                
                // Auto-disable timer
                timer.Once(_config.RemoveTime, () => 
                {
                    if (_removerMode.Contains(player.userID))
                    {
                        _removerMode.Remove(player.userID);
                        if (player.IsConnected)
                            player.ChatMessage("<color=#ffaa00>Remover Tool Timed Out.</color>");
                    }
                });
            }
        }
        #endregion

        #region Hooks
        // OnHammerHit fires when player hits something with a hammer
        // This is the correct hook — OnPlayerAttack does NOT fire for hammer hits
        private void OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (player == null || info?.HitEntity == null) return;
            if (!_removerMode.Contains(player.userID)) return;

            TryRemove(player, info.HitEntity);
        }
        #endregion

        #region Removal Logic
        private void TryRemove(BasePlayer player, BaseEntity target)
        {
            if (!CanRemove(player, target)) 
            {
                player.ChatMessage("<color=red>You are not allowed to remove this.</color>");
                return;
            }
            
            string entityName = target.ShortPrefabName ?? "Entity";

            // Refund
            Refund(player, target);
            
            // Kill the entity
            NextTick(() =>
            {
                if (target == null || target.IsDestroyed) return;
                target.Kill(BaseNetworkable.DestroyMode.Gib);
                player.ChatMessage($"<color=#aaffaa>Removed:</color> {entityName}");
            });
        }

        private bool CanRemove(BasePlayer player, BaseEntity target)
        {
            if (player.IsAdmin) return true;

            // Tool Cupboard auth check
            if (_config.UseToolCupboard)
            {
                if (!player.CanBuild()) return false;
            }

            // Entity Owner check
            if (_config.UseEntityOwner)
            {
                if (target.OwnerID != 0 && target.OwnerID != player.userID)
                {
                    // Allow team members
                    var team = RelationshipManager.ServerInstance?.FindPlayersTeam(player.userID);
                    if (team == null || !team.members.Contains(target.OwnerID))
                        return false;
                }
            }

            return true;
        }

        private void Refund(BasePlayer player, BaseEntity target)
        {
            if (_config.RefundRate <= 0.0f) return;
            
            if (target is BuildingBlock block)
            {
                var grade = block.grade;
                var gradeDef = block.blockDefinition?.GetGrade(grade, 0);
                if (gradeDef != null && gradeDef.CostToBuild() != null)
                {
                    foreach (var cost in gradeDef.CostToBuild())
                    {
                        int amount = Mathf.FloorToInt(cost.amount * _config.RefundRate);
                        if (amount > 0)
                        {
                            var item = ItemManager.CreateByItemID(cost.itemDef.itemid, amount);
                            if (item != null)
                                player.GiveItem(item);
                        }
                    }
                }
            }
            // For deployables, we skip detailed refund in this version
        }
        #endregion
    }
}

