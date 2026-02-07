using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("NWG Tools", "NWG Team", "1.0.0")]
    [Description("Unified Admin Tools, Code Sync, and Group Management.")]
    public class NWGTools : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public bool EnableCodeSync = true;
            public bool EnableGroupSetup = true;
            public List<GroupDef> DefaultGroups = new List<GroupDef>
            {
                new GroupDef { Name = "vip", Title = "VIP", Rank = 10 },
                new GroupDef { Name = "vip+", Title = "VIP+", Rank = 20 },
                new GroupDef { Name = "admin", Title = "Admin", Rank = 100 }
            };
        }

        private class GroupDef
        {
            public string Name;
            public string Title;
            public int Rank;
        }

        private PluginConfig _config;
        #endregion

        private const string PermUse = "nwgcore.admin";

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
            Puts("Creating new configuration file for NWG Tools");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            if (_config.EnableGroupSetup)
            {
                SetupGroups();
            }
        }
        #endregion

        #region Group Setup
        private void SetupGroups()
        {
            foreach (var group in _config.DefaultGroups)
            {
                if (!permission.GroupExists(group.Name))
                {
                    permission.CreateGroup(group.Name, group.Title, group.Rank);
                    Puts($"[NWG Tools] Created group: {group.Name}");
                }
            }
        }
        #endregion

        #region Code Sync
        private object CanUseLockedEntity(BasePlayer player, CodeLock lockEntity)
        {
            if (!_config.EnableCodeSync) return null;
            if (player == null || lockEntity == null) return null;
            
            // Allow if unlocked
            if (!lockEntity.IsLocked()) return null;

            // Check TC logic
            var parent = lockEntity.GetParentEntity();
            if (parent == null) return null;

            var building = parent.GetBuildingPrivilege()?.GetBuilding();
             // Logic for DecayEntity fallback if needed
            if (building == null)
            {
                 var decayEnt = parent as DecayEntity;
                 if (decayEnt != null) building = decayEnt.GetBuilding();
            }

            if (building == null || building.buildingPrivileges.Count == 0) return null;

            var tc = building.buildingPrivileges[0];
            var tcLock = tc.GetSlot(BaseEntity.Slot.Lock) as CodeLock;

            if (tcLock == null) return null;

            // Sync Condition: Codes match OR Guest code matches
            bool codeMatch = (tcLock.code == lockEntity.code) || (tcLock.guestCode == lockEntity.code && lockEntity.code.Length > 0);
            
            // If they don't match, we don't grant access via Sync logic unless we assume checking TC list
            if (!codeMatch) return null;

            // If codes match, check if player is authed on TC lock or is whitelisted
            if (tcLock.whitelistPlayers.Contains(player.userID) || tcLock.guestPlayers.Contains(player.userID))
            {
                return true; // Grant Access
            }

            return null;
        }

        private void OnCodeEntered(CodeLock codeLock, BasePlayer player, string code)
        {
            if (!_config.EnableCodeSync) return;
        }
        #endregion

        #region Tools UI
        [ChatCommand("tools")]
        private void CmdTools(BasePlayer player)
        {
             if (!permission.UserHasPermission(player.UserIDString, PermUse) && !player.IsAdmin)
             {
                 player.ChatMessage("No permission.");
                 return;
             }
             ShowToolsUI(player);
        }

        [ConsoleCommand("nwg_tools.ui")]
        private void ConsoleUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CmdTools(player);
        }

        [ConsoleCommand("nwg_tools.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, "NWG_Tools_UI");
        }

        private void ShowToolsUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "NWG_Tools_UI");
            var elements = new CuiElementContainer();

            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                CursorEnabled = true
            }, "Overlay", "NWG_Tools_UI");

            // Header Background
            elements.Add(new CuiPanel {
                Image = { Color = "0.4 0.6 0.2 0.3" },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, root);

            // Header Title
            elements.Add(new CuiLabel {
                Text = { Text = "ADMIN TOOLS", FontSize = 20, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.05 0.88", AnchorMax = "1 1" }
            }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "nwg_tools.close", Color = "0.8 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.92 0.9", AnchorMax = "0.98 0.98" },
                Text = { Text = "✕", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, root);

            // Action Buttons
            float y = 0.75f;
            
            AddActionButton(elements, root, "RELOAD ALL PLUGINS", "oxide.reload *", ref y);
            AddActionButton(elements, root, "TOGGLE GOD MODE", "nwg.god", ref y);
            AddActionButton(elements, root, "HEAL (LOOKED AT)", "nwg.heal target", ref y);
            AddActionButton(elements, root, "TP TO (LOOKED AT)", "nwg.tp target", ref y);
            AddActionButton(elements, root, "INV SEE (LOOKED AT)", "nwg.invsee target", ref y);
            AddActionButton(elements, root, "KICK (LOOKED AT)", "nwg.kick target", ref y);
            AddActionButton(elements, root, "BAN (LOOKED AT)", "nwg.ban target", ref y);

            CuiHelper.AddUi(player, elements);
        }

        private void AddActionButton(CuiElementContainer container, string parent, string text, string command, ref float y)
        {
            container.Add(new CuiButton {
                Button = { Command = command, Color = "0.4 0.6 0.2 0.8" },
                RectTransform = { AnchorMin = $"0.1 {y - 0.1f}", AnchorMax = $"0.9 {y}" },
                Text = { Text = text, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 14 }
            }, parent);
            y -= 0.12f;
        }

        [ChatCommand("grantvip")]
        private void CmdGrantVip(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            
            var target = GetLookedAtPlayer(player);
            if (target == null) { player.ChatMessage("No player found."); return; }

            permission.AddUserGroup(target.UserIDString, "vip");
            player.ChatMessage($"Granted VIP to {target.displayName}");
        }

        [ChatCommand("revokevip")]
        private void CmdRevokeVip(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            
            var target = GetLookedAtPlayer(player);
            if (target == null) { player.ChatMessage("No player found."); return; }

            permission.RemoveUserGroup(target.UserIDString, "vip");
            player.ChatMessage($"Revoked VIP from {target.displayName}");
        }

        [ChatCommand("bring")]
        private void CmdBring(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            BasePlayer target = null;
            if (args.Length > 0) target = BasePlayer.Find(args[0]);
            if (target == null) target = GetLookedAtPlayer(player);

            if (target == null) { player.ChatMessage("Player not found."); return; }

            target.Teleport(player);
            player.ChatMessage($"Brought {target.displayName} to you");
        }

        // Redundant God Mode removed from Tools to avoid conflicts with Admin plugin.
        // Use /god from NWGAdmin instead.

        [ChatCommand("heal")]
        private void CmdHeal(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            BasePlayer target = null;
            if (args.Length > 0) target = BasePlayer.Find(args[0]);
            if (target == null) target = GetLookedAtPlayer(player);

            if (target == null) { player.ChatMessage("Player not found."); return; }

            target.health = target.MaxHealth();
            target.metabolism.calories.value = target.metabolism.calories.max;
            target.metabolism.hydration.value = target.metabolism.hydration.max;
            player.ChatMessage($"Healed {target.displayName}");
        }

        [ChatCommand("kick")]
        private void CmdKick(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            BasePlayer target = null;
            if (args.Length > 0) target = BasePlayer.Find(args[0]);
            if (target == null) target = GetLookedAtPlayer(player);

            if (target == null) { player.ChatMessage("Player not found."); return; }

            target.Kick("Kicked by administrator");
            player.ChatMessage($"Kicked {target.displayName}");
        }

        [ChatCommand("ban")]
        private void CmdBan(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            if (args.Length == 0) { player.ChatMessage("Usage: /ban <player> [reason]"); return; }

            var target = BasePlayer.Find(args[0]);
            if (target == null) { player.ChatMessage("Player not found."); return; }

            string reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Banned by administrator";
            target.Kick(reason);
            Server.Command($"banid {target.UserIDString} \"{target.displayName}\" \"{reason}\"");
            Server.Command("server.writecfg");
            player.ChatMessage($"Banned {target.displayName}: {reason}");
        }

        [ChatCommand("mute")]
        private void CmdMute(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            if (args.Length == 0) { player.ChatMessage("Usage: /mute <player>"); return; }
            var target = BasePlayer.Find(args[0]);
            if (target == null) { player.ChatMessage("Player not found."); return; }

            Server.Command($"mute {target.UserIDString}"); 
            player.ChatMessage($"Muted {target.displayName}");
        }

        [ChatCommand("invsee")]
        private void CmdInvSee(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            BasePlayer target = null;
            if (args.Length > 0) target = BasePlayer.Find(args[0]);
            if (target == null) target = GetLookedAtPlayer(player);

            if (target == null) { player.ChatMessage("Player not found."); return; }

            // Open loot interface for the player's inventory
            player.EndLooting();
            timer.Once(0.1f, () => {
                player.inventory.loot.Clear();
                player.inventory.loot.AddContainer(target.inventory.containerMain);
                player.inventory.loot.AddContainer(target.inventory.containerWear);
                player.inventory.loot.AddContainer(target.inventory.containerBelt);
                player.inventory.loot.SendImmediate();
                player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", "player_corpse");
                player.SendNetworkUpdate();
            });
        }

        private BasePlayer GetLookedAtPlayer(BasePlayer source)
        {
            RaycastHit hit;
            if (Physics.Raycast(source.eyes.HeadRay(), out hit, 50f))
            {
                return hit.GetEntity() as BasePlayer;
            }
            return null;
        }
        #endregion
    }
}
