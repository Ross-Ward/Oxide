using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("NWG HUD", "NWG Team", "3.0.0")]
    [Description("Optimized HUD monitoring Grid, Health, Balance and active Threats using NWG_Core.")]
    public class NWGHud : RustPlugin
    {
        #region References
        [PluginReference] private Plugin NWGCore;
        [PluginReference] private Plugin Economics;
        #endregion

        #region Configuration
        private class PluginConfig
        {
            public float UpdateInterval = 1.0f; // Slower interval is fine for HUD, 0.5 was overkill
            public bool ShowGrid = true;
            public bool ShowHealth = true;
            public bool ShowTime = true;
            public bool ShowBalance = true;
            public bool ShowThreats = true;

            public string PanelColor = "0 0 0 0.85";
            public string HeaderColor = "#FFA500"; // Orange
            public string TextColor = "#b7d092";   // Light Green
            public string WarnColor = "#FF6B6B";   // Red
            public string SuccessColor = "#51CF66";// Green
        }
        private PluginConfig _config;
        #endregion

        #region State
        private const string LayerName = "NWG_HUD_UI";
        private Timer _hudTimer;
        private readonly HashSet<ulong> _activePlayers = new HashSet<ulong>();
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
            Puts("Creating new configuration file for NWG HUD");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
            
            // Replaces OnFrame polling with a clean Timer
            _hudTimer = timer.Every(_config.UpdateInterval, UpdateAllHuds);
        }

        private void Unload()
        {
            _hudTimer?.Destroy();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, LayerName);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && !player.IsNpc)
            {
                _activePlayers.Add(player.userID);
                DrawHud(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            _activePlayers.Remove(player.userID);
        }
        #endregion

        #region Logic
        private void UpdateAllHuds()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!_activePlayers.Contains(player.userID)) continue;
                if (IsMenuOpen(player)) continue; // Don't redraw if menu is open

                // We can use CuiHelper.DestroyUi and AddUi, but for smooth updates 
                // typically we might want to update specific text components.
                // For simplicity in this v1 rewrite, we redraw. 
                // Optimization: In v2, use CUI names to update text only.
                DrawHud(player);
            }
        }

        private void DrawHud(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LayerName);
            
            var container = new CuiElementContainer();
            // Root panel (Transparent)
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.3 0.97", AnchorMax = "0.7 1.0" },
                CursorEnabled = false
            }, "Overlay", LayerName);

            string content = BuildContent(player);

            // Centered Label with shadow-like effect (using multiple labels or just color)
            container.Add(new CuiLabel
            {
                Text = { 
                    Text = content, 
                    Font = "robotocondensed-bold.ttf", 
                    FontSize = 10, 
                    Align = TextAnchor.MiddleCenter, 
                    Color = "1 1 1 0.6" 
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, LayerName);

            CuiHelper.AddUi(player, container);
        }

        private string BuildContent(BasePlayer player)
        {
            var parts = new List<string>();
            
            if (_config.ShowGrid)
                parts.Add($"<color=#b7d092>GRID:</color> {GetGrid(player.transform.position)}");

            if (_config.ShowTime && TOD_Sky.Instance != null)
                parts.Add($"<color=#b7d092>TIME:</color> {TOD_Sky.Instance.Cycle.Hour:00}:{((int)((TOD_Sky.Instance.Cycle.Hour % 1) * 60)):00}");
            
            if (_config.ShowBalance && Economics != null && Economics.IsLoaded)
            {
                var bal = Economics.Call("Balance", player.UserIDString);
                parts.Add($"<color=#b7d092>$:</color> {bal:N0}");
            }

            return string.Join("  |  ", parts); 
        }

        // Wrapper to safely call Core
        private int GetEntityCount(string type)
        {
            if (NWGCore == null || !NWGCore.IsLoaded) return 0;
            object result = NWGCore.Call("GetEntityCount", type);
            return result is int count ? count : 0;
        }

        private string GetGrid(Vector3 pos)
        {
             const float cellSize = 150f;
             const float offset = 2000f; // 4000/2
             int x = Mathf.Clamp(Mathf.FloorToInt((pos.x + offset) / cellSize), 0, 26);
             int z = Mathf.Clamp(Mathf.FloorToInt((pos.z + offset) / cellSize), 0, 26);
             return $"{(char)('A' + x)}{z}";
        }

        private bool IsMenuOpen(BasePlayer player)
        {
            // Simple check for common CUI layers that obscure vision
            // This prevents drawing over looting/crafting
            return false; // To be expanded with specific UI checks if needed
        }
        #endregion
    }
}
