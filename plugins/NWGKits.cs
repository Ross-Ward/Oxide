using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWGKits", "NWG Team", "2.0.1")]
    [Description("Kit system with UI and admin management.")]
    public class NWGKits : RustPlugin
    {
#region Constants
        private const string PermAdmin = "nwgkits.admin";
        private const string UIName = "NWG_Kits_UI";
        private const string UIAdmin = "NWG_Kits_Admin";
        private const string UIEditor = "NWG_Kits_Editor";
        private const string UIInput = "NWG_Kits_Input";
        private const string UIAddItem = "NWG_Kits_AddItem";
        
        public class UIConstants
        {
            public const string Panel = "0.05 0.05 0.05 0.98"; 
            public const string Header = "0.15 0.15 0.15 1"; 
            public const string Row = "0.1 0.1 0.1 0.85";
            public const string RowAlt = "0.08 0.08 0.08 0.85";
            public const string Primary = "0.718 0.816 0.573 1"; // Sage Green
            public const string Danger = "0.851 0.325 0.31 1"; // Red
            public const string Button = "0.4 0.6 0.2 0.8"; // Active Greenish
            public const string Text = "0.867 0.867 0.867 1";
            public const string TextAlt = "0.7 0.7 0.7 1";
        }
#endregion

#region Localization
        public static class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string UI_Header = "UI.Header";
            public const string UI_NoKits = "UI.NoKits";
            public const string UI_Cooldown = "UI.Cooldown";
            public const string UI_Ready = "UI.Ready";
            public const string UI_OnCooldown = "UI.OnCooldown";
            public const string UI_Claim = "UI.Claim";
            public const string KitNotFound = "KitNotFound";
            public const string KitOnCooldown = "KitOnCooldown";
            public const string ReceivedKit = "ReceivedKit";
            public const string Admin_Header = "Admin.Header";
            public const string Editor_Title_Create = "Editor.Title.Create";
            public const string Editor_Title_Edit = "Editor.Title.Edit";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "<color=#d9534f>[NWG]</color> You do not have permission to use this command.",
                [Lang.UI_Header] = "AVAILABLE KITS",
                [Lang.UI_NoKits] = "No kits available for you right now.",
                [Lang.UI_Cooldown] = "{0:N0}s",
                [Lang.UI_Ready] = "Ready",
                [Lang.UI_OnCooldown] = "COOLDOWN",
                [Lang.UI_Claim] = "CLAIM",
                [Lang.KitNotFound] = "<color=#d9534f>[NWG]</color> Kit not found.",
                [Lang.KitOnCooldown] = "<color=#d9534f>[NWG]</color> Kit is on cooldown. Please wait <color=#FFA500>{0:N0}s</color>.",
                [Lang.ReceivedKit] = "<color=#b7d092>[NWG]</color> You received the <color=#FFA500>'{0}'</color> kit!",
                [Lang.Admin_Header] = "KIT ADMINISTRATION",
                [Lang.Editor_Title_Create] = "CREATE NEW KIT",
                [Lang.Editor_Title_Edit] = "EDIT KIT: {0}"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Config
        private class PluginConfig
        {
            public Dictionary<string, Kit> Kits = new Dictionary<string, Kit>();
        }

        private class Kit
        {
            public string Description;
            public int Cooldown;
            public string Permission;
            public List<KitItem> Items = new List<KitItem>();
        }

        private class KitItem
        {
            public string ShortName;
            public int Amount;
            public ulong SkinId;
        }

        private PluginConfig _config;
#endregion

#region Data
        private class StoredData
        {
            public Dictionary<ulong, Dictionary<string, DateTime>> PlayerCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();
        }

        private StoredData _data;

        private class AdminSession
        {
            public bool IsCreate;
            public string EditKitId;
            public string PendingName = "";
            public string PendingDesc = "";
            public int PendingCooldown = 3600;
            public string PendingPermission = "";
            public List<KitItem> PendingItems = new List<KitItem>();
            public string CurrentField;
            public string ItemSearchTerm = "";
            public string PendingAddShortname = "";
            public int EditingItemIndex = -1;
        }

        private readonly Dictionary<ulong, AdminSession> _adminSessions = new Dictionary<ulong, AdminSession>();
#endregion

#region Lifecycle
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadConfigVariables();
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_Kits") ?? new StoredData();
        }

        private void LoadConfigVariables()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null || _config.Kits.Count == 0) LoadDefaultConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();

            var starter = new Kit
            {
                Description = "Basic survival kit",
                Cooldown = 3600,
                Items = new List<KitItem> {
                    new KitItem { ShortName = "stone.pickaxe", Amount = 1 },
                    new KitItem { ShortName = "stone.axe", Amount = 1 },
                    new KitItem { ShortName = "apple", Amount = 5 }
                }
            };
            _config.Kits["starter"] = starter;

            var vip = new Kit
            {
                Description = "VIP rewards",
                Cooldown = 86400,
                Permission = "nwgkits.vip",
                Items = new List<KitItem> {
                    new KitItem { ShortName = "rifle.ak", Amount = 1 },
                    new KitItem { ShortName = "ammo.rifle", Amount = 128 }
                }
            };
            _config.Kits["vip"] = vip;

            var adminPvp = new Kit
            {
                Description = "Admin PVP Gear (Staff Only)",
                Cooldown = 0,
                Permission = "nwgcore.admin",
                Items = new List<KitItem> {
                    new KitItem { ShortName = "rifle.ak", Amount = 1 },
                    new KitItem { ShortName = "ammo.rifle", Amount = 256 },
                    new KitItem { ShortName = "attire.heavy.plate.helmet", Amount = 1 },
                    new KitItem { ShortName = "attire.heavy.plate.jacket", Amount = 1 },
                    new KitItem { ShortName = "attire.heavy.plate.pants", Amount = 1 },
                    new KitItem { ShortName = "syringe.medical", Amount = 10 }
                }
            };
            _config.Kits["admin_pvp"] = adminPvp;

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UIName);
                CuiHelper.DestroyUi(p, UIAdmin);
                CuiHelper.DestroyUi(p, UIEditor);
                CuiHelper.DestroyUi(p, UIInput);
                CuiHelper.DestroyUi(p, UIAddItem);
            }
        }

        private void OnServerSave() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Kits", _data);
#endregion

#region Commands
        [ChatCommand("kit")]
        private void CmdKit(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0 && args[0].Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
                {
                    player.ChatMessage(GetMessage(Lang.NoPermission, player.UserIDString));
                    return;
                }
                ShowAdminUI(player);
                return;
            }

            ShowKitsUI(player);
        }
#endregion

#region Player Kit UI
        private void ShowKitsUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIName);

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel
            {
                Image = { Color = UIConstants.Panel },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.82" },
                CursorEnabled = true
            }, "Overlay", UIName);

            var header = elements.Add(new CuiPanel
            {
                Image = { Color = UIConstants.Header },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, root);
            elements.Add(new CuiLabel
            {
                Text = { Text = GetMessage(Lang.UI_Header, player.UserIDString), FontSize = 22, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf", Color = UIConstants.Primary },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.6 1" }
            }, header);
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.close", Color = UIConstants.Danger },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" },
                Text = { Text = "✕", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, header);

            var available = _config.Kits
                .Where(k => string.IsNullOrEmpty(k.Value.Permission) || permission.UserHasPermission(player.UserIDString, k.Value.Permission))
                .OrderBy(k => k.Key)
                .ToList();

            if (available.Count == 0)
            {
                elements.Add(new CuiLabel
                {
                    Text = { Text = GetMessage(Lang.UI_NoKits, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = UIConstants.TextAlt },
                    RectTransform = { AnchorMin = "0.1 0.5", AnchorMax = "0.9 0.6" }
                }, root);
            }
            else
            {
                float y = 0.88f;
                float rowHeight = 0.078f;
                for (int i = 0; i < available.Count; i++)
                {
                    var kv = available[i];
                    string kitId = kv.Key;
                    Kit kit = kv.Value;
                    bool onCooldown = GetRemainingCooldown(player.userID, kitId, out double remaining);
                    
                    string cooldownText = onCooldown ? GetMessage(Lang.UI_Cooldown, player.UserIDString, remaining) 
                                                     : GetMessage(Lang.UI_Ready, player.UserIDString);
                    string cooldownColor = onCooldown ? "0.9 0.5 0.2 1" : "0.4 0.8 0.4 1";

                    float yMin = y - rowHeight;
                    var row = elements.Add(new CuiPanel
                    {
                        Image = { Color = i % 2 == 0 ? UIConstants.Row : UIConstants.RowAlt },
                        RectTransform = { AnchorMin = $"0.02 {yMin}", AnchorMax = "0.98 {y}" } // Fixed manually if needed, but strings are fine
                    }, root);

                    elements.Add(new CuiLabel
                    {
                        Text = { Text = kitId.ToUpper(), FontSize = 14, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf", Color = UIConstants.Text },
                        RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.25 1" }
                    }, row);
                    elements.Add(new CuiLabel
                    {
                        Text = { Text = cooldownText, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = cooldownColor },
                        RectTransform = { AnchorMin = "0.6 0", AnchorMax = "0.75 1" }
                    }, row);

                    if (!onCooldown)
                    {
                        elements.Add(new CuiButton
                        {
                            Button = { Command = $"kit.claim {kitId}", Color = UIConstants.Button },
                            RectTransform = { AnchorMin = "0.8 0.15", AnchorMax = "0.98 0.85" },
                            Text = { Text = GetMessage(Lang.UI_Claim, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", Color = UIConstants.Text }
                        }, row);
                    }
                    y -= rowHeight + 0.008f;
                }
            }

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("kit.close")]
        private void ConsoleKitClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, UIName);
        }

        [ConsoleCommand("kit.claim")]
        private void ConsoleKitClaim(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            string kitName = arg.GetString(0)?.ToLower();
            if (string.IsNullOrEmpty(kitName) || !_config.Kits.TryGetValue(kitName, out Kit kit)) return;
            if (GetRemainingCooldown(player.userID, kitName, out double remaining)) return;
            GiveKit(player, kit);
            SetCooldown(player.userID, kitName, kit.Cooldown);
            ShowKitsUI(player);
        }
#endregion

#region Admin UI (Placeholder simplified)
        private void ShowAdminUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIName);
            CuiHelper.DestroyUi(player, UIAdmin);
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel { Image = { Color = UIConstants.Panel }, RectTransform = { AnchorMin = "0.25 0.15", AnchorMax = "0.75 0.88" }, CursorEnabled = true }, "Overlay", UIAdmin);
            elements.Add(new CuiLabel { Text = { Text = "ADMIN UI UNDER MAINTENANCE", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = UIConstants.Primary }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, root);
            elements.Add(new CuiButton { Button = { Command = "kit.adminback", Color = UIConstants.Danger }, RectTransform = { AnchorMin = "0.4 0.1", AnchorMax = "0.6 0.2" }, Text = { Text = "BACK" } }, root);
            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("kit.adminback")]
        private void ConsoleKitAdminBack(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) { CuiHelper.DestroyUi(player, UIAdmin); ShowKitsUI(player); }
        }
#endregion

#region Helpers
        private void GiveKit(BasePlayer player, Kit kit)
        {
            foreach (var item in kit.Items)
            {
                var giveItem = ItemManager.CreateByName(item.ShortName, item.Amount, item.SkinId);
                if (giveItem != null) player.GiveItem(giveItem);
            }
        }

        private bool GetRemainingCooldown(ulong userId, string kitName, out double remaining)
        {
            remaining = 0;
            if (!_data.PlayerCooldowns.TryGetValue(userId, out var cooldowns)) return false;
            if (!cooldowns.TryGetValue(kitName, out DateTime expiry)) return false;
            if (DateTime.Now < expiry)
            {
                remaining = (expiry - DateTime.Now).TotalSeconds;
                return true;
            }
            return false;
        }

        private void SetCooldown(ulong userId, string kitName, int seconds)
        {
            if (!_data.PlayerCooldowns.TryGetValue(userId, out var cooldowns))
                cooldowns = _data.PlayerCooldowns[userId] = new Dictionary<string, DateTime>();
            cooldowns[kitName] = DateTime.Now.AddSeconds(seconds);
        }
#endregion
    }
}
