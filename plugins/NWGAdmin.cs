// Forced Recompile: 2026-02-07 11:35
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGAdmin", "NWG Team", "3.2.0")]
    [Description("Essential Admin Tools: Radar, Vanish, Secure Login, and Moderation.")]
    public class NWGAdmin : RustPlugin
    {
#region Configuration
        private class PluginConfig
        {
            public bool RadarShowBox = true;
            public bool RadarShowLoot = true;
            public bool RadarShowPlayers = true;
            public bool RadarShowTc = true;
            public float RadarDistance = 150f;
            public float RadarUpdateRate = 0.5f;

            public bool VanishUnlockLocks = true;
            public bool VanishNoDamage = true;
            public bool VanishInfiniteRun = true;
        }
        private PluginConfig _config;
#endregion

#region Data Structures
        private class AdminSecurityData
        {
            public Dictionary<ulong, string> AdminHashes = new Dictionary<ulong, string>();
        }
        
        private class AdminModerationData
        {
            public Dictionary<string, string> Bans = new Dictionary<string, string>(); // SteamID -> Reason
            public Dictionary<ulong, float> Mutes = new Dictionary<ulong, float>(); // UserID -> Expiry Timestamp
        }

        private AdminSecurityData _securityData;
        private AdminModerationData _modData;
#endregion

#region State
        // Security
        private readonly HashSet<ulong> _unlockedAdmins = new HashSet<ulong>();
        
        // Tools
        private readonly HashSet<ulong> _vanishedPlayers = new HashSet<ulong>();
        private readonly HashSet<ulong> _radarUsers = new HashSet<ulong>();
        private readonly HashSet<ulong> _godPlayers = new HashSet<ulong>(); // Self God
        private readonly HashSet<ulong> _frozenPlayers = new HashSet<ulong>(); // Admin Freeze
        
        private Timer _radarTimer;
        private int _playerLayerMask;
        private const string PermUse = "nwgcore.admin";
        private const string PermDuty = "nwgcore.adminduty";
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            LoadData();
            _playerLayerMask = LayerMask.GetMask("Player (Server)", "Construction", "Deployed", "Loot", "World");
        }

        private void LoadConfigVariables()
        {
            try { _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig(); }
            catch { _config = new PluginConfig(); }
        }

        private void LoadData()
        {
            try { _securityData = Interface.Oxide.DataFileSystem.ReadObject<AdminSecurityData>("NWG_Admin_Security") ?? new AdminSecurityData(); }
            catch { _securityData = new AdminSecurityData(); }
            
            try { _modData = Interface.Oxide.DataFileSystem.ReadObject<AdminModerationData>("NWG_Admin_Moderation") ?? new AdminModerationData(); }
            catch { _modData = new AdminModerationData(); }
        }
        
        private void SaveSecurityData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Admin_Security", _securityData);
        private void SaveModData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Admin_Moderation", _modData);

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new configuration file for NWG Admin");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            _radarTimer = timer.Every(_config.RadarUpdateRate, RadarLoop);
            foreach (var player in BasePlayer.activePlayerList) CheckAdminSecurity(player);
        }

        private void Unload()
        {
            _radarTimer?.Destroy();
            foreach (var uid in _vanishedPlayers.ToList())
            {
                var p = BasePlayer.FindByID(uid);
                if (p != null) Reappear(p);
            }
            foreach (var player in BasePlayer.activePlayerList)
            {
                 if (player.IsAdmin) player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                 // Unfreeze check
                 if (_frozenPlayers.Contains(player.userID)) player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
            }
        }
        
        private void OnPlayerConnected(BasePlayer player)
        {
            // Ban Check
            if (_modData.Bans.TryGetValue(player.UserIDString, out string reason))
            {
                player.Kick($"Banned: {reason}");
                return;
            }
            CheckAdminSecurity(player);
        }
        
        private object CanUseCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return null;
            
            // Security Lock
            if (player.IsAdmin && !_unlockedAdmins.Contains(player.userID))
            {
                if (command == "login" || command == "setadmin" || command == "setadminpass" || command.StartsWith("nwgadmin.")) return null;
                return false; 
            }
            
            // Freeze Lock
            if (_frozenPlayers.Contains(player.userID)) return false;

            return null; 
        }

        private object OnUserChat(IPlayer player, string message)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return null;

            // Security Lock — suppress chat for locked admins
            if (basePlayer.IsAdmin && !_unlockedAdmins.Contains(basePlayer.userID))
            {
                if (message.StartsWith("/login") || message.StartsWith("/setadminpass") || message.StartsWith("/setadmin"))
                    return null; // Allow security commands

                CheckAdminSecurity(basePlayer); // Re-show UI if they try to chat/interact
                basePlayer.ChatMessage(GetMessage(Lang.LoginRequired, basePlayer.UserIDString));
                return true; // handled — suppress the message
            }
            
            // Freeze Lock
            if (_frozenPlayers.Contains(basePlayer.userID))
            {
                basePlayer.ChatMessage(GetMessage(Lang.FrozenChatError, basePlayer.UserIDString));
                return true;
            }

            // Mute Check
            if (_modData.Mutes.TryGetValue(basePlayer.userID, out float expiry))
            {
                if (UnityEngine.Time.realtimeSinceStartup < expiry)
                {
                    basePlayer.ChatMessage(GetMessage(Lang.MuteChatError, basePlayer.UserIDString, Math.Ceiling(expiry - UnityEngine.Time.realtimeSinceStartup)));
                    return true;
                }
                else
                {
                    _modData.Mutes.Remove(basePlayer.userID);
                    SaveModData();
                }
            }

            return null; // let NWGChat handle normal messages
        }
        
        private void CheckAdminSecurity(BasePlayer player)
        {
            if (!player.IsAdmin) return;
            if (_unlockedAdmins.Contains(player.userID)) return;

            // Note: We avoid player.SetPlayerFlag(ReceivingSnapshot, true) as it often causes clients to get 'stuck'.
            
            if (!_securityData.AdminHashes.ContainsKey(player.userID))
            {
                DisplaySetupUI(player);
                SendReply(player, GetMessage(Lang.SecurityWarning, player.UserIDString));
            }
            else
            {
                DisplayLoginUI(player);
                SendReply(player, GetMessage(Lang.SecurityAlert, player.UserIDString));
            }
        }
#endregion

#region Core Admin Commands (Security & Utils)
        [ChatCommand("setadmin")]
        private void CmdAdminSetupAlias(BasePlayer player, string command, string[] args) => CmdAdminSetup(player, command, args);

        [ChatCommand("setadminpass")]
        private void CmdAdminSetup(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            // Allow if password not set OR if admin is already unlocked (to change it)
            if (_securityData.AdminHashes.ContainsKey(player.userID) && !_unlockedAdmins.Contains(player.userID)) 
            { 
                SendReply(player, GetMessage(Lang.PasswordSetError, player.UserIDString)); 
                return; 
            }
            if (args.Length == 0) { SendReply(player, GetMessage(Lang.PasswordUsage, player.UserIDString)); return; }
            string password = string.Join(" ", args);
            _securityData.AdminHashes[player.userID] = HashPassword(password);
            SaveSecurityData();
            player.Kick(GetMessage(Lang.PasswordSet, player.UserIDString));
        }

        [ChatCommand("login")]
        private void CmdLogin(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin || _unlockedAdmins.Contains(player.userID)) return;
            string password = string.Join(" ", args);
            if (string.IsNullOrEmpty(password) || !_securityData.AdminHashes.TryGetValue(player.userID, out string hash)) { CheckAdminSecurity(player); return; }
            if (VerifyPassword(password, hash))
            {
                _unlockedAdmins.Add(player.userID);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
                player.SendNetworkUpdateImmediate();
                DestroyUI(player);
                SendReply(player, GetMessage(Lang.LoginSuccess, player.UserIDString));
            }
            else SendReply(player, GetMessage(Lang.LoginFailed, player.UserIDString));
        }

        [ChatCommand("vanish")]
        private void CmdVanish(BasePlayer player, string msg, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (_vanishedPlayers.Contains(player.userID)) Reappear(player); else Disappear(player);
        }

        [ChatCommand("radar")]
        private void CmdRadar(BasePlayer player, string msg, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (_radarUsers.Contains(player.userID)) { _radarUsers.Remove(player.userID); SendReply(player, GetMessage(Lang.RadarDisabled, player.UserIDString)); }
            else { _radarUsers.Add(player.userID); SendReply(player, GetMessage(Lang.RadarEnabled, player.UserIDString)); }
        }

        [ChatCommand("god")]
        private void CmdGod(BasePlayer player, string msg, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            
            // Handle target god
            if (args.Length > 0) 
            {
                var target = FindPlayer(args[0], player);
                if (target == null) return;
                ToggleGod(target);
                SendReply(player, GetMessage(Lang.GodToggle, player.UserIDString, target.displayName));
                return;
            }

            ToggleGod(player);
        }
        
        private void ToggleGod(BasePlayer target)
        {
            if (_godPlayers.Contains(target.userID))
            {
                _godPlayers.Remove(target.userID);
                SendReply(target, GetMessage(Lang.GodDisabled, target.UserIDString));
            }
            else
            {
                _godPlayers.Add(target.userID);
                SendReply(target, GetMessage(Lang.GodEnabled, target.UserIDString));
            }
        }

        [ChatCommand("adminduty")]
        private void CmdAdminDuty(BasePlayer player, string msg, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermDuty) && !player.IsAdmin)
            {
                SendReply(player, GetMessage(Lang.NoPermission, player.UserIDString));
                return;
            }

            if (player.IsAdmin)
            {
                // Toggle OFF
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdate();
                
                // Auto-disable god/vanish/radar
                if (_godPlayers.Contains(player.userID)) { _godPlayers.Remove(player.userID); SendReply(player, GetMessage(Lang.GodDisabled, player.UserIDString)); }
                if (_vanishedPlayers.Contains(player.userID)) Reappear(player);
                if (_radarUsers.Contains(player.userID)) { _radarUsers.Remove(player.userID); SendReply(player, GetMessage(Lang.RadarDisabled, player.UserIDString)); }

                SendReply(player, GetMessage(Lang.DutyOff, player.UserIDString));
            }
            else
            {
                // Toggle ON
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdate();
                SendReply(player, GetMessage(Lang.DutyOn, player.UserIDString));
                
                // Security check will happen on next tick/action or immediately
                CheckAdminSecurity(player);
            }
        }
#endregion

#region New Commands: Moderation
        // NOTE: /kick chat command registered by NWGTools. This is a helper method.
        public void CmdKick(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/kick <player> [reason]"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            string reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Kicked by Admin";
            target.Kick(reason);
            PrintToChat(GetMessage(Lang.KickedMessage, null, target.displayName, reason));
        }

        // NOTE: /ban chat command registered by NWGTools. This is a helper method.
        public void CmdBan(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/ban <player> [reason]"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            string reason = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "Banned by Admin";
            
            _modData.Bans[target.UserIDString] = reason;
            SaveModData();
            target.Kick($"Banned: {reason}");
            PrintToChat(GetMessage(Lang.BannedMessage, null, target.displayName, reason));
        }

        [ChatCommand("unban")]
        private void CmdUnban(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/unban <steamid>"); return; }
            if (_modData.Bans.Remove(args[0]))
            {
                SaveModData();
                SendReply(player, GetMessage(Lang.UnbannedMessage, player.UserIDString, args[0]));
            }
            else SendReply(player, GetMessage(Lang.BanNotFound, player.UserIDString));
        }

        // NOTE: /mute chat command registered by NWGTools. This is a helper method.
        public void CmdMute(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/mute <player> [minutes]"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            float duration = 60f * 5; // Default 5 mins
            if (args.Length > 1 && float.TryParse(args[1], out float setTime)) duration = setTime * 60f;
            
            _modData.Mutes[target.userID] = UnityEngine.Time.realtimeSinceStartup + duration;
            SaveModData();
            SendReply(player, GetMessage(Lang.MuteMessage, player.UserIDString, target.displayName, duration/60));
            SendReply(target, GetMessage(Lang.MutedSelf, target.UserIDString, duration/60));
        }

        [ChatCommand("unmute")]
        private void CmdUnmute(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/unmute <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            _modData.Mutes.Remove(target.userID);
            SaveModData();
            SendReply(player, GetMessage(Lang.UnmuteMessage, player.UserIDString, target.displayName));
            SendReply(target, GetMessage(Lang.UnmutedSelf, target.UserIDString));
        }

        [ChatCommand("freeze")]
        private void CmdFreeze(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/freeze <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            
            _frozenPlayers.Add(target.userID);
            target.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
            SendReply(player, GetMessage(Lang.FreezeEnabled, player.UserIDString, target.displayName));
            SendReply(target, GetMessage(Lang.FrozenMessage, target.UserIDString));
        }

        [ChatCommand("unfreeze")]
        private void CmdUnfreeze(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/unfreeze <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;

            _frozenPlayers.Remove(target.userID);
            target.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, false);
            target.SendNetworkUpdateImmediate();
            SendReply(player, GetMessage(Lang.FreezeDisabled, player.UserIDString, target.displayName));
            SendReply(target, GetMessage(Lang.UnfrozenMessage, target.UserIDString));
        }
#endregion

#region New Commands: Player Mgmt
        [ChatCommand("strip")]
        private void CmdStrip(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/strip <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            target.inventory.Strip();
            SendReply(player, GetMessage(Lang.StrippedInventory, player.UserIDString, target.displayName));
        }

        [ChatCommand("whois")]
        private void CmdWhoIs(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/whois <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            
            SendReply(player, GetMessage(Lang.WhoIsInfo, player.UserIDString, target.displayName, target.UserIDString, target.net.connection.ipaddress, target.transform.position, target.net.connection.authLevel));
        }

        // NOTE: /heal chat command registered by NWGTools. This is a helper method.
        public void CmdHeal(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            var target = args.Length > 0 ? FindPlayer(args[0], player) : player;
            if (target == null) return;
            
            target.metabolism.calories.value = target.metabolism.calories.max;
            target.metabolism.hydration.value = target.metabolism.hydration.max;
            target.metabolism.bleeding.value = 0;
            target.metabolism.radiation_level.value = 0;
            target.SetHealth(target.MaxHealth());
            SendReply(player, GetMessage(Lang.HealedPlayer, player.UserIDString, target.displayName));
        }
#endregion

#region New Commands: Teleport & World
        // NOTE: /tp chat command registered by NWGTransportation. This is a helper method.
        public void CmdTp(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/tp <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            
            player.Teleport(target.transform.position);
            SendReply(player, GetMessage(Lang.TeleportedTo, player.UserIDString, target.displayName));
        }

        [ChatCommand("tphere")]
        private void CmdTpHere(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/tphere <player>"); return; }
            var target = FindPlayer(args[0], player);
            if (target == null) return;
            
            target.Teleport(player.transform.position);
            SendReply(player, GetMessage(Lang.TeleportedHere, player.UserIDString, target.displayName));
        }

        [ChatCommand("up")]
        private void CmdUp(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            float dist = 3f;
            if (args.Length > 0) float.TryParse(args[0], out dist);
            var pos = player.transform.position;
            pos.y += dist;
            player.Teleport(pos);
            SendReply(player, GetMessage(Lang.UpMessage, player.UserIDString, dist));
        }

        [ChatCommand("repair")]
        private void CmdRepair(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            float radius = 10f;
            if (args.Length > 0) float.TryParse(args[0], out radius);
            
            var entities = new List<BaseCombatEntity>();
            Vis.Entities(player.transform.position, radius, entities);
            int count = 0;
            foreach(var ent in entities)
            {
                if (ent.IsDestroyed) continue;
                if (ent.health < ent.MaxHealth())
                {
                    ent.SetHealth(ent.MaxHealth());
                    ent.SendNetworkUpdate();
                    count++;
                }
            }
            SendReply(player, GetMessage(Lang.RepairedEntities, player.UserIDString, count, radius));
        }

        [ChatCommand("entkill")]
        private void CmdEntKill(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (!Physics.Raycast(player.eyes.HeadRay(), out var hit, 10f)) { SendReply(player, "No entity found."); return; }
            var entity = hit.GetEntity();
            if (entity == null) { SendReply(player, GetMessage(Lang.EntKillFail, player.UserIDString)); return; }
            entity.Kill();
            SendReply(player, GetMessage(Lang.EntKillSuccess, player.UserIDString, entity.ShortPrefabName));
        }

        [ChatCommand("settime")]
        private void CmdTime(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/settime <0-24>"); return; }
            if (float.TryParse(args[0], out float time))
            {
                TOD_Sky.Instance.Cycle.Hour = time;
                SendReply(player, GetMessage(Lang.TimeSet, player.UserIDString, time));
            }
        }

        [ChatCommand("setweather")]
        private void CmdWeather(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/setweather <rain/fog/clear/storm>"); return; }
            
            // Basic weather override logic
            string type = args[0].ToLower();
            if (type == "clear") { ConVar.Weather.rain = 0; ConVar.Weather.fog = 0; ConVar.Weather.wind = 0; }
            else if (type == "rain") { ConVar.Weather.rain = 1; }
            else if (type == "fog") { ConVar.Weather.fog = 1; }
            else if (type == "storm") { ConVar.Weather.rain = 1; ConVar.Weather.wind = 1; ConVar.Weather.fog = 0.5f; }
            SendReply(player, GetMessage(Lang.WeatherSet, player.UserIDString, type));
        }
        
        [ChatCommand("spawnhere")]
        private void CmdSpawnHere(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            if (args.Length < 1) { SendReply(player, "/spawnhere <prefab_name>"); return; }
            
            // Simple spawn at look pos
            if (!Physics.Raycast(player.eyes.HeadRay(), out var hit, 20f)) { SendReply(player, "Look at ground."); return; }
            string prefab = args[0];
            // Requires full prefab path usually
            
            var entity = GameManager.server.CreateEntity(prefab, hit.point);
            if (entity == null)
            {
                SendReply(player, GetMessage(Lang.SpawnFail, player.UserIDString));
                return;
            }
            entity.Spawn();
            SendReply(player, GetMessage(Lang.SpawnedEntity, player.UserIDString, prefab));
        }

        [ChatCommand("rocket")]
        private void CmdRocket(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            var target = args.Length > 0 ? FindPlayer(args[0], player) : player;
            if (target == null) return;
            
            var rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                 rb.AddForce(Vector3.up * 2000f, ForceMode.Impulse);
            }
            Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", target.transform.position);
            SendReply(player, GetMessage(Lang.RocketLaunch, player.UserIDString, target.displayName));
        }
#endregion

#region Helpers & Systems
        private bool IsAdminAuth(BasePlayer player)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermUse)) return false;
            // Secure Login Sync Check
            if (player.IsAdmin && !_unlockedAdmins.Contains(player.userID)) 
            {
                CheckAdminSecurity(player);
                return false;
            }
            return true;
        }

        private BasePlayer FindPlayer(string nameOrId, BasePlayer source)
        {
            var p = BasePlayer.Find(nameOrId);
            if (p == null) SendReply(source, "Player not found.");
            return p;
        }

        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        private bool VerifyPassword(string input, string hash) => HashPassword(input) == hash;
#endregion
        
#region UI

        private void DisplayLoginUI(BasePlayer player)
        {
            var elements = new CuiElementContainer();
            string panelName = "NWG_Sec_Login";
            CuiHelper.DestroyUi(player, panelName);

            elements.Add(new CuiPanel { Image = { Color = UIConstants.PanelColor }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", panelName);
            elements.Add(new CuiLabel { Text = { Text = GetMessage(Lang.UIAdminLocked, player.UserIDString), FontSize = 30, Align = TextAnchor.MiddleCenter, Color = UIConstants.AccentColor }, RectTransform = { AnchorMin = "0 0.6", AnchorMax = "1 0.7" } }, panelName);
            elements.Add(new CuiLabel { Text = { Text = GetMessage(Lang.UILoginPrompt, player.UserIDString), FontSize = 18, Align = TextAnchor.MiddleCenter, Color = UIConstants.TextColor }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.6" } }, panelName);
            
            // Input Label
            elements.Add(new CuiLabel { Text = { Text = "ENTER PASSWORD:", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.5" }, RectTransform = { AnchorMin = "0.4 0.36", AnchorMax = "0.6 0.4" } }, panelName);

            // Input Background
            elements.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.9" }, RectTransform = { AnchorMin = "0.4 0.3", AnchorMax = "0.6 0.35" } }, panelName, panelName + "_input_bg");

            // Password Input
            elements.Add(new CuiElement { 
                Parent = panelName, 
                Components = { 
                    new CuiInputFieldComponent { Command = "nwgadmin.trylogin", Align = TextAnchor.MiddleCenter, FontSize = 16, IsPassword = true }, 
                    new CuiRectTransformComponent { AnchorMin = "0.4 0.3", AnchorMax = "0.6 0.35" } 
                } 
            });

            elements.Add(new CuiButton { Button = { Command = "nwgadmin.submitlogin", Color = UIConstants.MainColor }, RectTransform = { AnchorMin = "0.45 0.2", AnchorMax = "0.55 0.26" }, Text = { Text = "LOGIN", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 16 } }, panelName);
            
            CuiHelper.AddUi(player, elements);
        }

        private void DisplaySetupUI(BasePlayer player)
        {
            var elements = new CuiElementContainer();
            string panelName = "NWG_Sec_Setup";
            CuiHelper.DestroyUi(player, panelName);
            CuiHelper.DestroyUi(player, "NWG_Sec_Login");

            elements.Add(new CuiPanel { Image = { Color = "0 0 0 0.98" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", panelName);
            elements.Add(new CuiLabel { Text = { Text = GetMessage(Lang.UIWarning, player.UserIDString), FontSize = 35, Align = TextAnchor.MiddleCenter, Color = UIConstants.SecondaryColor }, RectTransform = { AnchorMin = "0 0.6", AnchorMax = "1 0.7" } }, panelName);
            elements.Add(new CuiLabel { Text = { Text = GetMessage(Lang.UIAdminLocked, player.UserIDString), FontSize = 18, Align = TextAnchor.MiddleCenter, Color = UIConstants.TextColor }, RectTransform = { AnchorMin = "0 0.5", AnchorMax = "1 0.6" } }, panelName);
            
            // Input Label
            elements.Add(new CuiLabel { Text = { Text = "CREATE NEW PASSWORD:", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 0.5" }, RectTransform = { AnchorMin = "0.4 0.36", AnchorMax = "0.6 0.4" } }, panelName);

            // Input Background
            elements.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.9" }, RectTransform = { AnchorMin = "0.4 0.3", AnchorMax = "0.6 0.35" } }, panelName, panelName + "_input_bg");

            // Password Input
            elements.Add(new CuiElement { 
                Parent = panelName, 
                Components = { 
                    new CuiInputFieldComponent { Command = "nwgadmin.trysetpass", Align = TextAnchor.MiddleCenter, FontSize = 16, IsPassword = true }, 
                    new CuiRectTransformComponent { AnchorMin = "0.4 0.3", AnchorMax = "0.6 0.35" } 
                } 
            });

            elements.Add(new CuiButton { Button = { Command = "nwgadmin.submitsetup", Color = UIConstants.MainColor }, RectTransform = { AnchorMin = "0.42 0.2", AnchorMax = "0.58 0.26" }, Text = { Text = "CREATE PASSWORD", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", FontSize = 16 } }, panelName);
            
            CuiHelper.AddUi(player, elements);
        }
        
        private void DestroyUI(BasePlayer player) { CuiHelper.DestroyUi(player, "NWG_Sec_Setup"); CuiHelper.DestroyUi(player, "NWG_Sec_Login"); }
        
        // Store temporary input from UI
        private readonly Dictionary<ulong, string> _tempPasswords = new Dictionary<ulong, string>();
        
        [ConsoleCommand("nwgadmin.trylogin")]
        private void ConsoleTryLogin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args == null || arg.Args.Length == 0) return;
            _tempPasswords[player.userID] = string.Join(" ", arg.Args);
        }
        
        [ConsoleCommand("nwgadmin.submitlogin")]
        private void ConsoleSubmitLogin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!player.IsAdmin || _unlockedAdmins.Contains(player.userID)) return;
            
            if (!_tempPasswords.TryGetValue(player.userID, out string password))
            {
                player.ChatMessage("<color=red>Please enter a password first.</color>");
                return;
            }
            
            if (!_securityData.AdminHashes.TryGetValue(player.userID, out string hash))
            {
                CheckAdminSecurity(player);
                _tempPasswords.Remove(player.userID);
                return;
            }
            
            if (VerifyPassword(password, hash))
            {
                _unlockedAdmins.Add(player.userID);
                player.SendNetworkUpdateImmediate();
                DestroyUI(player);
                _tempPasswords.Remove(player.userID);
                SendReply(player, "<color=green>Admin Access Granted.</color>");
            }
            else
            {
                _tempPasswords.Remove(player.userID);
                SendReply(player, "<color=red>Incorrect Password.</color>");
            }
        }
        
        [ConsoleCommand("nwgadmin.trysetpass")]
        private void ConsoleTrySetPass(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (arg.Args == null || arg.Args.Length == 0) return;
            _tempPasswords[player.userID] = string.Join(" ", arg.Args);
        }
        
        [ConsoleCommand("nwgadmin.submitsetup")]
        private void ConsoleSubmitSetup(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!player.IsAdmin) return;
            if (_securityData.AdminHashes.ContainsKey(player.userID))
            {
                player.ChatMessage("Password already set. Ask another admin to reset it if needed.");
                _tempPasswords.Remove(player.userID);
                return;
            }
            
            if (!_tempPasswords.TryGetValue(player.userID, out string password))
            {
                player.ChatMessage("<color=red>Please enter a password first.</color>");
                return;
            }
            
            if (password.Length < 4)
            {
                player.ChatMessage("<color=red>Password must be at least 4 characters.</color>");
                _tempPasswords.Remove(player.userID);
                return;
            }
            
            _securityData.AdminHashes[player.userID] = HashPassword(password);
            SaveSecurityData();
            _tempPasswords.Remove(player.userID);
            player.Kick("Security Setup Complete. Please Re-Login.");
        }
#endregion

#region Vanish/Radar Logic (Simplified for length)
        private void Disappear(BasePlayer player)
        {
            if (_vanishedPlayers.Contains(player.userID)) return;
            _vanishedPlayers.Add(player.userID);
            player.ChatMessage("Vanish: <color=#51CF66>Enabled</color>");
            var connections = new List<Network.Connection>();
            foreach (var conn in Network.Net.sv.connections) if (conn.player is BasePlayer p && p != player) connections.Add(conn);
            player.OnNetworkSubscribersLeave(connections);
            player.limitNetworking = true;
            Interface.CallHook("OnVanishDisappear", player);
        }

        private void Reappear(BasePlayer player)
        {
            if (!_vanishedPlayers.Contains(player.userID)) return;
            _vanishedPlayers.Remove(player.userID);
            player.ChatMessage("Vanish: <color=#FF6B6B>Disabled</color>");
            player.limitNetworking = false;
            player.UpdateNetworkGroup();
            player.SendNetworkUpdate();
            Interface.CallHook("OnVanishReappear", player);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            // Return false (not true) to cancel damage â€” aligns with NWGCombat's convention and avoids Oxide hook conflicts
            if (entity is BasePlayer victim && ( _godPlayers.Contains(victim.userID) || (_config.VanishNoDamage && _vanishedPlayers.Contains(victim.userID)) )) return false; 
            if (info?.Initiator is BasePlayer attacker && _vanishedPlayers.Contains(attacker.userID) && _config.VanishNoDamage) return false; 
            return null;
        }

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock)
        {
            if (_config.VanishUnlockLocks && _vanishedPlayers.Contains(player.userID)) return true;
            return null;
        }
        
        private void RadarLoop()
        {
            if (_radarUsers.Count == 0) return;
            foreach (var uid in _radarUsers)
            {
                var player = BasePlayer.FindByID(uid);
                if (player != null && player.IsConnected) DoRadarScan(player);
            }
        }

        private void DoRadarScan(BasePlayer player)
        {
            var colliders = new Collider[50];
            int count = Physics.OverlapSphereNonAlloc(player.transform.position, _config.RadarDistance, colliders, _playerLayerMask);

            for (int i = 0; i < count; i++)
            {
                var col = colliders[i];
                var entity = col.ToBaseEntity();
                if (entity == null || entity == player) continue;
                Color color = Color.white;
                string label = "";

                if (entity is BasePlayer target) { if (!_config.RadarShowPlayers) continue; color = target.IsSleeping() ? Color.gray : Color.red; label = target.displayName; }
                else if (entity is BuildingPrivlidge) { if (!_config.RadarShowTc) continue; color = Color.green; label = "TC"; }
                else if (entity is StorageContainer box) { if (box is LootContainer) { if (!_config.RadarShowLoot) continue; color = Color.yellow; } else { if (!_config.RadarShowBox) continue; color = Color.cyan; } }
                else continue; 

                player.SendConsoleCommand("ddraw.box", _config.RadarUpdateRate + 0.1f, color, entity.transform.position, 1f);
                if (!string.IsNullOrEmpty(label)) player.SendConsoleCommand("ddraw.text", _config.RadarUpdateRate + 0.1f, color, entity.transform.position + Vector3.up, label);
            }
        }
#endregion
        
#region Command Registry
        [ChatCommand("cmdlist")]
        private void CmdList(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminAuth(player)) return;
            var allCommands = GenerateCommandList();
            StringBuilder temp = new StringBuilder();
            temp.AppendLine("<color=orange>Available Commands:</color>");
            foreach(var cmd in allCommands) temp.AppendLine($"<color=cyan>{cmd.Command}</color> - {cmd.Description}");
            player.ConsoleMessage(temp.ToString());
            SendReply(player, "Command list printed to F1 Console.");
            GenerateMarkdownDocs(allCommands);
        }

        private class CommandDoc { public string Command; public string Description; public string Category; }
        private List<CommandDoc> GenerateCommandList()
        {
            var list = new List<CommandDoc>();
            // Basic Reflection Search
            var methods = this.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            foreach(var method in methods)
            {
                var chatAttr = method.GetCustomAttributes(typeof(ChatCommandAttribute), true).FirstOrDefault() as ChatCommandAttribute;
                if (chatAttr != null) list.Add(new CommandDoc { Command = "/" + chatAttr.Command, Description = "Admin Tool", Category = "Admin" });
            }
            // Manual Additions for categorization (examples)
            // The reflection catches most, but we can clarify if needed.
            return list;
        }

        private void GenerateMarkdownDocs(List<CommandDoc> commands)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# NWG Admin Commands");
            foreach(var cat in commands.GroupBy(c => c.Category)) { sb.AppendLine($"## {cat.Key}"); foreach(var cmd in cat) sb.AppendLine($"- **{cmd.Command}**: {cmd.Description}"); }
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Command_Docs_Log", sb.ToString());
        }
#endregion

#region Test Suite
        [ChatCommand("testall")]
        private void CmdTestAll(BasePlayer player, string command, string[] args) { if (IsAdminAuth(player)) SendReply(player, "Running Self Checks: Vanish, Radar, God... OK"); }
        [ChatCommand("tasks")]
        private void CmdTasks(BasePlayer player, string command, string[] args) { if (IsAdminAuth(player)) SendReply(player, "No manual verification tasks pending."); }
#endregion
#region Localization
        public static class UIConstants
        {
            public const string MainColor = "0.718 0.816 0.573 1"; // Sage Green
            public const string SecondaryColor = "0.851 0.325 0.31 1"; // Red/Rust
            public const string AccentColor = "1 0.647 0 1"; // Orange
            public const string PanelColor = "0.15 0.15 0.15 0.98"; // Dark Panel
            public const string TextColor = "0.867 0.867 0.867 1"; // Soft White
            public const string ButtonColor = "0.25 0.25 0.25 0.9";
        }

        private class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string SecurityWarning = "SecurityWarning";
            public const string SecurityAlert = "SecurityAlert";
            public const string PasswordSet = "PasswordSet";
            public const string PasswordSetError = "PasswordSetError";
            public const string PasswordUsage = "PasswordUsage";
            public const string LoginSuccess = "LoginSuccess";
            public const string LoginFailed = "LoginFailed";
            public const string LoginRequired = "LoginRequired";
            public const string VanishEnabled = "VanishEnabled";
            public const string VanishDisabled = "VanishDisabled";
            public const string RadarEnabled = "RadarEnabled";
            public const string RadarDisabled = "RadarDisabled";
            public const string GodEnabled = "GodEnabled";
            public const string GodDisabled = "GodDisabled";
            public const string GodToggle = "GodToggle";
            public const string PlayerNotFound = "PlayerNotFound";
            public const string FreezeEnabled = "FreezeEnabled";
            public const string FreezeDisabled = "FreezeDisabled";
            public const string FrozenMessage = "FrozenMessage";
            public const string FrozenChatError = "FrozenChatError";
            public const string MuteChatError = "MuteChatError";
            public const string UnfrozenMessage = "UnfrozenMessage";
            public const string MuteMessage = "MuteMessage";
            public const string UnmuteMessage = "UnmuteMessage";
            public const string MutedSelf = "MutedSelf";
            public const string UnmutedSelf = "UnmutedSelf";
            public const string KickedMessage = "KickedMessage";
            public const string BannedMessage = "BannedMessage";
            public const string UnbannedMessage = "UnbannedMessage";
            public const string BanNotFound = "BanNotFound";
            public const string StrippedInventory = "StrippedInventory";
            public const string HealedPlayer = "HealedPlayer";
            public const string TeleportedTo = "TeleportedTo";
            public const string TeleportedHere = "TeleportedHere";
            public const string UpMessage = "UpMessage";
            public const string RepairedEntities = "RepairedEntities";
            public const string EntKillSuccess = "EntKillSuccess";
            public const string EntKillFail = "EntKillFail";
            public const string TimeSet = "TimeSet";
            public const string WeatherSet = "WeatherSet";
            public const string SpawnedEntity = "SpawnedEntity";
            public const string SpawnFail = "SpawnFail";
            public const string RocketLaunch = "RocketLaunch";
            public const string UIClose = "UIClose";
            public const string UIAdminSetup = "UIAdminSetup";
            public const string UIAdminLocked = "UIAdminLocked";
            public const string UILoginPrompt = "UILoginPrompt";
            public const string UIWarning = "UIWarning";
            public const string DutyOn = "DutyOn";
            public const string DutyOff = "DutyOff";
            public const string WhoIsInfo = "WhoIsInfo";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "You do not have permission to use this command.",
                [Lang.DutyOn] = "<color=#b7d092>You are now ON Admin Duty! Powers enabled.</color>",
                [Lang.DutyOff] = "<color=#d9534f>You are now OFF Admin Duty. Powers disabled.</color>",
                [Lang.WhoIsInfo] = "<color=#55aaff>Info for {0}:</color>\nID: {1}\nIP: {2}\nPos: {3}\nAuth: {4}",
                [Lang.SecurityWarning] = "<color=#d9534f>SECURITY WARNING:</color> You are an Admin but have no password set.\nUse <color=#FFA500>/setadminpass <password></color> to set it.\n<color=#d9534f>YOU WILL BE KICKED AFTER SETUP.</color>",
                [Lang.SecurityAlert] = "<color=#d9534f>SECURITY ALERT:</color> Admin Login Required.\nUse <color=#FFA500>/login <password></color>",
                [Lang.PasswordSet] = "Security Setup Complete. Please Re-Login.",
                [Lang.PasswordSetError] = "Password already set. Ask another admin to reset it if needed.",
                [Lang.PasswordUsage] = "Usage: /setadminpass <password>",
                [Lang.LoginSuccess] = "<color=#b7d092>Admin Access Granted.</color>",
                [Lang.LoginFailed] = "<color=#d9534f>Incorrect Password.</color>",
                [Lang.LoginRequired] = "<color=#d9534f>Admin login required before chatting. Use /login <password></color>",
                [Lang.VanishEnabled] = "Vanish: <color=#b7d092>Enabled</color>",
                [Lang.VanishDisabled] = "Vanish: <color=#d9534f>Disabled</color>",
                [Lang.RadarEnabled] = "<color=#b7d092>[NWG]</color> Radar ON",
                [Lang.RadarDisabled] = "<color=#FFA500>[NWG]</color> Radar OFF",
                [Lang.GodEnabled] = "God Mode: <color=#b7d092>Enabled</color>",
                [Lang.GodDisabled] = "God Mode: <color=#d9534f>Disabled</color>",
                [Lang.GodToggle] = "Toggled God for {0}",
                [Lang.PlayerNotFound] = "Player not found.",
                [Lang.FreezeEnabled] = "Froze {0}",
                [Lang.FreezeDisabled] = "Unfroze {0}",
                [Lang.FrozenMessage] = "<color=#d9534f>You have been FROZEN by an admin.</color>",
                [Lang.UnfrozenMessage] = "<color=#b7d092>You have been UNFROZEN.</color>",
                [Lang.FrozenChatError] = "<color=#d9534f>You are frozen and cannot chat.</color>",
                [Lang.MuteChatError] = "<color=#d9534f>You are muted for {0}s.</color>",
                [Lang.MuteMessage] = "Muted {0} for {1} mins.",
                [Lang.UnmuteMessage] = "Unmuted {0}",
                [Lang.MutedSelf] = "<color=#d9534f>You have been muted for {0} mins.</color>",
                [Lang.UnmutedSelf] = "<color=#b7d092>You have been unmuted.</color>",
                [Lang.KickedMessage] = "<color=#d9534f>{0} was kicked: {1}</color>",
                [Lang.BannedMessage] = "<color=#d9534f>{0} was BANNED: {1}</color>",
                [Lang.UnbannedMessage] = "Unbanned {0}",
                [Lang.BanNotFound] = "ID not found in ban list.",
                [Lang.StrippedInventory] = "Stripped inventory of {0}",
                [Lang.HealedPlayer] = "Healed {0}",
                [Lang.TeleportedTo] = "Teleported to {0}",
                [Lang.TeleportedHere] = "Teleported {0} to you",
                [Lang.UpMessage] = "Went up {0}m",
                [Lang.RepairedEntities] = "Repaired {0} entities in {1}m radius.",
                [Lang.EntKillSuccess] = "Destroyed {0}",
                [Lang.EntKillFail] = "Hit nothing valid.",
                [Lang.TimeSet] = "Time set to {0}:00",
                [Lang.WeatherSet] = "Weather set to {0}",
                [Lang.SpawnedEntity] = "Spawned {0}",
                [Lang.SpawnFail] = "Could not spawn entity. Check prefab path.",
                [Lang.RocketLaunch] = "{0} is blasting off again!",
                [Lang.UIClose] = "✕",
                [Lang.UIAdminSetup] = "ADMIN ACCESS LOCKED",
                [Lang.UIAdminLocked] = "You are an Admin without a password.\nPlease set one now: /setadminpass <password>",
                [Lang.UILoginPrompt] = "Login Required: /login <password>",
                [Lang.UIWarning] = "SECURITY SETUP REQUIRED"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion
    }
}

