// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Rust;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWGInfo", "NWG Team", "1.0.0")]
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
            if (player != null) CuiHelper.DestroyUi(player, "NWG_Info_UI");
        }

        [ConsoleCommand("nwg_info.tab")]
        private void ConsoleTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int index = arg.GetInt(0);
            ShowInfoUI(player, index);
        }

        private void ShowInfoUI(BasePlayer player, int tabIndex = 0)
        {
            CuiHelper.DestroyUi(player, "NWG_Info_UI");
            
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
                
                var contentPanel = elements.Add(new CuiPanel {
                    Image = { Color = "0.15 0.15 0.15 0.8" },
                    RectTransform = { AnchorMin = "0.22 0.05", AnchorMax = "0.98 0.88" }
                }, root);

                elements.Add(new CuiLabel {
                    Text = { Text = tab.Title, FontSize = 22, Align = TextAnchor.UpperCenter, Font = "robotocondensed-bold.ttf", Color = "#b7d092" },
                    RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
                }, contentPanel);

                string fullText = string.Join("\n", tab.Lines);
                elements.Add(new CuiLabel {
                    Text = { Text = fullText, FontSize = 16, Align = TextAnchor.UpperLeft, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.88" }
                }, contentPanel);
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

