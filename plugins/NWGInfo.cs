// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWG Info", "NWG Team", "1.0.0")]
    [Description("Unified Server Info UI and Map Marker Manager.")]
    public class NWGInfo : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public bool ShowOnJoin = true;
            public List<InfoTab> Tabs = new List<InfoTab>();
            public List<MapMarkerConfig> PermanentMarkers = new List<MapMarkerConfig>();
        }

        private class InfoTab
        {
            public string Name; // Button Text
            public string Title; // Header Text
            public List<string> Lines = new List<string>();
        }

        private class MapMarkerConfig
        {
            public string Name;
            public string DisplayName;
            public float X;
            public float Z;
            public float Radius;
            public string ColorHex; // RRGGBB
        }

        private PluginConfig _config;

        // Scroll state per player (reset on tab change or close)
        private readonly Dictionary<ulong, float> _playerScroll = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, int> _playerTabIndex = new Dictionary<ulong, int>();
        private const float ScrollStep = 0.2f;   // viewport units per click
        private const float LineHeight = 0.052f; // used for scroll range
        private const int LinesPerPage = 18;      // only this many lines rendered at once so content never overflows
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
                if (_config == null || _config.Tabs.Count == 0)
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
            Puts("Creating new configuration file for NWG Info");
            _config = new PluginConfig();
            
            _config.Tabs.Add(new InfoTab 
            { 
                Name = "Welcome", 
                Title = "Welcome to NWG RUST", 
                Lines = new List<string> { 
                    "Welcome to our server!", 
                    "We provide a premium, customized experience.", 
                    "Check the 'Commands' tab to get started." 
                } 
            });

            _config.Tabs.Add(new InfoTab 
            { 
                Name = "Rules", 
                Title = "Server Rules", 
                Lines = new List<string> { 
                    "1. No cheating or scripting.", 
                    "2. No extreme toxicity or racism.", 
                    "3. Group limit: 8 per base/roam.",
                    "4. Respect staff decisions."
                } 
            });

            _config.Tabs.Add(new InfoTab 
            { 
                Name = "Commands", 
                Title = "Player Commands", 
                Lines = new List<string> { 
                    "<color=#b7d092>/shop</color> - Open the economics store",
                    "<color=#b7d092>/info</color> - Show this information menu",
                    "<color=#b7d092>/kit</color> - Claim a starting or reward kit",
                    "<color=#b7d092>/skin</color> - Change item skins (Hold item)",
                    "<color=#b7d092>/balance</color> - Check your wallet",
                    "<color=#b7d092>/sethome <name></color> - Set your current position as a home",
                    "<color=#b7d092>/home <name></color> - Teleport to your home",
                    "<color=#b7d092>/tpr <name></color> - Send a teleport request to a player",
                    "<color=#b7d092>/tpa</color> - Accept a pending teleport request",
                    "<color=#b7d092>/warp <name></color> - Teleport to a server location",
                    "<color=#b7d092>/setautocode <code></color> - Set your automatic lock code"
                } 
            });

            _config.Tabs.Add(new InfoTab 
            { 
                Name = "Admin", 
                Title = "Admin Commands", 
                Lines = new List<string> { 
                    "<color=#FF6B6B>/tools</color> - Open admin tool menu",
                    "<color=#FF6B6B>/setadminpass <pass></color> - Secure your admin session",
                    "<color=#FF6B6B>/adminduty</color> - Toggle Admin Duty",
                    "<color=#FF6B6B>/setbalance <player> <amt></color> - Set player balance",
                    "<color=#FF6B6B>/givemoney <player> <amt></color> - Give money to player",
                    "<color=#FF6B6B>/settime <0-24></color> - Set server time",
                    "<color=#FF6B6B>/setweather <type></color> - Set server weather",
                    "<color=#FF6B6B>/dungeon start global</color> - Start Global Dungeon",
                    "<color=#FF6B6B>/startraid</color> - Start Base Raid Event",
                    "<color=#FF6B6B>/spawnpiracy</color> - Spawn Pirate Tugboat",
                    "<color=#FF6B6B>/startrace</color> - Start Racing Event",
                    "<color=#FF6B6B>/tp <name></color> - Teleport to player",
                    "<color=#FF6B6B>/god</color> - Toggle god mode",
                    "<color=#FF6B6B>/vanish</color> - Toggle invisibility"
                } 
            });

            SaveConfig();
        }

        private void OnServerInitialized()
        {
            SpawnPermanentMarkers();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "NWG_Info_UI");
            }
            CleanupMarkers();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (_config.ShowOnJoin)
            {
                timer.Once(2f, () => 
                {
                    if (player != null && player.IsConnected) ShowInfoUI(player);
                });
            }
        }
        #endregion

        #region Command Scraping
        // Commands that appear in the Admin tab (all others go to Player Commands)
        private static readonly HashSet<string> AdminCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "setadminpass", "login", "vanish", "radar", "god", "kick", "ban", "unban", "mute", "unmute",
            "freeze", "unfreeze", "strip", "whois", "heal", "tp", "tphere", "up", "repair", "entkill",
            "settime", "setweather", "spawnhere", "rocket", "cmdlist", "testall", "tasks",
            "adminduty", "tools", "grantvip", "revokevip", "bring", "invsee",
            "startraid", "spawnpiracy", "startrace", "setbalance", "givemoney", "setwarp", "remove", "dungeon", "adminhelp"
        };

        private struct ScrapedCommand { public string Command; public string PluginName; }

        private List<ScrapedCommand> GetAllPluginChatCommands()
        {
            var list = new List<ScrapedCommand>();
            try
            {
                var plugins = Interface.Oxide.RootPluginManager.GetPlugins();
                if (plugins == null) return list;
                foreach (var plugin in plugins)
                {
                    if (plugin == null || !plugin.IsLoaded) continue;
                    var type = plugin.GetType();
                    if (type == null) continue;
                    string pluginName = plugin.Name ?? type.Name;
                    var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (methods == null) continue;
                    foreach (var method in methods)
                    {
                        var attr = method.GetCustomAttributes(typeof(ChatCommandAttribute), true).FirstOrDefault() as ChatCommandAttribute;
                        if (attr != null && !string.IsNullOrEmpty(attr.Command))
                            list.Add(new ScrapedCommand { Command = attr.Command.Trim().ToLower(), PluginName = pluginName });
                    }
                }
                return list.GroupBy(x => x.Command).Select(g => g.First()).OrderBy(x => x.Command).ToList();
            }
            catch (Exception ex) { Puts($"NWGInfo command scrape error: {ex.Message}"); }
            return list;
        }

        private List<string> GetPlayerCommandLines()
        {
            const string color = "#b7d092";
            var lines = new List<string>();
            foreach (var c in GetAllPluginChatCommands())
            {
                if (AdminCommandNames.Contains(c.Command)) continue;
                lines.Add($"<color={color}>/{c.Command}</color> - {GetCommandDescription(c.Command, c.PluginName, false)}");
            }
            return lines.Count > 0 ? lines : new List<string> { "<color=#b7d092>(No player commands loaded)</color>" };
        }

        private List<string> GetAdminCommandLines()
        {
            const string color = "#FF6B6B";
            var lines = new List<string>();
            foreach (var c in GetAllPluginChatCommands())
            {
                if (!AdminCommandNames.Contains(c.Command)) continue;
                lines.Add($"<color={color}>/{c.Command}</color> - {GetCommandDescription(c.Command, c.PluginName, true)}");
            }
            return lines.Count > 0 ? lines : new List<string> { "<color=#FF6B6B>(No admin commands loaded)</color>" };
        }

        private static string GetCommandDescription(string command, string pluginName, bool admin)
        {
            var desc = GetKnownDescription(command);
            return !string.IsNullOrEmpty(desc) ? desc : (admin ? "Admin" : pluginName);
        }

        private static string GetKnownDescription(string command)
        {
            switch (command.ToLowerInvariant())
            {
                case "info": return "Show this information menu";
                case "help": return "Show commands tab";
                case "shop": return "Open the economics store";
                case "balance": return "Check your wallet";
                case "kit": return "Claim a starting or reward kit";
                case "skin": return "Change item skins (hold item)";
                case "sethome": return "Set your current position as a home";
                case "home": return "Teleport to your home";
                case "tpr": return "Send a teleport request to a player";
                case "tpa": return "Accept a pending teleport request";
                case "warp": return "Teleport to a server location";
                case "setautocode": return "Set your automatic lock code";
                case "clan": return "Clan management";
                case "quests": return "View and manage quests";
                case "skills": return "View and upgrade skills";
                case "rates": return "View gather and XP rates";
                case "event": return "Event information";
                case "zone": return "Zone information";
                case "tools": return "Open admin tool menu";
                case "setadminpass": return "Secure your admin session";
                case "adminduty": return "Toggle Admin Duty";
                case "setbalance": return "Set player balance";
                case "givemoney": return "Give money to player";
                case "settime": return "Set server time (0-24)";
                case "setweather": return "Set server weather";
                case "dungeon": return "Dungeon events (e.g. start global)";
                case "startraid": return "Start Base Raid Event";
                case "spawnpiracy": return "Spawn Pirate Tugboat";
                case "startrace": return "Start Racing Event";
                case "tp": return "Teleport to player";
                case "god": return "Toggle god mode";
                case "vanish": return "Toggle invisibility";
                case "kick": return "Kick a player";
                case "ban": return "Ban a player";
                case "unban": return "Unban a player";
                case "mute": return "Mute a player";
                case "unmute": return "Unmute a player";
                case "bring": return "Bring a player to you";
                case "heal": return "Heal a player";
                case "invsee": return "View player inventory";
                case "remove": return "Remove building (admin)";
                case "setwarp": return "Create a warp point";
                default: return null;
            }
        }
        #endregion

        #region Info UI
        [ChatCommand("info")]
        private void CmdInfo(BasePlayer player)
        {
            ShowInfoUI(player);
        }

        [ChatCommand("help")]
        private void CmdHelp(BasePlayer player)
        {
            int cmdTab = _config.Tabs.FindIndex(t => t.Name.Equals("Commands", StringComparison.OrdinalIgnoreCase));
            ShowInfoUI(player, cmdTab >= 0 ? cmdTab : 0);
        }

        [ChatCommand("adminhelp")]
        private void CmdAdminHelp(BasePlayer player)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }
            int adminTab = _config.Tabs.FindIndex(t => t.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));
            ShowInfoUI(player, adminTab >= 0 ? adminTab : 0);
        }

        [ConsoleCommand("nwg_info.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
            {
                CuiHelper.DestroyUi(player, "NWG_Info_UI");
                _playerScroll.Remove(player.userID);
                _playerTabIndex.Remove(player.userID);
            }
        }

        [ConsoleCommand("nwg_info.tab")]
        private void ConsoleTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int index = arg.GetInt(0);
            ShowInfoUI(player, index);
        }

        [ConsoleCommand("nwg_info.scroll")]
        private void ConsoleScroll(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int direction = arg.GetInt(0); // 1 = scroll down (see more below), -1 = scroll up
            if (direction == 0) return;
            if (!_playerTabIndex.TryGetValue(player.userID, out int tabIndex)) return;
            int lineCount = GetContentLineCountForTab(tabIndex);
            float maxScroll = Math.Max(0f, (lineCount - LinesPerPage) * LineHeight);
            float scroll = _playerScroll.TryGetValue(player.userID, out float s) ? s : 0f;
            scroll += direction * ScrollStep;
            scroll = Math.Max(0f, Math.Min(scroll, maxScroll));
            _playerScroll[player.userID] = scroll;
            ShowInfoUI(player, tabIndex);
        }

        private int GetContentLineCountForTab(int tabIndex)
        {
            if (tabIndex < 0 || tabIndex >= _config.Tabs.Count) return 0;
            var tab = _config.Tabs[tabIndex];
            if (tab.Name != null && tab.Name.Equals("Commands", StringComparison.OrdinalIgnoreCase))
                return GetPlayerCommandLines().Count;
            if (tab.Name != null && tab.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return GetAdminCommandLines().Count;
            return (tab.Lines ?? new List<string>()).Count;
        }

        private void ShowInfoUI(BasePlayer player, int tabIndex = 0)
        {
            CuiHelper.DestroyUi(player, "NWG_Info_UI");

            if (_playerTabIndex.TryGetValue(player.userID, out int prevTab) && prevTab != tabIndex)
                _playerScroll.Remove(player.userID);
            _playerTabIndex[player.userID] = tabIndex;

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Info_UI");

            // Header Background
            elements.Add(new CuiPanel {
                Image = { Color = "0.4 0.6 0.2 0.3" },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, root);

            // Header Title
            elements.Add(new CuiLabel {
                Text = { Text = "SERVER INFORMATION", FontSize = 28, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.5 1" }
            }, root);

            // Sidebar Background
            elements.Add(new CuiPanel {
                Image = { Color = "0.1 0.1 0.1 0.5" },
                RectTransform = { AnchorMin = "0 0.08", AnchorMax = "0.2 0.9" }
            }, root);

            // Tabs
            float btnHeight = 0.07f;
            float btnGap = 0.01f;
            float startY = 0.82f;

            for(int i=0; i<_config.Tabs.Count; i++)
            {
                var tab = _config.Tabs[i];
                bool active = (i == tabIndex);
                string color = active ? "0.4 0.6 0.2 0.8" : "0.2 0.2 0.2 0.5";

                elements.Add(new CuiButton {
                    Button = { Command = $"nwg_info.tab {i}", Color = color },
                    RectTransform = { AnchorMin = $"0.01 {startY - btnHeight}", AnchorMax = $"0.19 {startY}" },
                    Text = { Text = tab.Name.ToUpper(), Align = TextAnchor.MiddleCenter, FontSize = 13, Font = "robotocondensed-bold.ttf" }
                }, root);

                startY -= (btnHeight + btnGap);
            }

            // Close Button (Standardized Position)
            elements.Add(new CuiButton {
                Button = { Command = "nwg_info.close", Color = "0.8 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.95 0.925", AnchorMax = "0.985 0.975" },
                Text = { Text = "✕", FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, root);

            // Content Area
            if (tabIndex >= 0 && tabIndex < _config.Tabs.Count)
            {
                var tab = _config.Tabs[tabIndex];
                List<string> contentLines;
                if (tab.Name != null && tab.Name.Equals("Commands", StringComparison.OrdinalIgnoreCase))
                    contentLines = GetPlayerCommandLines();
                else if (tab.Name != null && tab.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    contentLines = GetAdminCommandLines();
                else
                    contentLines = tab.Lines ?? new List<string>();

                var contentPanel = elements.Add(new CuiPanel {
                    Image = { Color = "0.15 0.15 0.15 0.8" },
                    RectTransform = { AnchorMin = "0.22 0.05", AnchorMax = "0.98 0.88" }
                }, root);

                elements.Add(new CuiLabel {
                    Text = { Text = tab.Title, FontSize = 22, Align = TextAnchor.UpperCenter, Font = "robotocondensed-bold.ttf", Color = "#b7d092" },
                    RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
                }, contentPanel);

                int lineCount = contentLines.Count > 0 ? contentLines.Count : 1;
                float maxScroll = Math.Max(0f, (lineCount - LinesPerPage) * LineHeight);
                float scroll = _playerScroll.TryGetValue(player.userID, out float s) ? Math.Max(0f, Math.Min(s, maxScroll)) : 0f;
                _playerScroll[player.userID] = scroll;

                // Only render visible lines so content stays inside the UI (no overflow)
                int startIndex = (int)(scroll / LineHeight);
                if (startIndex > lineCount - LinesPerPage) startIndex = Math.Max(0, lineCount - LinesPerPage);
                int endIndex = Math.Min(startIndex + LinesPerPage, lineCount);
                var visibleLines = new List<string>();
                for (int i = startIndex; i < endIndex; i++) visibleLines.Add(contentLines[i]);
                string fullText = visibleLines.Count > 0 ? string.Join("\n", visibleLines) : "";

                var viewport = elements.Add(new CuiPanel {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.92 0.88" }
                }, contentPanel);

                var scrollContent = elements.Add(new CuiPanel {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, viewport);

                elements.Add(new CuiLabel {
                    Text = { Text = fullText, FontSize = 16, Align = TextAnchor.UpperLeft, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, scrollContent);

                if (maxScroll > 0.001f)
                {
                    elements.Add(new CuiButton {
                        Button = { Command = "nwg_info.scroll -1", Color = "0.25 0.25 0.25 0.9" },
                        RectTransform = { AnchorMin = "0.93 0.75", AnchorMax = "0.98 0.88" },
                        Text = { Text = "▲", FontSize = 18, Align = TextAnchor.MiddleCenter }
                    }, contentPanel);
                    elements.Add(new CuiButton {
                        Button = { Command = "nwg_info.scroll 1", Color = "0.25 0.25 0.25 0.9" },
                        RectTransform = { AnchorMin = "0.93 0.05", AnchorMax = "0.98 0.18" },
                        Text = { Text = "▼", FontSize = 18, Align = TextAnchor.MiddleCenter }
                    }, contentPanel);
                }
            }

            CuiHelper.AddUi(player, elements);
        }
        #endregion

        #region Map Markers
        private List<BaseEntity> _spawnedMarkers = new List<BaseEntity>();

        private void SpawnPermanentMarkers()
        {
            CleanupMarkers();
            foreach(var markerCfg in _config.PermanentMarkers)
            {
                CreateMarker(new Vector3(markerCfg.X, 0, markerCfg.Z), markerCfg.Radius, markerCfg.DisplayName, markerCfg.ColorHex);
            }
        }

        private void CleanupMarkers()
        {
            foreach(var ent in _spawnedMarkers)
            {
                if (ent != null && !ent.IsDestroyed) ent.Kill();
            }
            _spawnedMarkers.Clear();
        }

        // Public API
        public void CreateMarker(Vector3 pos, float radius, string label, string colorHex)
        {
            // Vending Machine Marker
            var vmPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
            var vm = GameManager.server.CreateEntity(vmPrefab, pos) as VendingMachineMapMarker;
            if (vm == null) return;
            
            vm.markerShopName = label;
            vm.enableSaving = false;
            vm.Spawn();
            _spawnedMarkers.Add(vm);

            // Radius (Generic)
            var radiusPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
            var rad = GameManager.server.CreateEntity(radiusPrefab, pos) as MapMarkerGenericRadius;
            if (rad == null) return;

            rad.alpha = 0.5f;
            rad.radius = radius;
            Color color = Color.cyan;
            ColorUtility.TryParseHtmlString("#" + colorHex, out color);
            rad.color1 = color;
            rad.color2 = color;
            rad.enableSaving = false;
            rad.SetParent(vm); // Bind location
            rad.Spawn();
            // Note: MapMarkerGenericRadius usually needs to be updated or sent? 
            // VendingMachineMapMarker handles the map dot.
            // GenericRadius handles the circle.
        }
        #endregion
    }
}

