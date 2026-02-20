// NWGDrugBench - Dedicated Drug Crafting Station
// Provides a custom deployable crafting bench that integrates with NWGDrugs
// V 1.0.0 - Initial release

using Facepunch;
using Facepunch.Extend;
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

namespace Oxide.Plugins
{
    [Info("NWGDrugBench", "NWG Team", "1.0.0")]
    [Description("Custom deployable drug crafting station with dedicated UI. Requires NWGDrugs.")]
    public class NWGDrugBench : RustPlugin
    {
#region Constants
        private const string PERM_USE    = "nwgdrugbench.use";
        private const string PERM_DEPLOY = "nwgdrugbench.deploy";
        private const string PERM_ADMIN  = "nwgdrugbench.admin";

        private const string LAYER_MAIN    = "NWGDrugBench.ui.main";
        private const string LAYER_RECIPES = "NWGDrugBench.ui.recipes";
        private const string LAYER_DETAIL  = "NWGDrugBench.ui.detail";
        private const string LAYER_STATUS  = "NWGDrugBench.ui.status";
#endregion

#region UI Theme — Sage Green & Dark
        private const string BgColor      = "0.12 0.12 0.12 0.97";
        private const string PanelColor   = "0.18 0.18 0.18 0.95";
        private const string HeaderColor  = "0.08 0.08 0.08 1";
        private const string OnColor      = "0.718 0.816 0.573 1";
        private const string OffColor     = "0.851 0.325 0.31 1";
        private const string BtnColor     = "0.25 0.25 0.25 0.9";
        private const string BtnHover     = "0.35 0.35 0.35 0.9";
        private const string TextColor    = "0.867 0.867 0.867 1";
        private const string AccentColor  = "0.718 0.816 0.573 0.8";
        private const string MutedText    = "0.6 0.6 0.6 1";
        private const string ItemTile     = "0.22 0.22 0.21 1";
        private const string SuccessColor = "0.4 0.75 0.4 1";
        private const string ErrorColor   = "0.85 0.3 0.3 1";
#endregion

#region References
        [PluginReference] private Plugin NWGDrugs;
#endregion

#region Instance Fields
        private PluginConfig _config;
        private readonly HashSet<uint> _benchEntityIds = new HashSet<uint>();
        private readonly Dictionary<ulong, BenchSession> _activeSessions = new Dictionary<ulong, BenchSession>();
#endregion

#region Configuration
        private class PluginConfig
        {
            [JsonProperty("Bench skin ID")]
            public ulong BenchSkinId = 2950000001;

            [JsonProperty("Bench display name")]
            public string BenchDisplayName = "Drug Lab";

            [JsonProperty("Base entity prefab")]
            public string BasePrefab = "assets/prefabs/deployable/workbench1/workbench1.deployed.prefab";

            [JsonProperty("Crafting time multiplier")]
            public float CraftTimeMultiplier = 1f;

            [JsonProperty("Max queue size")]
            public int MaxQueueSize = 5;

            [JsonProperty("Allow placement indoors only")]
            public bool IndoorsOnly = false;

            [JsonProperty("Bench recipes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<BenchRecipe> Recipes = new List<BenchRecipe>
            {
                new BenchRecipe
                {
                    Identifier = "purified_weed",
                    DisplayName = "Purified Cannabis",
                    CraftTimeSec = 15f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "sticks", SkinId = 2661029427, DisplayName = "Low Quality Weed", Amount = 5 },
                        new BenchIngredient { ShortName = "lowgradefuel", SkinId = 0, DisplayName = "Low Grade Fuel", Amount = 2 }
                    },
                    Output = new BenchOutput { ShortName = "sticks", SkinId = 2660588149, DisplayName = "High Quality Weed", Amount = 2 }
                },
                new BenchRecipe
                {
                    Identifier = "cocaine_from_leaves",
                    DisplayName = "Refine Cocaine",
                    CraftTimeSec = 30f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "seed.hemp", SkinId = 2900000001, DisplayName = "Coca Leaves", Amount = 10 },
                        new BenchIngredient { ShortName = "lowgradefuel", SkinId = 0, DisplayName = "Low Grade Fuel", Amount = 5 },
                        new BenchIngredient { ShortName = "cloth", SkinId = 0, DisplayName = "Cloth", Amount = 3 }
                    },
                    Output = new BenchOutput { ShortName = "sticks", SkinId = 2900000002, DisplayName = "Cocaine Powder", Amount = 3 }
                },
                new BenchRecipe
                {
                    Identifier = "cook_meth",
                    DisplayName = "Cook Crystal Meth",
                    CraftTimeSec = 60f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "lowgradefuel", SkinId = 0, DisplayName = "Low Grade Fuel", Amount = 15 },
                        new BenchIngredient { ShortName = "sulfur", SkinId = 0, DisplayName = "Sulfur", Amount = 30 },
                        new BenchIngredient { ShortName = "cloth", SkinId = 0, DisplayName = "Cloth", Amount = 10 },
                        new BenchIngredient { ShortName = "metal.fragments", SkinId = 0, DisplayName = "Metal Fragments", Amount = 5 }
                    },
                    Output = new BenchOutput { ShortName = "horse.shoes.basic", SkinId = 2900000010, DisplayName = "Crystal Meth", Amount = 2 }
                },
                // --- Packaging recipes ---
                new BenchRecipe
                {
                    Identifier = "pack_weed_bag",
                    DisplayName = "Pack Weed → Bag",
                    CraftTimeSec = 10f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "sticks", SkinId = 2660588149, DisplayName = "High Quality Weed", Amount = 5 },
                        new BenchIngredient { ShortName = "cloth", SkinId = 0, DisplayName = "Cloth", Amount = 2 }
                    },
                    Output = new BenchOutput { ShortName = "smallwaterbottle", SkinId = 2950000020, DisplayName = "Bag of Weed", Amount = 1 }
                },
                new BenchRecipe
                {
                    Identifier = "pack_weed_brick",
                    DisplayName = "Pack Bags → Weed Brick",
                    CraftTimeSec = 20f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "smallwaterbottle", SkinId = 2950000020, DisplayName = "Bag of Weed", Amount = 5 },
                        new BenchIngredient { ShortName = "ducttape", SkinId = 0, DisplayName = "Duct Tape", Amount = 1 }
                    },
                    Output = new BenchOutput { ShortName = "box.wooden.large", SkinId = 2950000021, DisplayName = "Brick of Weed", Amount = 1 }
                },
                new BenchRecipe
                {
                    Identifier = "pack_coke_brick",
                    DisplayName = "Pack Cocaine → Coke Brick",
                    CraftTimeSec = 25f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "sticks", SkinId = 2900000002, DisplayName = "Cocaine Powder", Amount = 10 },
                        new BenchIngredient { ShortName = "ducttape", SkinId = 0, DisplayName = "Duct Tape", Amount = 2 }
                    },
                    Output = new BenchOutput { ShortName = "box.wooden.large", SkinId = 2950000030, DisplayName = "Coke Brick", Amount = 1 }
                },
                new BenchRecipe
                {
                    Identifier = "pack_meth_brick",
                    DisplayName = "Pack Meth → Meth Brick",
                    CraftTimeSec = 25f,
                    Ingredients = new List<BenchIngredient>
                    {
                        new BenchIngredient { ShortName = "horse.shoes.basic", SkinId = 2900000010, DisplayName = "Crystal Meth", Amount = 10 },
                        new BenchIngredient { ShortName = "ducttape", SkinId = 0, DisplayName = "Duct Tape", Amount = 2 }
                    },
                    Output = new BenchOutput { ShortName = "box.wooden.large", SkinId = 2950000040, DisplayName = "Meth Brick", Amount = 1 }
                }
            };
        }

        private class BenchRecipe
        {
            [JsonProperty("Identifier")] public string Identifier;
            [JsonProperty("Display name")] public string DisplayName;
            [JsonProperty("Craft time (seconds)")] public float CraftTimeSec = 15f;
            [JsonProperty("Ingredients")] public List<BenchIngredient> Ingredients = new List<BenchIngredient>();
            [JsonProperty("Output")] public BenchOutput Output = new BenchOutput();
        }

        private class BenchIngredient
        {
            [JsonProperty("Short name")] public string ShortName;
            [JsonProperty("Skin ID")] public ulong SkinId;
            [JsonProperty("Display name")] public string DisplayName;
            [JsonProperty("Amount")] public int Amount;
        }

        private class BenchOutput
        {
            [JsonProperty("Short name")] public string ShortName;
            [JsonProperty("Skin ID")] public ulong SkinId;
            [JsonProperty("Display name")] public string DisplayName;
            [JsonProperty("Amount")] public int Amount = 1;

            public Item CreateItem()
            {
                var def = ItemManager.FindItemDefinition(ShortName);
                if (def == null) return null;
                var item = ItemManager.Create(def, Amount, SkinId);
                if (item != null && !string.IsNullOrEmpty(DisplayName)) item.name = DisplayName;
                return item;
            }
        }

        private class BenchSession
        {
            public BaseEntity BenchEntity;
            public int SelectedRecipeIndex = -1;
            public float CraftStartTime;
            public float CraftEndTime;
            public Timer CraftTimer;
            public bool IsCrafting => CraftTimer != null;
        }
#endregion

#region Localization
        private static class Lang
        {
            public const string Title         = "Title";
            public const string SelectRecipe  = "SelectRecipe";
            public const string StartCraft    = "StartCraft";
            public const string CancelCraft   = "CancelCraft";
            public const string Crafting      = "Crafting";
            public const string CraftComplete = "CraftComplete";
            public const string NoPermission  = "NoPermission";
            public const string NeedIngredients = "NeedIngredients";
            public const string Ingredients   = "Ingredients";
            public const string Produces      = "Produces";
            public const string CraftTime     = "CraftTime";
            public const string Close         = "Close";
            public const string BenchDeployed = "BenchDeployed";
            public const string GiveBench     = "GiveBench";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Title]           = "⚗ DRUG LAB",
                [Lang.SelectRecipe]    = "Select a recipe to craft",
                [Lang.StartCraft]      = "START CRAFTING",
                [Lang.CancelCraft]     = "CANCEL",
                [Lang.Crafting]        = "Crafting... {0}s remaining",
                [Lang.CraftComplete]   = "<color=#b7d092>Crafting complete!</color> Produced {0} x{1}",
                [Lang.NoPermission]    = "You don't have permission to use this bench.",
                [Lang.NeedIngredients] = "Missing ingredients!",
                [Lang.Ingredients]     = "INGREDIENTS",
                [Lang.Produces]        = "PRODUCES",
                [Lang.CraftTime]       = "Craft Time: {0}s",
                [Lang.Close]           = "✕",
                [Lang.BenchDeployed]   = "<color=#b7d092>Drug Lab</color> deployed!",
                [Lang.GiveBench]       = "Gave Drug Lab to {0}"
            }, this);
        }

        private string Msg(string key, string userId = null, params object[] args)
            => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigData();
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_DEPLOY, this);
            permission.RegisterPermission(PERM_ADMIN, this);

            AddCovalenceCommand("nwgdrugbench.give", nameof(CmdGiveBench));
        }

        private void Unload()
        {
            foreach (var session in _activeSessions.Values)
            {
                session.CraftTimer?.Destroy();
                var player = BasePlayer.FindByID(session.BenchEntity?.OwnerID ?? 0);
                if (player != null) CuiHelper.DestroyUi(player, LAYER_MAIN);
            }
            _activeSessions.Clear();

            // Destroy all UIs
            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, LAYER_MAIN);
        }

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
#endregion

#region Hooks
        // Detect interaction with a drug bench
        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            var entity = go?.ToBaseEntity();
            if (entity == null) return;
            var player = plan?.GetOwnerPlayer();
            if (player == null) return;

            if (entity.skinID == _config.BenchSkinId)
            {
                _benchEntityIds.Add((uint)entity.net.ID.Value);
                player.ChatMessage(Msg(Lang.BenchDeployed, player.UserIDString));
            }
        }

        private void OnServerInitialized()
        {
            // Find existing benches
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BaseEntity be && be.skinID == _config.BenchSkinId)
                    _benchEntityIds.Add((uint)be.net.ID.Value);
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || container.skinID != _config.BenchSkinId) return null;
            if (!permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                player.ChatMessage(Msg(Lang.NoPermission, player.UserIDString));
                return false;
            }

            // Open our custom UI instead of native loot
            timer.Once(0.1f, () => OpenBenchUI(player, container));
            return false; // block native looting
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is BaseEntity be)
                _benchEntityIds.Remove((uint)be.net.ID.Value);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            CloseBenchUI(player);
        }
#endregion

#region Bench UI
        private void OpenBenchUI(BasePlayer player, BaseEntity bench)
        {
            if (_activeSessions.ContainsKey(player.userID))
                CloseBenchUI(player);

            _activeSessions[player.userID] = new BenchSession { BenchEntity = bench };
            DrawMainPanel(player);
        }

        private void CloseBenchUI(BasePlayer player)
        {
            if (_activeSessions.TryGetValue(player.userID, out var session))
            {
                session.CraftTimer?.Destroy();
                _activeSessions.Remove(player.userID);
            }
            CuiHelper.DestroyUi(player, LAYER_MAIN);
        }

        private void DrawMainPanel(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, LAYER_MAIN);
            var c = new CuiElementContainer();

            // Full-screen overlay
            c.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.6" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", LAYER_MAIN);

            // Main panel — centered
            string mainPanel = c.Add(new CuiPanel
            {
                Image = { Color = BgColor, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.2 0.15", AnchorMax = "0.8 0.85" }
            }, LAYER_MAIN);

            // Header
            string header = c.Add(new CuiPanel
            {
                Image = { Color = HeaderColor },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, mainPanel);

            c.Add(new CuiLabel
            {
                Text = { Text = Msg(Lang.Title, player.UserIDString), FontSize = 22, Align = TextAnchor.MiddleCenter, Color = OnColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, header);

            // Close button
            c.Add(new CuiButton
            {
                Button = { Command = "nwgdrugbench.close", Color = OffColor },
                Text = { Text = Msg(Lang.Close, player.UserIDString), FontSize = 18, Align = TextAnchor.MiddleCenter, Color = TextColor },
                RectTransform = { AnchorMin = "0.93 0.2", AnchorMax = "0.99 0.8" }
            }, header);

            // Recipe list (left panel)
            string leftPanel = c.Add(new CuiPanel
            {
                Image = { Color = PanelColor },
                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.38 0.88" }
            }, mainPanel);

            c.Add(new CuiLabel
            {
                Text = { Text = "RECIPES", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = AccentColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, leftPanel);

            // Recipe buttons
            float y = 0.88f;
            for (int i = 0; i < _config.Recipes.Count; i++)
            {
                var recipe = _config.Recipes[i];
                bool isSelected = _activeSessions.TryGetValue(player.userID, out var session) && session.SelectedRecipeIndex == i;

                c.Add(new CuiButton
                {
                    Button = { Command = $"nwgdrugbench.select {i}", Color = isSelected ? OnColor : BtnColor },
                    Text = { Text = recipe.DisplayName, FontSize = 13, Align = TextAnchor.MiddleCenter, Color = isSelected ? HeaderColor : TextColor, Font = "robotocondensed-regular.ttf" },
                    RectTransform = { AnchorMin = $"0.05 {y - 0.08f}", AnchorMax = $"0.95 {y}" }
                }, leftPanel);
                y -= 0.1f;
            }

            // Detail panel (right side — shows selected recipe)
            string rightPanel = c.Add(new CuiPanel
            {
                Image = { Color = PanelColor },
                RectTransform = { AnchorMin = "0.4 0.05", AnchorMax = "0.98 0.88" }
            }, mainPanel);

            if (_activeSessions.TryGetValue(player.userID, out var sess) && sess.SelectedRecipeIndex >= 0 && sess.SelectedRecipeIndex < _config.Recipes.Count)
                DrawRecipeDetail(c, rightPanel, player, _config.Recipes[sess.SelectedRecipeIndex], sess);
            else
            {
                c.Add(new CuiLabel
                {
                    Text = { Text = Msg(Lang.SelectRecipe, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = MutedText },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
                }, rightPanel);
            }

            CuiHelper.AddUi(player, c);
        }

        private void DrawRecipeDetail(CuiElementContainer c, string parent, BasePlayer player, BenchRecipe recipe, BenchSession session)
        {
            // Recipe title
            c.Add(new CuiLabel
            {
                Text = { Text = recipe.DisplayName, FontSize = 18, Align = TextAnchor.MiddleCenter, Color = OnColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.88", AnchorMax = "1 0.98" }
            }, parent);

            // Craft time
            float craftTime = recipe.CraftTimeSec * _config.CraftTimeMultiplier;
            c.Add(new CuiLabel
            {
                Text = { Text = Msg(Lang.CraftTime, player.UserIDString, craftTime.ToString("F0")), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = MutedText },
                RectTransform = { AnchorMin = "0 0.82", AnchorMax = "1 0.88" }
            }, parent);

            // Ingredients header
            c.Add(new CuiLabel
            {
                Text = { Text = Msg(Lang.Ingredients, player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = AccentColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.05 0.72", AnchorMax = "0.95 0.8" }
            }, parent);

            // Ingredient list
            float iy = 0.7f;
            foreach (var ing in recipe.Ingredients)
            {
                string ingPanel = c.Add(new CuiPanel
                {
                    Image = { Color = ItemTile },
                    RectTransform = { AnchorMin = $"0.05 {iy - 0.07f}", AnchorMax = $"0.95 {iy}" }
                }, parent);

                // Icon
                var def = ItemManager.FindItemDefinition(ing.ShortName);
                if (def != null)
                {
                    c.Add(new CuiElement { Components = {
                        new CuiImageComponent { SkinId = ing.SkinId, ItemId = def.itemid },
                        new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.12 0.9" }
                    }, Parent = ingPanel });
                }

                // Name
                string displayName = !string.IsNullOrEmpty(ing.DisplayName) ? ing.DisplayName : def?.displayName?.english ?? ing.ShortName;
                c.Add(new CuiLabel
                {
                    Text = { Text = displayName, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = TextColor },
                    RectTransform = { AnchorMin = "0.15 0", AnchorMax = "0.75 1" }
                }, ingPanel);

                // Amount
                c.Add(new CuiLabel
                {
                    Text = { Text = $"x{ing.Amount}", FontSize = 13, Align = TextAnchor.MiddleRight, Color = AccentColor, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.75 0", AnchorMax = "0.95 1" }
                }, ingPanel);

                iy -= 0.09f;
            }

            // Output section
            c.Add(new CuiLabel
            {
                Text = { Text = Msg(Lang.Produces, player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = AccentColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = $"0.05 {iy - 0.08f}", AnchorMax = $"0.95 {iy}" }
            }, parent);
            iy -= 0.08f;

            string outPanel = c.Add(new CuiPanel
            {
                Image = { Color = ItemTile },
                RectTransform = { AnchorMin = $"0.05 {iy - 0.07f}", AnchorMax = $"0.95 {iy}" }
            }, parent);

            var outDef = ItemManager.FindItemDefinition(recipe.Output.ShortName);
            if (outDef != null)
            {
                c.Add(new CuiElement { Components = {
                    new CuiImageComponent { SkinId = recipe.Output.SkinId, ItemId = outDef.itemid },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.12 0.9" }
                }, Parent = outPanel });
            }

            string outName = !string.IsNullOrEmpty(recipe.Output.DisplayName) ? recipe.Output.DisplayName : outDef?.displayName?.english ?? recipe.Output.ShortName;
            c.Add(new CuiLabel
            {
                Text = { Text = outName, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = TextColor },
                RectTransform = { AnchorMin = "0.15 0", AnchorMax = "0.75 1" }
            }, outPanel);

            c.Add(new CuiLabel
            {
                Text = { Text = $"x{recipe.Output.Amount}", FontSize = 13, Align = TextAnchor.MiddleRight, Color = SuccessColor, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.75 0", AnchorMax = "0.95 1" }
            }, outPanel);

            // Craft / Cancel button
            if (session.IsCrafting)
            {
                float remaining = Mathf.Max(0, session.CraftEndTime - UnityEngine.Time.time);
                c.Add(new CuiButton
                {
                    Button = { Command = "nwgdrugbench.cancel", Color = OffColor },
                    Text = { Text = Msg(Lang.Crafting, player.UserIDString, remaining.ToString("F0")), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = TextColor },
                    RectTransform = { AnchorMin = "0.2 0.03", AnchorMax = "0.8 0.12" }
                }, parent);

                // Progress bar background
                string progBg = c.Add(new CuiPanel
                {
                    Image = { Color = ItemTile },
                    RectTransform = { AnchorMin = "0.05 0.13", AnchorMax = "0.95 0.16" }
                }, parent);

                float totalTime = craftTime;
                float elapsed = totalTime - remaining;
                float progress = Mathf.Clamp01(elapsed / totalTime);
                c.Add(new CuiPanel
                {
                    Image = { Color = OnColor },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = $"{progress} 1" }
                }, progBg);
            }
            else
            {
                c.Add(new CuiButton
                {
                    Button = { Command = "nwgdrugbench.craft", Color = OnColor },
                    Text = { Text = Msg(Lang.StartCraft, player.UserIDString), FontSize = 15, Align = TextAnchor.MiddleCenter, Color = HeaderColor, Font = "robotocondensed-bold.ttf" },
                    RectTransform = { AnchorMin = "0.2 0.03", AnchorMax = "0.8 0.14" }
                }, parent);
            }
        }
#endregion

#region Commands
        [ConsoleCommand("nwgdrugbench.close")]
        private void CmdClose(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;
            CloseBenchUI(player);
        }

        [ConsoleCommand("nwgdrugbench.select")]
        private void CmdSelect(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !_activeSessions.TryGetValue(player.userID, out var session)) return;
            if (session.IsCrafting) return; // can't switch during craft

            int idx = arg.GetInt(0, -1);
            if (idx < 0 || idx >= _config.Recipes.Count) return;
            session.SelectedRecipeIndex = idx;
            DrawMainPanel(player);
        }

        [ConsoleCommand("nwgdrugbench.craft")]
        private void CmdCraft(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !_activeSessions.TryGetValue(player.userID, out var session)) return;
            if (session.IsCrafting || session.SelectedRecipeIndex < 0) return;

            var recipe = _config.Recipes[session.SelectedRecipeIndex];

            // Check if player has all ingredients
            if (!HasIngredients(player, recipe))
            {
                player.ChatMessage(Msg(Lang.NeedIngredients, player.UserIDString));
                return;
            }

            // Consume ingredients
            ConsumeIngredients(player, recipe);

            // Start crafting timer
            float craftTime = recipe.CraftTimeSec * _config.CraftTimeMultiplier;
            session.CraftStartTime = UnityEngine.Time.time;
            session.CraftEndTime = UnityEngine.Time.time + craftTime;

            // Progress UI update timer
            session.CraftTimer = timer.Repeat(1f, Mathf.CeilToInt(craftTime) + 1, () =>
            {
                if (!_activeSessions.ContainsKey(player.userID)) return;

                if (UnityEngine.Time.time >= session.CraftEndTime)
                {
                    // Crafting complete
                    session.CraftTimer?.Destroy();
                    session.CraftTimer = null;

                    var output = recipe.Output.CreateItem();
                    if (output != null)
                    {
                        player.GiveItem(output);
                        player.ChatMessage(Msg(Lang.CraftComplete, player.UserIDString, recipe.Output.DisplayName, recipe.Output.Amount));
                    }
                    DrawMainPanel(player);
                    return;
                }

                DrawMainPanel(player); // refresh UI with progress
            });

            DrawMainPanel(player);
        }

        [ConsoleCommand("nwgdrugbench.cancel")]
        private void CmdCancel(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null || !_activeSessions.TryGetValue(player.userID, out var session)) return;
            if (!session.IsCrafting) return;

            session.CraftTimer?.Destroy();
            session.CraftTimer = null;

            // Refund ingredients
            var recipe = _config.Recipes[session.SelectedRecipeIndex];
            RefundIngredients(player, recipe);
            DrawMainPanel(player);
        }

        private void CmdGiveBench(IPlayer iPlayer, string command, string[] args)
        {
            if (!iPlayer.HasPermission(PERM_ADMIN))
            {
                iPlayer.Reply("No permission");
                return;
            }

            BasePlayer target;
            if (args.Length > 0)
            {
                target = BasePlayer.Find(args[0]);
                if (target == null) { iPlayer.Reply($"Player '{args[0]}' not found."); return; }
            }
            else
            {
                target = iPlayer.Object as BasePlayer;
                if (target == null) { iPlayer.Reply("Must specify a player from console."); return; }
            }

            var benchDef = ItemManager.FindItemDefinition("workbench1");
            if (benchDef == null) { iPlayer.Reply("Could not find workbench1 definition."); return; }

            var item = ItemManager.Create(benchDef, 1, _config.BenchSkinId);
            if (item == null) { iPlayer.Reply("Failed to create bench item."); return; }
            item.name = _config.BenchDisplayName;

            target.GiveItem(item);
            iPlayer.Reply(Msg(Lang.GiveBench, null, target.displayName));
        }
#endregion

#region Inventory Helpers
        private bool HasIngredients(BasePlayer player, BenchRecipe recipe)
        {
            foreach (var ing in recipe.Ingredients)
            {
                int have = CountItems(player, ing.ShortName, ing.SkinId);
                if (have < ing.Amount) return false;
            }
            return true;
        }

        private int CountItems(BasePlayer player, string shortName, ulong skinId)
        {
            int total = 0;
            foreach (var item in player.inventory.containerMain.itemList.Concat(player.inventory.containerBelt.itemList))
            {
                if (item.info.shortname == shortName && item.skin == skinId)
                    total += item.amount;
            }
            return total;
        }

        private void ConsumeIngredients(BasePlayer player, BenchRecipe recipe)
        {
            foreach (var ing in recipe.Ingredients)
            {
                int remaining = ing.Amount;
                var items = player.inventory.containerMain.itemList.Concat(player.inventory.containerBelt.itemList)
                    .Where(i => i.info.shortname == ing.ShortName && i.skin == ing.SkinId)
                    .ToList();

                foreach (var item in items)
                {
                    if (remaining <= 0) break;
                    if (item.amount <= remaining)
                    {
                        remaining -= item.amount;
                        item.Remove();
                    }
                    else
                    {
                        item.amount -= remaining;
                        item.MarkDirty();
                        remaining = 0;
                    }
                }
                ItemManager.DoRemoves();
            }
        }

        private void RefundIngredients(BasePlayer player, BenchRecipe recipe)
        {
            foreach (var ing in recipe.Ingredients)
            {
                var def = ItemManager.FindItemDefinition(ing.ShortName);
                if (def == null) continue;
                var item = ItemManager.Create(def, ing.Amount, ing.SkinId);
                if (item == null) continue;
                if (!string.IsNullOrEmpty(ing.DisplayName)) item.name = ing.DisplayName;
                player.GiveItem(item);
            }
        }
#endregion
    }
}
