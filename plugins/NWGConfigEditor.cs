using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGConfigEditor", "NWG Team", "2.0.0")]
    [Description("In-game deep config editor supporting nested objects and lists.")]
    public class NWGConfigEditor : RustPlugin
    {
        #region Constants
        private const string PermUse = "nwgconfigeditor.use";
        private const string PanelEditor = "NWGCfg_Editor";
        private const string PanelInput = "NWGCfg_Input";
        private const string PanelMain = "NWGCfg_Main";

        private const string ColBg = "0.05 0.05 0.07 0.98";
        private const string ColHeader = "0.1 0.1 0.15 1";
        private const string ColRow1 = "0.15 0.15 0.2 0.7";
        private const string ColRow2 = "0.12 0.12 0.17 0.7";
        private const string ColAccent = "0.35 0.6 1 1";
        #endregion

        #region Session State
        private class EditorSession
        {
            public string PluginName;
            public JObject Config;
            public string NavigationPath = ""; // e.g. "Categories.0.Items"
            public int Page = 1;
            public string EditingKey;
        }

        private readonly Dictionary<ulong, EditorSession> _sessions = new Dictionary<ulong, EditorSession>();
        #endregion

        #region Lifecycle
        private void Init() => permission.RegisterPermission(PermUse, this);
        private void Unload() { foreach (var p in BasePlayer.activePlayerList) CloseAll(p); }
        #endregion

        #region Commands
        [ChatCommand("config")]
        private void CmdConfig(BasePlayer player) { if (HasPerm(player)) ShowPluginList(player, 1); }
        #endregion

        #region UI: Plugin List
        private void ShowPluginList(BasePlayer player, int page)
        {
            CloseAll(player);
            var configs = Directory.GetFiles(Interface.Oxide.ConfigDirectory, "NWG*.json").Select(Path.GetFileNameWithoutExtension).ToList();
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.9" }, CursorEnabled = true }, "Overlay", PanelMain);
            
            container.Add(new CuiLabel { Text = { Text = "NWG CONFIG FILES", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = ColAccent }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, root);

            for (int i = 0; i < configs.Count; i++)
            {
                float y = 0.85f - (i * 0.06f);
                var name = configs[i];
                container.Add(new CuiButton {
                    Button = { Command = $"nwgcfg.open {name}", Color = "0.2 0.4 0.6 0.8" },
                    RectTransform = { AnchorMin = $"0.05 {y-0.05f}", AnchorMax = $"0.95 {y}" },
                    Text = { Text = name.Replace("NWG", "NWG "), Align = TextAnchor.MiddleLeft }
                }, root);
            }
            CuiHelper.AddUi(player, container);
        }
        #endregion

        #region UI: Deep Editor
        private void ShowEditor(BasePlayer player)
        {
            var session = GetSession(player);
            if (session == null) return;

            CloseAll(player);
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" }, CursorEnabled = true }, "Overlay", PanelEditor);

            // Header
            container.Add(new CuiPanel { Image = { Color = ColHeader }, RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" } }, root);
            container.Add(new CuiLabel { Text = { Text = $"EDITING: {session.PluginName} > {session.NavigationPath}", FontSize = 14, Color = ColAccent }, RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.7 1" } }, root);
            
            container.Add(new CuiButton { Button = { Command = "nwgcfg.up", Color = "0.3 0.3 0.3 0.8" }, RectTransform = { AnchorMin = "0.01 0.1", AnchorMax = "0.08 0.9" }, Text = { Text = "UP" } }, root + "_Header");
            container.Add(new CuiButton { Button = { Command = "nwgcfg.save", Color = "0.2 0.6 0.2 0.8" }, RectTransform = { AnchorMin = "0.8 0.1", AnchorMax = "0.9 0.9" }, Text = { Text = "SAVE" } }, root + "_Header");
            container.Add(new CuiButton { Button = { Command = "nwgcfg.close", Color = "0.6 0.2 0.2 0.8" }, RectTransform = { AnchorMin = "0.92 0.1", AnchorMax = "0.98 0.9" }, Text = { Text = "✕" } }, root + "_Header");

            // Content
            var currentToken = GetPathToken(session.Config, session.NavigationPath);
            if (currentToken is JObject obj)
            {
                int i = 0;
                foreach (var prop in obj.Properties())
                {
                    AddRow(container, root, prop.Name, prop.Value, i++);
                }
            }
            else if (currentToken is JArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    AddRow(container, root, $"[{i}]", arr[i], i);
                }
            }

            CuiHelper.AddUi(player, container);
        }

        private void AddRow(CuiElementContainer container, string parent, string key, JToken val, int index)
        {
            float yMax = 0.92f - (index * 0.055f);
            float yMin = yMax - 0.05f;
            var row = container.Add(new CuiPanel { Image = { Color = index % 2 == 0 ? ColRow1 : ColRow2 }, RectTransform = { AnchorMin = $"0.01 {yMin}", AnchorMax = $"0.99 {yMax}" } }, parent);

            container.Add(new CuiLabel { Text = { Text = key, FontSize = 11, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = "0.02 0", AnchorMax = "0.4 1" } }, row);

            if (val is JObject || val is JArray)
            {
                container.Add(new CuiButton { Button = { Command = $"nwgcfg.nav {key}", Color = "0.3 0.4 0.5 0.8" }, RectTransform = { AnchorMin = "0.85 0.1", AnchorMax = "0.98 0.9" }, Text = { Text = "OPEN >" } }, row);
            }
            else if (val.Type == JTokenType.Boolean)
            {
                bool b = val.Value<bool>();
                container.Add(new CuiButton { Button = { Command = $"nwgcfg.toggle {key}", Color = b ? "0.2 0.5 0.2 1" : "0.5 0.2 0.2 1" }, RectTransform = { AnchorMin = "0.7 0.1", AnchorMax = "0.85 0.9" }, Text = { Text = b.ToString().ToUpper() } }, row);
            }
            else
            {
                container.Add(new CuiLabel { Text = { Text = val.ToString(), FontSize = 11, Align = TextAnchor.MiddleRight }, RectTransform = { AnchorMin = "0.4 0", AnchorMax = "0.8 1" } }, row);
                container.Add(new CuiButton { Button = { Command = $"nwgcfg.editfield {key}", Color = "0.2 0.3 0.4 0.8" }, RectTransform = { AnchorMin = "0.85 0.1", AnchorMax = "0.98 0.9" }, Text = { Text = "EDIT" } }, row);
            }
        }
        #endregion

        #region Actions
        [ConsoleCommand("nwgcfg.open")]
        private void CCOpen(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !HasPerm(p)) return;
            var name = a.GetString(0);
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, name + ".json");
            if (!File.Exists(path)) return;
            _sessions[p.userID] = new EditorSession { PluginName = name, Config = JObject.Parse(File.ReadAllText(path)) };
            ShowEditor(p);
        }

        [ConsoleCommand("nwgcfg.nav")]
        private void CCNav(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null) return;
            string key = a.GetString(0);
            s.NavigationPath = string.IsNullOrEmpty(s.NavigationPath) ? key : $"{s.NavigationPath}.{key}";
            ShowEditor(a.Player());
        }

        [ConsoleCommand("nwgcfg.up")]
        private void CCUp(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null) return;
            if (string.IsNullOrEmpty(s.NavigationPath)) { ShowPluginList(a.Player(), 1); return; }
            int lastDot = s.NavigationPath.LastIndexOf('.');
            s.NavigationPath = lastDot > 0 ? s.NavigationPath.Substring(0, lastDot) : "";
            ShowEditor(a.Player());
        }

        [ConsoleCommand("nwgcfg.save")]
        private void CCSave(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null) return;
            File.WriteAllText(Path.Combine(Interface.Oxide.ConfigDirectory, s.PluginName + ".json"), s.Config.ToString(Formatting.Indented));
            Server.Command($"oxide.reload {s.PluginName}");
            a.Player().ChatMessage($"Saved and reloaded {s.PluginName}");
        }

        [ConsoleCommand("nwgcfg.close")]
        private void CCClose(ConsoleSystem.Arg a) { CloseAll(a.Player()); _sessions.Remove(a.Player().userID); }

        [ConsoleCommand("nwgcfg.toggle")]
        private void CCToggle(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null) return;
            string key = a.GetString(0);
            var fullPath = string.IsNullOrEmpty(s.NavigationPath) ? key : $"{s.NavigationPath}.{key}";
            var token = s.Config.SelectToken(fullPath);
            if (token != null && token.Type == JTokenType.Boolean)
            {
                SetToken(s.Config, fullPath, !token.Value<bool>());
                ShowEditor(a.Player());
            }
        }

        [ConsoleCommand("nwgcfg.editfield")]
        private void CCEditField(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null) return;
            s.EditingKey = a.GetString(0);
            ShowInput(a.Player());
        }

        [ConsoleCommand("nwgcfg.setvalue")]
        private void CCSetValue(ConsoleSystem.Arg a)
        {
            var s = GetSession(a.Player()); if (s == null || s.EditingKey == null) return;
            var fullPath = string.IsNullOrEmpty(s.NavigationPath) ? s.EditingKey : $"{s.NavigationPath}.{s.EditingKey}";
            var current = s.Config.SelectToken(fullPath);
            if (current == null) return;

            string val = a.GetString(0);
            JToken newToken = current.Type == JTokenType.Integer ? (JToken)long.Parse(val) : (current.Type == JTokenType.Float ? (JToken)float.Parse(val) : (JToken)val);
            SetToken(s.Config, fullPath, newToken);
            CuiHelper.DestroyUi(a.Player(), PanelInput);
            ShowEditor(a.Player());
        }
        #endregion

        #region Helpers
        private bool HasPerm(BasePlayer p) => p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermUse);
        private void CloseAll(BasePlayer p) { CuiHelper.DestroyUi(p, PanelMain); CuiHelper.DestroyUi(p, PanelEditor); CuiHelper.DestroyUi(p, PanelInput); }
        private EditorSession GetSession(BasePlayer p) => p != null && _sessions.TryGetValue(p.userID, out var s) ? s : null;
        private JToken GetPathToken(JObject root, string path) => string.IsNullOrEmpty(path) ? root : root.SelectToken(path);
        
        private void SetToken(JObject root, string path, JToken val)
        {
            var token = root.SelectToken(path);
            if (token != null) token.Replace(val);
        }

        private void ShowInput(BasePlayer p)
        {
            var container = new CuiElementContainer();
            var root = container.Add(new CuiPanel { Image = { Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", PanelInput);
            container.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.6" } }, root);
            container.Add(new CuiLabel { Text = { Text = "ENTER NEW VALUE:", Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.3 0.55", AnchorMax = "0.7 0.6" } }, root);
            container.Add(new CuiElement { Parent = root, Components = { new CuiInputFieldComponent { Command = "nwgcfg.setvalue", Align = TextAnchor.MiddleCenter }, new CuiRectTransformComponent { AnchorMin = "0.35 0.45", AnchorMax = "0.65 0.52" } } });
            CuiHelper.AddUi(p, container);
        }
        #endregion
    }
}
