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
    [Info("NWG Config Editor", "NWG Team", "1.0.0")]
    [Description("In-game config editor for all NWG plugins.")]
    public class NWGConfigEditor : RustPlugin
    {
        #region Constants
        private const string PermUse = "nwgconfigeditor.use";
        private const string PanelMain = "NWGCfg_Main";
        private const string PanelEditor = "NWGCfg_Editor";
        private const string PanelConfirm = "NWGCfg_Confirm";
        private const string PanelInput = "NWGCfg_Input";

        // UI Colors
        private const string ColBg = "0.08 0.08 0.12 0.95";
        private const string ColHeader = "0.12 0.12 0.18 1";
        private const string ColRow1 = "0.14 0.14 0.2 0.7";
        private const string ColRow2 = "0.12 0.12 0.17 0.7";
        private const string ColBtn = "0.2 0.45 0.8 0.9";
        private const string ColBtnSave = "0.2 0.7 0.3 0.9";
        private const string ColBtnCancel = "0.7 0.2 0.2 0.9";
        private const string ColBtnToggleOn = "0.2 0.7 0.3 0.9";
        private const string ColBtnToggleOff = "0.5 0.2 0.2 0.9";
        private const string ColText = "0.9 0.9 0.95 1";
        private const string ColTextDim = "0.6 0.6 0.65 1";
        private const string ColAccent = "0.35 0.6 1 1";
        #endregion

        #region Session State
        private class EditorSession
        {
            public string PluginName;
            public string FilePath;
            public JObject Config;
            public JObject OriginalConfig;
            public int Page = 1;
            public string EditingKey;
        }

        private readonly Dictionary<ulong, EditorSession> _sessions = new Dictionary<ulong, EditorSession>();
        #endregion

        #region Lifecycle
        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                CloseAll(player);
        }
        #endregion

        #region Commands
        [ChatCommand("config")]
        private void CmdConfig(BasePlayer player, string command, string[] args)
        {
            if (!HasPerm(player)) { player.ChatMessage("<color=#ff4444>[NWG Config]</color> No permission."); return; }
            ShowPluginList(player, 1);
        }

        [ChatCommand("cfg")]
        private void CmdCfg(BasePlayer player, string command, string[] args)
        {
            CmdConfig(player, command, args);
        }
        #endregion

        #region Plugin Scanner
        private List<string> ScanNWGConfigs()
        {
            var configDir = Interface.Oxide.ConfigDirectory;
            var results = new List<string>();

            if (!Directory.Exists(configDir)) return results;

            foreach (var file in Directory.GetFiles(configDir, "NWG*.json"))
            {
                results.Add(Path.GetFileNameWithoutExtension(file));
            }

            results.Sort();
            return results;
        }

        private JObject LoadPluginConfig(string pluginName)
        {
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, pluginName + ".json");
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Puts($"[NWG Config Editor] Error loading {pluginName}: {ex.Message}");
                return null;
            }
        }

        private bool SavePluginConfig(string pluginName, JObject config)
        {
            var path = Path.Combine(Interface.Oxide.ConfigDirectory, pluginName + ".json");
            try
            {
                var json = config.ToString(Formatting.Indented);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                Puts($"[NWG Config Editor] Error saving {pluginName}: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region UI: Plugin List
        private void ShowPluginList(BasePlayer player, int page)
        {
            CloseAll(player);
            var plugins = ScanNWGConfigs();
            if (plugins.Count == 0)
            {
                player.ChatMessage("<color=#ff4444>[NWG Config]</color> No NWG config files found.");
                return;
            }

            var e = new CuiElementContainer();

            // Background
            e.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.15 0.1", AnchorMax = "0.85 0.9" },
                CursorEnabled = true
            }, "Overlay", PanelMain);

            // Header
            e.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, PanelMain, PanelMain + "_Header");

            e.Add(new CuiLabel
            {
                Text = { Text = "NWG CONFIG EDITOR", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = ColAccent },
                RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.9 1" }
            }, PanelMain + "_Header");

            // Close button
            e.Add(new CuiButton
            {
                Button = { Command = "nwgcfg.close", Color = ColBtnCancel },
                RectTransform = { AnchorMin = "0.92 0.15", AnchorMax = "0.98 0.85" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = ColText }
            }, PanelMain + "_Header");

            // Plugin list
            int perPage = 12;
            int totalPages = Mathf.CeilToInt((float)plugins.Count / perPage);
            page = Mathf.Clamp(page, 1, totalPages);

            var pagePlugins = plugins.Skip((page - 1) * perPage).Take(perPage).ToList();

            for (int i = 0; i < pagePlugins.Count; i++)
            {
                string plugName = pagePlugins[i];
                float yMax = 0.88f - (i * 0.065f);
                float yMin = yMax - 0.055f;
                string rowCol = i % 2 == 0 ? ColRow1 : ColRow2;

                // Row background
                e.Add(new CuiPanel
                {
                    Image = { Color = rowCol },
                    RectTransform = { AnchorMin = $"0.02 {yMin}", AnchorMax = $"0.98 {yMax}" }
                }, PanelMain, PanelMain + $"_Row{i}");

                // Plugin name
                string displayName = plugName.Replace("NWG", "NWG ").Replace("_", " ");
                e.Add(new CuiLabel
                {
                    Text = { Text = displayName, FontSize = 14, Align = TextAnchor.MiddleLeft, Color = ColText },
                    RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.7 1" }
                }, PanelMain + $"_Row{i}");

                // Edit button
                e.Add(new CuiButton
                {
                    Button = { Command = $"nwgcfg.edit {plugName}", Color = ColBtn },
                    RectTransform = { AnchorMin = "0.75 0.15", AnchorMax = "0.95 0.85" },
                    Text = { Text = "EDIT", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
                }, PanelMain + $"_Row{i}");
            }

            // Pagination
            if (page > 1)
            {
                e.Add(new CuiButton
                {
                    Button = { Command = $"nwgcfg.list {page - 1}", Color = ColBtn },
                    RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.15 0.06" },
                    Text = { Text = "← Prev", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
                }, PanelMain);
            }

            e.Add(new CuiLabel
            {
                Text = { Text = $"Page {page}/{totalPages}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColTextDim },
                RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.06" }
            }, PanelMain);

            if (page < totalPages)
            {
                e.Add(new CuiButton
                {
                    Button = { Command = $"nwgcfg.list {page + 1}", Color = ColBtn },
                    RectTransform = { AnchorMin = "0.85 0.02", AnchorMax = "0.98 0.06" },
                    Text = { Text = "Next →", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
                }, PanelMain);
            }

            CuiHelper.AddUi(player, e);
        }
        #endregion

        #region UI: Config Editor
        private void ShowConfigEditor(BasePlayer player, string pluginName, int page = 1)
        {
            var config = LoadPluginConfig(pluginName);
            if (config == null)
            {
                player.ChatMessage($"<color=#ff4444>[NWG Config]</color> Could not load config for {pluginName}.");
                return;
            }

            CloseAll(player);

            // Create/update session
            if (!_sessions.ContainsKey(player.userID) || _sessions[player.userID].PluginName != pluginName)
            {
                _sessions[player.userID] = new EditorSession
                {
                    PluginName = pluginName,
                    FilePath = Path.Combine(Interface.Oxide.ConfigDirectory, pluginName + ".json"),
                    Config = config,
                    OriginalConfig = JObject.Parse(config.ToString()),
                    Page = page
                };
            }
            else
            {
                _sessions[player.userID].Page = page;
            }

            var session = _sessions[player.userID];
            var e = new CuiElementContainer();

            // Background
            e.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" },
                CursorEnabled = true
            }, "Overlay", PanelEditor);

            // Header
            e.Add(new CuiPanel
            {
                Image = { Color = ColHeader },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 1" }
            }, PanelEditor, PanelEditor + "_Header");

            string displayName = pluginName.Replace("NWG", "NWG ").Replace("_", " ");
            e.Add(new CuiLabel
            {
                Text = { Text = $"EDITING: {displayName}", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = ColAccent },
                RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.7 1" }
            }, PanelEditor + "_Header");

            // Back button  
            e.Add(new CuiButton
            {
                Button = { Command = "nwgcfg.back", Color = ColBtn },
                RectTransform = { AnchorMin = "0.01 0.15", AnchorMax = "0.08 0.85" },
                Text = { Text = "← Back", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText }
            }, PanelEditor + "_Header");

            // Save button
            e.Add(new CuiButton
            {
                Button = { Command = "nwgcfg.save", Color = ColBtnSave },
                RectTransform = { AnchorMin = "0.82 0.15", AnchorMax = "0.92 0.85" },
                Text = { Text = "SAVE", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
            }, PanelEditor + "_Header");

            // Close button
            e.Add(new CuiButton
            {
                Button = { Command = "nwgcfg.close", Color = ColBtnCancel },
                RectTransform = { AnchorMin = "0.93 0.15", AnchorMax = "0.99 0.85" },
                Text = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText }
            }, PanelEditor + "_Header");

            // Flatten config to key-value pairs for display
            var fields = FlattenConfig(session.Config);
            int perPage = 14;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)fields.Count / perPage));
            page = Mathf.Clamp(page, 1, totalPages);

            var pageFields = fields.Skip((page - 1) * perPage).Take(perPage).ToList();

            for (int i = 0; i < pageFields.Count; i++)
            {
                var field = pageFields[i];
                float yMax = 0.9f - (i * 0.06f);
                float yMin = yMax - 0.05f;
                string rowCol = i % 2 == 0 ? ColRow1 : ColRow2;

                // Row
                e.Add(new CuiPanel
                {
                    Image = { Color = rowCol },
                    RectTransform = { AnchorMin = $"0.01 {yMin}", AnchorMax = $"0.99 {yMax}" }
                }, PanelEditor, PanelEditor + $"_Row{i}");

                // Key name (truncated for display)
                string keyDisplay = field.Key.Length > 35 ? "..." + field.Key.Substring(field.Key.Length - 32) : field.Key;
                e.Add(new CuiLabel
                {
                    Text = { Text = keyDisplay, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = ColTextDim },
                    RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.45 1" }
                }, PanelEditor + $"_Row{i}");

                // Value display + edit control
                var token = field.Value;
                if (token.Type == JTokenType.Boolean)
                {
                    bool val = token.Value<bool>();
                    e.Add(new CuiButton
                    {
                        Button = { Command = $"nwgcfg.toggle {field.Key}", Color = val ? ColBtnToggleOn : ColBtnToggleOff },
                        RectTransform = { AnchorMin = "0.5 0.1", AnchorMax = "0.75 0.9" },
                        Text = { Text = val ? "TRUE" : "FALSE", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
                    }, PanelEditor + $"_Row{i}");
                }
                else if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                {
                    // Show as read-only summary
                    string summary = token.Type == JTokenType.Object
                        ? $"{{ {((JObject)token).Count} fields }}"
                        : $"[ {((JArray)token).Count} items ]";
                    e.Add(new CuiLabel
                    {
                        Text = { Text = summary, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColTextDim },
                        RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.98 1" }
                    }, PanelEditor + $"_Row{i}");
                }
                else
                {
                    // String, Number — show value and edit button
                    string valStr = token.ToString();
                    if (valStr.Length > 20) valStr = valStr.Substring(0, 17) + "...";

                    e.Add(new CuiLabel
                    {
                        Text = { Text = valStr, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText },
                        RectTransform = { AnchorMin = "0.47 0", AnchorMax = "0.78 1" }
                    }, PanelEditor + $"_Row{i}");

                    e.Add(new CuiButton
                    {
                        Button = { Command = $"nwgcfg.editfield {field.Key}", Color = ColBtn },
                        RectTransform = { AnchorMin = "0.8 0.15", AnchorMax = "0.97 0.85" },
                        Text = { Text = "Edit", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText }
                    }, PanelEditor + $"_Row{i}");
                }
            }

            // Pagination
            if (page > 1)
            {
                e.Add(new CuiButton
                {
                    Button = { Command = $"nwgcfg.page {page - 1}", Color = ColBtn },
                    RectTransform = { AnchorMin = "0.01 0.01", AnchorMax = "0.12 0.05" },
                    Text = { Text = "← Prev", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText }
                }, PanelEditor);
            }

            e.Add(new CuiLabel
            {
                Text = { Text = $"Page {page}/{totalPages}  |  {fields.Count} fields", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColTextDim },
                RectTransform = { AnchorMin = "0.35 0.01", AnchorMax = "0.65 0.05" }
            }, PanelEditor);

            if (page < totalPages)
            {
                e.Add(new CuiButton
                {
                    Button = { Command = $"nwgcfg.page {page + 1}", Color = ColBtn },
                    RectTransform = { AnchorMin = "0.88 0.01", AnchorMax = "0.99 0.05" },
                    Text = { Text = "Next →", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText }
                }, PanelEditor);
            }

            CuiHelper.AddUi(player, e);
        }

        private void ShowInputDialog(BasePlayer player, string key)
        {
            CuiHelper.DestroyUi(player, PanelInput);
            var session = GetSession(player);
            if (session == null) return;

            session.EditingKey = key;
            var currentValue = GetNestedValue(session.Config, key);
            string currentStr = currentValue?.ToString() ?? "";

            var e = new CuiElementContainer();

            // Overlay
            e.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.7" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", PanelInput);

            // Dialog box
            e.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.3 0.35", AnchorMax = "0.7 0.65" }
            }, PanelInput, PanelInput + "_Box");

            // Title
            string keyDisplay = key.Length > 40 ? "..." + key.Substring(key.Length - 37) : key;
            e.Add(new CuiLabel
            {
                Text = { Text = $"Edit: {keyDisplay}", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColAccent },
                RectTransform = { AnchorMin = "0 0.75", AnchorMax = "1 0.95" }
            }, PanelInput + "_Box");

            // Current value label
            e.Add(new CuiLabel
            {
                Text = { Text = $"Current: {currentStr}", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColTextDim },
                RectTransform = { AnchorMin = "0 0.6", AnchorMax = "1 0.75" }
            }, PanelInput + "_Box");

            // Input background
            e.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.2 1" },
                RectTransform = { AnchorMin = "0.1 0.35", AnchorMax = "0.9 0.55" }
            }, PanelInput + "_Box", PanelInput + "_InputBg");

            // Input field
            e.Add(new CuiElement
            {
                Parent = PanelInput + "_InputBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleCenter,
                        CharsLimit = 100,
                        Command = "nwgcfg.setvalue",
                        FontSize = 14,
                        Text = currentStr
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.9" }
                }
            });

            // Cancel button
            e.Add(new CuiButton
            {
                Button = { Command = "nwgcfg.cancelinput", Color = ColBtnCancel },
                RectTransform = { AnchorMin = "0.15 0.08", AnchorMax = "0.45 0.28" },
                Text = { Text = "Cancel", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText }
            }, PanelInput + "_Box");

            // OK hint
            e.Add(new CuiLabel
            {
                Text = { Text = "Press Enter to submit", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColTextDim },
                RectTransform = { AnchorMin = "0.55 0.08", AnchorMax = "0.85 0.28" }
            }, PanelInput + "_Box");

            CuiHelper.AddUi(player, e);
        }
        #endregion

        #region Console Commands (UI Actions)
        [ConsoleCommand("nwgcfg.close")]
        private void CCClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CloseAll(player);
            _sessions.Remove(player.userID);
        }

        [ConsoleCommand("nwgcfg.list")]
        private void CCList(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            int page = arg.GetInt(0, 1);
            ShowPluginList(player, page);
        }

        [ConsoleCommand("nwgcfg.edit")]
        private void CCEdit(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            string pluginName = arg.GetString(0);
            if (string.IsNullOrEmpty(pluginName)) return;
            ShowConfigEditor(player, pluginName);
        }

        [ConsoleCommand("nwgcfg.back")]
        private void CCBack(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            _sessions.Remove(player.userID);
            ShowPluginList(player, 1);
        }

        [ConsoleCommand("nwgcfg.page")]
        private void CCPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            var session = GetSession(player);
            if (session == null) return;
            int page = arg.GetInt(0, 1);
            ShowConfigEditor(player, session.PluginName, page);
        }

        [ConsoleCommand("nwgcfg.toggle")]
        private void CCToggle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            var session = GetSession(player);
            if (session == null) return;

            string key = arg.GetString(0);
            if (string.IsNullOrEmpty(key)) return;

            var token = GetNestedValue(session.Config, key);
            if (token == null || token.Type != JTokenType.Boolean) return;

            SetNestedValue(session.Config, key, !token.Value<bool>());
            ShowConfigEditor(player, session.PluginName, session.Page);
        }

        [ConsoleCommand("nwgcfg.editfield")]
        private void CCEditField(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            var session = GetSession(player);
            if (session == null) return;

            string key = arg.GetString(0);
            if (string.IsNullOrEmpty(key)) return;
            ShowInputDialog(player, key);
        }

        [ConsoleCommand("nwgcfg.setvalue")]
        private void CCSetValue(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            var session = GetSession(player);
            if (session == null || session.EditingKey == null) return;

            string newValue = arg.GetString(0);
            if (newValue == null) return;

            var currentToken = GetNestedValue(session.Config, session.EditingKey);
            if (currentToken == null) return;

            // Type-aware parsing
            JToken newToken;
            switch (currentToken.Type)
            {
                case JTokenType.Integer:
                    if (long.TryParse(newValue, out long longVal))
                        newToken = new JValue(longVal);
                    else { player.ChatMessage("<color=#ff4444>[NWG Config]</color> Invalid number."); return; }
                    break;
                case JTokenType.Float:
                    if (float.TryParse(newValue, out float floatVal))
                        newToken = new JValue(floatVal);
                    else { player.ChatMessage("<color=#ff4444>[NWG Config]</color> Invalid number."); return; }
                    break;
                case JTokenType.Boolean:
                    if (bool.TryParse(newValue, out bool boolVal))
                        newToken = new JValue(boolVal);
                    else { player.ChatMessage("<color=#ff4444>[NWG Config]</color> Invalid boolean (true/false)."); return; }
                    break;
                default:
                    newToken = new JValue(newValue);
                    break;
            }

            SetNestedValue(session.Config, session.EditingKey, newToken);
            session.EditingKey = null;

            CuiHelper.DestroyUi(player, PanelInput);
            ShowConfigEditor(player, session.PluginName, session.Page);
        }

        [ConsoleCommand("nwgcfg.cancelinput")]
        private void CCCancelInput(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, PanelInput);
            var session = GetSession(player);
            if (session != null) session.EditingKey = null;
        }

        [ConsoleCommand("nwgcfg.save")]
        private void CCSave(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasPerm(player)) return;
            var session = GetSession(player);
            if (session == null) return;

            if (SavePluginConfig(session.PluginName, session.Config))
            {
                // Try to reload the plugin 
                var pluginClean = session.PluginName.Replace(" ", "");
                // Try common reload command patterns
                Server.Command($"oxide.reload {pluginClean}");

                session.OriginalConfig = JObject.Parse(session.Config.ToString());
                player.ChatMessage($"<color=#55ff55>[NWG Config]</color> Saved & reloading {session.PluginName}.");
                ShowConfigEditor(player, session.PluginName, session.Page);
            }
            else
            {
                player.ChatMessage($"<color=#ff4444>[NWG Config]</color> Failed to save {session.PluginName}.");
            }
        }
        #endregion

        #region JSON Helpers
        private List<KeyValuePair<string, JToken>> FlattenConfig(JObject obj, string prefix = "")
        {
            var result = new List<KeyValuePair<string, JToken>>();

            foreach (var prop in obj.Properties())
            {
                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                if (prop.Value.Type == JTokenType.Object)
                {
                    // Add the object itself as a header, then recurse
                    result.Add(new KeyValuePair<string, JToken>(key, prop.Value));
                }
                else
                {
                    result.Add(new KeyValuePair<string, JToken>(key, prop.Value));
                }
            }

            return result;
        }

        private JToken GetNestedValue(JObject obj, string path)
        {
            string[] parts = path.Split('.');
            JToken current = obj;

            foreach (var part in parts)
            {
                if (current is JObject jObj)
                    current = jObj[part];
                else
                    return null;
            }

            return current;
        }

        private void SetNestedValue(JObject obj, string path, JToken value)
        {
            string[] parts = path.Split('.');
            JToken current = obj;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (current is JObject jObj)
                    current = jObj[parts[i]];
                else
                    return;
            }

            if (current is JObject parent)
                parent[parts[parts.Length - 1]] = value;
        }
        #endregion

        #region Helpers
        private EditorSession GetSession(BasePlayer player)
        {
            return _sessions.TryGetValue(player.userID, out var s) ? s : null;
        }

        private bool HasPerm(BasePlayer player)
        {
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermUse);
        }

        private void CloseAll(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, PanelMain);
            CuiHelper.DestroyUi(player, PanelEditor);
            CuiHelper.DestroyUi(player, PanelConfirm);
            CuiHelper.DestroyUi(player, PanelInput);
        }
        #endregion
    }
}
