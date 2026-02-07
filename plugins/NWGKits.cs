using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWG Kits", "NWG Team", "1.0.0")]
    [Description("Simple and efficient kit system.")]
    public class NWGKits : RustPlugin
    {
        #region Config
        private class PluginConfig
        {
            public Dictionary<string, Kit> Kits = new Dictionary<string, Kit>();
        }

        private class Kit
        {
            public string Description;
            public int Cooldown; // Seconds
            public string Permission;
            public List<KitItem> Items = new List<KitItem>();
        }

        private class KitItem
        {
            public string ShortName;
            public int Amount;
            public ulong SkinId;
        }

        private PluginConfig _config;
        #endregion

        #region Data
        private class StoredData
        {
            public Dictionary<ulong, Dictionary<string, DateTime>> PlayerCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();
        }

        private StoredData _data;
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_Kits") ?? new StoredData();
        }

        private void LoadConfigVariables()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null || _config.Kits.Count == 0) LoadDefaultConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            
            var starter = new Kit {
                Description = "Basic survival kit",
                Cooldown = 3600,
                Items = new List<KitItem> {
                    new KitItem { ShortName = "stone.pickaxe", Amount = 1 },
                    new KitItem { ShortName = "stone.axe", Amount = 1 },
                    new KitItem { ShortName = "apple", Amount = 5 }
                }
            };
            _config.Kits["starter"] = starter;

            var vip = new Kit {
                Description = "VIP rewards",
                Cooldown = 86400,
                Permission = "nwgkits.vip",
                Items = new List<KitItem> {
                    new KitItem { ShortName = "rifle.ak", Amount = 1 },
                    new KitItem { ShortName = "ammo.rifle", Amount = 128 }
                }
            };
            _config.Kits["vip"] = vip;

            var adminPvp = new Kit {
                Description = "Admin PVP Gear (Staff Only)",
                Cooldown = 0,
                Permission = "nwgcore.admin",
                Items = new List<KitItem> {
                    new KitItem { ShortName = "rifle.ak", Amount = 1 },
                    new KitItem { ShortName = "ammo.rifle", Amount = 256 },
                    new KitItem { ShortName = "attire.heavy.plate.helmet", Amount = 1 },
                    new KitItem { ShortName = "attire.heavy.plate.jacket", Amount = 1 },
                    new KitItem { ShortName = "attire.heavy.plate.pants", Amount = 1 },
                    new KitItem { ShortName = "syringe.medical", Amount = 10 }
                }
            };
            _config.Kits["admin_pvp"] = adminPvp;

            SaveConfig();
        }

        private void OnServerSave() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Kits", _data);
        #endregion

        #region Commands
        [ChatCommand("kit")]
        private void CmdKit(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ListKits(player);
                return;
            }

            string kitName = args[0].ToLower();
            if (!_config.Kits.TryGetValue(kitName, out Kit kit))
            {
                player.ChatMessage("Kit not found.");
                return;
            }

            if (!string.IsNullOrEmpty(kit.Permission) && !permission.UserHasPermission(player.UserIDString, kit.Permission))
            {
                player.ChatMessage("No permission for this kit.");
                return;
            }

            if (GetRemainingCooldown(player.userID, kitName, out double remaining))
            {
                player.ChatMessage($"Kit on cooldown. Wait {remaining:N0}s.");
                return;
            }

            GiveKit(player, kit);
            SetCooldown(player.userID, kitName, kit.Cooldown);
            player.ChatMessage($"You received the '{kitName}' kit!");
        }

        private void ListKits(BasePlayer player)
        {
            var available = _config.Kits.Keys.Where(k => string.IsNullOrEmpty(_config.Kits[k].Permission) || permission.UserHasPermission(player.UserIDString, _config.Kits[k].Permission));
            player.ChatMessage("Available kits: " + string.Join(", ", available));
            player.ChatMessage("Usage: /kit <name>");
        }

        private void GiveKit(BasePlayer player, Kit kit)
        {
            foreach (var item in kit.Items)
            {
                var giveItem = ItemManager.CreateByName(item.ShortName, item.Amount, item.SkinId);
                if (giveItem != null) player.GiveItem(giveItem);
            }
        }

        private bool GetRemainingCooldown(ulong userId, string kitName, out double remaining)
        {
            remaining = 0;
            if (!_data.PlayerCooldowns.TryGetValue(userId, out var cooldowns)) return false;
            if (!cooldowns.TryGetValue(kitName, out DateTime expiry)) return false;

            if (DateTime.Now < expiry)
            {
                remaining = (expiry - DateTime.Now).TotalSeconds;
                return true;
            }
            return false;
        }

        private void SetCooldown(ulong userId, string kitName, int seconds)
        {
            if (!_data.PlayerCooldowns.TryGetValue(userId, out var cooldowns))
                cooldowns = _data.PlayerCooldowns[userId] = new Dictionary<string, DateTime>();
            
            cooldowns[kitName] = DateTime.Now.AddSeconds(seconds);
        }
        #endregion
    }
}
