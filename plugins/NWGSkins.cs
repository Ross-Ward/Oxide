using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGSkins", "NWG Team", "1.0.0")]
    [Description("Simple UI-based Skin Manager.")]
    public class NWGSkins : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public Dictionary<string, List<ulong>> Skins = new Dictionary<string, List<ulong>>();
            public bool AllowAnyWorkshopSkin = false; // logic to allow arbitrary skin IDs
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
            Puts("Creating new configuration file for NWG Skins");
            _config = new PluginConfig();
            // Default skins for Rock
            _config.Skins["rock"] = new List<ulong> { 0, 101, 102 }; 
            SaveConfig();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "NWG_Skins_UI");
            }
        }
        #endregion

        #region Commands
        [ChatCommand("skin")]
        private void CmdSkin(BasePlayer player)
        {
            var item = player.GetActiveItem();
            if (item == null)
            {
                SendReply(player, "Hold an item to skin it.");
                return;
            }

            ShowSkinUI(player, item);
        }

        [ConsoleCommand("skin.apply")]
        private void ConsoleApplySkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            ulong skinId = arg.GetUInt64(0);
            var item = player.GetActiveItem();
            
            if (item == null) return;
            
            item.skin = skinId;
            item.MarkDirty();
            BaseEntity heldEntity = item.GetHeldEntity();
            if (heldEntity != null)
            {
                heldEntity.skinID = skinId;
                heldEntity.SendNetworkUpdate();
            }
            
            SendReply(player, "Skin applied.");
            CuiHelper.DestroyUi(player, "NWG_Skins_UI");
        }

        [ConsoleCommand("skin.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, "NWG_Skins_UI");
        }
        #endregion

        #region UI
        private void ShowSkinUI(BasePlayer player, Item item)
        {
            CuiHelper.DestroyUi(player, "NWG_Skins_UI");
            
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.25 0.25", AnchorMax = "0.75 0.75" },
                CursorEnabled = true
            }, "Overlay", "NWG_Skins_UI");

            // Header Background
            elements.Add(new CuiPanel {
                Image = { Color = "0.4 0.6 0.2 0.3" },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 1" }
            }, root);

            // Header Title
            elements.Add(new CuiLabel {
                Text = { Text = $"SKINS: {item.info.displayName.translated.ToUpper()}", FontSize = 20, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.05 0.88", AnchorMax = "1 1" }
            }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "skin.close", Color = "0.8 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.92 0.9", AnchorMax = "0.98 0.98" },
                Text = { Text = "✕", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, root);

            List<ulong> skins = new List<ulong> { 0 }; // Default
            if (_config.Skins.TryGetValue(item.info.shortname, out var confSkins))
            {
                skins.AddRange(confSkins);
            }
            
            // Grid
            int cols = 4;
            float startX = 0.05f;
            float startY = 0.82f;
            float width = 0.21f;
            float height = 0.18f;
            float gap = 0.02f;

            for(int i=0; i<skins.Count; i++)
            {
                ulong skinId = skins[i];
                int r = i / cols;
                int c = i % cols;
                
                float xMin = startX + c * (width + gap);
                float yMax = startY - r * (height + gap);

                if (yMax < 0.1) break; 

                var pnl = elements.Add(new CuiPanel {
                    Image = { Color = "0.15 0.15 0.15 0.8" },
                    RectTransform = { AnchorMin = $"{xMin} {yMax - height}", AnchorMax = $"{xMin + width} {yMax}" }
                }, root);

                string txt = (skinId == 0) ? "DEFAULT" : $"{skinId}";

                elements.Add(new CuiButton {
                    Button = { Command = $"skin.apply {skinId}", Color = "0.4 0.6 0.2 0.8" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = txt, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 14 }
                }, pnl);
            }

            CuiHelper.AddUi(player, elements);
        }
        #endregion
    }
}

