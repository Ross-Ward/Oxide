using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG World", "NWG Team", "3.0.0")]
    [Description("Global World Settings: Stack Sizes, Gather Rates, Crafting Speeds.")]
    public class NWGWorld : RustPlugin
    {
        #region Configuration
        private class PluginConfig
        {
            public float GlobalStackMultiplier = 1.0f;
            public Dictionary<string, float> CategoryStackMultipliers = new Dictionary<string, float>();
            public Dictionary<string, int> ItemStackOverrides = new Dictionary<string, int>();

            public float GlobalGatherMultiplier = 1.0f;
            public Dictionary<string, float> GatherTypeMultipliers = new Dictionary<string, float>()
            {
                { "Tree", 1.0f },
                { "Ore", 1.0f },
                { "Flesh", 1.0f },
                { "Pickup", 1.0f },
                { "Quarry", 1.0f }
            };

            public float CraftingSpeedMultiplier = 1.0f; // 0.0 = Instant
            public bool InstantCraft = false; // Override for true instant
        }
        private PluginConfig _config;
        #endregion

        #region State
        private Dictionary<int, int> _vanillaStacks = new Dictionary<int, int>();
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
            Puts("Creating new configuration file for NWG World");
            _config = new PluginConfig();
            SaveConfig();
        }

        private void OnServerInitialized()
        {
            ApplyStackSizes();
            ApplyCraftingSpeed();
            
            if (Interface.Oxide.RootPluginManager.GetPlugin("StackSizeController") != null)
                PrintWarning("StackSizeController detected! NWG_World stack features might conflict.");
        }

        private void Unload()
        {
            RevertStackSizes();
        }
        #endregion

        #region Stack Sizes
        private void ApplyStackSizes()
        {
            foreach (var itemDef in ItemManager.GetItemDefinitions())
            {
                if (!_vanillaStacks.ContainsKey(itemDef.itemid))
                    _vanillaStacks[itemDef.itemid] = itemDef.stackable;
                
                int newStack = _vanillaStacks[itemDef.itemid];
                
                newStack = Mathf.RoundToInt(newStack * _config.GlobalStackMultiplier);

                string category = itemDef.category.ToString();
                if (_config.CategoryStackMultipliers.TryGetValue(category, out float catMult))
                {
                    newStack = Mathf.RoundToInt(_vanillaStacks[itemDef.itemid] * catMult);
                }

                if (_config.ItemStackOverrides.TryGetValue(itemDef.shortname, out int forcedStack))
                {
                    newStack = forcedStack;
                }

                if (newStack > int.MaxValue) newStack = int.MaxValue;
                itemDef.stackable = newStack;
            }
        }

        private void RevertStackSizes()
        {
            foreach (var kvp in _vanillaStacks)
            {
                var def = ItemManager.FindItemDefinition(kvp.Key);
                if (def != null) def.stackable = kvp.Value;
            }
        }
        #endregion

        #region Gather Rates
        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (entity == null || item == null) return;
            var player = entity.ToPlayer();
            if (player == null) return;

            float mult = _config.GlobalGatherMultiplier;

            string type = dispenser.gatherType.ToString(); 
            if (_config.GatherTypeMultipliers.TryGetValue(type, out float typeMult))
                mult = typeMult;

            if (mult != 1.0f)
            {
                item.amount = Mathf.RoundToInt(item.amount * mult);
            }
        }

        private void OnCollectiblePickup(CollectibleEntity collectible, BasePlayer player)
        {
            if (collectible == null || player == null) return;
            
            float mult = _config.GlobalGatherMultiplier;
            if (_config.GatherTypeMultipliers.TryGetValue("Pickup", out float pickupMult))
                mult = pickupMult;

            if (mult != 1.0f)
            {
                foreach (var item in collectible.itemList)
                {
                    item.amount = Mathf.RoundToInt(item.amount * mult);
                }
            }
        }

        private void OnQuarryGather(MiningQuarry quarry, Item item)
        {
            float mult = _config.GlobalGatherMultiplier;
            if (_config.GatherTypeMultipliers.TryGetValue("Quarry", out float qMult))
                mult = qMult;

            if (mult != 1.0f)
                item.amount = Mathf.RoundToInt(item.amount * mult);
        }
        #endregion

        #region Crafting
        private void ApplyCraftingSpeed()
        {
            if (_config.InstantCraft || _config.CraftingSpeedMultiplier < 1.0f)
            {
                foreach(var bp in ItemManager.bpList)
                {
                    if (_config.InstantCraft) bp.time = 0f;
                    else bp.time *= _config.CraftingSpeedMultiplier;
                }
            }
        }
        #endregion
        
        #region Commands
        [ChatCommand("rates")]
        private void CmdRates(BasePlayer player, string msg, string[] args)
        {
            if (player == null) return;
            if (!player.IsAdmin)
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }
            
            player.ChatMessage($"<color=#5BC0DE>--- SERVER RATES ---</color>");

            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /rates <type> <value>");
                player.ChatMessage($"Current Global Gather Multiplier: {_config.GlobalGatherMultiplier}");
                return;
            }

            var type = args[0].ToLower();
            if (!float.TryParse(args[1], out float val))
            {
                player.ChatMessage("Invalid value. Please provide a number.");
                return;
            }

            if (type == "gather") 
            {
                _config.GlobalGatherMultiplier = val;
                Config.WriteObject(_config);
                player.ChatMessage($"Global Gather set to {val}x");
            }
            else
            {
                player.ChatMessage($"Unknown rate type: {type}");
            }
        }
        #endregion
    }
}
