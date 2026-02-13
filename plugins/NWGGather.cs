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
            // ── Global Resource Modifiers ──
            // Key: resource display name (e.g. "Wood", "Stones", "Metal Ore")
            // Use "*" as a wildcard to set a default for all unspecified resources
            // These apply to dispenser gathering (hitting trees, rocks, etc.)
            public Dictionary<string, float> GatherResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f  // Default: 3x all resources from dispensers
            };

            // ── Dispenser Type Scale ──
            // Controls how much the *node itself* contains (scales containedItems)
            // Types: "Tree", "Ore", "Flesh" (corpses/animals)
            public Dictionary<string, float> GatherDispenserModifiers = new Dictionary<string, float>
            {
                // ["Tree"] = 1.0f,
                // ["Ore"] = 1.0f,
                // ["Flesh"] = 1.0f,
            };

            // ── Pickup Resource Modifiers ──
            // Collectible pickups (hemp, ground stones, mushrooms, etc.)
            // Key: resource display name or "*" for wildcard
            public Dictionary<string, float> PickupResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // ── Quarry Resource Modifiers ──
            // Mining quarry output
            public Dictionary<string, float> QuarryResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // ── Excavator Resource Modifiers ──
            // Excavator output
            public Dictionary<string, float> ExcavatorResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 3.0f
            };

            // ── Survey Resource Modifiers ──
            // Survey charge results
            public Dictionary<string, float> SurveyResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 1.0f
            };

            // ── Crop/Growable Modifiers ──
            // Harvesting planted crops
            public Dictionary<string, float> CropResourceModifiers = new Dictionary<string, float>
            {
                ["*"] = 2.0f
            };

            // ── Quarry & Excavator Speed ──
            // Time in seconds between resource ticks
            public float MiningQuarryResourceTickRate = 5f;    // Vanilla default: 5
            public float ExcavatorResourceTickRate = 3f;       // Vanilla default: 3
            public float ExcavatorTimeForFullResources = 120f; // Vanilla default: 120
            public float ExcavatorBeltSpeedMax = 0.1f;         // Vanilla default: 0.1

            // ── Notifications ──
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
                player.ChatMessage("<color=#ff4444>[NWG Gather]</color> You don't have permission to use admin commands.");
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
                    player.ChatMessage("<color=#55ff55>[NWG Gather]</color> Config reloaded and reapplied.");
                    break;
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowRates(BasePlayer player)
        {
            string msg = "<color=#55aaff>═══ NWG Gather Rates ═══</color>";

            // Gather (Dispenser) rates
            msg += FormatModifierSection("⛏ Dispenser Gathering", _config.GatherResourceModifiers);

            // Dispenser scale
            if (_config.GatherDispenserModifiers.Count > 0)
            {
                msg += "\n\n<color=#aaaaaa>Dispenser Node Scale:</color>";
                foreach (var kvp in _config.GatherDispenserModifiers)
                    msg += $"\n  <color=#ffcc00>{kvp.Key}:</color> <color=#55ff55>{kvp.Value}x</color>";
            }

            // Pickups
            msg += FormatModifierSection("🌿 Pickups", _config.PickupResourceModifiers);

            // Quarry
            msg += FormatModifierSection("⚙ Quarry", _config.QuarryResourceModifiers);
            if (_config.MiningQuarryResourceTickRate != DefaultMiningQuarryTickRate)
                msg += $"\n  <color=#aaaaaa>Tick Rate:</color> {_config.MiningQuarryResourceTickRate}s";

            // Excavator
            msg += FormatModifierSection("🏗 Excavator", _config.ExcavatorResourceModifiers);
            if (_config.ExcavatorResourceTickRate != DefaultExcavatorTickRate)
                msg += $"\n  <color=#aaaaaa>Tick Rate:</color> {_config.ExcavatorResourceTickRate}s";

            // Crops
            msg += FormatModifierSection("🌾 Crops", _config.CropResourceModifiers);

            // Survey
            msg += FormatModifierSection("📋 Survey", _config.SurveyResourceModifiers);

            player.ChatMessage(msg);

            // Show admin help if admin
            if (HasPermission(player))
            {
                player.ChatMessage("<color=#aaaaaa>Admin: Use console commands to modify rates.</color>\n" +
                    "<color=#ffcc00>gather.rate</color> <type> <resource> <multiplier>\n" +
                    "<color=#ffcc00>dispenser.scale</color> <tree|ore|corpse> <multiplier>\n" +
                    "<color=#ffcc00>quarry.tickrate</color> <seconds>\n" +
                    "<color=#ffcc00>excavator.tickrate</color> <seconds>");
            }
        }

        private void ShowHelp(BasePlayer player)
        {
            string msg = "<color=#55aaff>═══ NWG Gather Admin ═══</color>\n" +
                         "<color=#aaaaaa>Chat Commands:</color>\n" +
                         "  <color=#ffcc00>/gather</color> — Show rates (all players)\n" +
                         "  <color=#ffcc00>/gather resources</color> — List valid resource names\n" +
                         "  <color=#ffcc00>/gather dispensers</color> — List valid dispenser types\n" +
                         "  <color=#ffcc00>/gather reload</color> — Reload config\n\n" +
                         "<color=#aaaaaa>Console Commands:</color>\n" +
                         "  <color=#ffcc00>gather.rate <type> <resource> <mult|remove></color>\n" +
                         "    Types: dispenser, pickup, quarry, excavator, survey, crop\n" +
                         "    Use * for all resources\n" +
                         "  <color=#ffcc00>dispenser.scale <tree|ore|corpse> <mult></color>\n" +
                         "  <color=#ffcc00>quarry.tickrate <seconds></color>\n" +
                         "  <color=#ffcc00>excavator.tickrate <seconds></color>\n" +
                         "  <color=#ffcc00>gather.resources</color> — List valid resources\n" +
                         "  <color=#ffcc00>gather.dispensers</color> — List valid dispensers";
            player.ChatMessage(msg);
        }

        private void CmdListResources(BasePlayer player)
        {
            string msg = "<color=#55aaff>═══ Valid Resources ═══</color>";
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
            msg += "\n  <color=#ffcc00>*</color> (wildcard — applies to all unspecified resources)";
            player.ChatMessage(msg);
        }

        private void CmdListDispensers(BasePlayer player)
        {
            player.ChatMessage("<color=#55aaff>═══ Valid Dispensers ═══</color>\n" +
                "  <color=#ffcc00>tree</color> — Trees\n" +
                "  <color=#ffcc00>ore</color> — Ore nodes (stone, metal, sulfur)\n" +
                "  <color=#ffcc00>corpse / flesh</color> — Animal corpses");
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
                    arg.ReplyWith($"[NWG Gather] Reset {resourceKey} rate from {typeName}.");
                else
                    arg.ReplyWith($"[NWG Gather] No override found for {resourceKey} in {typeName}.");
            }
            else
            {
                dict[resourceKey] = modifier;
                arg.ReplyWith($"[NWG Gather] Set {resourceKey} to x{modifier} from {typeName}.");
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
            arg.ReplyWith($"[NWG Gather] Set {dispenserType} dispenser scale to x{modifier}.");
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

            arg.ReplyWith($"[NWG Gather] Mining Quarry tick rate set to {rate} seconds.");
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

            arg.ReplyWith($"[NWG Gather] Excavator tick rate set to {rate} seconds.");
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
            msg += "* (wildcard — all resources not set individually)";
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

            arg.ReplyWith("Available dispensers:\n  tree — Trees\n  ore — Ore nodes\n  corpse / flesh — Animal corpses");
        }

        [ConsoleCommand("nwggather.reload")]
        private void ConsoleCmdReload(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            RestoreExcavators();
            LoadConfigVariables();
            ApplyExcavators();
            arg.ReplyWith("[NWG Gather] Config reloaded and reapplied.");
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

        private string FormatModifierSection(string title, Dictionary<string, float> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0) return "";

            string section = $"\n\n<color=#aaaaaa>{title}:</color>";

            if (modifiers.ContainsKey("*"))
                section += $"\n  <color=#ffcc00>Default:</color> <color=#55ff55>{modifiers["*"]}x</color>";

            foreach (var kvp in modifiers.Where(x => x.Key != "*").Take(8))
                section += $"\n  <color=#ffcc00>{kvp.Key}:</color> <color=#55ff55>{kvp.Value}x</color>";

            int remaining = modifiers.Count(x => x.Key != "*") - 8;
            if (remaining > 0)
                section += $"\n  <color=#aaaaaa>... +{remaining} more</color>";

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
            player.ChatMessage("<color=#55aaff>[NWG Gather]</color> Enhanced gather rates are active! Type <color=#ffcc00>/gather</color> to see rates.");
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

