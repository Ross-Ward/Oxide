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
    [Info("NWGBuilding", "NWG Team", "3.1.1")]
    [Description("Building Utilities: /remove tool with hammer hit detection.")]
    public class NWGBuilding : RustPlugin
    {
#region Config
        private class PluginConfig
        {
            public float RefundRate = 0.5f;
            public float RemoveTime = 30f;
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

#region Localization
        public static class Lang
        {
            public const string Disabled = "Disabled";
            public const string Enabled = "Enabled";
            public const string TimedOut = "TimedOut";
            public const string NotAllowed = "NotAllowed";
            public const string Removed = "Removed";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Disabled] = "<color=#b7d092>[NWG]</color> Remover Tool <color=#d9534f>Disabled</color>.",
                [Lang.Enabled] = "<color=#b7d092>[NWG]</color> Remover Tool <color=#b7d092>Enabled</color> for <color=#FFA500>{0}s</color>.\nHit entities with your <color=#FFA500>Hammer</color> to remove them.",
                [Lang.TimedOut] = "<color=#b7d092>[NWG]</color> Remover Tool <color=#FFA500>Timed Out</color>.",
                [Lang.NotAllowed] = "<color=#d9534f>[NWG]</color> You are not allowed to remove this.",
                [Lang.Removed] = "<color=#b7d092>[NWG]</color> Removed: <color=#FFA500>{0}</color>"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
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
                player.ChatMessage(GetMessage(Lang.Disabled, player.UserIDString));
            }
            else
            {
                _removerMode.Add(player.userID);
                player.ChatMessage(GetMessage(Lang.Enabled, player.UserIDString, _config.RemoveTime));
                
                timer.Once(_config.RemoveTime, () => 
                {
                    if (_removerMode.Contains(player.userID))
                    {
                        _removerMode.Remove(player.userID);
                        if (player.IsConnected)
                            player.ChatMessage(GetMessage(Lang.TimedOut, player.UserIDString));
                    }
                });
            }
        }
#endregion

#region Hooks
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
                player.ChatMessage(GetMessage(Lang.NotAllowed, player.UserIDString));
                return;
            }
            
            string entityName = target.ShortPrefabName ?? "Entity";
            Refund(player, target);
            
            NextTick(() =>
            {
                if (target == null || target.IsDestroyed) return;
                target.Kill(BaseNetworkable.DestroyMode.Gib);
                player.ChatMessage(GetMessage(Lang.Removed, player.UserIDString, entityName));
            });
        }

        private bool CanRemove(BasePlayer player, BaseEntity target)
        {
            if (player.IsAdmin) return true;
            if (_config.UseToolCupboard && !player.CanBuild()) return false;
            if (_config.UseEntityOwner && target.OwnerID != 0 && target.OwnerID != player.userID)
            {
                var team = RelationshipManager.ServerInstance?.FindPlayersTeam(player.userID);
                if (team == null || !team.members.Contains(target.OwnerID)) return false;
            }
            return true;
        }

        private void Refund(BasePlayer player, BaseEntity target)
        {
            if (_config.RefundRate <= 0.0f) return;
            if (target is BuildingBlock block)
            {
                var gradeDef = block.blockDefinition?.GetGrade(block.grade, 0);
                if (gradeDef != null && gradeDef.CostToBuild() != null)
                {
                    foreach (var cost in gradeDef.CostToBuild())
                    {
                        int amount = Mathf.FloorToInt(cost.amount * _config.RefundRate);
                        if (amount > 0)
                        {
                            var item = ItemManager.CreateByItemID(cost.itemDef.itemid, amount);
                            if (item != null) player.GiveItem(item);
                        }
                    }
                }
            }
        }
#endregion
    }
}
