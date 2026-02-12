using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGStacks", "NWG Team", "1.0.0")]
    [Description("Configurable stack size controller. Increase stack sizes globally, by category, or per-item.")]
    public class NWGStacks : RustPlugin
    {
        #region Configuration

        private class PluginConfig
        {
            // Global multiplier applied to ALL items (before per-item overrides)
            public int GlobalMultiplier = 5;

            // Per-category multipliers (applied on top of GlobalMultiplier)
            // Categories: Resources, Components, Ammo, Medical, Food, Attire, Tool, Weapon, Construction, Traps, Electrical, Fun
            public Dictionary<string, int> CategoryMultipliers = new Dictionary<string, int>
            {
                ["Resources"]    = 1, // Already covered by global
                ["Components"]   = 1,
                ["Ammo"]         = 1,
                ["Medical"]      = 1,
                ["Food"]         = 1,
                ["Attire"]       = 1,
                ["Tool"]         = 1,
                ["Weapon"]       = 1,
                ["Construction"] = 1,
                ["Traps"]        = 1,
                ["Electrical"]   = 1,
                ["Fun"]          = 1
            };

            // Per-item overrides (shortname -> absolute max stack size)
            // These OVERRIDE the global/category multiplier entirely
            public Dictionary<string, int> ItemOverrides = new Dictionary<string, int>
            {
                // Examples (uncomment/add as needed):
                // ["wood"] = 50000,
                // ["stones"] = 50000,
                // ["metal.ore"] = 50000,
                // ["sulfur.ore"] = 50000,
                // ["cloth"] = 10000,
                // ["leather"] = 10000,
                // ["lowgradefuel"] = 5000,
                // ["gunpowder"] = 10000,
            };

            // Items to exclude from any stack modification (shortnames)
            public List<string> ExcludedItems = new List<string>
            {
                // Items that should keep their vanilla stack size
                // e.g., "supply.signal", "autoturret"
            };
        }

        private PluginConfig _config;

        #endregion

        #region State

        // Store original/vanilla stack sizes so we can restore on unload
        private readonly Dictionary<string, int> _originalStackSizes = new Dictionary<string, int>();
        private bool _applied = false;

        #endregion

        #region Lifecycle

        private void Init()
        {
            LoadConfigVariables();
            permission.RegisterPermission("nwgstacks.admin", this);
        }

        private void OnServerInitialized()
        {
            ApplyStackSizes();
            Puts($"[NWG Stacks] Applied stack sizes. Global Multiplier: {_config.GlobalMultiplier}x | {_originalStackSizes.Count} items tracked.");
        }

        private void Unload()
        {
            RestoreStackSizes();
            Puts("[NWG Stacks] Restored original stack sizes.");
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
            Puts("Creating new configuration file for NWG Stacks");
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Stack Size Logic

        private void ApplyStackSizes()
        {
            if (_applied) return;

            foreach (var itemDef in ItemManager.itemList)
            {
                if (itemDef == null) continue;

                string shortname = itemDef.shortname;

                // Store the original stack size
                if (!_originalStackSizes.ContainsKey(shortname))
                    _originalStackSizes[shortname] = itemDef.stackable;

                // Skip excluded items
                if (_config.ExcludedItems.Contains(shortname)) continue;

                // Skip non-stackable items (stackable <= 1)
                if (itemDef.stackable <= 1) continue;

                int newStackSize;

                // Check for per-item override first (absolute value, not a multiplier)
                if (_config.ItemOverrides.TryGetValue(shortname, out int overrideSize))
                {
                    newStackSize = overrideSize;
                }
                else
                {
                    // Apply global multiplier
                    newStackSize = itemDef.stackable * _config.GlobalMultiplier;

                    // Apply category multiplier on top if applicable
                    string category = GetItemCategory(itemDef);
                    if (!string.IsNullOrEmpty(category) && _config.CategoryMultipliers.TryGetValue(category, out int catMult) && catMult > 1)
                    {
                        newStackSize *= catMult;
                    }
                }

                // Ensure minimum of 1
                itemDef.stackable = Math.Max(1, newStackSize);
            }

            _applied = true;
        }

        private void RestoreStackSizes()
        {
            if (!_applied) return;

            foreach (var itemDef in ItemManager.itemList)
            {
                if (itemDef == null) continue;

                if (_originalStackSizes.TryGetValue(itemDef.shortname, out int originalSize))
                {
                    itemDef.stackable = originalSize;
                }
            }

            _originalStackSizes.Clear();
            _applied = false;
        }

        private string GetItemCategory(ItemDefinition itemDef)
        {
            if (itemDef == null) return null;

            switch (itemDef.category)
            {
                case ItemCategory.Resources:    return "Resources";
                case ItemCategory.Component:    return "Components";
                case ItemCategory.Ammunition:   return "Ammo";
                case ItemCategory.Medical:      return "Medical";
                case ItemCategory.Food:         return "Food";
                case ItemCategory.Attire:       return "Attire";
                case ItemCategory.Tool:         return "Tool";
                case ItemCategory.Weapon:       return "Weapon";
                case ItemCategory.Construction: return "Construction";
                case ItemCategory.Traps:        return "Traps";
                case ItemCategory.Electrical:   return "Electrical";
                case ItemCategory.Fun:          return "Fun";
                default:                        return null;
            }
        }

        #endregion

        #region Commands

        [ChatCommand("stacks")]
        private void CmdStacks(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player))
            {
                player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> You don't have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            switch (args[0].ToLower())
            {
                case "info":
                    CmdInfo(player);
                    break;

                case "check":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Usage: /stacks check <shortname>");
                        return;
                    }
                    CmdCheck(player, args[1]);
                    break;

                case "set":
                    if (args.Length < 3)
                    {
                        player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Usage: /stacks set <shortname> <amount>");
                        return;
                    }
                    CmdSet(player, args[1], args[2]);
                    break;

                case "setmulti":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Usage: /stacks setmulti <multiplier>");
                        return;
                    }
                    CmdSetMultiplier(player, args[1]);
                    break;

                case "reset":
                    CmdReset(player);
                    break;

                case "search":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Usage: /stacks search <partial name>");
                        return;
                    }
                    CmdSearch(player, args[1]);
                    break;

                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowHelp(BasePlayer player)
        {
            string msg = "<color=#55aaff>═══ NWG Stacks ═══</color>\n" +
                         "<color=#aaaaaa>Commands:</color>\n" +
                         "  <color=#ffcc00>/stacks info</color> — Show current config\n" +
                         "  <color=#ffcc00>/stacks check <shortname></color> — Check an item's stack size\n" +
                         "  <color=#ffcc00>/stacks set <shortname> <amount></color> — Set an item override\n" +
                         "  <color=#ffcc00>/stacks setmulti <multiplier></color> — Set global multiplier\n" +
                         "  <color=#ffcc00>/stacks search <name></color> — Search items by name\n" +
                         "  <color=#ffcc00>/stacks reset</color> — Reload & reapply from config";
            player.ChatMessage(msg);
        }

        private void CmdInfo(BasePlayer player)
        {
            string msg = $"<color=#55aaff>═══ NWG Stacks Info ═══</color>\n" +
                         $"<color=#aaaaaa>Global Multiplier:</color> <color=#55ff55>{_config.GlobalMultiplier}x</color>\n" +
                         $"<color=#aaaaaa>Item Overrides:</color> <color=#55ff55>{_config.ItemOverrides.Count}</color>\n" +
                         $"<color=#aaaaaa>Excluded Items:</color> <color=#55ff55>{_config.ExcludedItems.Count}</color>\n" +
                         $"<color=#aaaaaa>Items Tracked:</color> <color=#55ff55>{_originalStackSizes.Count}</color>";

            // Show active category multipliers
            var activeCats = _config.CategoryMultipliers.Where(x => x.Value > 1).ToList();
            if (activeCats.Count > 0)
            {
                msg += "\n<color=#aaaaaa>Category Multipliers:</color>";
                foreach (var cat in activeCats)
                    msg += $"\n  <color=#ffcc00>{cat.Key}:</color> {cat.Value}x";
            }

            player.ChatMessage(msg);
        }

        private void CmdCheck(BasePlayer player, string shortname)
        {
            var itemDef = ItemManager.FindItemDefinition(shortname);
            if (itemDef == null)
            {
                player.ChatMessage($"<color=#ff4444>[NWG Stacks]</color> Item '{shortname}' not found. Try /stacks search {shortname}");
                return;
            }

            int original = _originalStackSizes.ContainsKey(itemDef.shortname) ? _originalStackSizes[itemDef.shortname] : itemDef.stackable;
            bool hasOverride = _config.ItemOverrides.ContainsKey(itemDef.shortname);

            string msg = $"<color=#55aaff>═══ {itemDef.displayName.english} ═══</color>\n" +
                         $"<color=#aaaaaa>Shortname:</color> {itemDef.shortname}\n" +
                         $"<color=#aaaaaa>Category:</color> {GetItemCategory(itemDef) ?? "Other"}\n" +
                         $"<color=#aaaaaa>Vanilla Stack:</color> {original}\n" +
                         $"<color=#aaaaaa>Current Stack:</color> <color=#55ff55>{itemDef.stackable}</color>\n" +
                         $"<color=#aaaaaa>Has Override:</color> {(hasOverride ? "<color=#ffcc00>Yes</color>" : "No")}";
            player.ChatMessage(msg);
        }

        private void CmdSet(BasePlayer player, string shortname, string amountStr)
        {
            var itemDef = ItemManager.FindItemDefinition(shortname);
            if (itemDef == null)
            {
                player.ChatMessage($"<color=#ff4444>[NWG Stacks]</color> Item '{shortname}' not found.");
                return;
            }

            if (!int.TryParse(amountStr, out int amount) || amount < 1)
            {
                player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Amount must be a positive number.");
                return;
            }

            // Store as override
            _config.ItemOverrides[itemDef.shortname] = amount;
            SaveConfig();

            // Apply immediately
            itemDef.stackable = amount;

            player.ChatMessage($"<color=#55ff55>[NWG Stacks]</color> Set <color=#ffcc00>{itemDef.displayName.english}</color> stack size to <color=#55ff55>{amount}</color>.");
            Puts($"[NWG Stacks] {player.displayName} set {itemDef.shortname} stack to {amount}");
        }

        private void CmdSetMultiplier(BasePlayer player, string multStr)
        {
            if (!int.TryParse(multStr, out int mult) || mult < 1)
            {
                player.ChatMessage("<color=#ff4444>[NWG Stacks]</color> Multiplier must be a positive whole number.");
                return;
            }

            _config.GlobalMultiplier = mult;
            SaveConfig();

            // Re-apply all stack sizes
            RestoreStackSizes();
            ApplyStackSizes();

            player.ChatMessage($"<color=#55ff55>[NWG Stacks]</color> Global multiplier set to <color=#55ff55>{mult}x</color>. All stacks reapplied.");
            Puts($"[NWG Stacks] {player.displayName} set global multiplier to {mult}x");
        }

        private void CmdReset(BasePlayer player)
        {
            RestoreStackSizes();
            LoadConfigVariables();
            ApplyStackSizes();
            player.ChatMessage("<color=#55ff55>[NWG Stacks]</color> Config reloaded and stack sizes reapplied.");
        }

        private void CmdSearch(BasePlayer player, string search)
        {
            var results = ItemManager.itemList
                .Where(x => x.shortname.Contains(search.ToLower()) || x.displayName.english.ToLower().Contains(search.ToLower()))
                .Take(10)
                .ToList();

            if (results.Count == 0)
            {
                player.ChatMessage($"<color=#ff4444>[NWG Stacks]</color> No items found matching '{search}'.");
                return;
            }

            string msg = $"<color=#55aaff>═══ Search: '{search}' ═══</color>";
            foreach (var item in results)
            {
                int original = _originalStackSizes.ContainsKey(item.shortname) ? _originalStackSizes[item.shortname] : item.stackable;
                msg += $"\n<color=#ffcc00>{item.shortname}</color> — {item.displayName.english} ({original} → <color=#55ff55>{item.stackable}</color>)";
            }
            if (results.Count == 10)
                msg += "\n<color=#aaaaaa>... more results. Narrow your search.</color>";

            player.ChatMessage(msg);
        }

        #endregion

        #region Console Commands

        [ConsoleCommand("stacks.setglobal")]
        private void ConsoleCmdSetGlobal(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;

            if (!arg.HasArgs(1))
            {
                arg.ReplyWith("Usage: stacks.setglobal <multiplier>");
                return;
            }

            if (!int.TryParse(arg.GetString(0), out int mult) || mult < 1)
            {
                arg.ReplyWith("Multiplier must be a positive whole number.");
                return;
            }

            _config.GlobalMultiplier = mult;
            SaveConfig();
            RestoreStackSizes();
            ApplyStackSizes();
            arg.ReplyWith($"[NWG Stacks] Global multiplier set to {mult}x. All stacks reapplied.");
        }

        [ConsoleCommand("stacks.setitem")]
        private void ConsoleCmdSetItem(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin) return;

            if (!arg.HasArgs(2))
            {
                arg.ReplyWith("Usage: stacks.setitem <shortname> <amount>");
                return;
            }

            string shortname = arg.GetString(0);
            var itemDef = ItemManager.FindItemDefinition(shortname);
            if (itemDef == null)
            {
                arg.ReplyWith($"Item '{shortname}' not found.");
                return;
            }

            if (!int.TryParse(arg.GetString(1), out int amount) || amount < 1)
            {
                arg.ReplyWith("Amount must be a positive number.");
                return;
            }

            _config.ItemOverrides[itemDef.shortname] = amount;
            SaveConfig();
            itemDef.stackable = amount;
            arg.ReplyWith($"[NWG Stacks] Set {itemDef.shortname} stack size to {amount}.");
        }

        #endregion

        #region Helpers

        private bool HasPermission(BasePlayer player)
        {
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, "nwgstacks.admin");
        }

        #endregion
    }
}

