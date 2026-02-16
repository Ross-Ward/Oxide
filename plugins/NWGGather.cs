using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGGather", "NWG Team", "2.0.0")]
    [Description("Configurable gather rate controller. Modify rates for mining, pickups, quarries, excavators, surveys, and dispenser types.")]
    public class NWGGather : RustPlugin
    {
#region Configuration

        private class PluginConfig
        {
            // â”€â”€ Global Resource Modifiers â”€â”€
            // Key: resource display name (e.g. "Wood", "Stones", "Metal Ore")
            // Use "*" as a wildcard to set a default for all unspecified resources
            // These apply to dispenser gathering (hitting trees, rocks, etc.)
            public Dictionary<string, float> GatherResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f  // Default: 3x all resources from dispensers
            };

            // â”€â”€ Dispenser Type Scale â”€â”€
            // Controls how much the *node itself* contains (scales containedItems)
            // Types: "Tree", "Ore", "Flesh" (corpses/animals)
            public Dictionary<string, float> GatherDispenserModifiers = new Dictionary<string, float>
            {
                // ["Tree"] = 1.0f,
                // ["Ore"] = 1.0f,
                // ["Flesh"] = 1.0f,
            };

            // â”€â”€ Pickup Resource Modifiers â”€â”€
            // Collectible pickups (hemp, ground stones, mushrooms, etc.)
            // Key: resource display name or "*" for wildcard
            public Dictionary<string, float> PickupResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // â”€â”€ Quarry Resource Modifiers â”€â”€
            // Mining quarry output
            public Dictionary<string, float> QuarryResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // â”€â”€ Excavator Resource Modifiers â”€â”€
            // Excavator output
            public Dictionary<string, float> ExcavatorResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // â”€â”€ Survey Resource Modifiers â”€â”€
            // Survey charge results
            public Dictionary<string, float> SurveyResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 1.0f
            };

            // â”€â”€ Crop/Growable Modifiers â”€â”€
            // Harvesting planted crops
            public Dictionary<string, float> CropResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 2.0f
            };

            // â”€â”€ Quarry & Excavator Speed â”€â”€
            // Time in seconds between resource ticks
            public float MiningQuarryResourceTickRate = 5f;    // Vanilla default: 5
            public float ExcavatorResourceTickRate = 3f;       // Vanilla default: 3
            public float ExcavatorTimeForFullResources = 120f; // Vanilla default: 120
            public float ExcavatorBeltSpeedMax = 0.1f;         // Vanilla default: 0.1

            // â”€â”€ Notifications â”€â”€
            public bool ShowGatherNotification = false;
        }

        private PluginConfig _config;

        // Vanilla defaults for restoration on unload
        private const float DefaultMiningQuarryTickRate = 5f;
        private const float DefaultExcavatorTickRate = 3f;
        private const float DefaultExcavatorTimeForFullResources = 120f;
        private const float DefaultExcavatorBeltSpeedMax = 0.1f;

#endregion

#region State

        // Track notified players to avoid spam
        private readonly HashSet<ulong> _notifiedPlayers = new HashSet<ulong>();

        // Valid resources cache (display name -> ItemDefinition)
        private readonly Dictionary<string, ItemDefinition> _validResources = new Dictionary<string, ItemDefinition>();

        // Valid dispenser types
        private readonly Dictionary<string, ResourceDispenser.GatherType> _validDispensers = new Dictionary<string, ResourceDispenser.GatherType>
        {
            ["tree"]   = ResourceDispenser.GatherType.Tree,
            ["ore"]    = ResourceDispenser.GatherType.Ore,
            ["corpse"] = ResourceDispenser.GatherType.Flesh,
            ["flesh"]  = ResourceDispenser.GatherType.Flesh
        };

#endregion

#region Lifecycle

        private void Init()
        {
            LoadConfigVariables();
            permission.RegisterPermission("nwggather.admin", this);
        }

        private void OnServerInitialized()
        {
            // Build valid resources cache (Food + Resources categories)
            foreach (var def in ItemManager.itemList)
            {
                if (def.category == ItemCategory.Food || def.category == ItemCategory.Resources)
                {
                    var key = def.displayName.english.ToLower();
                    if (!_validResources.ContainsKey(key))
                        _validResources[key] = def;
                }
            }

            // Apply excavator modifications
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
            {
                ApplyExcavatorSettings(excavator);
            }

            // Log startup summary
            string wildcard = _config.GatherResourceModifiers.ContainsKey("*") ? $"{_config.GatherResourceModifiers["*"]}x" : "1x";
            Puts($"[NWG Gather] Active. Default: {wildcard} | Quarry Tick: {_config.MiningQuarryResourceTickRate}s | {_validResources.Count} resources tracked.");
        }

        private void Unload()
        {
            // Restore excavator defaults
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
            {
                RestoreExcavatorSettings(excavator);
            }

            _notifiedPlayers.Clear();
            _validResources.Clear();
        }

        private void LoadConfigVariables()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new configuration file for NWG Gather");
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
#endregion

#region Localization
        public static class Lang
        {
            public const string NoPermission = "NoPermission";
            public const string ConfigReloaded = "ConfigReloaded";
            public const string RatesHeader = "RatesHeader";
            public const string RatesSection = "RatesSection";
            public const string RatesDefault = "RatesDefault";
            public const string RatesMore = "RatesMore";
            public const string RatesFooter = "RatesFooter";
            public const string AdminHelp = "AdminHelp";
            public const string HelpHeader = "HelpHeader";
            public const string ValidResources = "ValidResources";
            public const string ValidDispensers = "ValidDispensers";
            public const string InvalidType = "InvalidType";
            public const string InvalidResource = "InvalidResource";
            public const string InvalidDispenser = "InvalidDispenser";
            public const string ModifierPositive = "ModifierPositive";
            public const string RateSet = "RateSet";
            public const string RateReset = "RateReset";
            public const string RateNoOverride = "RateNoOverride";
            public const string TickRateSet = "TickRateSet";
            public const string TickRateLow = "TickRateLow";
            public const string Notification = "Notification";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.NoPermission] = "<color=#d9534f>[NWG]</color> You don't have permission to use this command.",
                [Lang.ConfigReloaded] = "<color=#b7d092>[NWG]</color> Config reloaded and reapplied.",
                [Lang.RatesHeader] = "<color=#b7d092>â•â•â• NWG Gather Rates â•â•â•</color>",
                [Lang.RatesSection] = "\n\n<color=#aaaaaa>{0}:</color>",
                [Lang.RatesDefault] = "\n  <color=#FFA500>Default:</color> <color=#b7d092>{0}x</color>",
                [Lang.RatesMore] = "\n  <color=#aaaaaa>... +{0} more</color>",
                [Lang.RatesFooter] = "\n  <color=#aaaaaa>Tick Rate:</color> <color=#FFA500>{0}s</color>",
                [Lang.AdminHelp] = "<color=#aaaaaa>Admin Commands:</color>\n" +
                                   "<color=#FFA500>gather.rate</color> <type> <resource> <multiplier>\n" +
                                   "<color=#FFA500>dispenser.scale</color> <tree|ore|corpse> <multiplier>",
                [Lang.HelpHeader] = "<color=#b7d092>â•â•â• NWG Gather Admin â•â•â•</color>\n" +
                                    "<color=#aaaaaa>Chat Commands:</color>\n" +
                                    "  <color=#FFA500>/gather</color> â€” Show rates\n" +
                                    "  <color=#FFA500>/gather resources</color> â€” List valid resources\n" +
                                    "  <color=#FFA500>/gather reload</color> â€” Reload config\n\n" +
                                    "<color=#aaaaaa>Console Commands:</color>\n" +
                                    "  <color=#FFA500>gather.rate <type> <resource> <mult|remove></color>\n" +
                                    "  <color=#FFA500>quarry.tickrate <seconds></color>",
                [Lang.ValidResources] = "<color=#b7d092>â•â•â• Valid Resources â•â•â•</color>",
                [Lang.ValidDispensers] = "<color=#b7d092>â•â•â• Valid Dispensers â•â•â•</color>\n" +
                                         "  <color=#FFA500>tree</color> â€” Trees\n" +
                                         "  <color=#FFA500>ore</color> â€” Ore nodes\n" +
                                         "  <color=#FFA500>corpse</color> â€” Animal corpses",
                [Lang.InvalidType] = "<color=#d9534f>Invalid type.</color> Use: dispenser, pickup, quarry, excavator, survey, crop",
                [Lang.InvalidResource] = "<color=#d9534f>'{0}' is not a valid resource.</color>",
                [Lang.InvalidDispenser] = "<color=#d9534f>'{0}' is not a valid dispenser.</color>",
                [Lang.ModifierPositive] = "<color=#d9534f>Modifier must be a positive number.</color>",
                [Lang.RateSet] = "<color=#b7d092>[NWG]</color> Set <color=#FFA500>{0}</color> to <color=#b7d092>x{1}</color> from {2}.",
                [Lang.RateReset] = "<color=#b7d092>[NWG]</color> Reset <color=#FFA500>{0}</color> rate from {1}.",
                [Lang.RateNoOverride] = "<color=#d9534f>[NWG]</color> No override found for {0} in {1}.",
                [Lang.TickRateSet] = "<color=#b7d092>[NWG]</color> <color=#FFA500>{0}</color> tick rate set to {1} seconds.",
                [Lang.TickRateLow] = "<color=#d9534f>Tick rate can't be lower than 1 second.</color>",
                [Lang.Notification] = "<color=#b7d092>[NWG]</color> Enhanced gather rates are active! Type <color=#FFA500>/gather</color> to see rates."
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Gather Hooks

        /// <summary>
        /// Dispenser gathering (trees, ore nodes, barrels, animals)
        /// Supports per-resource modifiers, wildcard, and dispenser scale balancing
        /// </summary>
        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (item == null || entity == null) return;

            var player = entity.ToPlayer();
            if (player == null) return;

            var resourceName = item.info.displayName.english;
            var originalAmount = item.amount;

            // Apply resource modifier (specific or wildcard)
            float modifier = GetModifier(_config.GatherResourceModifiers, resourceName);
            if (modifier != 1.0f)
            {
                item.amount = Mathf.CeilToInt(item.amount * modifier);
            }

            // Apply dispenser scale balancing (keeps node from depleting faster)
            if (dispenser != null && _config.GatherDispenserModifiers.Count > 0)
            {
                var gatherType = dispenser.gatherType.ToString("G");
                if (_config.GatherDispenserModifiers.TryGetValue(gatherType, out float dispenserMod) && dispenserMod > 0f)
                {
                    try
                    {
                        var contained = dispenser.containedItems.SingleOrDefault(x => x.itemid == item.info.itemid);
                        if (contained != null)
                        {
                            // Compensate the node so it doesn't deplete faster from boosted rates
                            contained.amount += originalAmount - item.amount / dispenserMod;

                            if (contained.amount < 0)
                                item.amount += (int)contained.amount;
                        }
                    }
                    catch { }
                }
            }

            NotifyPlayer(player);
        }

        /// <summary>
        /// Bonus hits (the X on trees, sparkle on ore nodes)
        /// Uses same modifiers as dispenser gathering
        /// </summary>
        private void OnDispenserBonus(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            OnDispenserGather(dispenser, entity, item);
        }

        /// <summary>
        /// Collectible pickups (hemp, ground stones, mushrooms, etc.)
        /// </summary>
        private void OnCollectiblePickup(CollectibleEntity collectible, BasePlayer player)
        {
            if (collectible == null || player == null) return;

            foreach (var itemAmount in collectible.itemList)
            {
                if (itemAmount?.itemDef == null) continue;

                float modifier = GetModifier(_config.PickupResourceModifiers, itemAmount.itemDef.displayName.english);
                if (modifier != 1.0f)
                {
                    itemAmount.amount = Mathf.CeilToInt(itemAmount.amount * modifier);
                }
            }

            NotifyPlayer(player);
        }

        /// <summary>
        /// Mining quarry output
        /// </summary>
        private void OnQuarryGather(MiningQuarry quarry, Item item)
        {
            if (item == null || quarry == null) return;

            float modifier = GetModifier(_config.QuarryResourceModifiers, item.info.displayName.english);
            if (modifier != 1.0f)
            {
                item.amount = Mathf.Max(1, Mathf.CeilToInt(item.amount * modifier));
            }
        }

        /// <summary>
        /// Excavator output
        /// </summary>
        private void OnExcavatorGather(ExcavatorArm excavator, Item item)
        {
            if (item == null || excavator == null) return;

            float modifier = GetModifier(_config.ExcavatorResourceModifiers, item.info.displayName.english);
            if (modifier != 1.0f)
            {
                item.amount = Mathf.Max(1, Mathf.CeilToInt(item.amount * modifier));
            }
        }

        /// <summary>
        /// Crop/growable harvesting
        /// </summary>
        private void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
        {
            if (item == null) return;

            float modifier = GetModifier(_config.CropResourceModifiers, item.info.displayName.english);
            if (modifier != 1.0f)
            {
                item.amount = Mathf.Max(1, Mathf.CeilToInt(item.amount * modifier));
            }
        }

        /// <summary>
        /// Survey charge results
        /// </summary>
        private void OnSurveyGather(SurveyCharge survey, Item item)
        {
            if (item == null || survey == null) return;

            float modifier = GetModifier(_config.SurveyResourceModifiers, item.info.displayName.english);
            if (modifier != 1.0f)
            {
                item.amount = Mathf.Max(1, Mathf.CeilToInt(item.amount * modifier));
            }
        }

        /// <summary>
        /// When a quarry is turned on, apply our custom tick rate
        /// </summary>
        private void OnMiningQuarryEnabled(MiningQuarry quarry)
        {
            if (_config.MiningQuarryResourceTickRate == DefaultMiningQuarryTickRate) return;
            quarry.CancelInvoke("ProcessResources");
            quarry.InvokeRepeating("ProcessResources", _config.MiningQuarryResourceTickRate, _config.MiningQuarryResourceTickRate);
        }

#endregion

#region Chat Commands

        [ChatCommand("gather")]
        private void CmdGather(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowRates(player);
                return;
            }

            // Admin-only commands beyond here
            if (!HasPermission(player))
            {
                player.ChatMessage(GetMessage(Lang.NoPermission, player.UserIDString));
                return;
            }

            switch (args[0].ToLower())
            {
                case "help":
                    ShowHelp(player);
                    break;
                case "resources":
                    CmdListResources(player);
                    break;
                case "dispensers":
                    CmdListDispensers(player);
                    break;
                case "reload":
                    RestoreExcavators();
                    LoadConfigVariables();
                    ApplyExcavators();
                    player.ChatMessage(GetMessage(Lang.ConfigReloaded, player.UserIDString));
                    break;
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowRates(BasePlayer player)
        {
            string msg = GetMessage(Lang.RatesHeader, player.UserIDString);

            // Gather (Dispenser) rates
            msg += FormatModifierSection("â› Dispenser Gathering", _config.GatherResourceModifiers, player.UserIDString);

            // Dispenser scale
            if (_config.GatherDispenserModifiers.Count > 0)
            {
                msg += GetMessage(Lang.RatesSection, player.UserIDString, "Dispenser Node Scale");
                foreach (var kvp in _config.GatherDispenserModifiers)
                    msg += $"\n  <color=#FFA500>{kvp.Key}:</color> <color=#b7d092>{kvp.Value}x</color>";
            }

            // Pickups
            msg += FormatModifierSection("ðŸŒ¿ Pickups", _config.PickupResourceModifiers, player.UserIDString);

            // Quarry
            msg += FormatModifierSection("âš™ Quarry", _config.QuarryResourceModifiers, player.UserIDString);
            if (_config.MiningQuarryResourceTickRate != DefaultMiningQuarryTickRate)
                msg += GetMessage(Lang.RatesFooter, player.UserIDString, _config.MiningQuarryResourceTickRate);

            // Excavator
            msg += FormatModifierSection("ðŸ— Excavator", _config.ExcavatorResourceModifiers, player.UserIDString);
            if (_config.ExcavatorResourceTickRate != DefaultExcavatorTickRate)
                msg += GetMessage(Lang.RatesFooter, player.UserIDString, _config.ExcavatorResourceTickRate);

            // Crops
            msg += FormatModifierSection("ðŸŒ¾ Crops", _config.CropResourceModifiers, player.UserIDString);

            // Survey
            msg += FormatModifierSection("ðŸ“‹ Survey", _config.SurveyResourceModifiers, player.UserIDString);

            player.ChatMessage(msg);

            // Show admin help if admin
            if (HasPermission(player))
            {
                player.ChatMessage(GetMessage(Lang.AdminHelp, player.UserIDString));
            }
        }

        private void ShowHelp(BasePlayer player)
        {
            player.ChatMessage(GetMessage(Lang.HelpHeader, player.UserIDString));
        }

        private void CmdListResources(BasePlayer player)
        {
            string msg = GetMessage(Lang.ValidResources, player.UserIDString);
            int count = 0;
            foreach (var kvp in _validResources.OrderBy(x => x.Key))
            {
                msg += $"\n  {kvp.Value.displayName.english}";
                count++;
                if (count >= 30)
                {
                    msg += $"\n<color=#aaaaaa>... and {_validResources.Count - 30} more (use console gather.resources for full list)</color>";
                    break;
                }
            }
            msg += "\n  <color=#FFA500>*</color> (wildcard â€” applies to all unspecified resources)";
            player.ChatMessage(msg);
        }

        private void CmdListDispensers(BasePlayer player)
        {
            player.ChatMessage(GetMessage(Lang.ValidDispensers, player.UserIDString));
        }

#endregion

#region Console Commands

        [ConsoleCommand("gather.rate")]
        private void ConsoleCmdGatherRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            if (!arg.HasArgs(3))
            {
                arg.ReplyWith("Usage: gather.rate <type:dispenser|pickup|quarry|excavator|survey|crop> <resource> <multiplier|remove>\n" +
                              "Use * for the resource name to set a default for all resources.");
                return;
            }

            var type = arg.GetString(0).ToLower();
            var resourceInput = arg.GetString(1);
            var modifierStr = arg.GetString(2);

            // Validate resource (accept * wildcard or valid resource name)
            string resourceKey;
            if (resourceInput == "*")
            {
                resourceKey = "*";
            }
            else
            {
                var lower = resourceInput.ToLower();
                if (!_validResources.ContainsKey(lower))
                {
                    arg.ReplyWith($"'{resourceInput}' is not a valid resource. Use gather.resources for a list.");
                    return;
                }
                resourceKey = _validResources[lower].displayName.english;
            }

            // Parse modifier or "remove"
            bool remove = modifierStr.ToLower() == "remove";
            float modifier = 0f;
            if (!remove)
            {
                if (!float.TryParse(modifierStr, out modifier) || modifier <= 0)
                {
                    arg.ReplyWith("Modifier must be a positive number, or use 'remove' to reset.");
                    return;
                }
            }

            // Get the right dictionary
            Dictionary<string, float> dict;
            string typeName;
            switch (type)
            {
                case "dispenser":
                    dict = _config.GatherResourceModifiers;
                    typeName = "Dispensers";
                    break;
                case "pickup":
                case "pickups":
                    dict = _config.PickupResourceModifiers;
                    typeName = "Pickups";
                    break;
                case "quarry":
                    dict = _config.QuarryResourceModifiers;
                    typeName = "Quarries";
                    break;
                case "excavator":
                    dict = _config.ExcavatorResourceModifiers;
                    typeName = "Excavators";
                    break;
                case "survey":
                    dict = _config.SurveyResourceModifiers;
                    typeName = "Survey Charges";
                    break;
                case "crop":
                case "crops":
                    dict = _config.CropResourceModifiers;
                    typeName = "Crops";
                    break;
                default:
                    arg.ReplyWith("Invalid type. Use: dispenser, pickup, quarry, excavator, survey, crop");
                    return;
            }

            if (remove)
            {
                if (dict.Remove(resourceKey))
                    arg.ReplyWith(GetMessage(Lang.RateReset, arg.Player()?.UserIDString, resourceKey, typeName));
                else
                    arg.ReplyWith(GetMessage(Lang.RateNoOverride, arg.Player()?.UserIDString, resourceKey, typeName));
            }
            else
            {
                dict[resourceKey] = modifier;
                arg.ReplyWith(GetMessage(Lang.RateSet, arg.Player()?.UserIDString, resourceKey, modifier, typeName));
            }

            SaveConfig();
        }

        [ConsoleCommand("dispenser.scale")]
        private void ConsoleCmdDispenserScale(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            if (!arg.HasArgs(2))
            {
                arg.ReplyWith("Usage: dispenser.scale <tree|ore|corpse> <multiplier>");
                return;
            }

            var dispenserInput = arg.GetString(0).ToLower();
            if (!_validDispensers.ContainsKey(dispenserInput))
            {
                arg.ReplyWith($"'{dispenserInput}' is not a valid dispenser. Use: tree, ore, corpse");
                return;
            }

            var modifier = arg.GetFloat(1, -1);
            if (modifier <= 0)
            {
                arg.ReplyWith("Modifier must be a positive number.");
                return;
            }

            var dispenserType = _validDispensers[dispenserInput].ToString("G");
            _config.GatherDispenserModifiers[dispenserType] = modifier;
            SaveConfig();
            arg.ReplyWith(GetMessage(Lang.RateSet, arg.Player()?.UserIDString, dispenserType, modifier, "Dispenser Scale"));
        }

        [ConsoleCommand("quarry.tickrate")]
        private void ConsoleCmdQuarryTickRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            if (!arg.HasArgs())
            {
                arg.ReplyWith("Usage: quarry.tickrate <seconds>");
                return;
            }

            var rate = arg.GetFloat(0, -1);
            if (rate < 1)
            {
                arg.ReplyWith("Tick rate can't be lower than 1 second.");
                return;
            }

            _config.MiningQuarryResourceTickRate = rate;
            SaveConfig();

            // Apply to all active quarries
            foreach (var quarry in UnityEngine.Object.FindObjectsOfType<MiningQuarry>().Where(q => q.IsOn()))
            {
                quarry.CancelInvoke("ProcessResources");
                quarry.InvokeRepeating("ProcessResources", rate, rate);
            }

            arg.ReplyWith(GetMessage(Lang.TickRateSet, arg.Player()?.UserIDString, "Mining Quarry", rate));
        }

        [ConsoleCommand("excavator.tickrate")]
        private void ConsoleCmdExcavatorTickRate(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            if (!arg.HasArgs())
            {
                arg.ReplyWith("Usage: excavator.tickrate <seconds>");
                return;
            }

            var rate = arg.GetFloat(0, -1);
            if (rate < 1)
            {
                arg.ReplyWith("Tick rate can't be lower than 1 second.");
                return;
            }

            _config.ExcavatorResourceTickRate = rate;
            SaveConfig();

            // Apply to all excavators
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
            {
                excavator.CancelInvoke("ProcessResources");
                excavator.InvokeRepeating("ProcessResources", rate, rate);
            }

            arg.ReplyWith(GetMessage(Lang.TickRateSet, arg.Player()?.UserIDString, "Excavator", rate));
        }

        [ConsoleCommand("gather.rates")]
        private void ConsoleCmdRates(ConsoleSystem.Arg arg)
        {
            string msg = "[NWG Gather] Current Rates:\n";
            msg += FormatConsoleModifiers("Dispenser", _config.GatherResourceModifiers);
            msg += FormatConsoleModifiers("Pickup", _config.PickupResourceModifiers);
            msg += FormatConsoleModifiers("Quarry", _config.QuarryResourceModifiers);
            msg += FormatConsoleModifiers("Excavator", _config.ExcavatorResourceModifiers);
            msg += FormatConsoleModifiers("Survey", _config.SurveyResourceModifiers);
            msg += FormatConsoleModifiers("Crop", _config.CropResourceModifiers);

            if (_config.GatherDispenserModifiers.Count > 0)
            {
                msg += "\nDispenser Scale:";
                foreach (var kvp in _config.GatherDispenserModifiers)
                    msg += $"\n  {kvp.Key}: x{kvp.Value}";
            }

            msg += $"\nQuarry Tick: {_config.MiningQuarryResourceTickRate}s | Excavator Tick: {_config.ExcavatorResourceTickRate}s";
            arg.ReplyWith(msg);
        }

        [ConsoleCommand("gather.resources")]
        private void ConsoleCmdResources(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            string msg = "Available resources:\n";
            msg = _validResources.OrderBy(x => x.Key).Aggregate(msg, (current, kvp) => current + kvp.Value.displayName.english + "\n");
            msg += "* (wildcard â€” all resources not set individually)";
            arg.ReplyWith(msg);
        }

        [ConsoleCommand("gather.dispensers")]
        private void ConsoleCmdDispensers(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin)
            {
                arg.ReplyWith("You don't have permission to use this command.");
                return;
            }

            arg.ReplyWith("Available dispensers:\n  tree â€” Trees\n  ore â€” Ore nodes\n  corpse / flesh â€” Animal corpses");
        }

        [ConsoleCommand("nwggather.reload")]
        private void ConsoleCmdReload(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            RestoreExcavators();
            LoadConfigVariables();
            ApplyExcavators();
            arg.ReplyWith(GetMessage(Lang.ConfigReloaded, arg.Player()?.UserIDString));
        }

#endregion

#region Helpers

        /// <summary>
        /// Gets the modifier for a resource. Checks specific name first, then falls back to wildcard "*".
        /// Returns 1.0 if nothing is configured.
        /// </summary>
        private float GetModifier(Dictionary<string, float> modifiers, string resourceName)
        {
            if (modifiers == null || modifiers.Count == 0) return 1.0f;

            // Check for exact resource name match
            if (modifiers.TryGetValue(resourceName, out float specific))
                return specific;

            // Fall back to wildcard
            if (modifiers.TryGetValue("*", out float wildcard))
                return wildcard;

            return 1.0f;
        }

        private string FormatModifierSection(string title, Dictionary<string, float> modifiers, string userId)
        {
            if (modifiers == null || modifiers.Count == 0) return "";

            string section = GetMessage(Lang.RatesSection, userId, title);

            if (modifiers.ContainsKey("*"))
                section += GetMessage(Lang.RatesDefault, userId, modifiers["*"]);

            foreach (var kvp in modifiers.Where(x => x.Key != "*").Take(8))
                section += $"\n  <color=#FFA500>{kvp.Key}:</color> <color=#b7d092>{kvp.Value}x</color>";

            int remaining = modifiers.Count(x => x.Key != "*") - 8;
            if (remaining > 0)
                section += GetMessage(Lang.RatesMore, userId, remaining);

            return section;
        }

        private string FormatConsoleModifiers(string label, Dictionary<string, float> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0) return $"\n{label}: Default";

            string result = $"\n{label}:";
            foreach (var kvp in modifiers)
                result += $"\n  {kvp.Key}: x{kvp.Value}";
            return result;
        }

        private bool HasPermission(BasePlayer player)
        {
            return player.IsAdmin || permission.UserHasPermission(player.UserIDString, "nwggather.admin");
        }

        private void NotifyPlayer(BasePlayer player)
        {
            if (!_config.ShowGatherNotification || player == null) return;
            if (_notifiedPlayers.Contains(player.userID)) return;

            _notifiedPlayers.Add(player.userID);
            player.ChatMessage(GetMessage(Lang.Notification, player.UserIDString));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
                _notifiedPlayers.Remove(player.userID);
        }

        private void ApplyExcavatorSettings(ExcavatorArm excavator)
        {
            if (excavator == null) return;

            if (_config.ExcavatorResourceTickRate != DefaultExcavatorTickRate)
            {
                excavator.CancelInvoke("ProcessResources");
                excavator.InvokeRepeating("ProcessResources", _config.ExcavatorResourceTickRate, _config.ExcavatorResourceTickRate);
            }

            if (_config.ExcavatorBeltSpeedMax != DefaultExcavatorBeltSpeedMax)
                excavator.beltSpeedMax = _config.ExcavatorBeltSpeedMax;

            if (_config.ExcavatorTimeForFullResources != DefaultExcavatorTimeForFullResources)
                excavator.timeForFullResources = _config.ExcavatorTimeForFullResources;
        }

        private void RestoreExcavatorSettings(ExcavatorArm excavator)
        {
            if (excavator == null) return;

            if (_config.ExcavatorResourceTickRate != DefaultExcavatorTickRate)
            {
                excavator.CancelInvoke("ProcessResources");
                excavator.InvokeRepeating("ProcessResources", DefaultExcavatorTickRate, DefaultExcavatorTickRate);
            }

            if (_config.ExcavatorBeltSpeedMax != DefaultExcavatorBeltSpeedMax)
                excavator.beltSpeedMax = DefaultExcavatorBeltSpeedMax;

            if (_config.ExcavatorTimeForFullResources != DefaultExcavatorTimeForFullResources)
                excavator.timeForFullResources = DefaultExcavatorTimeForFullResources;
        }

        private void ApplyExcavators()
        {
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
                ApplyExcavatorSettings(excavator);
        }

        private void RestoreExcavators()
        {
            foreach (var excavator in UnityEngine.Object.FindObjectsOfType<ExcavatorArm>())
                RestoreExcavatorSettings(excavator);
        }

#endregion
    }
}

