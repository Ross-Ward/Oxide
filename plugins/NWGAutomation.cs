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
                    var codeLock = GameManager.server.CreateEntity("assets/prefabs/locks/keypad/lock.code.prefab") as CodeLock;
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
                        player.ChatMessage("<color=#aaffaa>AutoLock:</color> Code lock deployed & set.");
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
                
                SendReply(player, $"<color=#aaffaa>AutoCode</color>: Lock set to {prefs.Code}");
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
        private const string BgColor = "0.08 0.08 0.12 0.95";
        private const string HeaderColor = "0.15 0.15 0.22 1";
        private const string OnColor = "0.2 0.7 0.3 0.9";
        private const string OffColor = "0.7 0.2 0.2 0.9";
        private const string BtnColor = "0.2 0.2 0.3 0.8";

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
            e.Add(new CuiLabel { Text = { Text = "⚙  PLAYER SETTINGS", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 1 1" }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "0.9 1" } }, bg);

            // Close button
            e.Add(new CuiButton { Button = { Command = "nwgsettings.close", Color = "0.8 0.2 0.2 0.8" }, RectTransform = { AnchorMin = "0.92 0.92", AnchorMax = "0.98 0.98" }, Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter } }, bg);

            // Settings rows
            float y = 0.82f;
            float rowH = 0.08f;
            float gap = 0.02f;

            // Auto Door Close
            DrawToggleRow(e, bg, ref y, rowH, gap,
                "Auto Door Close", "Doors automatically close after a delay",
                prefs.AutoDoorEnabled, "nwgsettings.toggle autodoor");

            // Auto Door Delay
            DrawValueRow(e, bg, ref y, rowH, gap,
                "Door Close Delay", $"{prefs.AutoDoorDelay}s",
                "nwgsettings.setdelay");

            // Auto Lock
            DrawToggleRow(e, bg, ref y, rowH, gap,
                "Auto Lock Doors", "Automatically place code lock on new doors",
                prefs.AutoLockEnabled, "nwgsettings.toggle autolock");

            // Auto Code
            DrawToggleRow(e, bg, ref y, rowH, gap,
                "Auto Code Lock", "Automatically set your saved code on locks",
                prefs.AutoCodeEnabled, "nwgsettings.toggle autocode");

            // Auto Auth
            DrawToggleRow(e, bg, ref y, rowH, gap,
                "Auto Team Auth", "Auto-authorize teammates on TCs, turrets, locks",
                prefs.AutoAuthEnabled, "nwgsettings.toggle autoauth");

            // Saved Code display
            string codeDisplay = string.IsNullOrEmpty(prefs.Code) ? "<color=#ff6666>Not Set</color>" : $"<color=#aaffaa>{prefs.Code}</color>";
            DrawValueRow(e, bg, ref y, rowH, gap,
                "Saved Code", codeDisplay,
                "nwgsettings.setcode");

            // Footer
            e.Add(new CuiLabel
            {
                Text = { Text = "Use /setautocode <4-digit> to change your code", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.6 1" },
                RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.9 0.08" }
            }, bg);

            CuiHelper.AddUi(player, e);
        }

        private void DrawToggleRow(CuiElementContainer e, string parent, ref float y, float h, float gap, string label, string desc, bool isOn, string cmd)
        {
            // Label
            e.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 15, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 1 1" },
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
            string toggleText = isOn ? "ON" : "OFF";
            e.Add(new CuiButton
            {
                Button = { Command = cmd, Color = toggleColor },
                RectTransform = { AnchorMin = $"0.78 {y - h + 0.01f}", AnchorMax = $"0.96 {y - 0.01f}" },
                Text = { Text = toggleText, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, parent);

            y -= h + gap;
        }

        private void DrawValueRow(CuiElementContainer e, string parent, ref float y, float h, float gap, string label, string value, string cmd)
        {
            e.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 15, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 1 1" },
                RectTransform = { AnchorMin = $"0.04 {y - h}", AnchorMax = $"0.45 {y}" }
            }, parent);

            e.Add(new CuiLabel
            {
                Text = { Text = value, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = $"0.5 {y - h}", AnchorMax = $"0.75 {y}" }
            }, parent);

            e.Add(new CuiButton
            {
                Button = { Command = cmd, Color = BtnColor },
                RectTransform = { AnchorMin = $"0.78 {y - h + 0.01f}", AnchorMax = $"0.96 {y - 0.01f}" },
                Text = { Text = "CHANGE", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.7 0.8 1 1" }
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
            player.ChatMessage("<color=#55aaff>Enter your desired door close delay in seconds:</color>\nUse <color=#ffcc00>/doordelay <seconds></color> (e.g., /doordelay 3)");
        }

        [ConsoleCommand("nwgsettings.setcode")]
        private void CmdSetCode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, SettingsPanel);
            player.ChatMessage("<color=#55aaff>Set your auto-code:</color>\nUse <color=#ffcc00>/setautocode <4-digit code></color> (e.g., /setautocode 1234)");
        }
        #endregion

        #region Chat Commands
        [ChatCommand("setautocode")]
        private void CmdAutoCode(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { player.ChatMessage("Usage: /setautocode <4-digit code>"); return; }
            
            string code = args[0];
            if (code.Length != 4 || !int.TryParse(code, out _))
            {
                player.ChatMessage("<color=red>Code must be exactly 4 digits.</color>");
                return;
            }

            var prefs = GetOrCreate(player.userID);
            prefs.Code = code;
            SaveData();
            player.ChatMessage($"<color=#aaffaa>AutoCode set to:</color> {code}");
        }

        [ChatCommand("doordelay")]
        private void CmdDoorDelay(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0) { player.ChatMessage("Usage: /doordelay <seconds>"); return; }
            
            if (!float.TryParse(args[0], out float delay) || delay < 1f || delay > 30f)
            {
                player.ChatMessage("<color=red>Delay must be between 1 and 30 seconds.</color>");
                return;
            }

            var prefs = GetOrCreate(player.userID);
            prefs.AutoDoorDelay = delay;
            SaveData();
            player.ChatMessage($"<color=#aaffaa>Door close delay set to:</color> {delay}s");
        }
        #endregion
    }
}

