using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Newtonsoft.Json;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("NWGClans", "NWG Team", "3.0.0")]
    [Description("Unified Clan and Kit System.")]
    public class NWGClans : RustPlugin
    {
#region References
        [PluginReference] private Plugin ImageLibrary;
        [PluginReference] private Plugin NWGCore;
#endregion

#region Config
        private class PluginConfig
        {
            public int MaxClanMembers = 8;
            public int MaxAlliances = 2;
            public bool UseTagColors = true;
            public string ChatPrefix = "[Clan]";
        }

        private PluginConfig _config;
#endregion

#region Data
        private class StoredData
        {
            public Dictionary<string, Clan> Clans = new Dictionary<string, Clan>();
        }

        private class Clan
        {
            public string Tag;
            public string Description;
            public ulong OwnerId;
            public List<ulong> Members = new List<ulong>();
            public List<string> Alliances = new List<string>(); // List of Clan Tags
            public List<ulong> Invites = new List<ulong>(); // Player IDs
        }

        private StoredData _data;
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();

            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_Clans");
            }
            catch
            {
                _data = new StoredData();
            }
            if (_data == null) _data = new StoredData();
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
            Puts("Creating new configuration file for NWG Clans");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void Unload()
        {
            SaveData();
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("NWG_Clans", _data);
            NWGCore?.Call("RegisterAPI", "Clans");
        }
#endregion

#region Localization
        public static class Lang
        {
            public const string Usage = "Usage";
            public const string AlreadyInClan = "AlreadyInClan";
            public const string ClanExists = "ClanExists";
            public const string Created = "Created";
            public const string PlayerNotFound = "PlayerNotFound";
            public const string NotOwner = "NotOwner";
            public const string ClanFull = "ClanFull";
            public const string Invited = "Invited";
            public const string InvitedTo = "InvitedTo";
            public const string ClanNotFound = "ClanNotFound";
            public const string NotInvited = "NotInvited";
            public const string LeaveCurrent = "LeaveCurrent";
            public const string Joined = "Joined";
            public const string Disbanded = "Disbanded";
            public const string Left = "Left";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Usage] = "<color=#d9534f>[NWG]</color> Usage: /clan create <tag>, /clan join <tag>, /clan leave, /clan invite <player>",
                [Lang.AlreadyInClan] = "<color=#d9534f>[NWG]</color> You are already in a clan.",
                [Lang.ClanExists] = "<color=#d9534f>[NWG]</color> Clan tag <color=#FFA500>'{0}'</color> already exists.",
                [Lang.Created] = "<color=#b7d092>[NWG]</color> Clan <color=#FFA500>{0}</color> created successfully!",
                [Lang.PlayerNotFound] = "<color=#d9534f>[NWG]</color> Player not found.",
                [Lang.NotOwner] = "<color=#d9534f>[NWG]</color> You must be the clan owner to do that.",
                [Lang.ClanFull] = "<color=#d9534f>[NWG]</color> Your clan is full (Max: {0}).",
                [Lang.Invited] = "<color=#b7d092>[NWG]</color> Invited <color=#FFA500>{0}</color> to your clan.",
                [Lang.InvitedTo] = "<color=#b7d092>[NWG]</color> You have been invited to clan <color=#FFA500>{0}</color>. Type <color=#FFA500>/clan join {0}</color> to join.",
                [Lang.ClanNotFound] = "<color=#d9534f>[NWG]</color> Clan <color=#FFA500>'{0}'</color> not found.",
                [Lang.NotInvited] = "<color=#d9534f>[NWG]</color> You have not been invited to join this clan.",
                [Lang.LeaveCurrent] = "<color=#d9534f>[NWG]</color> You must leave your current clan first.",
                [Lang.Joined] = "<color=#b7d092>[NWG]</color> You have joined clan <color=#FFA500>{0}</color>!",
                [Lang.Disbanded] = "<color=#b7d092>[NWG]</color> Clan disbanded.",
                [Lang.Left] = "<color=#b7d092>[NWG]</color> You have left the clan."
            }, this);
        }
        
        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Clan Logic
        private Clan GetClan(string tag)
        {
            if (_data.Clans.TryGetValue(tag, out var clan)) return clan;
            return null;
        }

        private Clan GetPlayerClan(ulong playerId)
        {
            foreach(var clan in _data.Clans.Values)
            {
                if (clan.Members.Contains(playerId)) return clan;
            }
            return null;
        }

        // --- Commands ---

        [ChatCommand("clan")]
        private void CmdClan(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage(GetMessage(Lang.Usage, player.UserIDString));
                return;
            }

            string action = args[0].ToLower();

            if (action == "create")
            {
                if (args.Length < 2) { player.ChatMessage(GetMessage(Lang.Usage, player.UserIDString)); return; }
                string tag = args[1];
                
                if (GetPlayerClan(player.userID) != null) { player.ChatMessage(GetMessage(Lang.AlreadyInClan, player.UserIDString)); return; }
                if (_data.Clans.ContainsKey(tag)) { player.ChatMessage(GetMessage(Lang.ClanExists, player.UserIDString, tag)); return; }

                var clan = new Clan { Tag = tag, OwnerId = player.userID };
                clan.Members.Add(player.userID);
                _data.Clans[tag] = clan;
                
                player.ChatMessage(GetMessage(Lang.Created, player.UserIDString, tag));
                Interface.CallHook("OnClanCreate", tag);
            }
            else if (action == "invite")
            {
                if (args.Length < 2) return;
                var target = BasePlayer.Find(args[1]);
                if (target == null) { player.ChatMessage(GetMessage(Lang.PlayerNotFound, player.UserIDString)); return; }

                var clan = GetPlayerClan(player.userID);
                if (clan == null || clan.OwnerId != player.userID) { player.ChatMessage(GetMessage(Lang.NotOwner, player.UserIDString)); return; }
                
                if (clan.Members.Count >= _config.MaxClanMembers) { player.ChatMessage(GetMessage(Lang.ClanFull, player.UserIDString, _config.MaxClanMembers)); return; }
                
                if (!clan.Invites.Contains(target.userID)) clan.Invites.Add(target.userID);
                player.ChatMessage(GetMessage(Lang.Invited, player.UserIDString, target.displayName));
                target.ChatMessage(GetMessage(Lang.InvitedTo, target.UserIDString, clan.Tag));
            }
            else if (action == "join")
            {
                if (args.Length < 2) return;
                string tag = args[1];
                var clan = GetClan(tag);
                
                if (clan == null) { player.ChatMessage(GetMessage(Lang.ClanNotFound, player.UserIDString, tag)); return; }
                if (!clan.Invites.Contains(player.userID)) { player.ChatMessage(GetMessage(Lang.NotInvited, player.UserIDString)); return; }
                
                if (GetPlayerClan(player.userID) != null) { player.ChatMessage(GetMessage(Lang.LeaveCurrent, player.UserIDString)); return; }

                clan.Invites.Remove(player.userID);
                clan.Members.Add(player.userID);
                player.ChatMessage(GetMessage(Lang.Joined, player.UserIDString, tag));
            }
            else if (action == "leave")
            {
                var clan = GetPlayerClan(player.userID);
                if (clan == null) return;
                
                if (clan.OwnerId == player.userID)
                {
                    // Disband? Or transfer? For now disband.
                    _data.Clans.Remove(clan.Tag);
                    player.ChatMessage(GetMessage(Lang.Disbanded, player.UserIDString));
                    Interface.CallHook("OnClanDisband", clan.Tag);
                }
                else
                {
                    clan.Members.Remove(player.userID);
                    player.ChatMessage(GetMessage(Lang.Left, player.UserIDString));
                }
            }
        }
#endregion



#region Hooks (API for other plugins)
        [HookMethod("GetClanTag")]
        public string GetClanTag(ulong playerId)
        {
            var clan = GetPlayerClan(playerId);
            return clan?.Tag;
        }

        [HookMethod("GetClanOf")]
        public string GetClanOf(ulong playerId) => GetClanTag(playerId);
        
        [HookMethod("IsClanMember")]
        public bool IsClanMember(ulong playerId, string tag)
        {
             var clan = GetClan(tag);
             return clan != null && clan.Members.Contains(playerId);
        }
#endregion
    }
}

