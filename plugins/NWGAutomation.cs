using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGAutomation", "NWG Team", "3.1.0")]
    [Description("Automated Base Management: Doors, Locks, Codes, Authorization + Player Settings UI.")]
    public class NWGAutomation : RustPlugin
    {
#region References
        [PluginReference] private Plugin Clans; 
#endregion

#region Configuration
        private class PluginConfig
        {
            public bool EnableAutoDoor = true;
            public float AutoDoorDelay = 5.0f;
            public bool EnableAutoCode = true;
            public bool EnableAutoLock = true;
            public bool EnableAutoAuth = true;
            
            public bool ShareWithTeam = true;
            public bool ShareWithClan = true;
        }
        private PluginConfig _config;
#endregion

#region Data
        private class PlayerData
        {
            public string Code = "";
            public bool AutoDoorEnabled = true;
            public float AutoDoorDelay = 5f;
            public bool AutoLockEnabled = true;
            public bool AutoCodeEnabled = true;
            public bool AutoAuthEnabled = true;
        }
        private Dictionary<ulong, PlayerData> _data;

        private PlayerData GetOrCreate(ulong id)
        {
            if (!_data.ContainsKey(id))
                _data[id] = new PlayerData { AutoDoorDelay = _config.AutoDoorDelay };
            return _data[id];
        }
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            
            try 
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>("NWG_Automation");
            } 
            catch { _data = new Dictionary<ulong, PlayerData>(); }

            if (_data == null) _data = new Dictionary<ulong, PlayerData>();
        }

        private void LoadConfigVariables()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) LoadDefaultConfig();
            }
            catch { LoadDefaultConfig(); }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Automation", _data);

        private void OnServerSave() => SaveData();
#endregion
        
#region Localization
        public static class Lang
        {
            public const string AutoLockSet = "AutoLockSet";
            public const string AutoCodeSet = "AutoCodeSet";
            public const string SettingsTitle = "SettingsTitle";
            public const string Close = "Close";
            public const string AutoDoorLabel = "AutoDoorLabel";
            public const string AutoDoorDesc = "AutoDoorDesc";
            public const string AutoDoorDelayLabel = "AutoDoorDelayLabel";
            public const string AutoLockLabel = "AutoLockLabel";
            public const string AutoLockDesc = "AutoLockDesc";
            public const string AutoCodeLabel = "AutoCodeLabel";
            public const string AutoCodeDesc = "AutoCodeDesc";
            public const string AutoAuthLabel = "AutoAuthLabel";
            public const string AutoAuthDesc = "AutoAuthDesc";
            public const string SavedCodeLabel = "SavedCodeLabel";
            public const string CodeNotSet = "CodeNotSet";
            public const string EnterDelay = "EnterDelay";
            public const string EnterCode = "EnterCode";
            public const string UsageAutoCode = "UsageAutoCode";
            public const string InvalidCode = "InvalidCode";
            public const string AutoCodeUpdated = "AutoCodeUpdated";
            public const string UsageDoorDelay = "UsageDoorDelay";
            public const string InvalidDelay = "InvalidDelay";
            public const string DoorDelayUpdated = "DoorDelayUpdated";
            public const string Change = "Change";
            public const string FooterHelp = "FooterHelp";
            public const string On = "On";
            public const string Off = "Off";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.AutoLockSet] = "<color=#b7d092>AutoLock:</color> Code lock deployed & set.",
                [Lang.AutoCodeSet] = "<color=#b7d092>AutoCode</color>: Lock set to {0}",
                [Lang.SettingsTitle] = "⚙  PLAYER SETTINGS",
                [Lang.Close] = "✖",
                [Lang.AutoDoorLabel] = "Auto Door Close",
                [Lang.AutoDoorDesc] = "Doors automatically close after a delay",
                [Lang.AutoDoorDelayLabel] = "Door Close Delay",
                [Lang.AutoLockLabel] = "Auto Lock Doors",
                [Lang.AutoLockDesc] = "Automatically place code lock on new doors",
                [Lang.AutoCodeLabel] = "Auto Code Lock",
                [Lang.AutoCodeDesc] = "Automatically set your saved code on locks",
                [Lang.AutoAuthLabel] = "Auto Team Auth",
                [Lang.AutoAuthDesc] = "Auto-authorize teammates on TCs, turrets, locks",
                [Lang.SavedCodeLabel] = "Saved Code",
                [Lang.CodeNotSet] = "<color=#d9534f>Not Set</color>",
                [Lang.EnterDelay] = "<color=#b7d092>Enter desired door close delay (1-30s):</color>\nUse <color=#FFA500>/doordelay <seconds></color>",
                [Lang.EnterCode] = "<color=#b7d092>Set your auto-code:</color>\nUse <color=#FFA500>/setautocode <4-digits></color>",
                [Lang.UsageAutoCode] = "Usage: /setautocode <4-digit code>",
                [Lang.InvalidCode] = "<color=#d9534f>Code must be exactly 4 digits.</color>",
                [Lang.AutoCodeUpdated] = "<color=#b7d092>AutoCode set to:</color> {0}",
                [Lang.UsageDoorDelay] = "Usage: /doordelay <seconds>",
                [Lang.InvalidDelay] = "<color=#d9534f>Delay must be between 1 and 30 seconds.</color>",
                [Lang.DoorDelayUpdated] = "<color=#b7d092>Door close delay set to:</color> {0}s",
                [Lang.Change] = "CHANGE",
                [Lang.FooterHelp] = "Use /setautocode <4-digit> to change your code",
                [Lang.On] = "ON",
                [Lang.Off] = "OFF"
            }, this);
        }
        
        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Auto Door Logic
        private Dictionary<ulong, Timer> _doorTimers = new Dictionary<ulong, Timer>();

        private void OnDoorOpened(Door door, BasePlayer player)
        {
            if (!_config.EnableAutoDoor) return;
            if (door == null || player == null) return;
            
            var prefs = GetOrCreate(player.userID);
            if (!prefs.AutoDoorEnabled) return;

            var delay = prefs.AutoDoorDelay > 0 ? prefs.AutoDoorDelay : _config.AutoDoorDelay;

            if (_doorTimers.ContainsKey(door.net.ID.Value))
            {
                _doorTimers[door.net.ID.Value].Destroy();
                _doorTimers.Remove(door.net.ID.Value);
            }

            _doorTimers[door.net.ID.Value] = timer.Once(delay, () => CloseDoor(door));
        }

        private void OnDoorClosed(Door door, BasePlayer player)
        {
            if (door == null) return;
            if (_doorTimers.ContainsKey(door.net.ID.Value))
            {
                _doorTimers[door.net.ID.Value].Destroy();
                _doorTimers.Remove(door.net.ID.Value);
            }
        }

        private void CloseDoor(Door door)
        {
            if (door == null || door.IsDestroyed || !door.IsOpen()) return;
            door.SetFlag(BaseEntity.Flags.Open, false);
            door.SendNetworkUpdateImmediate();
        }
#endregion

#region Auto Lock & Code Logic
        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var entity = go.ToBaseEntity();
            if (entity == null) return;
            var player = planner?.GetOwnerPlayer();
            if (player == null) return;

            if (_config.EnableAutoLock && entity is Door door && door.GetSlot(BaseEntity.Slot.Lock) == null)
            {
                var prefs = GetOrCreate(player.userID);
                if (!prefs.AutoLockEnabled) return;

                var lockItem = player.inventory.FindItemByItemID(ItemManager.FindItemDefinition("lock.code").itemid);
                if (lockItem != null)
                {
                    // Consume the lock from inventory and deploy it
                    var codeLock = GameManager.server.CreateEntity(CodeLockPrefab) as CodeLock;
                    if (codeLock != null)
                    {
                        codeLock.Spawn();
                        codeLock.SetParent(door, door.GetSlotAnchorName(BaseEntity.Slot.Lock));
                        door.SetSlot(BaseEntity.Slot.Lock, codeLock);
                        codeLock.whitelistPlayers.Add(player.userID);

                        // Set auto code if available
                        if (_config.EnableAutoCode && prefs.AutoCodeEnabled && !string.IsNullOrEmpty(prefs.Code))
                        {
                            codeLock.code = prefs.Code;
                            codeLock.SetFlag(BaseEntity.Flags.Locked, true);

                            if (_config.EnableAutoAuth && prefs.AutoAuthEnabled)
                                TryAuthorizeOthers(codeLock, player);
                        }

                        lockItem.UseItem(1);
                        player.ChatMessage(GetMessage(Lang.AutoLockSet, player.UserIDString));
                    }
                }
            }
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity entity, BaseEntity instance)
        {
            if (entity is CodeLock codeLock && _config.EnableAutoCode)
            {
                var player = deployer.GetOwnerPlayer();
                if (player == null) return;

                var prefs = GetOrCreate(player.userID);
                if (!prefs.AutoCodeEnabled || string.IsNullOrEmpty(prefs.Code)) return;

                codeLock.code = prefs.Code;
                codeLock.whitelistPlayers.Add(player.userID);
                codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                
                if (_config.EnableAutoAuth && prefs.AutoAuthEnabled)
                    TryAuthorizeOthers(codeLock, player);
                
                SendReply(player, GetMessage(Lang.AutoCodeSet, player.UserIDString, prefs.Code));
            }
        }
#endregion

#region Auto Authorization
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!_config.EnableAutoAuth) return;

            if (entity is BuildingPrivlidge priv)
            {
                var ownerId = priv.OwnerID;
                if (ownerId == 0) return;
                var prefs = GetOrCreate(ownerId);
                if (!prefs.AutoAuthEnabled) return;
                NextTick(() => { if (priv != null && !priv.IsDestroyed) AuthorizeTeam(priv, ownerId); });
            }
            else if (entity is AutoTurret turret)
            {
                var ownerId = turret.OwnerID;
                if (ownerId == 0) return;
                var prefs = GetOrCreate(ownerId);
                if (!prefs.AutoAuthEnabled) return;
                NextTick(() => { if (turret != null && !turret.IsDestroyed) AuthorizeTeam(turret, ownerId); });
            }
        }

        private void AuthorizeTeam(BaseEntity entity, ulong ownerId)
        {
            List<ulong> toAuth = new List<ulong>();
            
            if (_config.ShareWithTeam && RelationshipManager.ServerInstance != null)
            {
                var team = RelationshipManager.ServerInstance.FindPlayersTeam(ownerId);
                if (team != null) toAuth.AddRange(team.members);
            }
            
            if (entity is BuildingPrivlidge priv)
            {
                foreach (var id in toAuth)
                    priv.authorizedPlayers.Add(id);
                priv.SendNetworkUpdate();
            }
            else if (entity is AutoTurret turret)
            {
                foreach (var id in toAuth)
                    turret.authorizedPlayers.Add(id);
                turret.SendNetworkUpdate();
            }
            else if (entity is CodeLock codeLock)
            {
                foreach (var id in toAuth)
                    if (!codeLock.whitelistPlayers.Contains(id))
                        codeLock.whitelistPlayers.Add(id);
                codeLock.SendNetworkUpdate();
            }
        }
        
        private void TryAuthorizeOthers(CodeLock codeLock, BasePlayer owner) => AuthorizeTeam(codeLock, owner.userID);
#endregion

#region Settings UI
        private const string SettingsPanel = "NWGSettingsPanel";
        private const string CodeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";
        private const string BgColor = "0.15 0.15 0.15 0.98"; // Dark Panel
        private const string HeaderColor = "0.1 0.1 0.1 1"; // Header
        private const string OnColor = "0.718 0.816 0.573 1"; // Sage Green
        private const string OffColor = "0.851 0.325 0.31 1"; // Red/Rust
        private const string BtnColor = "0.25 0.25 0.25 0.9";
        private const string TextColor = "0.867 0.867 0.867 1";
        private const string AccentColor = "1 0.647 0 1"; // Orange

        [ChatCommand("settings")]
        private void CmdSettings(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            DrawSettingsUI(player);
        }

        private void DrawSettingsUI(BasePlayer player)
        {
            var prefs = GetOrCreate(player.userID);
            CuiHelper.DestroyUi(player, SettingsPanel);

            var e = new CuiElementContainer();

            // Background
            var bg = e.Add(new CuiPanel
            {
                Image = { Color = BgColor },
                RectTransform = { AnchorMin = "0.25 0.15", AnchorMax = "0.75 0.85" },
                CursorEnabled = true
            }, "Overlay", SettingsPanel);

            // Header
            e.Add(new CuiPanel { Image = { Color = HeaderColor }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, bg);
            e.Add(new CuiLabel { Text = { Text = GetMessage(Lang.SettingsTitle, player.UserIDString), FontSize = 20, Align = TextAnchor.MiddleCenter, Color = OnColor, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "0.9 1" } }, bg);

            // Close button
            e.Add(new CuiButton { Button = { Command = "nwgsettings.close", Color = "0.8 0.2 0.2 0.8" }, RectTransform = { AnchorMin = "0.92 0.92", AnchorMax = "0.98 0.98" }, Text = { Text = GetMessage(Lang.Close, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter } }, bg);

            // Settings rows
            float y = 0.82f;
            float rowH = 0.08f;
            float gap = 0.02f;

            // Auto Door Close
            DrawToggleRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.AutoDoorLabel, player.UserIDString), GetMessage(Lang.AutoDoorDesc, player.UserIDString),
                prefs.AutoDoorEnabled, "nwgsettings.toggle autodoor", player.UserIDString);

            // Auto Door Delay
            DrawValueRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.AutoDoorDelayLabel, player.UserIDString), $"{prefs.AutoDoorDelay}s",
                "nwgsettings.setdelay", player.UserIDString);

            // Auto Lock
            DrawToggleRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.AutoLockLabel, player.UserIDString), GetMessage(Lang.AutoLockDesc, player.UserIDString),
                prefs.AutoLockEnabled, "nwgsettings.toggle autolock", player.UserIDString);

            // Auto Code
            DrawToggleRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.AutoCodeLabel, player.UserIDString), GetMessage(Lang.AutoCodeDesc, player.UserIDString),
                prefs.AutoCodeEnabled, "nwgsettings.toggle autocode", player.UserIDString);

            // Auto Auth
            DrawToggleRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.AutoAuthLabel, player.UserIDString), GetMessage(Lang.AutoAuthDesc, player.UserIDString),
                prefs.AutoAuthEnabled, "nwgsettings.toggle autoauth", player.UserIDString);

            // Saved Code display
            string codeDisplay = string.IsNullOrEmpty(prefs.Code) ? GetMessage(Lang.CodeNotSet, player.UserIDString) : $"<color=#aaffaa>{prefs.Code}</color>";
            DrawValueRow(e, bg, ref y, rowH, gap,
                GetMessage(Lang.SavedCodeLabel, player.UserIDString), codeDisplay,
                "nwgsettings.setcode", player.UserIDString);

            // Footer
            e.Add(new CuiLabel
            {
                Text = { Text = GetMessage(Lang.FooterHelp, player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.6 1" },
                RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.9 0.08" }
            }, bg);

            CuiHelper.AddUi(player, e);
        }

        private void DrawToggleRow(CuiElementContainer e, string parent, ref float y, float h, float gap, string label, string desc, bool isOn, string cmd, string userId)
        {
            // Label
            e.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 15, Align = TextAnchor.MiddleLeft, Color = TextColor },
                RectTransform = { AnchorMin = $"0.04 {y - h}", AnchorMax = $"0.45 {y}" }
            }, parent);

            // Description
            e.Add(new CuiLabel
            {
                Text = { Text = desc, FontSize = 10, Align = TextAnchor.MiddleLeft, Color = "0.5 0.5 0.6 1" },
                RectTransform = { AnchorMin = $"0.04 {y - h - 0.015f}", AnchorMax = $"0.65 {y - h + 0.015f}" }
            }, parent);

            // Toggle button
            string toggleColor = isOn ? OnColor : OffColor;
            string toggleText = isOn ? GetMessage(Lang.On, userId) : GetMessage(Lang.Off, userId);
            e.Add(new CuiButton
            {
                Button = { Command = cmd, Color = toggleColor },
                RectTransform = { AnchorMin = $"0.78 {y - h + 0.01f}", AnchorMax = $"0.96 {y - 0.01f}" },
                Text = { Text = toggleText, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);

            y -= h + gap;
        }

        private void DrawValueRow(CuiElementContainer e, string parent, ref float y, float h, float gap, string label, string value, string cmd, string userId)
        {
            e.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 15, Align = TextAnchor.MiddleLeft, Color = TextColor },
                RectTransform = { AnchorMin = $"0.04 {y - h}", AnchorMax = $"0.45 {y}" }
            }, parent);

            e.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = TextColor },
                RectTransform = { AnchorMin = $"0.5 {y - h}", AnchorMax = $"0.75 {y}" }
            }, parent);

            e.Add(new CuiButton
            {
                Button = { Command = cmd, Color = BtnColor },
                RectTransform = { AnchorMin = $"0.78 {y - h + 0.01f}", AnchorMax = $"0.96 {y - 0.01f}" },
                Text = { Text = GetMessage(Lang.Change, userId), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = AccentColor }
            }, parent);

            y -= h + gap;
        }

        [ConsoleCommand("nwgsettings.close")]
        private void CmdClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, SettingsPanel);
        }

        [ConsoleCommand("nwgsettings.toggle")]
        private void CmdToggle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string setting = arg.GetString(0, "").ToLower();
            var prefs = GetOrCreate(player.userID);

            switch (setting)
            {
                case "autodoor":
                    prefs.AutoDoorEnabled = !prefs.AutoDoorEnabled;
                    break;
                case "autolock":
                    prefs.AutoLockEnabled = !prefs.AutoLockEnabled;
                    break;
                case "autocode":
                    prefs.AutoCodeEnabled = !prefs.AutoCodeEnabled;
                    break;
                case "autoauth":
                    prefs.AutoAuthEnabled = !prefs.AutoAuthEnabled;
                    break;
            }

            SaveData();
            DrawSettingsUI(player); // Refresh UI
        }

        [ConsoleCommand("nwgsettings.setdelay")]
        private void CmdSetDelay(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, SettingsPanel);
            player.ChatMessage(GetMessage(Lang.EnterDelay, player.UserIDString));
        }

        [ConsoleCommand("nwgsettings.setcode")]
        private void CmdSetCode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, SettingsPanel);
            player.ChatMessage(GetMessage(Lang.EnterCode, player.UserIDString));
        }
#endregion

#region Chat Commands
        [ChatCommand("setautocode")]
        private void CmdAutoCode(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { player.ChatMessage(GetMessage(Lang.UsageAutoCode, player.UserIDString)); return; }
            
            string code = args[0];
            if (code.Length != 4 || !int.TryParse(code, out _))
            {
                player.ChatMessage(GetMessage(Lang.InvalidCode, player.UserIDString));
                return;
            }

            var prefs = GetOrCreate(player.userID);
            prefs.Code = code;
            SaveData();
            player.ChatMessage(GetMessage(Lang.AutoCodeUpdated, player.UserIDString, code));
        }

        [ChatCommand("doordelay")]
        private void CmdDoorDelay(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { player.ChatMessage(GetMessage(Lang.UsageDoorDelay, player.UserIDString)); return; }
            
            if (!float.TryParse(args[0], out float delay) || delay < 1f || delay > 30f)
            {
                player.ChatMessage(GetMessage(Lang.InvalidDelay, player.UserIDString));
                return;
            }

            var prefs = GetOrCreate(player.userID);
            prefs.AutoDoorDelay = delay;
            SaveData();
            player.ChatMessage(GetMessage(Lang.DoorDelayUpdated, player.UserIDString, delay));
        }
#endregion
    }
}
