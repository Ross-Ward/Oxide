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
    [Info("NWGTools", "NWG Team", "1.0.0")]
    [Description("Unified Admin Tools, Code Sync, and Group Management.")]
    public class NWGTools : RustPlugin
    {
        [PluginReference]
        private Plugin NWGAdmin;

        public static class UIConstants
        {
            public const string MainColor = "0.718 0.816 0.573 1"; // Sage Green
            public const string SecondaryColor = "0.851 0.325 0.31 1"; // Red/Rust
            public const string AccentColor = "1 0.647 0 1"; // Orange
            public const string PanelColor = "0.15 0.15 0.15 0.98"; // Dark Panel
            public const string HeaderColor = "0.1 0.1 0.1 1";
            public const string TextColor = "0.867 0.867 0.867 1";
        }
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

            // If codes match, check if player has building privilege AND is authed on TC lock
            if (tc.IsAuthed(player) && (tcLock.whitelistPlayers.Contains(player.userID) || tcLock.guestPlayers.Contains(player.userID)))
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
                 player.ChatMessage(GetMessage(Lang.NoPermission, player.UserIDString));
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
                Image = { Color = UIConstants.PanelColor },
                RectTransform = { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                CursorEnabled = true
            }, "Overlay", "NWG_Tools_UI");

            // Header Background
            elements.Add(new CuiPanel {
                Image = { Color = UIConstants.HeaderColor },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, root);

            // Header Title
            elements.Add(new CuiLabel {
                Text = { Text = GetMessage(Lang.UITitle, player.UserIDString), FontSize = 20, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf", Color = UIConstants.MainColor },
                RectTransform = { AnchorMin = "0.05 0.88", AnchorMax = "1 1" }
            }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "nwg_tools.close", Color = "0.851 0.325 0.31 1" },
                RectTransform = { AnchorMin = "0.92 0.9", AnchorMax = "0.98 0.98" },
                Text = { Text = "✕", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, root);

            // Action Buttons
            float y = 0.75f;
            
            AddActionButton(elements, root, GetMessage(Lang.BtnReloadAll, player.UserIDString), "oxide.reload *", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnGod, player.UserIDString), "god", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnHeal, player.UserIDString), "heal", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnTp, player.UserIDString), "tp", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnInv, player.UserIDString), "invsee", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnKick, player.UserIDString), "kick", ref y);
            AddActionButton(elements, root, GetMessage(Lang.BtnBan, player.UserIDString), "ban", ref y);

            CuiHelper.AddUi(player, elements);
        }

        private void AddActionButton(CuiElementContainer container, string parent, string text, string command, ref float y)
        {
            container.Add(new CuiButton {
                Button = { Command = command, Color = UIConstants.MainColor },
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
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            permission.AddUserGroup(target.UserIDString, "vip");
            player.ChatMessage(GetMessage(Lang.GrantedVip, player.UserIDString, target.displayName));
        }

        [ChatCommand("revokevip")]
        private void CmdRevokeVip(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            
            var target = GetLookedAtPlayer(player);
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            permission.RemoveUserGroup(target.UserIDString, "vip");
            player.ChatMessage(GetMessage(Lang.RevokedVip, player.UserIDString, target.displayName));
        }

        [ChatCommand("bring")]
        private void CmdBring(BasePlayer player, string msg, string[] args)
        {
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;

            BasePlayer target = null;
            if (args.Length > 0) target = BasePlayer.Find(args[0]);
            if (target == null) target = GetLookedAtPlayer(player);

            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            target.Teleport(player.transform.position);
            player.ChatMessage(GetMessage(Lang.BroughtPlayer, player.UserIDString, target.displayName));
        }

        // Redundant God Mode removed from Tools to avoid conflicts with Admin plugin.
        // Use /god from NWGAdmin instead.

        [ChatCommand("heal")]
        private void CmdHeal(BasePlayer player, string msg, string[] args)
        {
            if (NWGAdmin != null) { NWGAdmin.Call("CmdHeal", player, "heal", args); return; }
            
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            BasePlayer target = args.Length > 0 ? BasePlayer.Find(args[0]) : GetLookedAtPlayer(player) ?? player;
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            target.health = target.MaxHealth();
            target.metabolism.calories.value = target.metabolism.calories.max;
            target.metabolism.hydration.value = target.metabolism.hydration.max;
            player.ChatMessage(GetMessage(Lang.HealedPlayer, player.UserIDString, target.displayName));
        }

        [ChatCommand("kick")]
        private void CmdKick(BasePlayer player, string msg, string[] args)
        {
            if (NWGAdmin != null) { NWGAdmin.Call("CmdKick", player, "kick", args); return; }

            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            BasePlayer target = args.Length > 0 ? BasePlayer.Find(args[0]) : GetLookedAtPlayer(player);
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            target.Kick("Kicked by administrator");
            player.ChatMessage(GetMessage(Lang.KickedPlayer, player.UserIDString, target.displayName));
        }

        [ChatCommand("ban")]
        private void CmdBan(BasePlayer player, string msg, string[] args)
        {
            if (NWGAdmin != null) { NWGAdmin.Call("CmdBan", player, "ban", args); return; }

            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            if (args.Length == 0) { player.ChatMessage(GetMessage(Lang.BanUsage, player.UserIDString)); return; }
            var target = BasePlayer.Find(args[0]);
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            string reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Banned by administrator";
            target.Kick(reason);
            Server.Command($"banid {target.UserIDString} \"{target.displayName}\" \"{reason}\"");
            Server.Command("server.writecfg");
            player.ChatMessage(GetMessage(Lang.BannedPlayer, player.UserIDString, target.displayName, reason));
        }

        [ChatCommand("mute")]
        private void CmdMute(BasePlayer player, string msg, string[] args)
        {
            if (NWGAdmin != null) { NWGAdmin.Call("CmdMute", player, "mute", args); return; }

            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse))) return;
            if (args.Length == 0) { player.ChatMessage(GetMessage(Lang.MuteUsage, player.UserIDString)); return; }
            var target = BasePlayer.Find(args[0]);
            if (target == null) { player.ChatMessage(GetMessage(Lang.NoPlayerFound, player.UserIDString)); return; }

            Server.Command($"mute {target.UserIDString}"); 
            player.ChatMessage(GetMessage(Lang.MutedPlayer, player.UserIDString, target.displayName));
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
            if (Physics.Raycast(source.eyes.HeadRay(), out hit, 50f)) return hit.GetEntity() as BasePlayer;
            return null;
        }
#endregion

#region Localization
        private class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string NoPlayerFound = "NoPlayerFound";
            public const string UITitle = "UITitle";
            public const string BtnReloadAll = "BtnReloadAll";
            public const string BtnGod = "BtnGod";
            public const string BtnHeal = "BtnHeal";
            public const string BtnTp = "BtnTp";
            public const string BtnInv = "BtnInv";
            public const string BtnKick = "BtnKick";
            public const string BtnBan = "BtnBan";
            public const string GrantedVip = "GrantedVip";
            public const string RevokedVip = "RevokedVip";
            public const string BroughtPlayer = "BroughtPlayer";
            public const string HealedPlayer = "HealedPlayer";
            public const string KickedPlayer = "KickedPlayer";
            public const string BannedPlayer = "BannedPlayer";
            public const string MutedPlayer = "MutedPlayer";
            public const string BanUsage = "BanUsage";
            public const string MuteUsage = "MuteUsage";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "No permission.",
                [Lang.NoPlayerFound] = "Player not found.",
                [Lang.UITitle] = "ADMIN TOOLS",
                [Lang.BtnReloadAll] = "RELOAD ALL PLUGINS",
                [Lang.BtnGod] = "TOGGLE GOD MODE",
                [Lang.BtnHeal] = "HEAL (LOOKED AT)",
                [Lang.BtnTp] = "TP TO (LOOKED AT)",
                [Lang.BtnInv] = "INV SEE (LOOKED AT)",
                [Lang.BtnKick] = "KICK (LOOKED AT)",
                [Lang.BtnBan] = "BAN (LOOKED AT)",
                [Lang.GrantedVip] = "Granted VIP to {0}",
                [Lang.RevokedVip] = "Revoked VIP from {0}",
                [Lang.BroughtPlayer] = "Brought {0} to you",
                [Lang.HealedPlayer] = "Healed {0}",
                [Lang.KickedPlayer] = "Kicked {0}",
                [Lang.BannedPlayer] = "Banned {0}: {1}",
                [Lang.MutedPlayer] = "Muted {0}",
                [Lang.BanUsage] = "Usage: /ban <player> [reason]",
                [Lang.MuteUsage] = "Usage: /mute <player>"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion
    }
}

