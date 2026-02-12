using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGPerms", "NWG Team", "1.0.0")]
    [Description("GUI-based permissions manager. Browse players, groups, and plugins to grant/revoke permissions visually.")]
    public class NWGPerms : RustPlugin
    {
        #region Configuration
        private class PluginConfig
        {
            [JsonProperty("GUI Transparency (0-1)")]
            public double GuiTransparency = 0.95;
            [JsonProperty("Background Colour")]
            public string BgColour = "0.15 0.15 0.15 1";
            [JsonProperty("Header Colour")]
            public string HeaderColour = "0.2 0.2 0.2 1";
            [JsonProperty("Button Colour")]
            public string ButtonColour = "0.4 0.4 0.4 0.9";
            [JsonProperty("Button Hover Colour")]
            public string ButtonHoverColour = "0.5 0.5 0.5 1";
            [JsonProperty("Granted Colour (On)")]
            public string OnColour = "0.2 0.7 0.2 0.9";
            [JsonProperty("Revoked Colour (Off)")]
            public string OffColour = "0.7 0.2 0.2 0.9";
            [JsonProperty("Inherited Colour")]
            public string InheritedColour = "0.9 0.6 0.2 0.9";
            [JsonProperty("Accent Colour")]
            public string AccentColour = "0.3 0.6 0.9 1";
            [JsonProperty("Plugin BlockList (comma separated)")]
            public string BlockList = "";
            [JsonProperty("Grant All = Per Page Only")]
            public bool AllPerPage = false;
        }
        private PluginConfig _config;
        #endregion

        #region State
        private List<string> _plugList = new List<string>();
        private Dictionary<int, string> _numberedPerms = new Dictionary<int, string>();
        private HashSet<ulong> _menuOpen = new HashSet<ulong>();
        private Dictionary<ulong, AdminSession> _sessions = new Dictionary<ulong, AdminSession>();
        private string _btnOn, _btnOff;

        private class AdminSession
        {
            public string InheritedCheck = "";
            public int PluginPage = 1, PermPage = 1, PlayerPage = 1, GroupPage = 1;
            public string SubjectGroup;
            public BasePlayer Subject;
        }

        const string PERM_USE = "nwgperms.use";
        #endregion

        #region Data
        private StoredData _data;
        private class StoredData
        {
            public Dictionary<string, BackupEntry> Entries = new Dictionary<string, BackupEntry>();
        }
        private class BackupEntry
        {
            public List<string> Perms = new List<string>();
            public List<string> Players = new List<string>();
            public int IsPlayer; // 0=group, 1=player
        }
        void SaveStoredData() => Interface.Oxide.DataFileSystem.WriteObject("NWGPerms", _data);
        #endregion

        #region Lifecycle
        void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            LoadConfigVars();
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWGPerms") ?? new StoredData();
        }

        void OnServerInitialized()
        {
            foreach (var p in BasePlayer.activePlayerList.Where(p => _menuOpen.Contains(p.userID)))
                CloseUI(p, true);
        }

        void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList.Where(p => _menuOpen.Contains(p.userID)))
                CloseUI(p, true);
            SaveStoredData();
        }

        void OnPluginLoaded(Plugin p) { if (!(p is NWGPerms)) RefreshPlugList(); }
        void OnPluginUnloaded(Plugin p) { if (!(p is NWGPerms)) RefreshPlugList(); }
        void OnPlayerDisconnected(BasePlayer p, string r) { if (_menuOpen.Contains(p.userID)) CloseUI(p, true); }

        void LoadConfigVars()
        {
            try { _config = Config.ReadObject<PluginConfig>(); if (_config == null) LoadDefaultConfig(); }
            catch { LoadDefaultConfig(); }
        }
        protected override void LoadDefaultConfig() { Puts("Creating new config for NWG Perms"); _config = new PluginConfig(); SaveConfig(); }
        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Helpers
        bool IsAllowed(BasePlayer p) => p?.net?.connection != null && (p.net.connection.authLevel == 2 || permission.UserHasPermission(p.UserIDString, PERM_USE));
        string Strip(string s) => s.Replace(@"\", "").Replace(@"/", "");
        string NoSpaces(string s) => s.Replace(" ", "-");
        string AddSpaces(string s) => s.Replace("-", " ");

        void SetButtons(bool granted) { _btnOn = granted ? _config.OffColour : _config.OnColour; _btnOff = granted ? _config.OnColour : _config.OffColour; }

        void RefreshPlugList()
        {
            _plugList.Clear();
            _numberedPerms.Clear();
            foreach (var plug in plugins.GetAll())
            {
                if (plug.IsCorePlugin) continue;
                var name = plug.ToString().Replace("Oxide.Plugins.", "").ToLower();
                if (_config.BlockList.ToLower().Split(',').Contains(name)) continue;
                if (permission.GetPermissions().Any(perm => perm.ToLower().Contains($"{name}.")))
                    if (!_plugList.Contains(name)) _plugList.Add(name);
            }
            _plugList.Sort();
        }

        AdminSession GetSession(BasePlayer p)
        {
            if (!_sessions.ContainsKey(p.userID)) _sessions[p.userID] = new AdminSession();
            return _sessions[p.userID];
        }

        object[] CheckPerm(BasePlayer admin, string group, string perm)
        {
            bool has = false;
            var inherited = new List<string>();
            var s = GetSession(admin);
            if (group == "true")
                has = permission.GroupHasPermission(s.SubjectGroup, perm);
            else
            {
                var ud = permission.GetUserData(s.Subject.UserIDString);
                has = ud.Perms.Contains(perm);
                foreach (var g in permission.GetUserGroups(s.Subject.UserIDString))
                    if (permission.GroupHasPermission(g, perm)) inherited.Add(g);
            }
            return new object[] { has, inherited };
        }

        BasePlayer FindPlayerById(ulong id) => BasePlayer.allPlayerList.FirstOrDefault(p => p.userID == id);
        BasePlayer FindPlayerByName(string name) => BasePlayer.allPlayerList.FirstOrDefault(p =>
            p.displayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            p.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase));

        void CloseUI(BasePlayer p, bool all)
        {
            CuiHelper.DestroyUi(p, "NWGPBg"); CuiHelper.DestroyUi(p, "NWGPMain");
            CuiHelper.DestroyUi(p, "NWGPPerms"); CuiHelper.DestroyUi(p, "NWGPConfirm");
            CuiHelper.DestroyUi(p, "NWGPData"); CuiHelper.DestroyUi(p, "NWGPMsg");
            CuiHelper.DestroyUi(p, "NWGPDataConf");
            if (all) { CuiHelper.DestroyUi(p, "NWGPBg"); _menuOpen.Remove(p.userID); _sessions.Remove(p.userID); }
        }

        void ShowMsg(BasePlayer p, string msg)
        {
            CuiHelper.DestroyUi(p, "NWGPMsg");
            timer.Once(1.5f, () => { if (p != null) CuiHelper.DestroyUi(p, "NWGPMsg"); });
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { FadeIn = 0.3f, Color = "0.1 0.1 0.1 0.95" }, RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.6" }, CursorEnabled = false, FadeOut = 0.3f }, "Overlay", "NWGPMsg");
            e.Add(new CuiLabel { FadeOut = 0.5f, Text = { FadeIn = 0.5f, Text = msg, FontSize = 28, Align = TextAnchor.MiddleCenter } }, m);
            CuiHelper.AddUi(p, e);
        }
        #endregion

        #region Chat Command
        [ChatCommand("perms")]
        void CmdPerms(BasePlayer player, string cmd, string[] args)
        {
            if (!IsAllowed(player)) { player.ChatMessage("<color=#ff4444>[NWG Perms]</color> You need auth level 2 or permission."); return; }

            if (!_sessions.ContainsKey(player.userID)) _sessions[player.userID] = new AdminSession();
            var s = GetSession(player);
            RefreshPlugList();

            // /perms help
            if (args?.Length == 1 && args[0] == "help")
            {
                player.ChatMessage(
                    "<color=#55aaff>═══ NWG Perms Help ═══</color>\n" +
                    "<color=#ffcc00>/perms</color> — Open player selector\n" +
                    "<color=#ffcc00>/perms group</color> — Open group selector\n" +
                    "<color=#ffcc00>/perms player <name|id></color> — Edit player permissions\n" +
                    "<color=#ffcc00>/perms group <name></color> — Edit group permissions\n" +
                    "<color=#ffcc00>/perms data</color> — Backup/restore/purge data\n" +
                    "<color=#ffcc00>/perms help</color> — Show this help"
                );
                return;
            }

            // /perms data
            if (args?.Length == 1 && args[0] == "data") { DrawBg(player); DrawDataUI(player); return; }

            int page = args?.Length >= 3 ? Convert.ToInt32(args[2]) : 1;

            if (args == null || args.Length < 2)
            {
                bool group = args?.Length == 1 && args[0] == "group";
                if (_menuOpen.Contains(player.userID)) CloseUI(player, true);
                _sessions[player.userID] = new AdminSession();
                DrawBg(player); DrawMainUI(player, group, 1);
                return;
            }

            if (args[0] == "player")
            {
                ulong n; bool num = ulong.TryParse(args[1], out n);
                s.Subject = num ? FindPlayerById(n) : FindPlayerByName(args[1]);
                if (s.Subject == null) { player.ChatMessage($"<color=#ff4444>[NWG Perms]</color> Player '{args[1]}' not found."); return; }
                if (_menuOpen.Contains(player.userID)) CloseUI(player, true);
                DrawBg(player); DrawPluginsUI(player, $"Permissions for {Strip(s.Subject.displayName)}", "false", page);
            }
            else if (args[0] == "group")
            {
                if (!permission.GetGroups().Contains(args[1])) { player.ChatMessage($"<color=#ff4444>[NWG Perms]</color> Group '{args[1]}' not found."); return; }
                s.SubjectGroup = args[1];
                if (_menuOpen.Contains(player.userID)) CloseUI(player, true);
                DrawBg(player); DrawPluginsUI(player, $"Permissions for {args[1]}", "true", page);
            }
            else player.ChatMessage("<color=#ffcc00>[NWG Perms]</color> /perms, /perms player <name>, /perms group <name>, /perms data, /perms help");
        }
        #endregion

        #region UI Drawing
        void DrawBg(BasePlayer p)
        {
            _menuOpen.Add(p.userID);
            var e = new CuiElementContainer();
            var bg = e.Add(new CuiPanel { Image = { Color = $"{_config.BgColour.Substring(0, _config.BgColour.LastIndexOf(' '))} {_config.GuiTransparency}" }, RectTransform = { AnchorMin = "0.3 0.1", AnchorMax = "0.7 0.9" }, CursorEnabled = true, FadeOut = 0.1f }, "Overlay", "NWGPBg");
            // Header bar
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, bg);
            // Footer bar
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.05" } }, bg);
            // Close button with accent color
            e.Add(new CuiButton { Button = { Command = "nwgp.close", Color = _config.OffColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.995" }, Text = { Text = "✖", FontSize = 12, Align = TextAnchor.MiddleCenter } }, bg);
            CuiHelper.AddUi(p, e);
        }

        void DrawMainUI(BasePlayer p, bool group, int page)
        {
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPMain");
            string switchLabel = group ? "Players" : "Groups";
            string currentLabel = !group ? "Players" : "Groups";

            // Title with accent color
            e.Add(new CuiLabel { Text = { Text = "NWG PERMISSIONS MANAGER", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 0.995" } }, m);
            // Switch button with accent color
            e.Add(new CuiButton { Button = { Command = $"nwgp.toggle {group} 1", Color = _config.AccentColour }, RectTransform = { AnchorMin = "0.35 0.015", AnchorMax = "0.65 0.045" }, Text = { Text = $"View All {switchLabel}", FontSize = 12, Align = TextAnchor.MiddleCenter } }, m);
            // Section header
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0 0.89", AnchorMax = "1 0.94" } }, m);
            e.Add(new CuiLabel { Text = { Text = $"All {currentLabel}", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" }, RectTransform = { AnchorMin = "0 0.89", AnchorMax = "1 0.94" } }, m);

            int pos = 20 - (page * 20), count = 0;
            float top = 0.865f, bot = 0.84f;

            if (group)
            {
                foreach (var g in permission.GetGroups().OrderBy(x => x))
                {
                    pos++; count++;
                    if (pos > 0 && pos < 21)
                    {
                        e.Add(new CuiButton { Button = { Command = $"nwgp.select group {g}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.25 {bot}", AnchorMax = $"0.75 {top}" }, Text = { Text = g, FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
                        top -= 0.027f; bot -= 0.027f;
                    }
                }
            }
            else
            {
                foreach (var pl in BasePlayer.allPlayerList.OrderBy(x => x.displayName).ThenBy(x => x?.net?.connection == null))
                {
                    pos++; count++;
                    if (pos > 0 && pos < 21)
                    {
                        string color = pl?.net?.connection == null ? "0.7 0.7 0.7 1" : "0.4 1 0.4 1";
                        e.Add(new CuiButton { Button = { Command = $"nwgp.select player {pl.userID}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.25 {bot}", AnchorMax = $"0.75 {top}" }, Text = { Text = Strip(pl.displayName), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = color } }, m);
                        top -= 0.027f; bot -= 0.027f;
                    }
                }
            }

            if (count > page * 20)
                e.Add(new CuiButton { Button = { Command = $"nwgp.toggle {group} {page + 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.015", AnchorMax = "0.9 0.045" }, Text = { Text = "→", FontSize = 14, Align = TextAnchor.MiddleCenter } }, m);
            if (page > 1)
                e.Add(new CuiButton { Button = { Command = $"nwgp.toggle {group} {page - 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.015", AnchorMax = "0.2 0.045" }, Text = { Text = "←", FontSize = 14, Align = TextAnchor.MiddleCenter } }, m);

            CuiHelper.AddUi(p, e);
        }

        void DrawPluginsUI(BasePlayer p, string title, string group, int page)
        {
            var s = GetSession(p);
            var backPage = group == "false" ? s.PlayerPage : s.GroupPage;
            string toggle = group == "true" ? "false" : "true";
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPPerms");

            int total = 0, pos = 60 - (page * 60);
            for (int i = 0; i < _plugList.Count; i++)
            {
                pos++; total++;
                float yOff(int p2) => 0.89f - (p2 * 3f) / 100f;
                float yOff2(int p2) => 0.91f - (p2 * 3f) / 100f;
                if (pos > 0 && pos < 21)
                    e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {i} null null {group} null 1", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.1 {yOff(pos)}", AnchorMax = $"0.3 {yOff2(pos)}" }, Text = { Text = _plugList[i], FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                else if (pos > 20 && pos < 41)
                    e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {i} null null {group} null 1", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 {yOff(pos - 20)}", AnchorMax = $"0.6 {yOff2(pos - 20)}" }, Text = { Text = _plugList[i], FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                else if (pos > 40 && pos < 61)
                    e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {i} null null {group} null 1", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 {yOff(pos - 40)}", AnchorMax = $"0.9 {yOff2(pos - 40)}" }, Text = { Text = _plugList[i], FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            }

            e.Add(new CuiButton { Button = { Command = $"nwgp.toggle {toggle} {backPage}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.75 0.04" }, Text = { Text = "Back", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiLabel { Text = { Text = title, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, m);

            if (group == "false")
            {
                e.Add(new CuiButton { Button = { Command = "nwgp.revokeall", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.1", AnchorMax = "0.6 0.14" }, Text = { Text = "Revoke All", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
                e.Add(new CuiButton { Button = { Command = "nwgp.groups 1", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = "Groups", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            }
            else
                e.Add(new CuiButton { Button = { Command = "nwgp.playersin 1", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = "Players", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);

            if (total > page * 60)
                e.Add(new CuiButton { Button = { Command = $"nwgp.nav {group} {page + 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = "->", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            if (page > 1)
                e.Add(new CuiButton { Button = { Command = $"nwgp.nav {group} {page - 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = "<-", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);

            CuiHelper.AddUi(p, e);
        }

        void DrawPermsUI(BasePlayer p, string title, int plugNum, string group, int page)
        {
            var s = GetSession(p);
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPPerms");

            int total = 0, pos = 20 - (page * 20);
            // Grant/Revoke All buttons
            e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} grant null {group} all {page}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.5 {0.89f - (pos * 3f) / 100f}", AnchorMax = $"0.6 {0.91f - (pos * 3f) / 100f}" }, Text = { Text = "Grant All", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} revoke null {group} all {page}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.65 {0.89f - (pos * 3f) / 100f}", AnchorMax = $"0.75 {0.91f - (pos * 3f) / 100f}" }, Text = { Text = "Revoke All", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

            foreach (var perm in _numberedPerms)
            {
                SetButtons(true);
                pos++; total++;
                if (pos < 1 || pos > 20) continue;

                float y1 = 0.89f - (pos * 3f) / 100f, y2 = 0.91f - (pos * 3f) / 100f;
                string output = perm.Value.Substring(perm.Value.IndexOf('.') + 1);
                var check = CheckPerm(p, group, perm.Value);
                if ((bool)check[0]) SetButtons(false);

                var inherited = (List<string>)check[1];
                if (inherited.Count > 0)
                    e.Add(new CuiButton { Button = { Command = $"nwgp.inherited {plugNum} {perm.Value} {group} {page} {perm.Value}", Color = _config.InheritedColour }, RectTransform = { AnchorMin = $"0.8 {y1}", AnchorMax = $"0.9 {y2}" }, Text = { Text = "Inherited", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

                e.Add(new CuiButton { Button = { Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.1 {y1}", AnchorMax = $"0.45 {y2}" }, Text = { Text = output, FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} grant {perm.Value} {group} null {page}", Color = _btnOn }, RectTransform = { AnchorMin = $"0.5 {y1}", AnchorMax = $"0.6 {y2}" }, Text = { Text = "Granted", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} revoke {perm.Value} {group} null {page}", Color = _btnOff }, RectTransform = { AnchorMin = $"0.65 {y1}", AnchorMax = $"0.75 {y2}" }, Text = { Text = "Revoked", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            }

            e.Add(new CuiButton { Button = { Command = $"nwgp.nav {group} {s.PluginPage}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.04" }, Text = { Text = "Back", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiLabel { Text = { Text = title, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, m);
            if (total > page * 20)
                e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} null null {group} null {page + 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = "->", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            if (page > 1)
                e.Add(new CuiButton { Button = { Command = $"nwgp.permslist {plugNum} null null {group} null {page - 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = "<-", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);

            CuiHelper.AddUi(p, e);
        }

        void DrawGroupsForPlayerUI(BasePlayer p, string title, int page)
        {
            var s = GetSession(p);
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPPerms");
            int total = 0, pos = 20 - (page * 20);

            foreach (var g in permission.GetGroups().OrderBy(x => x))
            {
                SetButtons(true); pos++; total++;
                if (pos < 1 || pos > 20) continue;
                float y1 = 0.89f - (pos * 3f) / 100f, y2 = 0.91f - (pos * 3f) / 100f;
                foreach (var u in permission.GetUsersInGroup(g))
                    if (u.Contains(s.Subject.UserIDString)) { SetButtons(false); break; }

                e.Add(new CuiButton { Button = { Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.2 {y1}", AnchorMax = $"0.5 {y2}" }, Text = { Text = g, FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                e.Add(new CuiButton { Button = { Command = $"nwgp.groupmod add {NoSpaces(g)} {page}", Color = _btnOn }, RectTransform = { AnchorMin = $"0.55 {y1}", AnchorMax = $"0.65 {y2}" }, Text = { Text = "Granted", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                e.Add(new CuiButton { Button = { Command = $"nwgp.groupmod remove {NoSpaces(g)} {page}", Color = _btnOff }, RectTransform = { AnchorMin = $"0.7 {y1}", AnchorMax = $"0.8 {y2}" }, Text = { Text = "Revoked", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            }

            e.Add(new CuiButton { Button = { Command = $"nwgp.removeallgroups {page}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.1", AnchorMax = "0.6 0.14" }, Text = { Text = "Remove From All", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = $"nwgp.nav false {s.PluginPage}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.04" }, Text = { Text = "Back", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiLabel { Text = { Text = $"Groups for {title}", FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, m);
            if (total > page * 20)
                e.Add(new CuiButton { Button = { Command = $"nwgp.groups {page + 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.7 0.02", AnchorMax = "0.8 0.04" }, Text = { Text = "->", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            if (page > 1)
                e.Add(new CuiButton { Button = { Command = $"nwgp.groups {page - 1}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.2 0.02", AnchorMax = "0.3 0.04" }, Text = { Text = "<-", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            CuiHelper.AddUi(p, e);
        }

        void DrawPlayersInGroupUI(BasePlayer p, string groupName, int page)
        {
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPPerms");
            int total = 0, pos = 20 - (page * 20);
            foreach (var u in permission.GetUsersInGroup(groupName))
            {
                pos++; total++;
                if (pos > 0 && pos < 21)
                {
                    float y1 = 0.89f - (pos * 3f) / 100f, y2 = 0.91f - (pos * 3f) / 100f;
                    e.Add(new CuiButton { Button = { Color = _config.ButtonColour }, RectTransform = { AnchorMin = $"0.2 {y1}", AnchorMax = $"0.8 {y2}" }, Text = { Text = u, FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
                }
            }
            e.Add(new CuiLabel { Text = { Text = $"Players in {groupName}", FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.emptygroup", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = "Remove All Players", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            var s = GetSession(p);
            e.Add(new CuiButton { Button = { Command = $"nwgp.nav true {s.PlayerPage}", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.75 0.04" }, Text = { Text = "Back", FontSize = 11, Align = TextAnchor.MiddleCenter } }, m);
            CuiHelper.AddUi(p, e);
        }

        void DrawDataUI(BasePlayer p)
        {
            var e = new CuiElementContainer();
            var m = e.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "NWGPData");
            e.Add(new CuiLabel { Text = { Text = "NWG PERMS - DATA MANAGEMENT", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }, RectTransform = { AnchorMin = "0 0.96", AnchorMax = "1 0.995" } }, m);

            // Local Backup Section
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0.05 0.84", AnchorMax = "0.95 0.87" } }, m);
            e.Add(new CuiLabel { Text = { Text = "LOCAL DATA BACKUP", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" }, RectTransform = { AnchorMin = "0.05 0.84", AnchorMax = "0.95 0.87" } }, m);

            e.Add(new CuiLabel { Text = { Text = "Save:", FontSize = 11, Align = TextAnchor.MiddleRight }, RectTransform = { AnchorMin = "0.05 0.8", AnchorMax = "0.25 0.83" } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 0", Color = _config.OnColour }, RectTransform = { AnchorMin = "0.3 0.8", AnchorMax = "0.47 0.83" }, Text = { Text = "Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 1", Color = _config.OnColour }, RectTransform = { AnchorMin = "0.53 0.8", AnchorMax = "0.7 0.83" }, Text = { Text = "Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

            e.Add(new CuiLabel { Text = { Text = "Load:", FontSize = 11, Align = TextAnchor.MiddleRight }, RectTransform = { AnchorMin = "0.05 0.76", AnchorMax = "0.25 0.79" } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 2", Color = _config.AccentColour }, RectTransform = { AnchorMin = "0.3 0.76", AnchorMax = "0.47 0.79" }, Text = { Text = "Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 3", Color = _config.AccentColour }, RectTransform = { AnchorMin = "0.53 0.76", AnchorMax = "0.7 0.79" }, Text = { Text = "Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            
            e.Add(new CuiButton { Button = { Command = "nwgp.data 4", Color = _config.OffColour }, RectTransform = { AnchorMin = "0.3 0.72", AnchorMax = "0.7 0.75" }, Text = { Text = "Wipe Local Backup", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

            // Purge Section
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0.05 0.64", AnchorMax = "0.95 0.67" } }, m);
            e.Add(new CuiLabel { Text = { Text = "PURGE UNLOADED PERMISSIONS", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" }, RectTransform = { AnchorMin = "0.05 0.64", AnchorMax = "0.95 0.67" } }, m);
            
            e.Add(new CuiButton { Button = { Command = "nwgp.data 5", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.6", AnchorMax = "0.75 0.63" }, Text = { Text = "Purge Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 6", Color = _config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.56", AnchorMax = "0.75 0.59" }, Text = { Text = "Purge Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

            // Global Remove Section
            e.Add(new CuiPanel { Image = { Color = _config.HeaderColour }, RectTransform = { AnchorMin = "0.05 0.48", AnchorMax = "0.95 0.51" } }, m);
            e.Add(new CuiLabel { Text = { Text = "GLOBAL REMOVE (DANGER)", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 0.4 0.4 1" }, RectTransform = { AnchorMin = "0.05 0.48", AnchorMax = "0.95 0.51" } }, m);
            
            e.Add(new CuiButton { Button = { Command = "nwgp.data 7", Color = _config.OffColour }, RectTransform = { AnchorMin = "0.25 0.44", AnchorMax = "0.75 0.47" }, Text = { Text = "Empty All Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 8", Color = _config.OffColour }, RectTransform = { AnchorMin = "0.25 0.4", AnchorMax = "0.75 0.43" }, Text = { Text = "Delete All Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);
            e.Add(new CuiButton { Button = { Command = "nwgp.data 9", Color = _config.OffColour }, RectTransform = { AnchorMin = "0.25 0.36", AnchorMax = "0.75 0.39" }, Text = { Text = "Wipe All Player Permissions", FontSize = 10, Align = TextAnchor.MiddleCenter } }, m);

            CuiHelper.AddUi(p, e);
        }
        #endregion

        #region Console Commands
        [ConsoleCommand("nwgp.close")]
        void CCClose(ConsoleSystem.Arg a) { var p = a.Player(); if (p != null) CloseUI(p, true); }

        [ConsoleCommand("nwgp.toggle")]
        void CCToggle(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 2) return;
            bool group = !Convert.ToBoolean(a.Args[0]); int page = Convert.ToInt32(a.Args[1]);
            var s = GetSession(p);
            if (group) s.GroupPage = page; else s.PlayerPage = page;
            CloseUI(p, false); DrawMainUI(p, group, page);
        }

        [ConsoleCommand("nwgp.select")]
        void CCSelect(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 2) return;
            var s = GetSession(p);
            CloseUI(p, false);
            if (a.Args[0] == "player") { s.Subject = FindPlayerById(Convert.ToUInt64(a.Args[1])); CmdPerms(p, null, new[] { "player", a.Args[1] }); }
            else { s.SubjectGroup = a.Args[1]; CmdPerms(p, null, new[] { "group", a.Args[1] }); }
        }

        [ConsoleCommand("nwgp.nav")]
        void CCNav(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 2) return;
            var s = GetSession(p); s.PluginPage = Convert.ToInt32(a.Args[1]);
            CloseUI(p, false);
            if (a.Args[0] == "true")
            {
                if (string.IsNullOrEmpty(s.SubjectGroup)) { ShowMsg(p, "No group selected."); return; }
                CmdPerms(p, null, new[] { "group", s.SubjectGroup, s.PluginPage.ToString() });
            }
            else
            {
                if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
                CmdPerms(p, null, new[] { "player", s.Subject.userID.ToString(), s.PluginPage.ToString() });
            }
        }

        [ConsoleCommand("nwgp.permslist")]
        void CCPermsList(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 6) return;
            var s = GetSession(p);
            int plugNum = Convert.ToInt32(a.Args[0]); string action = a.Args[1]; string perm = a.Args[2];
            string group = a.Args[3]; string allFlag = a.Args[4]; int page = Convert.ToInt32(a.Args[5]);

            // Validate subject exists
            if (group == "false" && s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            if (group == "true" && string.IsNullOrEmpty(s.SubjectGroup)) { ShowMsg(p, "No group selected."); return; }

            // Bounds check on plugNum
            if (plugNum < 0 || plugNum >= _plugList.Count) { RefreshPlugList(); if (plugNum < 0 || plugNum >= _plugList.Count) { ShowMsg(p, "Plugin index out of range. Try again."); return; } }

            // Build perm list first
            string plugName = _plugList[plugNum];
            _numberedPerms.Clear();
            int num = 0;
            foreach (var pm in permission.GetPermissions().OrderBy(x => x))
                if (pm.ToLower().Contains($"{plugName}.")) _numberedPerms[++num] = pm;

            // Apply permission changes
            bool changed = false;
            if (allFlag == "all")
            {
                foreach (var np in _numberedPerms)
                {
                    if (_config.AllPerPage && (np.Key <= (page * 20) - 20 || np.Key > page * 20)) continue;
                    if (action == "grant" && group == "false") { permission.GrantUserPermission(s.Subject.UserIDString, np.Value, null); changed = true; }
                    if (action == "revoke" && group == "false") { permission.RevokeUserPermission(s.Subject.UserIDString, np.Value); changed = true; }
                    if (action == "grant" && group == "true") { permission.GrantGroupPermission(s.SubjectGroup, np.Value, null); changed = true; }
                    if (action == "revoke" && group == "true") { permission.RevokeGroupPermission(s.SubjectGroup, np.Value); changed = true; }
                }
                if (changed) ShowMsg(p, $"{(action == "grant" ? "Granted" : "Revoked")} all permissions");
            }
            else if (perm != "null")
            {
                if (group == "false")
                {
                    string id = s.Subject.UserIDString;
                    if (action == "grant") { permission.GrantUserPermission(id, perm, null); changed = true; }
                    if (action == "revoke") { permission.RevokeUserPermission(id, perm); changed = true; }
                }
                else
                {
                    if (action == "grant") { permission.GrantGroupPermission(s.SubjectGroup, perm, null); changed = true; }
                    if (action == "revoke") { permission.RevokeGroupPermission(s.SubjectGroup, perm); changed = true; }
                }
            }

            // Refresh UI
            CloseUI(p, false);
            string title = group == "false" ? $"{Strip(s.Subject.displayName)} - {plugName}" : $"{s.SubjectGroup} - {plugName}";
            DrawPermsUI(p, title, plugNum, group, page);
        }

        [ConsoleCommand("nwgp.groups")]
        void CCGroups(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 1) return;
            var s = GetSession(p); s.GroupPage = Convert.ToInt32(a.Args[0]);
            if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            CloseUI(p, false); DrawGroupsForPlayerUI(p, Strip(s.Subject.displayName), s.GroupPage);
        }

        [ConsoleCommand("nwgp.playersin")]
        void CCPlayersIn(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 1) return;
            var s = GetSession(p); s.PlayerPage = Convert.ToInt32(a.Args[0]);
            if (string.IsNullOrEmpty(s.SubjectGroup)) { ShowMsg(p, "No group selected."); return; }
            CloseUI(p, false); DrawPlayersInGroupUI(p, s.SubjectGroup, s.PlayerPage);
        }

        [ConsoleCommand("nwgp.groupmod")]
        void CCGroupMod(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 3) return;
            var s = GetSession(p); string g = AddSpaces(a.Args[1]); int page = Convert.ToInt32(a.Args[2]);
            if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            if (a.Args[0] == "add") { permission.AddUserGroup(s.Subject.UserIDString, g); ShowMsg(p, $"Added to {g}"); }
            if (a.Args[0] == "remove") { permission.RemoveUserGroup(s.Subject.UserIDString, g); ShowMsg(p, $"Removed from {g}"); }
            CloseUI(p, false); DrawGroupsForPlayerUI(p, Strip(s.Subject.displayName), page);
        }

        [ConsoleCommand("nwgp.revokeall")]
        void CCRevokeAll(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p)) return;
            var s = GetSession(p);
            if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            foreach (var pm in permission.GetUserPermissions(s.Subject.UserIDString))
                permission.RevokeUserPermission(s.Subject.UserIDString, pm);
            ShowMsg(p, "All permissions revoked.");
        }

        [ConsoleCommand("nwgp.removeallgroups")]
        void CCRemoveAllGroups(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p)) return;
            var s = GetSession(p); int page = a.Args?.Length > 0 ? Convert.ToInt32(a.Args[0]) : 1;
            if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            int count = 0;
            foreach (var g in permission.GetUserGroups(s.Subject.UserIDString))
            { permission.RemoveUserGroup(s.Subject.UserIDString, g); count++; }
            ShowMsg(p, $"Removed from {count} groups");
            CloseUI(p, false); DrawGroupsForPlayerUI(p, Strip(s.Subject.displayName), page);
        }

        [ConsoleCommand("nwgp.emptygroup")]
        void CCEmptyGroup(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p)) return;
            var s = GetSession(p);
            if (string.IsNullOrEmpty(s.SubjectGroup)) { ShowMsg(p, "No group selected."); return; }
            foreach (var u in permission.GetUsersInGroup(s.SubjectGroup))
            {
                string id = u.Length > 17 ? u.Substring(0, 17) : u;
                permission.RemoveUserGroup(id, s.SubjectGroup);
            }
            CloseUI(p, false); CmdPerms(p, null, new[] { "group", s.SubjectGroup });
        }

        [ConsoleCommand("nwgp.inherited")]
        void CCInherited(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 5) return;
            var s = GetSession(p);
            if (s.Subject == null) { ShowMsg(p, "No player selected."); return; }
            s.InheritedCheck = a.Args[4] == s.InheritedCheck ? "" : a.Args[4];
            int plugNum = Convert.ToInt32(a.Args[0]); string group = a.Args[2]; int page = Convert.ToInt32(a.Args[3]);
            if (plugNum < 0 || plugNum >= _plugList.Count) { RefreshPlugList(); if (plugNum < 0 || plugNum >= _plugList.Count) { ShowMsg(p, "Plugin index out of range."); return; } }
            string plugName = _plugList[plugNum];
            _numberedPerms.Clear(); int num = 0;
            foreach (var pm in permission.GetPermissions().OrderBy(x => x))
                if (pm.ToLower().Contains($"{plugName}.")) _numberedPerms[++num] = pm;
            CloseUI(p, false);
            DrawPermsUI(p, $"{Strip(s.Subject.displayName)} - {plugName}", plugNum, group, page);
        }

        [ConsoleCommand("nwgp.data")]
        void CCData(ConsoleSystem.Arg a)
        {
            var p = a.Player(); if (p == null || !IsAllowed(p) || a.Args?.Length < 1) return;
            int cmd = Convert.ToInt32(a.Args[0]);
            switch (cmd)
            {
                case 0: DoBackup(true, true); ShowMsg(p, "Groups Saved"); break;
                case 1: DoBackup(true, false); ShowMsg(p, "Players Saved"); break;
                case 2: DoBackup(false, true); ShowMsg(p, "Groups Loaded"); break;
                case 3: DoBackup(false, false); ShowMsg(p, "Players Loaded"); break;
                case 4: _data = new StoredData(); SaveStoredData(); ShowMsg(p, "Backup Wiped"); break;
                case 5: PurgeGroups(); ShowMsg(p, "Groups Purged"); break;
                case 6: PurgePlayers(); ShowMsg(p, "Players Purged"); break;
                case 7: EmptyAllGroups(); ShowMsg(p, "Groups Emptied"); break;
                case 8: DeleteAllGroups(); ShowMsg(p, "Groups Deleted"); break;
                case 9: WipePlayerPerms(); ShowMsg(p, "Player Perms Wiped"); break;
            }
        }
        #endregion

        #region Data Operations
        void DoBackup(bool save, bool groups)
        {
            permission.SaveData();
            if (save)
            {
                if (groups)
                {
                    _data.Entries = _data.Entries.Where(x => x.Value.IsPlayer == 1).ToDictionary(x => x.Key, x => x.Value);
                    foreach (var g in permission.GetGroups())
                    {
                        var perms = permission.GetGroupPermissions(g).ToList();
                        var players = permission.GetUsersInGroup(g).ToList();
                        _data.Entries[g] = new BackupEntry { Perms = perms, Players = players, IsPlayer = 0 };
                    }
                }
                else
                {
                    var userData = ProtoStorage.Load<Dictionary<string, Oxide.Core.Libraries.UserData>>(new[] { "oxide.users" });
                    if (userData == null) return;
                    userData = userData.Where(x => x.Value.Perms.Count > 0).ToDictionary(x => x.Key, x => x.Value);
                    _data.Entries = _data.Entries.Where(x => x.Value.IsPlayer == 0).ToDictionary(x => x.Key, x => x.Value);
                    foreach (var u in userData)
                        _data.Entries[u.Key] = new BackupEntry { Perms = u.Value.Perms.ToList(), IsPlayer = 1 };
                }
            }
            else
            {
                foreach (var r in _data.Entries)
                {
                    if (r.Value.IsPlayer == 0 && groups)
                    {
                        if (!permission.GroupExists(r.Key)) permission.CreateGroup(r.Key, r.Key, 0);
                        if (r.Value.Players != null) foreach (var pl in r.Value.Players) permission.AddUserGroup(pl, r.Key);
                        foreach (var pm in r.Value.Perms) permission.GrantGroupPermission(r.Key, pm, null);
                    }
                    else if (r.Value.IsPlayer == 1 && !groups)
                        foreach (var pm in r.Value.Perms) permission.GrantUserPermission(r.Key, pm, null);
                }
            }
            permission.SaveData(); SaveStoredData();
        }

        void EmptyAllGroups() { foreach (var g in permission.GetGroups().ToList()) { permission.RemoveGroup(g); permission.CreateGroup(g, g, 0); } }
        void DeleteAllGroups() { foreach (var g in permission.GetGroups().ToList()) permission.RemoveGroup(g); permission.CreateGroup("default", "default", 0); permission.CreateGroup("admin", "admin", 0); }
        void WipePlayerPerms()
        {
            foreach (var pm in permission.GetPermissions())
                foreach (var u in permission.GetPermissionUsers(pm))
                    if (u.Length > 17) permission.RevokeUserPermission(u.Substring(0, 17), pm);
            permission.SaveData();
        }
        void PurgeGroups()
        {
            var allPerms = permission.GetPermissions();
            foreach (var g in permission.GetGroups())
                foreach (var pm in permission.GetGroupPermissions(g))
                    if (!allPerms.Contains(pm)) permission.RevokeGroupPermission(g, pm);
            permission.SaveData();
        }
        void PurgePlayers()
        {
            var allPerms = permission.GetPermissions();
            var userData = ProtoStorage.Load<Dictionary<string, Oxide.Core.Libraries.UserData>>(new[] { "oxide.users" });
            if (userData == null) return;
            foreach (var u in userData.Where(x => x.Value.Perms.Count > 0))
                foreach (var pm in u.Value.Perms.ToList())
                    if (!allPerms.Contains(pm)) permission.RevokeUserPermission(u.Key, pm);
            permission.SaveData();
        }
        #endregion
    }
}

