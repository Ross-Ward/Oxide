using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGChat", "NWG Team", "3.0.0")]
    [Description("Manages Chat Formatting, Titles, and Groups for NWG.")]
    public class NWGChat : RustPlugin
    {
        #region Configuration
        private class ChatGroup
        {
            public string Name;
            public int Priority; // Higher = Primary
            public string Title;
            public string TitleColor = "#ffffff";
            public string NameColor = "#55aaff";
            public string MessageColor = "#ffffff";
            public bool IsHidden = false; // For backend groups
        }

        private class PluginConfig
        {
            public int MaxMessageLength = 120;
            public List<ChatGroup> Groups = new List<ChatGroup>();
        }
        private PluginConfig _config;
        #endregion

        #region State
        // Cache player's primary group to avoid continuous LINQ
        private Dictionary<ulong, ChatGroup> _playerGroupCache = new Dictionary<ulong, ChatGroup>();
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
                if (_config == null || _config.Groups.Count == 0)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }

            // Register Groups as Permissions
            foreach (var group in _config.Groups)
            {
                if (!permission.GroupExists(group.Name))
                    permission.CreateGroup(group.Name, group.Name, 0);
            }
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new configuration file for NWG Chat");
            _config = new PluginConfig();
            // Default Groups
            _config.Groups.Add(new ChatGroup { Name = "admin", Priority = 100, Title = "[Admin]", TitleColor = "#ff5555", NameColor = "#ffaa55", MessageColor = "#ffaa55" });
            _config.Groups.Add(new ChatGroup { Name = "mod", Priority = 50, Title = "[Mod]", TitleColor = "#55ff55", NameColor = "#aaffaa", MessageColor = "#ffffff" });
            _config.Groups.Add(new ChatGroup { Name = "vip", Priority = 10, Title = "[VIP]", TitleColor = "#ffff55", NameColor = "#ffffaa", MessageColor = "#ffffff" });
            _config.Groups.Add(new ChatGroup { Name = "default", Priority = 0, Title = "[Player]", TitleColor = "#55aaff", NameColor = "#55aaff", MessageColor = "#ffffff" });
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            if (BasePlayer.activePlayerList != null)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    UpdatePlayerCache(player.IPlayer);
                }
            }
        }

        private void OnUserGroupAdded(string id, string groupName)
        {
            var p = covalence.Players.FindPlayer(id);
            if (p != null) UpdatePlayerCache(p);
        }

        private void OnUserGroupRemoved(string id, string groupName)
        {
            var p = covalence.Players.FindPlayer(id);
            if (p != null) UpdatePlayerCache(p);
        }
        #endregion

        #region Hooks
        private object OnUserChat(IPlayer player, string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            // Ignore commands early — let the game/other plugins handle them
            if (message.StartsWith("/") || message.StartsWith("!")) return null;

            // Mute Check logic removed temporarily due to API incompatibility
            /*
            if (player.IsMuted)
            {
                player.Message("<color=red>You are muted and cannot chat.</color>");
                return true;
            }
            */

            // Update Cache if needed (rare)
            ulong uid = ulong.Parse(player.Id);
            if (!_playerGroupCache.ContainsKey(uid)) UpdatePlayerCache(player);
            
            var group = _playerGroupCache[uid];

            // Format Logic
            string finalMessage = FormatMessage(group, player, message);

            // Broadcast to all
            // We return true to suppress default chat, and send our own
            Server.Broadcast(finalMessage);
            
            // Log to console
            Puts($"{player.Name}: {message}");
            
            return true;
        }
        #endregion

        #region Core
        private void UpdatePlayerCache(IPlayer player)
        {
             // Find best group
             ChatGroup best = null;
             foreach (var g in _config.Groups)
             {
                 if (player.BelongsToGroup(g.Name) || (g.Name == "default"))
                 {
                     if (best == null || g.Priority > best.Priority)
                         best = g;
                 }
             }
             if (best == null) best = _config.Groups.FirstOrDefault(x => x.Name == "default");
             
             ulong uid = ulong.Parse(player.Id);
             _playerGroupCache[uid] = best;
        }

        private string FormatMessage(ChatGroup group, IPlayer player, string message)
        {
            // Sanitize
            if (message.Length > _config.MaxMessageLength) message = message.Substring(0, _config.MaxMessageLength);
            
            // Unity Rich Text support
            string title = string.IsNullOrEmpty(group.Title) ? "" : $"<color={group.TitleColor}>{group.Title}</color> ";
            string name = $"<color={group.NameColor}>{player.Name}</color>";
            string msg = $"<color={group.MessageColor}>{message}</color>";

            return $"{title}{name}: {msg}";
        }
        #endregion
    }
}


