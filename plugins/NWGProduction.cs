using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGProduction", "NWG Team", "3.0.0")]
    [Description("Optimized Smelting Controller and Furnace Splitter.")]
    public class NWGProduction : RustPlugin
    {
#region Configuration
        private class PluginConfig
        {
            public float GlobalSpeedMultiplier = 2.0f;
            public float FuelConsumptionMultiplier = 1.0f;
            public float CharcoalChance = 0.75f;
            public bool EnableAutoSplit = true;
            public Dictionary<string, float> SpeedModifiers = new Dictionary<string, float>
            {
                ["furnace.shortname"] = 1.0f,
                ["furnace.large"] = 2.0f,
                ["refinery_small_deployed"] = 1.0f,
                ["electric.furnace.deployed"] = 1.0f
            };
        }
        private PluginConfig _config;
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
            Puts("Creating new configuration file for NWG Production");
            _config = new PluginConfig();
            SaveConfig();
        }


        protected override void SaveConfig() => Config.WriteObject(_config);
#endregion

#region Localization
        public static class Lang
        {
            public const string ConfigReloaded = "ConfigReloaded";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ConfigReloaded] = "<color=#b7d092>[NWG]</color> Config reloaded."
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Smelting Controller
        // Hook called by the engine when an oven cooks
        private object OnOvenCook(BaseOven oven, Item fuel)
        {
            if (oven == null) return null;

            // 1. Get Speed Multiplier
            float speedMult = _config.GlobalSpeedMultiplier;
            if (_config.SpeedModifiers.TryGetValue(oven.ShortPrefabName, out float modifier))
                speedMult *= modifier;

            // If standard speed (1.0), do nothing, let Rust handle it
            if (speedMult <= 1.0f) return null;

            // 2. Accelerate Cooking
            // We simulate extra ticks. We do (Speed - 1) extra ticks.
            // e.g., Speed 2.0 means 1 normal tick + 1 extra tick.
            
            int extraTicks = Mathf.FloorToInt(speedMult) - 1;
            // Handle fractional speed (e.g. 1.5x)
            if (UnityEngine.Random.Range(0f, 1f) < (speedMult % 1)) extraTicks++;

            for (int i = 0; i < extraTicks; i++)
            {
                // Consume Fuel
                // We manually consume fuel here because we are bypassing the internal loop for these extra ticks
                // However, bypassing OnOvenCook recursively is tricky. 
                // Instead, we just call the cooking logic directly on the items.
                
                CookItems(oven);
            }

            // Return null to allow the default 1 tick to proceed as well
            return null;
        }

        private void CookItems(BaseOven oven)
        {
            // Simple iteration over inventory to cook items
            // This mirrors BaseOven.Cook() logic but optimized
            foreach (var item in oven.inventory.itemList.ToList()) 
            {
                if (item == null) continue;
                
                var cookable = item.info.GetComponent<ItemModCookable>();
                if (cookable == null) continue;

                // Check temperature requirements
                if (cookable.lowTemp > oven.cookingTemperature || cookable.highTemp < oven.cookingTemperature) continue;

                // Cook it
                // Logic: 1 ore -> cooked item
                // Use default rust logic for conversion rates (usually 1:1)
                
                // Be careful with concurrency modifying the list
                if (cookable.becomeOnCooked != null)
                {
                    int amountToCook = 1; 
                    if (item.amount < amountToCook) continue;

                    // Reduce raw
                    item.UseItem(amountToCook);

                    // Create cooked
                    var result = ItemManager.Create(cookable.becomeOnCooked, cookable.amountOfBecome * amountToCook);
                    if (result != null)
                    {
                        if (!result.MoveToContainer(oven.inventory))
                        {
                            result.Drop(oven.transform.position + Vector3.up, Vector3.up);
                        }
                    }
                }
            }
        }
        
        // Hook for fuel consumption
        private void OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // We can modify fuel consumption here
            // Usage: 1 means normal usage.
            // If we want 0.5x usage (efficient), we can add fuel back sometimes?
            // Or prevent consumption.
            
            if (_config.FuelConsumptionMultiplier < 1.0f)
            {
                 // Chance to refund the fuel used by the engine
                 if (UnityEngine.Random.Range(0f, 1f) > _config.FuelConsumptionMultiplier)
                 {
                     fuel.amount += 1; // Refund
                 }
            }
        }
#endregion

#region Splitter Logic
        // Hook: Called when an item is moved within/into an inventory
        private object CanMoveItem(Item item, PlayerInventory playerInventory, ItemContainerId targetContainerId, int targetSlot, int amount)
        {
            if (!_config.EnableAutoSplit) return null;
            if (item == null || playerInventory == null) return null;

            // Find target container
            var targetContainer = playerInventory.FindContainer(targetContainerId);
            if (targetContainer == null)
            {
                // Might be an entity container
                 var player = playerInventory.baseEntity;
                 // This hook logic is tricky in generic cases, simpler:
                 // Use OnItemAddedToContainer? No, that's too late for splitting effectively without reshuffling.
                 return null; 
            }
            
            // Check if target is an oven
            var oven = targetContainer.entityOwner as BaseOven;
            if (oven == null) return null;

            // Is it cookable?
            var cookable = item.info.GetComponent<ItemModCookable>();
            if (cookable == null) return null;

            // Split Logic
            // We want to distribute the stack across ALL available input slots
            // Return null to let default behavior happen if we don't want to interfere,
            // OR return true/false to override.
            
            // Simplified: If dragging a large stack, split it across empty slots.
            // We'll use a NextTick to reorganize to avoid fighting the drag event.
            
            NextTick(() => {
                if (oven == null || oven.IsDestroyed) return;
                SplitOres(oven);
            });

            return null;
        }
        
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (!_config.EnableAutoSplit) return;
            if (container.entityOwner is BaseOven oven)
            {
                 if (item.info.GetComponent<ItemModCookable>() != null)
                 {
                     // Re-balance logic
                     NextTick(() => SplitOres(oven));
                 }
            }
        }

        [ConsoleCommand("nwgproduction.reload")]
        private void ConsoleCmdReload(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            LoadConfigVariables();
            arg.ReplyWith(GetMessage(Lang.ConfigReloaded, arg.Player()?.UserIDString));
        }

        private void SplitOres(BaseOven oven)
        {
            var container = oven.inventory;
            int maxSlots = oven.inputSlots;
            if (maxSlots <= 0) return;

            // Get all cookable items in input slots
            var ores = container.itemList
                .Where(x => x.position < maxSlots && x.info.GetComponent<ItemModCookable>() != null)
                .ToList();
            if (ores.Count == 0) return;

            // Group by item type
            var groups = ores.GroupBy(x => x.info.itemid).ToList();

            foreach (var group in groups)
            {
                int totalAmount = group.Sum(x => x.amount);
                if (totalAmount <= 0) continue;

                // Find slots available for this ore type (empty or same item)
                var availableSlots = Enumerable.Range(0, maxSlots)
                    .Where(i =>
                    {
                        var slot = container.GetSlot(i);
                        return slot == null || slot.info.itemid == group.Key;
                    })
                    .ToList();

                int splitCount = availableSlots.Count;
                if (splitCount <= 1) continue; // nothing to split

                int amountPerSlot = totalAmount / splitCount;
                int remainder = totalAmount % splitCount;
                if (amountPerSlot <= 0) continue;

                // Remove all existing items of this type from input slots
                var itemId = group.First().info.itemid;
                foreach (var item in group.ToList())
                    item.RemoveFromContainer();
                foreach (var item in group.ToList())
                    item.Remove();

                // Create balanced stacks in available slots
                for (int i = 0; i < splitCount; i++)
                {
                    int amount = amountPerSlot + (i < remainder ? 1 : 0);
                    if (amount <= 0) continue;

                    var newItem = ItemManager.CreateByItemID(itemId, amount);
                    if (newItem == null) continue;
                    newItem.MoveToContainer(container, availableSlots[i]);
                }
            }
        }
#endregion
    }
}

