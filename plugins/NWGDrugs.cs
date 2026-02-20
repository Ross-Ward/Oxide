// NWGDrugs - Modular Drug System for Rust RP
// Originally based on NWGWeed by The_Kiiiing (v2.0.11), refactored to NWG standards
// V 3.0.0 - Full NWG refactor: localization, Sage Green theme, multi-drug architecture

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("NWGDrugs", "NWG Team", "3.0.0")]
    [Description("Modular drug system: gathering, crafting, and consumption with configurable effects.")]
    public class NWGDrugs : RustPlugin
    {
#region Constants
        private const float JOINT_USE_COOLDOWN = 2f;

        private const string PERM_GATHER = "nwgdrugs.gather";
        private const string PERM_CRAFT  = "nwgdrugs.craft";
        private const string PERM_GIVE   = "nwgdrugs.give";

        private const string CMD_TOGGLE_UI = "nwgdrugs.toggleui";
        private const string CMD_CRAFT     = "nwgdrugs.craft";

        private const string SHAKE_EFFECT   = "assets/bundled/prefabs/fx/screen_land.prefab";
        private const string SHAKE2_EFFECT  = "assets/bundled/prefabs/fx/takedamage_generic.prefab";
        private const string VOMIT_EFFECT   = "assets/bundled/prefabs/fx/gestures/drink_vomit.prefab";
        private const string LICK_EFFECT    = "assets/bundled/prefabs/fx/gestures/lick.prefab";
        private const string BREATHE_EFFECT = "assets/prefabs/npc/bear/sound/breathe.prefab";
        private const string SMOKE_EFFECT   = "assets/bundled/prefabs/fx/door/barricade_spawn.prefab";
#endregion

#region References
        [PluginReference] private Plugin CustomSkinsStacksFix, StackModifier, Loottable, DeployableNature, PlanterboxDefender;
#endregion

#region UI Theme — Sage Green & Dark
        private const string BgColor      = "0.15 0.15 0.15 0.98";
        private const string HeaderColor  = "0.1 0.1 0.1 1";
        private const string OnColor      = "0.718 0.816 0.573 1";
        private const string OffColor     = "0.851 0.325 0.31 1";
        private const string BtnColor     = "0.25 0.25 0.25 0.9";
        private const string TextColor    = "0.867 0.867 0.867 1";
        private const string AccentColor  = "1 0.647 0 1";
        private const string ItemTileColor = "0.2 0.2 0.19 1";

        private const string LAYER_RECIPE       = "NWGDrugs.ui.craft";
        private const string LAYER_TOGGLE       = "NWGDrugs.ui.toggle";
        private const string LAYER_CRAFT_BUTTON = "NWGDrugs.ui.craftbutton";
        private const string LAYER_BLUR         = "NWGDrugs.ui.effects.blur";
        private const string LAYER_COLOR        = "NWGDrugs.ui.effects.color";
#endregion

#region Instance Fields
        private readonly Dictionary<ulong, float> _lastUsed = new Dictionary<ulong, float>();
        private readonly Dictionary<Item, Timer> _jointTimers = new Dictionary<Item, Timer>();
        private readonly List<ulong> _openUis = new List<ulong>();
        private PluginConfig _config;
        private PlayerData _playerData;
#endregion

#region Configuration
        private class PluginConfig
        {
            [JsonProperty("Drug Categories", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, DrugCategory> DrugCategories = new Dictionary<string, DrugCategory>
            {
                ["cannabis"] = new DrugCategory
                {
                    DisplayName = "Cannabis",
                    GrowablePrefabId = 3587624038,
                    CollectiblePrefabId = 3006540952,
                    Strains = new StrainConfig
                    {
                        Enabled = true,
                        YieldPerYGene = 0.15f,
                        PenaltyPerWGene = 0.1f,
                        CrossbreedBonusPerXGene = 0.05f,
                        Strains = new List<StrainDefinition>
                        {
                            new StrainDefinition { Name = "Schwag",        MinH = 0, MinG = 0, MinY = 0, Priority = 0,  PotencyMultiplier = 0.5f, ColorTag = "#888888" },
                            new StrainDefinition { Name = "Northern Lights",MinH = 3, MinG = 2, MinY = 0, Priority = 10, PotencyMultiplier = 1.0f, ColorTag = "#5599ff" },
                            new StrainDefinition { Name = "OG Kush",       MinH = 4, MinG = 3, MinY = 2, Priority = 20, PotencyMultiplier = 1.3f, ColorTag = "#66cc66" },
                            new StrainDefinition { Name = "Sour Diesel",   MinH = 5, MinG = 3, MinY = 3, Priority = 30, PotencyMultiplier = 1.5f, ColorTag = "#ffcc00" },
                            new StrainDefinition { Name = "Purple Haze",   MinH = 5, MinG = 5, MinY = 4, Priority = 40, PotencyMultiplier = 2.0f, ColorTag = "#cc66ff" },
                            new StrainDefinition { Name = "White Widow",   MinH = 6, MinG = 5, MinY = 5, Priority = 50, PotencyMultiplier = 2.5f, ColorTag = "#ffffff" }
                        }
                    },
                    Drops = new List<DrugDropConfig>
                    {
                        new DrugDropConfig
                        {
                            ShortName = "sticks", SkinId = 2661029427,
                            DisplayName = "Low Quality Weed", Identifier = "low_quality",
                            DropChance = 0.4f, DropAmountMin = 1, DropAmountMax = 3,
                            BiomeMask = 6, MinHGenesChance = 1, MinHGenesGuaranteed = 3
                        },
                        new DrugDropConfig
                        {
                            ShortName = "sticks", SkinId = 2661031542,
                            DisplayName = "Medium Quality Weed", Identifier = "med_quality",
                            DropChance = 0.3f, DropAmountMin = 1, DropAmountMax = 3,
                            BiomeMask = 1, MinHGenesChance = 1, MinHGenesGuaranteed = 3
                        },
                        new DrugDropConfig
                        {
                            ShortName = "sticks", SkinId = 2660588149,
                            DisplayName = "High Quality Weed", Identifier = "high_quality",
                            DropChance = 0.1f, DropAmountMin = 1, DropAmountMax = 2,
                            BiomeMask = 8, MinHGenesChance = 1, MinHGenesGuaranteed = 3
                        }
                    },
                    Recipes = new List<DrugRecipe>
                    {
                        // --- Joint recipes ---
                        new DrugRecipe
                        {
                            Identifier = "low_quality", IsConsumable = true,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "note", SkinId = 0, Amount = 1 },
                                [1] = new RecipeIngredient { ShortName = "sticks", SkinId = 2661029427, Amount = 1 },
                                [2] = new RecipeIngredient { ShortName = "sticks", SkinId = 2661029427, Amount = 1 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "horse.shoes.basic", SkinId = 2894101592,
                                DisplayName = "Low Quality Joint", Amount = 1
                            },
                            Boosts = new BoostConfig
                            {
                                WoodPercentage = 0.4f, WoodDuration = 20f, HealingPerUse = 1f,
                                JointDurability = 120f, JointDurabilityLossPerHit = 10f
                            }
                        },
                        new DrugRecipe
                        {
                            Identifier = "med_quality", IsConsumable = true,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "note", SkinId = 0, Amount = 1 },
                                [1] = new RecipeIngredient { ShortName = "sticks", SkinId = 2661031542, Amount = 1 },
                                [2] = new RecipeIngredient { ShortName = "sticks", SkinId = 2661031542, Amount = 1 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "horse.shoes.basic", SkinId = 2894101290,
                                DisplayName = "Medium Quality Joint", Amount = 1
                            },
                            Boosts = new BoostConfig
                            {
                                OrePercentage = 0.8f, OreDuration = 20f, HealingPerUse = 4f,
                                JointDurability = 120f, JointDurabilityLossPerHit = 10f
                            }
                        },
                        new DrugRecipe
                        {
                            Identifier = "high_quality", IsConsumable = true,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "note", SkinId = 0, Amount = 1 },
                                [1] = new RecipeIngredient { ShortName = "sticks", SkinId = 2660588149, Amount = 1 },
                                [2] = new RecipeIngredient { ShortName = "sticks", SkinId = 2660588149, Amount = 1 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "horse.shoes.basic", SkinId = 2893700325,
                                DisplayName = "High Quality Joint", Amount = 1
                            },
                            Boosts = new BoostConfig
                            {
                                ScrapPercentage = 1f, ScrapDuration = 30f,
                                MaxHealthPercentage = 0.3f, MaxHealthDuration = 30f,
                                HealingPerUse = 8f,
                                JointDurability = 120f, JointDurabilityLossPerHit = 10f
                            }
                        },
                        // --- Bag packaging ---
                        new DrugRecipe
                        {
                            Identifier = "weed_bag", IsConsumable = false,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "sticks", SkinId = 2660588149, Amount = 5 },
                                [1] = new RecipeIngredient { ShortName = "cloth", SkinId = 0, Amount = 2 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "smallwaterbottle", SkinId = 2950000020,
                                DisplayName = "Bag of Weed", Amount = 1
                            },
                            Boosts = new BoostConfig()
                        },
                        // --- Brick packaging ---
                        new DrugRecipe
                        {
                            Identifier = "weed_brick", IsConsumable = false,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "smallwaterbottle", SkinId = 2950000020, Amount = 5 },
                                [1] = new RecipeIngredient { ShortName = "ducttape", SkinId = 0, Amount = 1 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "box.wooden.large", SkinId = 2950000021,
                                DisplayName = "Brick of Weed", Amount = 1
                            },
                            Boosts = new BoostConfig()
                        }
                    }
                },
                ["coca"] = new DrugCategory
                {
                    DisplayName = "Coca",
                    GrowablePrefabId = 0,
                    CollectiblePrefabId = 3006540952,
                    Effects = new EffectProfile
                    {
                        EnableBlur = false, EnableColorShift = false,
                        EnableShake = true, ShakeCount = 3,
                        EnableSmoke = false, EnableBreathing = false,
                        EnableLick = true, VomitChance = 0.02f,
                        EnableHeartbeat = true, EnableSpeedLines = true
                    },
                    Drops = new List<DrugDropConfig>
                    {
                        new DrugDropConfig
                        {
                            ShortName = "seed.hemp", SkinId = 2900000001,
                            DisplayName = "Coca Leaves", Identifier = "coca_leaves",
                            DropChance = 0.15f, DropAmountMin = 1, DropAmountMax = 2,
                            BiomeMask = 8, DisableGrowableGathering = true
                        }
                    },
                    Recipes = new List<DrugRecipe>
                    {
                        new DrugRecipe
                        {
                            Identifier = "cocaine_powder", IsConsumable = false,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "seed.hemp", SkinId = 2900000001, Amount = 5 },
                                [1] = new RecipeIngredient { ShortName = "lowgradefuel", SkinId = 0, Amount = 2 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "sticks", SkinId = 2900000002,
                                DisplayName = "Cocaine Powder", Amount = 1
                            },
                            Boosts = new BoostConfig()
                        },
                        new DrugRecipe
                        {
                            Identifier = "cocaine_line", IsConsumable = true,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "sticks", SkinId = 2900000002, Amount = 1 },
                                [1] = new RecipeIngredient { ShortName = "note", SkinId = 0, Amount = 1 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "horse.shoes.basic", SkinId = 2900000003,
                                DisplayName = "Cocaine Line", Amount = 2
                            },
                            Boosts = new BoostConfig
                            {
                                OrePercentage = 0.5f, OreDuration = 45f,
                                WoodPercentage = 0.5f, WoodDuration = 45f,
                                CaloriesPerUse = -15f, HydrationPerUse = -10f,
                                BleedingPerUse = 0.5f,
                                JointDurability = 30f, JointDurabilityLossPerHit = 30f
                            }
                        },
                        // --- Brick packaging ---
                        new DrugRecipe
                        {
                            Identifier = "coke_brick", IsConsumable = false,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "sticks", SkinId = 2900000002, Amount = 10 },
                                [1] = new RecipeIngredient { ShortName = "ducttape", SkinId = 0, Amount = 2 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "box.wooden.large", SkinId = 2950000030,
                                DisplayName = "Coke Brick", Amount = 1
                            },
                            Boosts = new BoostConfig()
                        }
                    }
                },
                ["meth"] = new DrugCategory
                {
                    DisplayName = "Methamphetamine",
                    GrowablePrefabId = 0,
                    CollectiblePrefabId = 0,
                    Effects = new EffectProfile
                    {
                        EnableBlur = true, BlurOpacity = 0.3f, BlurDuration = 4f,
                        EnableColorShift = true, ColorShiftInterval = 0.15f,
                        ColorTint = "0.8 0.9 1 0.25",
                        EnableShake = true, ShakeCount = 5,
                        EnableSmoke = true, SmokePuffCount = 2,
                        EnableBreathing = false,
                        EnableLick = false, VomitChance = 0.15f,
                        EnableHeartbeat = true, EnableSpeedLines = true
                    },
                    Drops = new List<DrugDropConfig>(),
                    Recipes = new List<DrugRecipe>
                    {
                        new DrugRecipe
                        {
                            Identifier = "crystal_meth", IsConsumable = true,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "lowgradefuel", SkinId = 0, Amount = 5 },
                                [1] = new RecipeIngredient { ShortName = "sulfur", SkinId = 0, Amount = 10 },
                                [2] = new RecipeIngredient { ShortName = "cloth", SkinId = 0, Amount = 3 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "horse.shoes.basic", SkinId = 2900000010,
                                DisplayName = "Crystal Meth", Amount = 1
                            },
                            Boosts = new BoostConfig
                            {
                                MaxHealthPercentage = 0.25f, MaxHealthDuration = 60f,
                                ScrapPercentage = 0.5f, ScrapDuration = 60f,
                                OrePercentage = 1f, OreDuration = 60f,
                                PoisonPerUse = 3f, CaloriesPerUse = -30f,
                                HydrationPerUse = -20f,
                                JointDurability = 60f, JointDurabilityLossPerHit = 20f
                            }
                        },
                        // --- Brick packaging ---
                        new DrugRecipe
                        {
                            Identifier = "meth_brick", IsConsumable = false,
                            IngredientSlots = new Dictionary<int, RecipeIngredient>
                            {
                                [0] = new RecipeIngredient { ShortName = "horse.shoes.basic", SkinId = 2900000010, Amount = 10 },
                                [1] = new RecipeIngredient { ShortName = "ducttape", SkinId = 0, Amount = 2 }
                            },
                            ProducedItem = new ProducedItemConfig
                            {
                                ShortName = "box.wooden.large", SkinId = 2950000040,
                                DisplayName = "Meth Brick", Amount = 1
                            },
                            Boosts = new BoostConfig()
                        }
                    }
                }
            };

            [JsonProperty("Require permission for crafting")]
            public bool RequireCraftPermission = true;

            [JsonProperty("Require permission for gathering")]
            public bool RequireGatherPermission = true;

            [JsonProperty("Disable built-in stack fix")]
            public bool DisableStackFix = false;

            [JsonProperty("Auto extinguish on unequip")]
            public bool ExtinguishOnUnequip = true;

            // Runtime caches - not serialized
            [JsonIgnore] private HashSet<ulong> _consumableSkins;
            [JsonIgnore] private HashSet<ulong> _drugItemSkins;

            public bool IsConsumableSkin(ulong skin)
            {
                if (_consumableSkins == null)
                {
                    _consumableSkins = new HashSet<ulong>();
                    foreach (var cat in DrugCategories.Values)
                        foreach (var r in cat.Recipes)
                            if (r.IsConsumable) _consumableSkins.Add(r.ProducedItem.SkinId);
                }
                return _consumableSkins.Contains(skin);
            }

            public bool IsDrugItemSkin(ulong skin)
            {
                if (_drugItemSkins == null)
                {
                    _drugItemSkins = new HashSet<ulong>();
                    foreach (var cat in DrugCategories.Values)
                    {
                        foreach (var d in cat.Drops) _drugItemSkins.Add(d.SkinId);
                        foreach (var r in cat.Recipes) _drugItemSkins.Add(r.ProducedItem.SkinId);
                    }
                }
                return _drugItemSkins.Contains(skin);
            }

            public DrugRecipe GetConsumableRecipe(Item item)
            {
                foreach (var cat in DrugCategories.Values)
                    foreach (var r in cat.Recipes)
                        if (r.IsConsumable && r.ProducedItem.SkinId == item.skin && r.ProducedItem.ShortName == item.info.shortname)
                            return r;
                return null;
            }

            public List<DrugRecipe> GetAllRecipes()
            {
                var all = new List<DrugRecipe>();
                foreach (var cat in DrugCategories.Values)
                    all.AddRange(cat.Recipes);
                return all;
            }

            public EffectProfile GetEffectProfile(Item item)
            {
                foreach (var kvp in DrugCategories)
                    foreach (var r in kvp.Value.Recipes)
                        if (r.IsConsumable && r.ProducedItem.SkinId == item.skin && r.ProducedItem.ShortName == item.info.shortname)
                            return kvp.Value.Effects;
                return new EffectProfile();
            }

            public void InvalidateCaches()
            {
                _consumableSkins = null;
                _drugItemSkins = null;
            }
        }
#endregion

#region Config Data Classes
        private class DrugCategory
        {
            [JsonProperty("Display Name")]
            public string DisplayName = "Drug";

            [JsonProperty("Growable Plant Prefab ID")]
            public uint GrowablePrefabId;

            [JsonProperty("Collectable Prefab ID")]
            public uint CollectiblePrefabId;

            [JsonProperty("Effect Profile")]
            public EffectProfile Effects = new EffectProfile();

            [JsonProperty("Strain System", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public StrainConfig Strains = new StrainConfig();

            [JsonProperty("Drop Configs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<DrugDropConfig> Drops = new List<DrugDropConfig>();

            [JsonProperty("Crafting Recipes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<DrugRecipe> Recipes = new List<DrugRecipe>();
        }

        private class StrainConfig
        {
            [JsonProperty("Enabled")] public bool Enabled = false;
            [JsonProperty("Y gene yield multiplier (per Y gene)")] public float YieldPerYGene = 0.15f;
            [JsonProperty("W gene yield penalty (per W gene)")] public float PenaltyPerWGene = 0.1f;
            [JsonProperty("X gene crossbreed bonus (per X gene)")] public float CrossbreedBonusPerXGene = 0.05f;

            [JsonProperty("Strains", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<StrainDefinition> Strains = new List<StrainDefinition>();

            public StrainDefinition ResolveStrain(int hGenes, int gGenes, int yGenes)
            {
                StrainDefinition best = null;
                foreach (var s in Strains)
                    if (hGenes >= s.MinH && gGenes >= s.MinG && yGenes >= s.MinY)
                        if (best == null || s.Priority > best.Priority)
                            best = s;
                return best;
            }

            public float GetYieldMultiplier(int yGenes, int wGenes, int xGenes)
            {
                float mult = 1f + (yGenes * YieldPerYGene) - (wGenes * PenaltyPerWGene) + (xGenes * CrossbreedBonusPerXGene);
                return Mathf.Max(0.1f, mult);
            }
        }

        private class StrainDefinition
        {
            [JsonProperty("Strain name")] public string Name = "Unknown";
            [JsonProperty("Min H genes")] public int MinH = 0;
            [JsonProperty("Min G genes")] public int MinG = 0;
            [JsonProperty("Min Y genes")] public int MinY = 0;
            [JsonProperty("Priority (higher wins)")] public int Priority = 0;
            [JsonProperty("Potency multiplier")] public float PotencyMultiplier = 1f;
            [JsonProperty("Color tag (for item name, e.g. #b7d092)")] public string ColorTag = "#b7d092";
        }

        private class EffectProfile
        {
            [JsonProperty("Enable blur")] public bool EnableBlur = true;
            [JsonProperty("Blur opacity")] public float BlurOpacity = 0.5f;
            [JsonProperty("Blur duration (s)")] public float BlurDuration = 2.5f;
            [JsonProperty("Enable color shift")] public bool EnableColorShift = true;
            [JsonProperty("Color shift interval (s)")] public float ColorShiftInterval = 0.25f;
            [JsonProperty("Color tint (RGBA, empty = random)")] public string ColorTint = "";
            [JsonProperty("Color opacity")] public float ColorOpacity = 0.3f;
            [JsonProperty("Enable screen shake")] public bool EnableShake = true;
            [JsonProperty("Shake count")] public int ShakeCount = 2;
            [JsonProperty("Enable smoke particles")] public bool EnableSmoke = true;
            [JsonProperty("Smoke puff count")] public int SmokePuffCount = 4;
            [JsonProperty("Enable breathing sound")] public bool EnableBreathing = true;
            [JsonProperty("Enable lick effect")] public bool EnableLick = true;
            [JsonProperty("Vomit chance (0-1)")] public float VomitChance = 0.09f;
            [JsonProperty("Enable heartbeat sound")] public bool EnableHeartbeat = false;
            [JsonProperty("Speed lines effect")] public bool EnableSpeedLines = false;
        }

        private class DrugDropConfig
        {
            [JsonProperty("Item short name")] public string ShortName;
            [JsonProperty("Item skin id")] public ulong SkinId;
            [JsonProperty("Custom item name")] public string DisplayName;
            [JsonProperty("Identifier")] public string Identifier = string.Empty;
            [JsonProperty("Drop chance (1 = 100%)")] public float DropChance;
            [JsonProperty("Drop amount min")] public int DropAmountMin = 1;
            [JsonProperty("Drop amount max")] public int DropAmountMax = 3;
            [JsonProperty("Biome mask")] public ushort BiomeMask;
            [JsonProperty("Min H genes for chance")] public int MinHGenesChance;
            [JsonProperty("Min H genes guaranteed")] public int MinHGenesGuaranteed;
            [JsonProperty("Min G genes for chance")] public int MinGGenesChance;
            [JsonProperty("Min G genes guaranteed")] public int MinGGenesGuaranteed;
            [JsonProperty("Disable collectable gathering")] public bool DisableCollGathering;
            [JsonProperty("Disable growable gathering")] public bool DisableGrowableGathering;

            [JsonIgnore]
            public ItemDefinition ItemDef => ItemManager.FindItemDefinition(ShortName);

            public Item CreateItem(int amount, string nameOverride = null)
            {
                var itm = ItemManager.Create(ItemDef, amount, SkinId);
                if (itm == null) return null;
                if (!string.IsNullOrEmpty(nameOverride))
                    itm.name = nameOverride;
                else if (!string.IsNullOrEmpty(DisplayName))
                    itm.name = DisplayName;
                return itm;
            }

            public bool IsValidBiome(ushort locationMask) => (BiomeMask & locationMask) > 0;

            public bool TryGather(ushort locationMask, bool isCollectable, int hGenes, int gGenes, int yGenes, int wGenes, int xGenes, StrainConfig strains, out Item result)
            {
                result = null;
                if (!IsValidBiome(locationMask)) return false;
                if (isCollectable && DisableCollGathering) return false;
                if (!isCollectable && DisableGrowableGathering) return false;

                float chance = DropChance;
                if (!isCollectable)
                {
                    if (hGenes < MinHGenesChance || gGenes < MinGGenesChance) return false;
                    if (hGenes >= MinHGenesGuaranteed && gGenes >= MinGGenesGuaranteed) chance = 1f;
                }

                if (Random.Range(0f, 1f) > chance) return false;

                int amount = Random.Range(DropAmountMin, DropAmountMax + 1);

                // Apply yield gene multiplier
                if (!isCollectable && strains != null && strains.Enabled)
                {
                    float yieldMult = strains.GetYieldMultiplier(yGenes, wGenes, xGenes);
                    amount = Mathf.Max(1, Mathf.RoundToInt(amount * yieldMult));
                }

                if (amount <= 0) return false;

                // Resolve strain name
                string nameOverride = null;
                if (!isCollectable && strains != null && strains.Enabled)
                {
                    var strain = strains.ResolveStrain(hGenes, gGenes, yGenes);
                    if (strain != null)
                        nameOverride = $"<color={strain.ColorTag}>{strain.Name}</color> {DisplayName}";
                }

                result = CreateItem(amount, nameOverride);
                return result != null;
            }
        }

        private class DrugRecipe
        {
            [JsonProperty("Identifier")] public string Identifier = string.Empty;
            [JsonProperty("Is consumable (joint-like)")] public bool IsConsumable;

            [JsonProperty("Ingredient Slots")]
            public Dictionary<int, RecipeIngredient> IngredientSlots = new Dictionary<int, RecipeIngredient>();

            [JsonProperty("Produced Item")]
            public ProducedItemConfig ProducedItem = new ProducedItemConfig();

            [JsonProperty("Boosts")]
            public BoostConfig Boosts = new BoostConfig();

            public bool TryCraft(MixingTable table, List<Item> overflow)
            {
                if (IngredientSlots.Count < 1) return false;

                var collect = Pool.Get<List<Item>>();
                var collectAmt = Pool.Get<List<int>>();
                int craftTimes = int.MaxValue;

                for (int slot = 0; slot < table.inventory.capacity; slot++)
                {
                    if (!IngredientSlots.TryGetValue(slot, out var ingredient)) continue;
                    var item = table.inventory.GetSlot(slot);
                    if (item == null || item.info.shortname != ingredient.ShortName ||
                        item.skin != ingredient.SkinId || item.amount < ingredient.Amount)
                    {
                        Pool.FreeUnmanaged(ref collect);
                        Pool.FreeUnmanaged(ref collectAmt);
                        return false;
                    }
                    craftTimes = Mathf.Min(craftTimes, Mathf.FloorToInt(item.amount / (float)ingredient.Amount));
                    collect.Add(item);
                    collectAmt.Add(ingredient.Amount);
                }

                for (int i = 0; i < collect.Count; i++)
                {
                    var item = collect[i];
                    int amount = collectAmt[i] * craftTimes;
                    if (item.amount == amount) { item.Remove(); }
                    else { item.amount -= amount; item.RemoveFromContainer(); overflow.Add(item); }
                }

                Pool.FreeUnmanaged(ref collect);
                Pool.FreeUnmanaged(ref collectAmt);
                ItemManager.DoRemoves();

                var result = ProducedItem.CreateItem(ProducedItem.Amount * craftTimes);
                if (result == null) return false;

                if (!result.MoveToContainer(table.inventory))
                    result.DropAndTossUpwards(table.transform.position + table.transform.up * 1.2f);

                return true;
            }
        }

        private class RecipeIngredient
        {
            [JsonProperty("Item short name")] public string ShortName;
            [JsonProperty("Item skin id")] public ulong SkinId;
            [JsonProperty("Amount")] public int Amount;
            [JsonIgnore] public ItemDefinition ItemDef => ItemManager.FindItemDefinition(ShortName);
        }

        private class ProducedItemConfig
        {
            [JsonProperty("Item short name")] public string ShortName;
            [JsonProperty("Item skin id")] public ulong SkinId;
            [JsonProperty("Custom item name")] public string DisplayName;
            [JsonProperty("Amount")] public int Amount = 1;

            [JsonIgnore] public ItemDefinition ItemDef => ItemManager.FindItemDefinition(ShortName);
            [JsonIgnore] public string UiName => string.IsNullOrEmpty(DisplayName) ? ItemDef?.displayName?.english ?? ShortName : DisplayName;

            public Item CreateItem(int amount)
            {
                var itm = ItemManager.Create(ItemDef, amount, SkinId);
                if (itm != null && !string.IsNullOrEmpty(DisplayName)) itm.name = DisplayName;
                return itm;
            }
        }

        private class BoostConfig
        {
            [JsonProperty("Wood boost %")] public float WoodPercentage;
            [JsonProperty("Wood duration (s)")] public float WoodDuration;
            [JsonProperty("Ore boost %")] public float OrePercentage;
            [JsonProperty("Ore duration (s)")] public float OreDuration;
            [JsonProperty("Scrap boost %")] public float ScrapPercentage;
            [JsonProperty("Scrap duration (s)")] public float ScrapDuration;
            [JsonProperty("Max Health %")] public float MaxHealthPercentage;
            [JsonProperty("Max Health duration (s)")] public float MaxHealthDuration;
            [JsonProperty("Healing per use")] public float HealingPerUse;
            [JsonProperty("Regen per use")] public float RegenerationPerUse;
            [JsonProperty("Poison per use")] public float PoisonPerUse;
            [JsonProperty("Radiation per use")] public float RadiationPoisonPerUse;
            [JsonProperty("Bleeding per use")] public float BleedingPerUse;
            [JsonProperty("Calories per use")] public float CaloriesPerUse;
            [JsonProperty("Hydration per use")] public float HydrationPerUse;
            [JsonProperty("Joint durability (s)")] public float JointDurability = 120f;
            [JsonProperty("Durability loss per hit (s)")] public float JointDurabilityLossPerHit = 10f;

            [JsonIgnore] public float DurabilityLossPerSecond => 100f / JointDurability;

            public void ApplyToPlayer(BasePlayer player)
            {
                var mods = Pool.Get<List<ModifierDefintion>>();
                if (ScrapDuration > 0 && ScrapPercentage > 0)
                    mods.Add(new ModifierDefintion { source = Modifier.ModifierSource.Tea, type = Modifier.ModifierType.Scrap_Yield, duration = ScrapDuration, value = ScrapPercentage });
                if (WoodDuration > 0 && WoodPercentage > 0)
                    mods.Add(new ModifierDefintion { source = Modifier.ModifierSource.Tea, type = Modifier.ModifierType.Wood_Yield, duration = WoodDuration, value = WoodPercentage });
                if (OreDuration > 0 && OrePercentage > 0)
                    mods.Add(new ModifierDefintion { source = Modifier.ModifierSource.Tea, type = Modifier.ModifierType.Ore_Yield, duration = OreDuration, value = OrePercentage });
                if (MaxHealthDuration > 0 && MaxHealthPercentage > 0)
                    mods.Add(new ModifierDefintion { source = Modifier.ModifierSource.Tea, type = Modifier.ModifierType.Max_Health, duration = MaxHealthDuration, value = MaxHealthPercentage });
                player.modifiers.Add(mods);
                Pool.FreeUnmanaged(ref mods);

                if (HealingPerUse > 0) player.Heal(HealingPerUse);
                if (RadiationPoisonPerUse != 0) player.metabolism.radiation_poison.Add(RadiationPoisonPerUse);
                if (PoisonPerUse != 0) player.metabolism.poison.Add(PoisonPerUse);
                if (BleedingPerUse != 0) player.metabolism.bleeding.Add(BleedingPerUse);
                if (CaloriesPerUse != 0) player.metabolism.calories.Add(CaloriesPerUse);
                if (HydrationPerUse != 0) player.metabolism.hydration.Add(HydrationPerUse);
                if (RegenerationPerUse != 0) player.metabolism.pending_health.Add(RegenerationPerUse);
                player.metabolism.isDirty = true;
                player.metabolism.SendChangesToClient();
            }
        }
#endregion

#region Data
        private class PlayerData
        {
            [JsonProperty]
            public Dictionary<ulong, bool> CraftingUiState = new Dictionary<ulong, bool>();

            public bool IsCraftingUiOpen(ulong playerId)
            {
                CraftingUiState.TryAdd(playerId, false);
                return CraftingUiState[playerId];
            }

            public bool ToggleCraftingUi(ulong playerId)
            {
                if (CraftingUiState.TryAdd(playerId, true)) return true;
                return CraftingUiState[playerId] = !CraftingUiState[playerId];
            }
        }
#endregion

#region Localization
        private static class Lang
        {
            public const string ShowRecipes = "ShowRecipes";
            public const string HideRecipes = "HideRecipes";
            public const string StartCrafting = "StartCrafting";
            public const string CraftHelp = "CraftHelp";
            public const string GiveSuccess = "GiveSuccess";
            public const string GiveNoPlayer = "GiveNoPlayer";
            public const string GiveInvalidPlayer = "GiveInvalidPlayer";
            public const string GiveInvalidType = "GiveInvalidType";
            public const string GiveNoDropConfig = "GiveNoDropConfig";
            public const string GiveNoRecipeConfig = "GiveNoRecipeConfig";
            public const string CraftFailed = "CraftFailed";
            public const string ItemCreateFailed = "ItemCreateFailed";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ShowRecipes] = "SHOW RECIPES",
                [Lang.HideRecipes] = "HIDE RECIPES",
                [Lang.StartCrafting] = "START CRAFTING",
                [Lang.CraftHelp] = "Use this button to craft drug recipes",
                [Lang.GiveSuccess] = "Gave {0} x{1} to {2}",
                [Lang.GiveNoPlayer] = "No player found with name or id '{0}'",
                [Lang.GiveInvalidPlayer] = "Invalid player. Please specify a target player explicitly",
                [Lang.GiveInvalidType] = "Invalid item type! Must be 'raw' or 'product'",
                [Lang.GiveNoDropConfig] = "No drop config found with identifier '{0}'",
                [Lang.GiveNoRecipeConfig] = "No recipe config found with identifier '{0}'",
                [Lang.CraftFailed] = "Crafting failed: could not create item {0}[{1}]",
                [Lang.ItemCreateFailed] = "Failed to create drug item — check config"
            }, this);
        }

        private string GetMessage(string key, string userId = null, params object[] args)
            => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigData();
            _playerData = Interface.Oxide.DataFileSystem.ReadObject<PlayerData>("NWGDrugs") ?? new PlayerData();

            permission.RegisterPermission(PERM_GATHER, this);
            permission.RegisterPermission(PERM_CRAFT, this);
            permission.RegisterPermission(PERM_GIVE, this);

            AddCovalenceCommand(CMD_CRAFT, nameof(CmdCraft));
            AddCovalenceCommand(CMD_TOGGLE_UI, nameof(CmdToggleUi));
            AddCovalenceCommand("nwgdrugs.give", nameof(CmdGive));

            timer.In(1f, () =>
            {
                if (ShouldDisableStackFix())
                {
                    Unsubscribe(nameof(CanStackItem));
                    Unsubscribe(nameof(CanCombineDroppedItem));
                    Unsubscribe(nameof(OnItemSplit));
                }
                if (!_config.ExtinguishOnUnequip)
                    Unsubscribe(nameof(OnActiveItemChanged));
            });
        }

        private void OnServerInitialized()
        {
            // Register custom items with Loottable plugin
            foreach (var cat in _config.DrugCategories.Values)
                foreach (var drop in cat.Drops)
                    Loottable?.Call("AddCustomItem", this, drop.ItemDef?.itemid ?? 0, drop.SkinId, drop.DisplayName);
        }

        private void Unload()
        {
            RemoveEffectsGlobal();
            foreach (var id in _openUis.ToArray())
            {
                var player = BasePlayer.FindByID(id);
                if (player != null) DestroyUi(player);
            }
            foreach (var joint in _jointTimers.Keys.ToList())
                ExtinguishJoint(null, joint);
            SavePlayerData();
        }

        private void OnServerSave() => SavePlayerData();

        private void LoadConfigData()
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
        private void SavePlayerData() => Interface.Oxide.DataFileSystem.WriteObject("NWGDrugs", _playerData);
#endregion

#region Helpers
        private bool ShouldDisableStackFix()
        {
            if (_config.DisableStackFix) return true;
            return StackModifier != null || Loottable != null || CustomSkinsStacksFix != null;
        }

        private bool AllowedToCraft(BasePlayer player)
            => !_config.RequireCraftPermission || permission.UserHasPermission(player.UserIDString, PERM_CRAFT);

        private bool AllowedToGather(BasePlayer player) => AllowedToGather(player.UserIDString);
        private bool AllowedToGather(string playerId)
            => !_config.RequireGatherPermission || permission.UserHasPermission(playerId, PERM_GATHER);

        private void SendNote(BasePlayer player, Item item, int amount = 0)
            => player.Command("note.inv", item.info.itemid, amount == 0 ? item.amount.ToString() : amount.ToString(), item.name);

        private bool IsDeployableNature(BaseEntity entity)
        {
            if (DeployableNature == null) return false;
            var b = DeployableNature.Call("STCanGainXP", null, entity) as Boolean?;
            return b == false;
        }

        private bool IsConsumableOrDrug(Item item) => item != null && _config.IsDrugItemSkin(item.skin);
        private bool IsConsumable(Item item) => item != null && _config.IsConsumableSkin(item.skin);

        private bool OnCooldown(BasePlayer player)
        {
            ulong id = player.userID;
            if (!_lastUsed.ContainsKey(id) || Time.time - _lastUsed[id] > JOINT_USE_COOLDOWN)
            {
                _lastUsed[id] = Time.time;
                return false;
            }
            return true;
        }
#endregion

#region Drug Usage
        private void UseDrug(BasePlayer player, Item item)
        {
            if (!item.HasFlag(global::Item.Flag.OnFire) || item.isBroken) return;
            var recipe = _config.GetConsumableRecipe(item);
            if (recipe == null) return;
            recipe.Boosts.ApplyToPlayer(player);
            var profile = _config.GetEffectProfile(item);
            RunEffects(player, profile);
            LoseCondition(item, recipe.Boosts.DurabilityLossPerSecond * recipe.Boosts.JointDurabilityLossPerHit);
        }

        private void IgniteDrug(BasePlayer player, Item item)
        {
            if (item.HasFlag(global::Item.Flag.OnFire)) return;
            var recipe = _config.GetConsumableRecipe(item);
            if (recipe == null) return;
            item.SetFlag(global::Item.Flag.OnFire, true);
            RunEffect("assets/prefabs/weapons/torch/effects/ignite.prefab", player);
            _jointTimers[item] = timer.Every(1, () => LoseCondition(item, recipe.Boosts.DurabilityLossPerSecond));
        }

        private void ExtinguishJoint(BasePlayer player, Item item)
        {
            if (!item.HasFlag(global::Item.Flag.OnFire)) return;
            item.SetFlag(global::Item.Flag.OnFire, false);
            item.MarkDirty();
            if (player != null) RunEffect("assets/prefabs/weapons/torch/effects/extinguish.prefab", player);
            if (_jointTimers.TryGetValue(item, out var t)) { t.Destroy(); _jointTimers.Remove(item); }
        }

        private void LoseCondition(Item item, float loss)
        {
            if (item.condition - loss < 1f)
            {
                if (_jointTimers.TryGetValue(item, out var t)) { t.Destroy(); _jointTimers.Remove(item); }
                var player = item.GetOwnerPlayer();
                item.Remove();
                if (player != null) RunEffect("assets/bundled/prefabs/fx/impacts/additive/fire.prefab", player);
            }
            else { item.condition -= loss; }
        }
#endregion

#region Gathering
        private void OnPlantGather(BasePlayer player, Vector3 pos, DrugCategory cat, bool isCollectable, int hGenes = 0, int gGenes = 0, int yGenes = 0, int wGenes = 0, int xGenes = 0)
        {
            ushort biome = (ushort)TerrainMeta.BiomeMap.GetBiomeMaxType(pos);
            foreach (var drop in cat.Drops)
            {
                if (drop.TryGather(biome, isCollectable, hGenes, gGenes, yGenes, wGenes, xGenes, cat.Strains, out var item))
                {
                    if (player.inventory.containerMain.IsFull() && player.inventory.containerBelt.IsFull())
                    {
                        item.Drop(pos, Vector3.up * 3f);
                        SendNote(player, item);
                        SendNote(player, item, -item.amount);
                    }
                    else
                    {
                        int amt = item.amount;
                        player.inventory.GiveItem(item);
                        SendNote(player, item, amt);
                    }
                }
            }
        }
#endregion

#region Hooks — Stack Fix
        private object CanStackItem(Item item, Item target)
        {
            if (ShouldDisableStackFix() || !IsConsumableOrDrug(item)) return null;
            if (item.info.itemid != target.info.itemid || item.skin != target.skin || item.name != target.name)
                return false;
            return null;
        }

        private Item OnItemSplit(Item item, int amount)
        {
            if (ShouldDisableStackFix() || !IsConsumableOrDrug(item)) return null;
            item.amount -= amount;
            var split = ItemManager.Create(item.info, amount, item.skin);
            split.name = item.name;
            split.MarkDirty();
            item.MarkDirty();
            return split;
        }

        private object CanCombineDroppedItem(WorldItem w1, WorldItem w2) => CanStackItem(w1.item, w2.item);
#endregion

#region Hooks — Input & Gathering
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            var item = player.GetActiveItem();
            if (!IsConsumable(item)) return;
            if (input.IsDown(BUTTON.FIRE_SECONDARY) && !OnCooldown(player))
            {
                if (item.HasFlag(global::Item.Flag.OnFire)) ExtinguishJoint(player, item);
                else IgniteDrug(player, item);
            }
            if (input.IsDown(BUTTON.FIRE_PRIMARY) && !OnCooldown(player))
                UseDrug(player, item);
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (IsConsumable(oldItem) && _config.ExtinguishOnUnequip)
                ExtinguishJoint(player, oldItem);
        }

        private void OnGrowableGather(GrowableEntity plant, BasePlayer player)
        {
            if (PlanterboxDefender != null && PlanterboxDefender.Call("CanLootGrowableEntity", plant, player) != null) return;
            if (plant.State != PlantProperties.State.Ripe || !AllowedToGather(player)) return;

            foreach (var cat in _config.DrugCategories.Values)
            {
                if (cat.GrowablePrefabId == plant.prefabID)
                {
                    int hGenes = plant.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.Hardiness);
                    int gGenes = plant.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.GrowthSpeed);
                    int yGenes = plant.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.Yield);
                    int wGenes = plant.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.WaterRequirement);
                    int xGenes = plant.Genes.GetGeneTypeCount(GrowableGenetics.GeneType.Empty);
                    OnPlantGather(player, plant.transform.position, cat, false, hGenes, gGenes, yGenes, wGenes, xGenes);
                }
            }
        }

        private void OnCollectiblePickup(CollectibleEntity entity, BasePlayer player)
        {
            if (player == null || entity.IsDestroyed || !AllowedToGather(player) || IsDeployableNature(entity)) return;
            foreach (var cat in _config.DrugCategories.Values)
                if (cat.CollectiblePrefabId == entity.prefabID)
                    OnPlantGather(player, entity.transform.position, cat, true);
        }

        // Support AutoFarm
        private void OnAutoFarmGather(string userID, StorageContainer container, Vector3 pos, bool isCollectable, int hGenes = 0, int gGenes = 0, int yGenes = 0, int wGenes = 0, int xGenes = 0)
        {
            if (!AllowedToGather(userID)) return;
            ushort biome = (ushort)TerrainMeta.BiomeMap.GetBiomeMaxType(pos);
            foreach (var cat in _config.DrugCategories.Values)
                foreach (var drop in cat.Drops)
                    if (drop.TryGather(biome, isCollectable, hGenes, gGenes, yGenes, wGenes, xGenes, cat.Strains, out var item))
                        if (container.inventory.IsFull() || !item.MoveToContainer(container.inventory))
                            item.Drop(pos, Vector3.up * 3f);
        }
#endregion

#region Hooks — Crafting UI & Misc
        private void OnLootEntity(BasePlayer player, MixingTable entity)
        {
            if (entity == null || !AllowedToCraft(player)) return;
            entity.OnlyAcceptValidIngredients = false;
            CreateToggleUi(player);
            if (_playerData.IsCraftingUiOpen(player.userID))
            {
                CreateRecipeUi(player);
                CreateCraftButtonUi(player);
            }
        }

        private void OnLootEntityEnd(BasePlayer player, MixingTable entity)
        {
            if (entity == null) return;
            entity.OnlyAcceptValidIngredients = true;
            DestroyUi(player);
        }

        private ItemContainer.CanAcceptResult? CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (container.entityOwner?.prefabID == 3846783416u && IsConsumable(item))
                return ItemContainer.CanAcceptResult.CannotAccept;
            return null;
        }

        private void OnPlayerDeath(BasePlayer p, HitInfo hitInfo) => OnPlayerDisconnected(p, null);
        private void OnPlayerDisconnected(BasePlayer p, string reason)
        {
            RemoveEffects(p);
            DestroyUi(p);
        }
#endregion

#region UI
        private void CreateToggleUi(BasePlayer player)
        {
            var container = new CuiElementContainer();
            string root = container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "430 608", OffsetMax = "572 633" }
            }, "Hud.Menu", LAYER_TOGGLE);

            bool isOpen = _playerData.IsCraftingUiOpen(player.userID);
            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Button = { Command = CMD_TOGGLE_UI, Color = isOpen ? OnColor : BtnColor },
                Text = { Align = TextAnchor.MiddleCenter, Text = GetMessage(isOpen ? Lang.HideRecipes : Lang.ShowRecipes, player.UserIDString), Color = TextColor, FontSize = 12 }
            }, root);

            _openUis.Add(player.userID);
            CuiHelper.AddUi(player, container);
        }

        private void CreateRecipeUi(BasePlayer player)
        {
            const int itemSize = 14;
            const int recipeH = 40;
            var allRecipes = _config.GetAllRecipes();
            var container = new CuiElementContainer();

            container.Add(new CuiElement
            {
                Components = {
                    new CuiImageComponent { Color = BgColor, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                    new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "192 302", OffsetMax = "572 582" },
                    new CuiScrollViewComponent
                    {
                        ContentTransform = new CuiRectTransform { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"0 -{Mathf.Max(0, (allRecipes.Count - 7) * recipeH)}", OffsetMax = "0 0" },
                        Vertical = true, Horizontal = false, ScrollSensitivity = 10f, Elasticity = 0.05f,
                        VerticalScrollbar = new CuiScrollbar { HandleColor = "1 1 1 1", Size = 8 }
                    }
                },
                Parent = "Hud.Menu", Name = LAYER_RECIPE
            });

            for (int i = 0; i < allRecipes.Count; i++)
            {
                var recipe = allRecipes[i];
                string panel = container.Add(new CuiPanel
                {
                    Image = { Color = "0.5 0.5 0.15 0" },
                    RectTransform = { AnchorMin = "0.02 1", AnchorMax = "0.96 1", OffsetMax = $"0 -{i * recipeH}", OffsetMin = $"0 -{(i + 1) * recipeH - 2}" }
                }, LAYER_RECIPE);

                // Product icon
                container.Add(new CuiElement { Components = {
                    new CuiImageComponent { SkinId = recipe.ProducedItem.SkinId, ItemId = recipe.ProducedItem.ItemDef?.itemid ?? 0 },
                    new CuiRectTransformComponent { AnchorMin = "0.05 0.5", AnchorMax = "0.05 0.5", OffsetMin = $"{-itemSize} {-itemSize}", OffsetMax = $"{itemSize} {itemSize}" }
                }, Parent = panel });

                // Product name
                string nameText = recipe.ProducedItem.Amount > 1 ? $"{recipe.ProducedItem.UiName} x{recipe.ProducedItem.Amount}" : recipe.ProducedItem.UiName;
                container.Add(new CuiLabel { RectTransform = { AnchorMin = "0.11 0", AnchorMax = "0.4 1" },
                    Text = { Text = nameText, Align = TextAnchor.MiddleLeft, Color = TextColor, FontSize = 12 }
                }, panel);

                // Ingredients
                for (int slot = 0; slot < 5; slot++)
                {
                    string anchor = $"{0.58f + slot * 0.095f} 0.5";
                    container.Add(new CuiPanel { Image = { Color = ItemTileColor },
                        RectTransform = { AnchorMin = anchor, AnchorMax = anchor, OffsetMin = $"{-itemSize} {-itemSize}", OffsetMax = $"{itemSize} {itemSize}" }
                    }, panel);

                    if (!recipe.IngredientSlots.TryGetValue(slot, out var ing)) continue;

                    container.Add(new CuiElement { Components = {
                        new CuiImageComponent { SkinId = ing.SkinId, ItemId = ing.ItemDef?.itemid ?? 0 },
                        new CuiRectTransformComponent { AnchorMin = anchor, AnchorMax = anchor, OffsetMin = $"{-itemSize} {-itemSize}", OffsetMax = $"{itemSize} {itemSize}" }
                    }, Parent = panel });

                    var amtText = ing.Amount < 1000 ? ing.Amount.ToString() : (ing.Amount / 1000f).ToString(CultureInfo.InvariantCulture) + "k";
                    container.Add(new CuiElement { Components = {
                        new CuiRectTransformComponent { AnchorMin = anchor, AnchorMax = anchor, OffsetMin = $"{-itemSize - 8} {-itemSize - 2}", OffsetMax = $"{itemSize - 2} 0" },
                        new CuiTextComponent { Text = amtText, Align = TextAnchor.MiddleRight, Color = TextColor, FontSize = 12 },
                        new CuiOutlineComponent { Color = "0 0 0 1", Distance = "0.8 0.8" }
                    }, Parent = panel });
                }
            }
            CuiHelper.AddUi(player, container);
        }

        private void CreateCraftButtonUi(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiElement { Components = {
                new CuiImageComponent { Color = BgColor, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = "193 40", OffsetMax = "420 98" }
            }, Parent = "Hud.Menu", Name = LAYER_CRAFT_BUTTON });

            container.Add(new CuiButton
            {
                RectTransform = { AnchorMin = "0.03 0.1", AnchorMax = "0.48 0.9" },
                Button = { Command = CMD_CRAFT, Color = OnColor },
                Text = { Align = TextAnchor.MiddleCenter, Text = GetMessage(Lang.StartCrafting, player.UserIDString), Color = TextColor, FontSize = 12 }
            }, LAYER_CRAFT_BUTTON);

            container.Add(new CuiLabel { RectTransform = { AnchorMin = "0.52 0", AnchorMax = "0.95 1" },
                Text = { Text = GetMessage(Lang.CraftHelp, player.UserIDString), Align = TextAnchor.MiddleLeft, Color = TextColor, FontSize = 12, Font = "robotocondensed-regular.ttf" }
            }, LAYER_CRAFT_BUTTON);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyUi(BasePlayer player, string layer = null)
        {
            if (layer != null) { CuiHelper.DestroyUi(player, layer); return; }
            if (_openUis.Remove(player.userID))
            {
                CuiHelper.DestroyUi(player, LAYER_RECIPE);
                CuiHelper.DestroyUi(player, LAYER_TOGGLE);
                CuiHelper.DestroyUi(player, LAYER_CRAFT_BUTTON);
            }
        }
#endregion

#region Commands
        private void CmdToggleUi(IPlayer iPlayer, string command, string[] args)
        {
            if (iPlayer.Object is not BasePlayer player) return;
            DestroyUi(player);
            if (_playerData.ToggleCraftingUi(player.userID))
            {
                CreateRecipeUi(player);
                CreateCraftButtonUi(player);
            }
            CreateToggleUi(player);
        }

        private void CmdCraft(IPlayer iPlayer, string command, string[] args)
        {
            var player = iPlayer.Object as BasePlayer;
            var table = player?.inventory.loot?.entitySource as MixingTable;
            if (iPlayer.IsServer || player == null || table == null || !AllowedToCraft(player)) return;

            var overflow = Pool.Get<List<Item>>();
            foreach (var recipe in _config.GetAllRecipes())
            {
                if (recipe.TryCraft(table, overflow))
                {
                    foreach (var item in overflow)
                        if (!item.MoveToContainer(player.inventory.containerMain))
                            item.DropAndTossUpwards(table.transform.position + table.transform.up * 1.2f);
                }
            }
            Pool.FreeUnmanaged(ref overflow);
        }

        private void CmdGive(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.HasPermission(PERM_GIVE)) return;
            if (args.Length < 2) { iPlayer.Reply("Usage: nwgdrugs.give <raw|product> <identifier> [amount] [player]"); return; }

            string itemType = args[0];
            string identifier = args[1];
            int amount = args.Length > 2 && int.TryParse(args[2], out var a) ? a : 1;
            string targetName = args.Length > 3 ? args[3] : null;

            var player = targetName != null ? BasePlayer.Find(targetName) : iPlayer.Object as BasePlayer;
            if (player == null)
            {
                iPlayer.Reply(targetName != null
                    ? GetMessage(Lang.GiveNoPlayer, null, targetName)
                    : GetMessage(Lang.GiveInvalidPlayer));
                return;
            }

            Item giveItem = null;
            if (itemType == "raw")
            {
                foreach (var cat in _config.DrugCategories.Values)
                {
                    var drop = cat.Drops.Find(d => d.Identifier?.Equals(identifier, StringComparison.OrdinalIgnoreCase) ?? false);
                    if (drop != null) { giveItem = drop.CreateItem(amount); break; }
                }
                if (giveItem == null) { iPlayer.Reply(GetMessage(Lang.GiveNoDropConfig, null, identifier)); return; }
            }
            else if (itemType == "product")
            {
                foreach (var cat in _config.DrugCategories.Values)
                {
                    var recipe = cat.Recipes.Find(r => r.Identifier?.Equals(identifier, StringComparison.OrdinalIgnoreCase) ?? false);
                    if (recipe != null) { giveItem = recipe.ProducedItem.CreateItem(amount); break; }
                }
                if (giveItem == null) { iPlayer.Reply(GetMessage(Lang.GiveNoRecipeConfig, null, identifier)); return; }
            }
            else { iPlayer.Reply(GetMessage(Lang.GiveInvalidType)); return; }

            player.GiveItem(giveItem);
            iPlayer.Reply(GetMessage(Lang.GiveSuccess, null, giveItem.name ?? giveItem.info.displayName.english, amount, player.displayName));
        }
#endregion

#region Effects
        private void RunEffects(BasePlayer player, EffectProfile fx)
        {
            float time = fx.BlurDuration;
            timer.In(time, () => CuiHelper.DestroyUi(player, LAYER_BLUR));
            timer.In(time, () => CuiHelper.DestroyUi(player, LAYER_COLOR));

            // Screen shake
            if (fx.EnableShake)
                timer.Repeat(time / Mathf.Max(1, fx.ShakeCount), fx.ShakeCount, () =>
                    RunEffect(Random.Range(0, 2) == 1 ? SHAKE_EFFECT : SHAKE2_EFFECT, player));

            // Sound effects
            if (fx.EnableBreathing) RunEffect(BREATHE_EFFECT, player);
            if (fx.EnableLick) RunEffect(LICK_EFFECT, player);
            if (fx.EnableHeartbeat) RunEffect("assets/bundled/prefabs/fx/player/beartrap_scream.prefab", player);

            // Smoke
            if (fx.EnableSmoke)
                timer.Repeat(0.25f, fx.SmokePuffCount, () => RunEffect(SMOKE_EFFECT, player, false));

            // Vomit
            if (fx.VomitChance > 0)
                timer.In(time / 2f, () => { if (Random.Range(0f, 1f) < fx.VomitChance) RunEffect(VOMIT_EFFECT, player); });

            // Visual effects
            if (fx.EnableBlur) CreateBlur(player, fx.BlurOpacity);
            if (fx.EnableColorShift)
                timer.Repeat(fx.ColorShiftInterval, Mathf.FloorToInt(time / fx.ColorShiftInterval),
                    () => CreateColor(player, fx.ColorTint, fx.ColorOpacity));
        }

        private void RunEffect(string effect, BasePlayer player, bool defaultPos = true, bool broadcast = true)
        {
            if (player == null) return;
            if (defaultPos) Effect.server.Run(effect, player, 0, Vector3.zero, Vector3.zero, null, broadcast);
            else Effect.server.Run(effect, player, 0, Vector3.up * 1.7f, new Vector3(1, 0, 0), null, broadcast);
        }

        private void CreateBlur(BasePlayer player, float opacity = 0.5f)
        {
            if (player == null) return;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { CursorEnabled = false,
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
                Image = { Color = $"0 0 0 {opacity}", Material = "assets/content/ui/uibackgroundblur.mat" }, FadeOut = 1f
            }, "Overlay", LAYER_BLUR);
            CuiHelper.DestroyUi(player, LAYER_BLUR);
            CuiHelper.AddUi(player, c);
        }

        private void CreateColor(BasePlayer player, string tint = "", float opacity = 0.3f)
        {
            if (player == null) return;
            string color = string.IsNullOrEmpty(tint) ? RandomColor(opacity) : tint;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMax = "0 0" },
                Image = { Color = color }, FadeOut = 0.5f
            }, LAYER_BLUR, LAYER_COLOR);
            CuiHelper.DestroyUi(player, LAYER_COLOR);
            CuiHelper.AddUi(player, c);
        }

        private void RemoveEffectsGlobal()
        {
            foreach (var player in BasePlayer.activePlayerList) RemoveEffects(player);
        }

        private void RemoveEffects(BasePlayer player) => CuiHelper.DestroyUi(player, LAYER_BLUR);

        private string RandomColor(float opacity = 0.3f)
        {
            var rng = new System.Random();
            return $"{rng.NextDouble()} {rng.NextDouble()} {rng.NextDouble()} {opacity}";
        }
#endregion
    }
}
