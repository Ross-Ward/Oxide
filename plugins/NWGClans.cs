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
        #endregion

        #region Config
        private class PluginConfig
        {
            public int MaxClanMembers = 8;
            public int MaxAlliances = 2;
            public bool UseTagColors = true;
            public string ChatPrefix = "[Clan]";
            
            // Kits Config
            public List<KitDefinition> Kits = new List<KitDefinition>();
        }

        private class KitDefinition
        {
            public string Name;
            public string Permission;
            public int Cooldown; // Seconds
            public int MaxUses;
            public List<ItemDef> Items = new List<ItemDef>();
        }

        private class ItemDef
        {
            public string ShortName;
            public int Amount;
            public ulong SkinId;
            public string Container; // "inventory", "belt", "wear"
        }

        private PluginConfig _config;
        #endregion

        #region Data
        private class StoredData
        {
            public Dictionary<string, Clan> Clans = new Dictionary<string, Clan>();
            public Dictionary<ulong, PlayerKitData> PlayerKits = new Dictionary<ulong, PlayerKitData>();
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

        private class PlayerKitData
        {
            public Dictionary<string, KitUsage> Usages = new Dictionary<string, KitUsage>();
        }

        private class KitUsage
        {
            public int Uses;
            public double LastUsedTime; // RealtimeSinceStartup or Timestamp? Timestamp is safer for restarts.
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
                if (_config == null || _config.Kits.Count == 0)
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
            _config.Kits.Add(new KitDefinition { 
                Name = "starter", 
                Cooldown = 3600, 
                Items = new List<ItemDef> { 
                    new ItemDef { ShortName = "stone", Amount = 1000, Container = "inventory" } 
                } 
            });
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
        }
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
                SendReply(player, "Usage: /clan create <tag>, /clan join <tag>, /clan leave, /clan invite <player>");
                return;
            }

            string action = args[0].ToLower();

            if (action == "create")
            {
                if (args.Length < 2) { SendReply(player, "Usage: /clan create <tag>"); return; }
                string tag = args[1];
                
                if (GetPlayerClan(player.userID) != null) { SendReply(player, "You are already in a clan."); return; }
                if (_data.Clans.ContainsKey(tag)) { SendReply(player, "Clan tag already exists."); return; }

                var clan = new Clan { Tag = tag, OwnerId = player.userID };
                clan.Members.Add(player.userID);
                _data.Clans[tag] = clan;
                
                SendReply(player, $"Clan {tag} created!");
                Interface.CallHook("OnClanCreate", tag);
            }
            else if (action == "invite")
            {
                if (args.Length < 2) return;
                var target = BasePlayer.Find(args[1]);
                if (target == null) { SendReply(player, "Player not found."); return; }

                var clan = GetPlayerClan(player.userID);
                if (clan == null || clan.OwnerId != player.userID) { SendReply(player, "You must be a clan owner."); return; }
                
                if (clan.Members.Count >= _config.MaxClanMembers) { SendReply(player, "Clan is full."); return; }
                
                if (!clan.Invites.Contains(target.userID)) clan.Invites.Add(target.userID);
                SendReply(player, $"Invited {target.displayName}.");
                SendReply(target, $"You have been invited to clan {clan.Tag}. Type /clan join {clan.Tag} to join.");
            }
            else if (action == "join")
            {
                if (args.Length < 2) return;
                string tag = args[1];
                var clan = GetClan(tag);
                
                if (clan == null) { SendReply(player, "Clan not found."); return; }
                if (!clan.Invites.Contains(player.userID)) { SendReply(player, "You are not invited to this clan."); return; }
                
                if (GetPlayerClan(player.userID) != null) { SendReply(player, "Leave your current clan first."); return; }

                clan.Invites.Remove(player.userID);
                clan.Members.Add(player.userID);
                SendReply(player, $"Joined clan {tag}!");
            }
            else if (action == "leave")
            {
                var clan = GetPlayerClan(player.userID);
                if (clan == null) return;
                
                if (clan.OwnerId == player.userID)
                {
                    // Disband? Or transfer? For now disband.
                    _data.Clans.Remove(clan.Tag);
                    SendReply(player, "Clan disbanded.");
                    Interface.CallHook("OnClanDisband", clan.Tag);
                }
                else
                {
                    clan.Members.Remove(player.userID);
                    SendReply(player, "Left clan.");
                }
            }
        }
        #endregion

        #region Kit Logic
        [ChatCommand("kit")]
        private void CmdKit(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                List<string> kitNames = _config.Kits.Select(k => k.Name).ToList();
                SendReply(player, "Available Kits: " + string.Join(", ", kitNames));
                return;
            }

            string kitName = args[0].ToLower();
            var kit = _config.Kits.FirstOrDefault(k => k.Name.ToLower() == kitName);
            
            if (kit == null) { SendReply(player, "Kit not found."); return; }

            // Check Permission
            if (!string.IsNullOrEmpty(kit.Permission) && !permission.UserHasPermission(player.UserIDString, kit.Permission))
            {
                SendReply(player, "No permission.");
                return;
            }

            // Check Data
            if (!_data.PlayerKits.TryGetValue(player.userID, out var pkd))
            {
                pkd = new PlayerKitData();
                _data.PlayerKits[player.userID] = pkd;
            }

            if (!pkd.Usages.TryGetValue(kitName, out var usage))
            {
                usage = new KitUsage();
                pkd.Usages[kitName] = usage;
            }

            // Cooldown
            double now = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            if (kit.Cooldown > 0 && (now - usage.LastUsedTime) < kit.Cooldown)
            {
                int left = (int)(kit.Cooldown - (now - usage.LastUsedTime));
                SendReply(player, $"Cooldown: {left}s");
                return;
            }

            // Max Uses
            if (kit.MaxUses > 0 && usage.Uses >= kit.MaxUses)
            {
                SendReply(player, "Max uses reached.");
                return;
            }

            // Redeem
            usage.Uses++;
            usage.LastUsedTime = now;
            GiveKit(player, kit);
            SendReply(player, $"Redeemed kit {kitName}.");
        }

        private void GiveKit(BasePlayer player, KitDefinition kit)
        {
            foreach(var itemDef in kit.Items)
            {
                var item = ItemManager.CreateByName(itemDef.ShortName, itemDef.Amount, itemDef.SkinId);
                if (item == null) continue;
                
                if (itemDef.Container == "belt") player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                else if (itemDef.Container == "wear") player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                else player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
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

