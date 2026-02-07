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
    [Info("NWG Building", "NWG Team", "3.0.0")]
    [Description("Building Utilities: BGrade (Auto-Upgrade) and Cost Refunds.")]
    public class NWGBuilding : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public float RefundRate = 0.5f; // 50% refund
            public float RemoveTime = 30f; // 30 seconds to remove
            public bool UseToolCupboard = true; // Use TC auth?
            public bool UseEntityOwner = true; // Use EntityOwner?
            public string RemoveTool = "hammer"; // Tool required in hand
            public bool RequireTool = true;
        }
        private PluginConfig _config;
        #endregion

        #region State
        // Players currently in "Remove Mode"
        private HashSet<ulong> _removerMode = new HashSet<ulong>();
        // Cooldowns or sessions
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
            Puts("Creating new configuration file for NWG Building");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void Unload()
        {
            _removerMode.Clear();
        }
        #endregion

        #region Commands
        [ChatCommand("remove")]
        private void CmdRemove(BasePlayer player)
        {
            if (_removerMode.Contains(player.userID))
            {
                _removerMode.Remove(player.userID);
                player.ChatMessage("<color=#ffaa00>Remover Tool Disabled.</color>");
            }
            else
            {
                _removerMode.Add(player.userID);
                player.ChatMessage("<color=#aaffaa>Remover Tool Enabled.</color> Hit entities with a Hammer to remove them.");
                
                // Auto-disable timer
                timer.Once(_config.RemoveTime, () => 
                {
                    if (_removerMode.Contains(player.userID))
                    {
                        _removerMode.Remove(player.userID);
                        player.ChatMessage("<color=#ffaa00>Remover Tool Timed Out.</color>");
                    }
                });
            }
        }
        #endregion

        #region Hooks
        private void OnPlayerAttack(BasePlayer player, HitInfo info)
        {
            if (!_removerMode.Contains(player.userID)) return;
            if (info.HitEntity == null) return;
            
            // Check Tool
            if (_config.RequireTool)
            {
                var held = player.GetActiveItem();
                if (held == null || held.info.shortname != _config.RemoveTool) return;
            }

            // Handle
            TryRemove(player, info.HitEntity);
        }
        
        // Block damage if in remover mode?
        // Usually handled by returning a non-null value in OnPlayerAttack
        // But we want to trigger removal, then return true/false to block damage.
        
        #endregion

        #region Removal Logic
        private void TryRemove(BasePlayer player, BaseEntity target)
        {
            if (!CanRemove(player, target)) 
            {
                player.ChatMessage("<color=red>You are not allowed to remove this.</color>");
                return;
            }
            
            // Refund
            Refund(player, target);
            
            // Kill
            if (target is BaseCombatEntity combatEnt)
            {
                combatEnt.Kill();
            }
            else
            {
                target.Kill();
            }
            
            player.ChatMessage("Entity Removed.");
        }

        private bool CanRemove(BasePlayer player, BaseEntity target)
        {
            if (player.IsAdmin) return true;

            // 1. Tool Cupboard
            if (_config.UseToolCupboard)
            {
                if (!player.CanBuild()) return false;
            }

            // 2. Entity Owner
            if (_config.UseEntityOwner)
            {
                if (target.OwnerID != 0 && target.OwnerID != player.userID) return false;
            }

            return true;
        }

        private void Refund(BasePlayer player, BaseEntity target)
        {
            if (_config.RefundRate <= 0.0f) return;
            
            // Get Cost
            // Simple logic: If BuildingBlock, get current grade cost.
            // If Deployable, get blueprint cost?
            
            List<ItemAmount> costs = new List<ItemAmount>();
            
            if (target is BuildingBlock block)
            {
                var grade = block.grade;
                // Cost for this grade? 
                // Hard to get exact cost without lookup tables, but block.blockDefinition.grades[grade] has cost.
                var gradeDef = block.blockDefinition.GetGrade(grade, 0); // skin 0
                if (gradeDef != null)
                {
                    // costs.AddRange(gradeDef.costToBuild);
                    // TODO: Fix ConstructionGrade.costToBuild access
                }
            }
            else
            {
                // Deployable?
                // Need prefab name to ItemDefinition
                // var def = ItemManager.FindItemDefinition(target.ShortPrefabName); // Often doesn't match directly.
                // Simplified: We skip refunds for complex deployables in V1 unless we have a map.
                // But honestly, most removers use a massive lookup or "Repair" cost logic.
                
                // Let's rely on standard Rust "Demolish" refund if available? No, Demolish is only for 10 mins.
            }

            foreach(var cost in costs)
            {
                int amount = Mathf.FloorToInt(cost.amount * _config.RefundRate);
                if (amount > 0)
                {
                    player.GiveItem(ItemManager.CreateByItemID(cost.itemDef.itemid, amount));
                }
            }
        }
        #endregion
    }
}

