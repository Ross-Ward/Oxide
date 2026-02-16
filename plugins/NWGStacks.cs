using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGStacks", "NWG Team", "1.0.1")]
    [Description("Configurable stack size controller.")]
    public class NWGStacks : RustPlugin
    {
#region Configuration
        private class PluginConfig
        {
            public int GlobalMultiplier = 5;
            public Dictionary<string, int> CategoryMultipliers = new Dictionary<string, int>
            {
                ["Resources"] = 1, ["Components"] = 1, ["Ammo"] = 1, ["Medical"] = 1, ["Food"] = 1,
                ["Attire"] = 1, ["Tool"] = 1, ["Weapon"] = 1, ["Construction"] = 1, ["Traps"] = 1,
                ["Electrical"] = 1, ["Fun"] = 1
            };
            public Dictionary<string, int> ItemOverrides = new Dictionary<string, int>();
            public List<string> ExcludedItems = new List<string>();
        }
        private PluginConfig _config;
#endregion

#region State
        private readonly Dictionary<string, int> _originalStackSizes = new Dictionary<string, int>();
        private bool _applied = false;
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
        }

        private void OnServerInitialized()
        {
            ApplyStackSizes();
            Puts($"[NWG Stacks] Applied stack sizes. Global Multiplier: {_config.GlobalMultiplier}x.");
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
#endregion

#region Stack Size Logic
        private void ApplyStackSizes()
        {
            if (_applied) return;
            foreach (var itemDef in ItemManager.itemList)
            {
                if (itemDef == null) continue;
                string shortname = itemDef.shortname;
                if (!_originalStackSizes.ContainsKey(shortname)) _originalStackSizes[shortname] = itemDef.stackable;
                if (_config.ExcludedItems.Contains(shortname)) continue;
                if (itemDef.stackable <= 1) continue;

                int newStackSize;
                if (_config.ItemOverrides.TryGetValue(shortname, out int overrideSize))
                {
                    newStackSize = overrideSize;
                }
                else
                {
                    newStackSize = itemDef.stackable * _config.GlobalMultiplier;
                    string category = GetItemCategory(itemDef);
                    if (!string.IsNullOrEmpty(category) && _config.CategoryMultipliers.TryGetValue(category, out int catMult) && catMult > 1)
                        newStackSize *= catMult;
                }
                itemDef.stackable = Math.Max(1, newStackSize);
            }
            _applied = true;
        }

        private void RestoreStackSizes()
        {
            if (!_applied) return;
            foreach (var itemDef in ItemManager.itemList)
            {
                if (itemDef != null && _originalStackSizes.TryGetValue(itemDef.shortname, out int originalSize))
                    itemDef.stackable = originalSize;
            }
            _originalStackSizes.Clear();
            _applied = false;
        }

        private string GetItemCategory(ItemDefinition itemDef)
        {
            if (itemDef == null) return null;
            switch (itemDef.category)
            {
                case ItemCategory.Resources: return "Resources";
                case ItemCategory.Component: return "Components";
                case ItemCategory.Ammunition: return "Ammo";
                case ItemCategory.Medical: return "Medical";
                case ItemCategory.Food: return "Food";
                case ItemCategory.Attire: return "Attire";
                case ItemCategory.Tool: return "Tool";
                case ItemCategory.Weapon: return "Weapon";
                case ItemCategory.Construction: return "Construction";
                case ItemCategory.Traps: return "Traps";
                case ItemCategory.Electrical: return "Electrical";
                case ItemCategory.Fun: return "Fun";
                default: return null;
            }
        }
#endregion
    }
}
