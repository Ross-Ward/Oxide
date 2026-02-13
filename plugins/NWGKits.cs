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
    [Info("NWGKits", "NWG Team", "2.0.0")]
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
        private const string ColBg = "0.06 0.06 0.08 0.98";
        private const string ColHeader = "0.12 0.14 0.18 1";
        private const string ColRow = "0.1 0.12 0.16 0.85";
        private const string ColRowAlt = "0.08 0.1 0.14 0.85";
        private const string ColAccent = "0.35 0.55 0.25 1";
        private const string ColDanger = "0.7 0.2 0.2 0.9";
        private const string ColButton = "0.2 0.35 0.15 0.9";
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
                    player.ChatMessage("No permission.");
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
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.82" },
                CursorEnabled = true
            }, "Overlay", UIName);

            // Header bar (title + close) - contained so KITS stays at top
            var header = elements.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, root, "KitsHeader");
            elements.Add(new CuiLabel
            {
                Text = { Text = "KITS", FontSize = 22, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.6 1" }
            }, header);
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.close", Color = ColDanger },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" },
                Text = { Text = "✕", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, header);

            // Use full width so no gap on the right
            const float listRight = 0.98f;
            var available = _config.Kits
                .Where(k => string.IsNullOrEmpty(k.Value.Permission) || permission.UserHasPermission(player.UserIDString, k.Value.Permission))
                .OrderBy(k => k.Key)
                .ToList();

            if (available.Count == 0)
            {
                elements.Add(new CuiLabel
                {
                    Text = { Text = "No kits available.", FontSize = 14, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.1 0.5", AnchorMax = $"{listRight - 0.02f} 0.6" }
                }, root);
            }
            else
            {
                float y = 0.88f;
                float rowHeight = 0.078f;
                const int maxDescLen = 60; // Show more of the description
                for (int i = 0; i < available.Count; i++)
                {
                    var kv = available[i];
                    string kitId = kv.Key;
                    Kit kit = kv.Value;
                    bool onCooldown = GetRemainingCooldown(player.userID, kitId, out double remaining);
                    string cooldownText = onCooldown ? $"{remaining:N0}s" : "Ready";
                    string cooldownColor = onCooldown ? "0.9 0.5 0.2 1" : "0.4 0.8 0.4 1";
                    string desc = string.IsNullOrEmpty(kit.Description) ? "No description" : (kit.Description.Length > maxDescLen ? kit.Description.Substring(0, maxDescLen - 3) + "..." : kit.Description);

                    float yMin = y - rowHeight;
                    var row = elements.Add(new CuiPanel
                    {
                        Image = { Color = i % 2 == 0 ? ColRow : ColRowAlt },
                        RectTransform = { AnchorMin = $"0.02 {yMin}", AnchorMax = $"{listRight} {y}" }
                    }, root);

                    // Kit name (left, narrow so description has room)
                    elements.Add(new CuiLabel
                    {
                        Text = { Text = kitId.ToUpper(), FontSize = 14, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0.02 0.55", AnchorMax = "0.2 0.95" }
                    }, row);
                    // Description (wide center area so it reads well)
                    elements.Add(new CuiLabel
                    {
                        Text = { Text = desc, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.8 0.8 0.8 1" },
                        RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.58 0.5" }
                    }, row);
                    // Status (Ready or cooldown time)
                    elements.Add(new CuiLabel
                    {
                        Text = { Text = cooldownText, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = cooldownColor },
                        RectTransform = { AnchorMin = "0.6 0.2", AnchorMax = "0.72 0.8" }
                    }, row);

                    // Right column: CLAIM button or "on cooldown" panel
                    if (onCooldown)
                    {
                        var cooldownPanel = elements.Add(new CuiPanel
                        {
                            Image = { Color = "0.25 0.22 0.2 0.9" },
                            RectTransform = { AnchorMin = "0.74 0.15", AnchorMax = "0.98 0.85" }
                        }, row);
                        elements.Add(new CuiLabel
                        {
                            Text = { Text = "ON COOLDOWN", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.75 0.5 0.25 1" },
                            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }, cooldownPanel);
                    }
                    else
                    {
                        elements.Add(new CuiButton
                        {
                            Button = { Command = $"kit.claim {kitId}", Color = ColButton },
                            RectTransform = { AnchorMin = "0.74 0.15", AnchorMax = "0.98 0.85" },
                            Text = { Text = "CLAIM", FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                        }, row);
                    }
                    y -= rowHeight + 0.008f;
                }
            }

            if (permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "kit.admin", Color = "0.25 0.25 0.35 0.95" },
                    RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.2 0.06" },
                    Text = { Text = "ADMIN", FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                }, root);
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
            if (string.IsNullOrEmpty(kitName) || !_config.Kits.TryGetValue(kitName, out Kit kit))
            {
                player.ChatMessage("Kit not found.");
                return;
            }
            if (!string.IsNullOrEmpty(kit.Permission) && !permission.UserHasPermission(player.UserIDString, kit.Permission))
            {
                player.ChatMessage("No permission for this kit.");
                return;
            }
            if (GetRemainingCooldown(player.userID, kitName, out double remaining))
            {
                player.ChatMessage($"Kit on cooldown. Wait {remaining:N0}s.");
                return;
            }
            GiveKit(player, kit);
            SetCooldown(player.userID, kitName, kit.Cooldown);
            player.ChatMessage($"You received the '{kitName}' kit!");
            ShowKitsUI(player);
        }
        #endregion

        #region Admin UI
        private void ShowAdminUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIName);
            CuiHelper.DestroyUi(player, UIAdmin);
            CuiHelper.DestroyUi(player, UIEditor);
            CuiHelper.DestroyUi(player, UIInput);

            const float listRight = 0.90f; // Inset from right so scrollbar doesn't cover EDIT/DELETE

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.25 0.15", AnchorMax = "0.75 0.88" },
                CursorEnabled = true
            }, "Overlay", UIAdmin);

            // Header bar - title and close contained at top only
            var header = elements.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
            }, root, "KitsAdminHeader");
            elements.Add(new CuiLabel
            {
                Text = { Text = "KIT ADMIN", FontSize = 20, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.6 1" }
            }, header);
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.adminback", Color = ColDanger },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, header);

            elements.Add(new CuiButton
            {
                Button = { Command = "kit.admincreate", Color = ColAccent },
                RectTransform = { AnchorMin = "0.02 0.86", AnchorMax = "0.28 0.92" },
                Text = { Text = "+ CREATE KIT", FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
            }, root);

            var kitIds = _config.Kits.Keys.OrderBy(x => x).ToList();
            float y = 0.8f;
            foreach (var kitId in kitIds)
            {
                var kit = _config.Kits[kitId];
                string desc = string.IsNullOrEmpty(kit.Description) ? "" : (kit.Description.Length > 25 ? kit.Description.Substring(0, 22) + "..." : kit.Description);
                var row = elements.Add(new CuiPanel
                {
                    Image = { Color = ColRow },
                    RectTransform = { AnchorMin = $"0.02 {y - 0.06f}", AnchorMax = $"{listRight} {y}" }
                }, root);
                elements.Add(new CuiLabel
                {
                    Text = { Text = kitId.ToUpper(), FontSize = 13, Align = TextAnchor.MiddleLeft },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.24 1" }
                }, row);
                elements.Add(new CuiLabel
                {
                    Text = { Text = desc, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.7 0.7 0.7 1" },
                    RectTransform = { AnchorMin = "0.26 0", AnchorMax = "0.52 1" }
                }, row);
                elements.Add(new CuiButton
                {
                    Button = { Command = $"kit.adminedit {kitId}", Color = "0.2 0.4 0.6 0.9" },
                    RectTransform = { AnchorMin = "0.56 0.15", AnchorMax = "0.72 0.85" },
                    Text = { Text = "EDIT", FontSize = 11, Align = TextAnchor.MiddleCenter }
                }, row);
                elements.Add(new CuiButton
                {
                    Button = { Command = $"kit.admindelete {kitId}", Color = ColDanger },
                    RectTransform = { AnchorMin = "0.74 0.15", AnchorMax = "0.98 0.85" },
                    Text = { Text = "DELETE", FontSize = 11, Align = TextAnchor.MiddleCenter }
                }, row);
                y -= 0.068f;
            }

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("kit.admin")]
        private void ConsoleKitAdmin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null && permission.UserHasPermission(player.UserIDString, PermAdmin))
                ShowAdminUI(player);
        }

        [ConsoleCommand("kit.adminback")]
        private void ConsoleKitAdminBack(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIAdmin);
            CuiHelper.DestroyUi(player, UIEditor);
            CuiHelper.DestroyUi(player, UIInput);
            _adminSessions.Remove(player.userID);
            ShowKitsUI(player);
        }

        [ConsoleCommand("kit.admincreate")]
        private void ConsoleKitAdminCreate(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            _adminSessions[player.userID] = new AdminSession
            {
                IsCreate = true,
                PendingName = "newkit",
                PendingDesc = "Description",
                PendingCooldown = 3600,
                PendingPermission = "",
                PendingItems = new List<KitItem>()
            };
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.adminedit")]
        private void ConsoleKitAdminEdit(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            string kitId = arg.GetString(0);
            if (string.IsNullOrEmpty(kitId) || !_config.Kits.TryGetValue(kitId, out Kit kit)) return;

            var itemsCopy = kit.Items.Select(x => new KitItem { ShortName = x.ShortName, Amount = x.Amount, SkinId = x.SkinId }).ToList();
            _adminSessions[player.userID] = new AdminSession
            {
                IsCreate = false,
                EditKitId = kitId,
                PendingName = kitId,
                PendingDesc = kit.Description ?? "",
                PendingCooldown = kit.Cooldown,
                PendingPermission = kit.Permission ?? "",
                PendingItems = itemsCopy
            };
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.admindelete")]
        private void ConsoleKitAdminDelete(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PermAdmin)) return;
            string kitId = arg.GetString(0)?.ToLower();
            if (string.IsNullOrEmpty(kitId) || !_config.Kits.ContainsKey(kitId)) return;
            _config.Kits.Remove(kitId);
            SaveConfig();
            player.ChatMessage($"Kit '{kitId}' deleted.");
            ShowAdminUI(player);
        }
        #endregion

        #region Editor UI (Create/Edit kit)
        private void ShowEditorUI(BasePlayer player)
        {
            if (!_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;

            CuiHelper.DestroyUi(player, UIAdmin);
            CuiHelper.DestroyUi(player, UIEditor);
            CuiHelper.DestroyUi(player, UIInput);
            CuiHelper.DestroyUi(player, UIAddItem);

            const float listRight = 0.90f; // Inset from right so scrollbar doesn't cover EDIT/REMOVE/close

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.92" },
                CursorEnabled = true
            }, "Overlay", UIEditor);

            string title = session.IsCreate ? "CREATE KIT" : $"EDIT KIT: {session.EditKitId}";
            // Header bar - title and close at top only, no overlap with content
            var header = elements.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" }
            }, root, "KitsEditorHeader");
            elements.Add(new CuiLabel
            {
                Text = { Text = title, FontSize = 18, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.7 1" }
            }, header);
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.editorback", Color = ColDanger },
                RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, header);

            float y = 0.88f;
            AddEditorRow(elements, root, "Name", session.PendingName, "name", ref y, listRight);
            AddEditorRow(elements, root, "Description", session.PendingDesc, "desc", ref y, listRight);
            AddEditorRow(elements, root, "Cooldown (sec)", session.PendingCooldown.ToString(), "cooldown", ref y, listRight);
            AddEditorRow(elements, root, "Permission", string.IsNullOrEmpty(session.PendingPermission) ? "(optional)" : session.PendingPermission, "permission", ref y, listRight);

            y -= 0.05f;
            elements.Add(new CuiLabel
            {
                Text = { Text = "ITEMS", FontSize = 12, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"0.02 {y - 0.03f}", AnchorMax = $"0.3 {y}" }
            }, root);
            y -= 0.048f;
            // ADD ITEM button on its own row, full width of content area
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.additem", Color = ColAccent },
                RectTransform = { AnchorMin = $"0.02 {y - 0.042f}", AnchorMax = $"{listRight} {y}" },
                Text = { Text = "+ ADD ITEM", FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
            }, root);
            y -= 0.05f;

            for (int i = 0; i < session.PendingItems.Count; i++)
            {
                var it = session.PendingItems[i];
                string line = $"{it.ShortName} x{it.Amount}" + (it.SkinId != 0 ? $" (skin:{it.SkinId})" : "");
                var row = elements.Add(new CuiPanel
                {
                    Image = { Color = i % 2 == 0 ? ColRow : ColRowAlt },
                    RectTransform = { AnchorMin = $"0.02 {y - 0.038f}", AnchorMax = $"{listRight} {y}" }
                }, root);
                elements.Add(new CuiLabel
                {
                    Text = { Text = line, FontSize = 11, Align = TextAnchor.MiddleLeft },
                    RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.62 1" }
                }, row);
                elements.Add(new CuiButton
                {
                    Button = { Command = $"kit.edititemamount {i}", Color = "0.25 0.35 0.5 0.9" },
                    RectTransform = { AnchorMin = "0.64 0.15", AnchorMax = "0.78 0.85" },
                    Text = { Text = "EDIT", FontSize = 10, Align = TextAnchor.MiddleCenter }
                }, row);
                elements.Add(new CuiButton
                {
                    Button = { Command = $"kit.removeitem {i}", Color = ColDanger },
                    RectTransform = { AnchorMin = "0.8 0.15", AnchorMax = "0.98 0.85" },
                    Text = { Text = "REMOVE", FontSize = 10, Align = TextAnchor.MiddleCenter }
                }, row);
                y -= 0.042f;
            }

            y -= 0.02f;
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.editorsave", Color = ColButton },
                RectTransform = { AnchorMin = $"0.02 {y - 0.05f}", AnchorMax = $"0.3 {y}" },
                Text = { Text = "SAVE KIT", FontSize = 14, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
            }, root);

            elements.Add(new CuiButton
            {
                Button = { Command = "kit.editorback", Color = "0.3 0.3 0.3 0.9" },
                RectTransform = { AnchorMin = $"0.34 {y - 0.05f}", AnchorMax = $"0.5 {y}" },
                Text = { Text = "CANCEL", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, root);

            CuiHelper.AddUi(player, elements);
        }

        private void AddEditorRow(CuiElementContainer elements, string root, string label, string value, string fieldId, ref float y, float contentRight = 0.98f)
        {
            y -= 0.055f;
            var row = elements.Add(new CuiPanel
            {
                Image = { Color = ColRow },
                RectTransform = { AnchorMin = $"0.02 {y - 0.048f}", AnchorMax = $"{contentRight} {y}" }
            }, root);
            elements.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 12, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.22 1" }
            }, row);
            elements.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.24 0", AnchorMax = "0.72 1" }
            }, row);
            elements.Add(new CuiButton
            {
                Button = { Command = $"kit.editfield {fieldId}", Color = "0.25 0.35 0.5 0.9" },
                RectTransform = { AnchorMin = "0.76 0.15", AnchorMax = "0.98 0.85" },
                Text = { Text = "EDIT", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, row);
        }

        [ConsoleCommand("kit.editfield")]
        private void ConsoleEditField(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            session.CurrentField = arg.GetString(0);
            string current = session.CurrentField == "name" ? session.PendingName
                : session.CurrentField == "desc" ? session.PendingDesc
                : session.CurrentField == "cooldown" ? session.PendingCooldown.ToString()
                : session.CurrentField == "permission" ? session.PendingPermission : "";
            ShowInputPanel(player, $"Enter {session.CurrentField}:", current);
        }

        private void ShowInputPanel(BasePlayer player, string prompt, string currentValue, string command = "kit.inputsubmit")
        {
            CuiHelper.DestroyUi(player, UIInput);
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.85" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UIInput);
            var panel = elements.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.35 0.4", AnchorMax = "0.65 0.55" }
            }, root);
            elements.Add(new CuiLabel
            {
                Text = { Text = prompt, FontSize = 14, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.05 0.6", AnchorMax = "0.95 0.95" }
            }, panel);
            elements.Add(new CuiElement
            {
                Parent = panel,
                Components = {
                    new CuiInputFieldComponent { Command = command, FontSize = 14, CharsLimit = 120, Text = currentValue },
                    new CuiRectTransformComponent { AnchorMin = "0.05 0.2", AnchorMax = "0.95 0.55" }
                }
            });
            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("kit.inputsubmit")]
        private void ConsoleInputSubmit(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIInput);
            if (!_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;

            string value = GetFullInputString(arg).Trim();
            switch (session.CurrentField)
            {
                case "name":
                    session.PendingName = value.Trim().ToLower().Replace(" ", "_");
                    if (string.IsNullOrEmpty(session.PendingName)) session.PendingName = "kit";
                    break;
                case "desc":
                    session.PendingDesc = value.Trim();
                    break;
                case "cooldown":
                    if (int.TryParse(value, out int cd) && cd >= 0) session.PendingCooldown = cd;
                    break;
                case "permission":
                    session.PendingPermission = value.Trim();
                    break;
            }
            session.CurrentField = null;
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.additem")]
        private void ConsoleAddItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            session.ItemSearchTerm = "";
            ShowAddItemSearchUI(player);
        }

        [ConsoleCommand("kit.itemsearch")]
        private void ConsoleItemSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            session.ItemSearchTerm = GetFullInputString(arg).Trim();
            ShowAddItemSearchUI(player);
        }

        [ConsoleCommand("kit.additemselect")]
        private void ConsoleAddItemSelect(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            string shortname = arg.GetString(0);
            if (string.IsNullOrEmpty(shortname)) return;
            var def = ItemManager.FindItemDefinition(shortname);
            if (def == null) return;
            session.PendingAddShortname = def.shortname;
            CuiHelper.DestroyUi(player, UIAddItem);
            string label = def.displayName?.english ?? def.shortname;
            ShowInputPanel(player, $"Amount for {label}:", "1", "kit.additemamountsubmit");
        }

        [ConsoleCommand("kit.additemamountsubmit")]
        private void ConsoleAddItemAmountSubmit(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIInput);
            if (!_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            if (string.IsNullOrEmpty(session.PendingAddShortname)) return;
            string value = GetFullInputString(arg).Trim();
            if (!int.TryParse(value, out int amount) || amount < 1) amount = 1;
            session.PendingItems.Add(new KitItem { ShortName = session.PendingAddShortname, Amount = amount, SkinId = 0 });
            session.PendingAddShortname = "";
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.additemclose")]
        private void ConsoleAddItemClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIAddItem);
            if (_adminSessions.TryGetValue(player.userID, out AdminSession session))
                session.ItemSearchTerm = "";
            ShowEditorUI(player);
        }

        private void ShowAddItemSearchUI(BasePlayer player)
        {
            if (!_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;

            CuiHelper.DestroyUi(player, UIAddItem);

            const float listRight = 0.92f;
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.2 0.15", AnchorMax = "0.8 0.88" },
                CursorEnabled = true
            }, "Overlay", UIAddItem);

            // Header
            var header = elements.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.94", AnchorMax = "1 1" }
            }, root, "AddItemHeader");
            elements.Add(new CuiLabel
            {
                Text = { Text = "ADD ITEM — Search, click item, then set amount", FontSize = 16, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.75 1" }
            }, header);
            elements.Add(new CuiButton
            {
                Button = { Command = "kit.additemclose", Color = ColDanger },
                RectTransform = { AnchorMin = "0.94 0.1", AnchorMax = "0.99 0.9" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, header);

            // Search row
            elements.Add(new CuiLabel
            {
                Text = { Text = "Search:", FontSize = 12, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.02 0.88", AnchorMax = "0.12 0.92" }
            }, root);
            elements.Add(new CuiElement
            {
                Parent = root,
                Components = {
                    new CuiInputFieldComponent { Command = "kit.itemsearch", FontSize = 14, CharsLimit = 60, Text = session.ItemSearchTerm },
                    new CuiRectTransformComponent { AnchorMin = "0.14 0.88", AnchorMax = $"{listRight} 0.92" }
                }
            });

            // Item grid: closest matches from ItemManager
            string term = session.ItemSearchTerm ?? "";
            var itemList = ItemManager.itemList;
            var matches = itemList
                .Where(d => d.shortname != null &&
                    (string.IsNullOrWhiteSpace(term) ||
                     d.shortname.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (d.displayName?.english != null && d.displayName.english.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)))
                .OrderBy(d => d.shortname)
                .Take(48)
                .ToList();

            const int cols = 4;
            const float rowH = 0.058f;
            const float gap = 0.006f;
            float yMax = 0.86f;
            for (int i = 0; i < matches.Count; i++)
            {
                var def = matches[i];
                int row = i / cols, col = i % cols;
                float xMin = 0.02f + col * (listRight - 0.02f) / cols + (col > 0 ? gap * 0.5f : 0);
                float xMax = 0.02f + (col + 1) * (listRight - 0.02f) / cols - (col < cols - 1 ? gap * 0.5f : 0);
                float yMin = yMax - rowH - row * (rowH + gap);
                float yMaxCell = yMin + rowH;
                string label = def.displayName?.english ?? def.shortname;
                if (label.Length > 22) label = label.Substring(0, 19) + "...";
                string shortnameSafe = def.shortname?.Replace("\"", "") ?? "";
                elements.Add(new CuiButton
                {
                    Button = { Command = $"kit.additemselect {shortnameSafe}", Color = ColRow },
                    RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMaxCell}" },
                    Text = { Text = label, FontSize = 10, Align = TextAnchor.MiddleCenter }
                }, root);
            }

            if (matches.Count == 0)
            {
                elements.Add(new CuiLabel
                {
                    Text = { Text = "No items match your search.", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.7 0.7 0.7 1" },
                    RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = $"{listRight} 0.5" }
                }, root);
            }

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("kit.edititemamount")]
        private void ConsoleEditItemAmount(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            int idx = arg.GetInt(0);
            if (idx < 0 || idx >= session.PendingItems.Count) return;
            session.EditingItemIndex = idx;
            var it = session.PendingItems[idx];
            var def = ItemManager.FindItemDefinition(it.ShortName);
            string label = def?.displayName?.english ?? it.ShortName;
            ShowInputPanel(player, $"New amount for {label}:", it.Amount.ToString(), "kit.edititemamountsubmit");
        }

        [ConsoleCommand("kit.edititemamountsubmit")]
        private void ConsoleEditItemAmountSubmit(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIInput);
            if (!_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            if (session.EditingItemIndex < 0 || session.EditingItemIndex >= session.PendingItems.Count) return;
            string value = GetFullInputString(arg).Trim();
            if (int.TryParse(value, out int amount) && amount >= 1)
                session.PendingItems[session.EditingItemIndex].Amount = amount;
            session.EditingItemIndex = -1;
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.removeitem")]
        private void ConsoleRemoveItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;
            int idx = arg.GetInt(0);
            if (idx >= 0 && idx < session.PendingItems.Count)
            {
                session.PendingItems.RemoveAt(idx);
            }
            ShowEditorUI(player);
        }

        [ConsoleCommand("kit.editorback")]
        private void ConsoleEditorBack(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, UIEditor);
            CuiHelper.DestroyUi(player, UIInput);
            _adminSessions.Remove(player.userID);
            ShowAdminUI(player);
        }

        [ConsoleCommand("kit.editorsave")]
        private void ConsoleEditorSave(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !_adminSessions.TryGetValue(player.userID, out AdminSession session)) return;

            string name = session.PendingName.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(name)) { player.ChatMessage("Kit name cannot be empty."); return; }

            if (session.IsCreate && _config.Kits.ContainsKey(name))
            {
                player.ChatMessage($"A kit named '{name}' already exists.");
                return;
            }
            if (!session.IsCreate && session.EditKitId != name && _config.Kits.ContainsKey(name))
            {
                player.ChatMessage($"A kit named '{name}' already exists.");
                return;
            }

            var kit = new Kit
            {
                Description = session.PendingDesc,
                Cooldown = session.PendingCooldown,
                Permission = string.IsNullOrWhiteSpace(session.PendingPermission) ? null : session.PendingPermission.Trim(),
                Items = new List<KitItem>(session.PendingItems)
            };

            if (!session.IsCreate && session.EditKitId != name)
                _config.Kits.Remove(session.EditKitId);
            _config.Kits[name] = kit;
            SaveConfig();

            player.ChatMessage(session.IsCreate ? $"Kit '{name}' created." : $"Kit '{name}' saved.");
            CuiHelper.DestroyUi(player, UIEditor);
            _adminSessions.Remove(player.userID);
            ShowAdminUI(player);
        }
        #endregion

        #region Helpers
        /// <summary>Gets the full input string from a console command (joins all args so spaces work).</summary>
        private static string GetFullInputString(ConsoleSystem.Arg arg)
        {
            if (arg == null || !arg.HasArgs()) return "";
            var parts = new List<string>();
            for (int i = 0; i < 64; i++)
            {
                string s = arg.GetString(i);
                if (string.IsNullOrEmpty(s)) break;
                parts.Add(s);
            }
            return string.Join(" ", parts);
        }

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
