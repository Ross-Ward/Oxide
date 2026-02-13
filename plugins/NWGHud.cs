using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("NWGHUD", "NWG Team", "4.0.0")]
    [Description("Clean top HUD bar below compass showing player identity, clan, grid, bearing, time, and balance.")]
    public class NWGHud : RustPlugin
    {
        #region References
        [PluginReference] private Plugin NWGCore;
        [PluginReference] private Plugin NWGClans;
        [PluginReference] private Plugin NWGSkills;
        [PluginReference] private Plugin Economics;
        #endregion

        #region Configuration
        private class PluginConfig
        {
            public float UpdateInterval = 1.0f;
            public bool ShowPlayerName = true;
            public bool ShowClan = true;
            public bool ShowGrid = true;
            public bool ShowBearing = true;
            public bool ShowTime = true;
            public bool ShowBalance = true;
            public bool ShowOnlinePlayers = true;

            public string PanelBgColor   = "0 0 0 0.45";
            public string AccentColor    = "#b7d092";   // Light sage green
            public string ClanColor      = "#FFA500";   // Orange for clan tag
            public string TextColor      = "#DDDDDD";   // Soft white
            public string SeparatorColor = "#555555";   // Dim separator
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
            Puts("Creating new configuration file for NWG HUD v4");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerConnected(player);
            }
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

        #region HUD Drawing
        private void UpdateAllHuds()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!_activePlayers.Contains(player.userID)) continue;
                DrawHud(player);
            }
        }

        private void DrawHud(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LayerName);
            var container = new CuiElementContainer();

            // ── Root panel: sits just below the compass ──
            // Compass occupies roughly 0.35-0.65 horizontally, top ~0.96-1.0
            // We position our bar at the same width, just beneath it
            container.Add(new CuiPanel
            {
                Image = { Color = _config.PanelBgColor },
                RectTransform = { AnchorMin = "0.335 0.945", AnchorMax = "0.665 0.97" },
                CursorEnabled = false
            }, "Overlay", LayerName);

            // ── Build the single-line content ──
            string line = BuildLine(player);

            container.Add(new CuiLabel
            {
                Text = {
                    Text = line,
                    Font = "robotocondensed-regular.ttf",
                    FontSize = 10,
                    Align = TextAnchor.MiddleCenter,
                    Color = "1 1 1 0.85"
                },
                RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
            }, LayerName);

            CuiHelper.AddUi(player, container);
        }

        private string BuildLine(BasePlayer player)
        {
            var sep = $" <color={_config.SeparatorColor}>│</color> ";
            var parts = new List<string>();

            // ── Player identity: Name + Clan ──
            string identity = "";
            string clanTag = GetClanTag(player.userID);
            if (!string.IsNullOrEmpty(clanTag)) identity += $"<color={_config.ClanColor}>[{clanTag}]</color> ";
            identity += $"<color={_config.TextColor}>{player.displayName}</color>";
            parts.Add(identity.Trim());

            // ── Grid position ──
            parts.Add($"<color={_config.AccentColor}>GRID</color> {GetGrid(player.transform.position)}");

            // ── Balance ──
            if (Economics != null && Economics.IsLoaded)
            {
                var bal = Economics.Call("Balance", player.UserIDString);
                if (bal != null) parts.Add($"<color={_config.AccentColor}>$</color>{Convert.ToDouble(bal):N0}");
            }

            // ── Player Level ──
            if (NWGSkills != null && NWGSkills.IsLoaded)
            {
                var level = NWGSkills.Call("GetLevel", player.userID);
                parts.Add($"<color={_config.AccentColor}>LVL</color> {level ?? 1}");
            }

            // ── Online players ──
            parts.Add($"<color={_config.AccentColor}>{BasePlayer.activePlayerList.Count}</color> online");

            return string.Join(sep, parts);
        }
        #endregion

        #region Helpers
        private string GetClanTag(ulong playerId)
        {
            if (NWGClans == null || !NWGClans.IsLoaded) return null;
            return NWGClans.Call<string>("GetClanTag", playerId);
        }

        private string GetGrid(Vector3 pos)
        {
            float worldSize = ConVar.Server.worldsize;
            float offset = worldSize / 2f;
            const float cellSize = 150f;
            int maxCell = (int)(worldSize / cellSize) - 1;
            int x = Mathf.Clamp(Mathf.FloorToInt((pos.x + offset) / cellSize), 0, maxCell);
            int z = Mathf.Clamp(Mathf.FloorToInt((pos.z + offset) / cellSize), 0, maxCell);
            // Convert x to letter(s): 0=A, 25=Z, 26=AA, etc.
            string col = "";
            int cx = x;
            do
            {
                col = (char)('A' + cx % 26) + col;
                cx = cx / 26 - 1;
            } while (cx >= 0);
            return $"{col}{z}";
        }

        private string GetCardinal(float yaw)
        {
            // Normalize to 0-360
            yaw = (yaw % 360 + 360) % 360;
            if (yaw >= 337.5f || yaw < 22.5f)  return "N";
            if (yaw < 67.5f)  return "NE";
            if (yaw < 112.5f) return "E";
            if (yaw < 157.5f) return "SE";
            if (yaw < 202.5f) return "S";
            if (yaw < 247.5f) return "SW";
            if (yaw < 292.5f) return "W";
            return "NW";
        }

        private int GetEntityCount(string type)
        {
            if (NWGCore == null || !NWGCore.IsLoaded) return 0;
            object result = NWGCore.Call("GetEntityCount", type);
            return result is int count ? count : 0;
        }
        #endregion
    }
}

