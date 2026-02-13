using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGMarket", "NWG Team", "4.0.0")]
    [Description("In-game Shop and Economy for NWG with ImageLibrary integration.")]
    public class NWGMarket : RustPlugin
    {
        #region References
        [PluginReference] private Plugin ImageLibrary;
        #endregion

        #region Config
        private class PluginConfig
        {
            public double StartingBalance = 2500.0;
            public string CurrencySymbol = "$";
            public string ShopTitle = "NWG STORE";
            public string ShopCommand = "shop";
            public string BalanceCommand = "balance";
            
            public List<ShopCategory> Categories = new List<ShopCategory>();
        }

        private class ShopCategory
        {
            public string Name;
            public List<ShopItem> Items = new List<ShopItem>();
        }

        private class ShopItem
        {
            public string ShortName;
            public string DisplayName; 
            public ulong SkinId;
            public int Amount = 1;
            public double BuyPrice; 
            public double SellPrice; 
            public string Command; 
        }

        private PluginConfig _config;
        #endregion

        #region Data
        private class StoredData
        {
            public Dictionary<ulong, double> Balances = new Dictionary<ulong, double>();
        }

        private StoredData _data;
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            LoadData();
            
            cmd.AddChatCommand(_config.ShopCommand, this, nameof(CmdShop));
            cmd.AddChatCommand(_config.BalanceCommand, this, nameof(CmdBalance));
        }

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_Market") ?? new StoredData();
        }

        private void LoadConfigVariables()
        {
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null || _config.Categories.Count == 0)
            {
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Generating NWG Market Defaults with requested pricing.");
            _config = new PluginConfig();

            // Resources
            var resCat = new ShopCategory { Name = "Resources" };
            resCat.Items.Add(new ShopItem { ShortName = "wood", Amount = 1000, BuyPrice = 100, SellPrice = 20 });
            resCat.Items.Add(new ShopItem { ShortName = "stones", Amount = 1000, BuyPrice = 200, SellPrice = 40 });
            resCat.Items.Add(new ShopItem { ShortName = "metal.fragments", Amount = 1000, BuyPrice = 400, SellPrice = 80 });
            resCat.Items.Add(new ShopItem { ShortName = "metal.refined", Amount = 100, BuyPrice = 500, SellPrice = 100 });
            resCat.Items.Add(new ShopItem { ShortName = "sulfur", Amount = 1000, BuyPrice = 500, SellPrice = 100 });
            resCat.Items.Add(new ShopItem { ShortName = "cloth", Amount = 100, BuyPrice = 400, SellPrice = 80 });
            resCat.Items.Add(new ShopItem { ShortName = "leather", Amount = 100, BuyPrice = 200, SellPrice = 40 });
            resCat.Items.Add(new ShopItem { ShortName = "charcoal", Amount = 1000, BuyPrice = 500, SellPrice = 100 });
            _config.Categories.Add(resCat);
            
            // Weapons
            var weapCat = new ShopCategory { Name = "Weapons" };
            weapCat.Items.Add(new ShopItem { ShortName = "rifle.ak", Amount = 1, BuyPrice = 3000, SellPrice = 600 });
            weapCat.Items.Add(new ShopItem { ShortName = "rifle.semi", Amount = 1, BuyPrice = 1500, SellPrice = 300 });
            weapCat.Items.Add(new ShopItem { ShortName = "smg.thompson", Amount = 1, BuyPrice = 1200, SellPrice = 250 });
            weapCat.Items.Add(new ShopItem { ShortName = "pistol.semi", Amount = 1, BuyPrice = 950, SellPrice = 200 });
            weapCat.Items.Add(new ShopItem { ShortName = "shotgun.pump", Amount = 1, BuyPrice = 1000, SellPrice = 200 });
            weapCat.Items.Add(new ShopItem { ShortName = "bow.hunting", Amount = 1, BuyPrice = 120, SellPrice = 20 });
            weapCat.Items.Add(new ShopItem { ShortName = "crossbow", Amount = 1, BuyPrice = 300, SellPrice = 60 });
            weapCat.Items.Add(new ShopItem { ShortName = "pistol.revolver", Amount = 1, BuyPrice = 600, SellPrice = 120 });
            _config.Categories.Add(weapCat);

            // Ammo
            var ammoCat = new ShopCategory { Name = "Ammunition" };
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.rifle", Amount = 128, BuyPrice = 750, SellPrice = 150 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.pistol", Amount = 128, BuyPrice = 350, SellPrice = 70 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.shotgun", Amount = 64, BuyPrice = 200, SellPrice = 40 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.rifle.explosive", Amount = 64, BuyPrice = 1250, SellPrice = 250 });
            ammoCat.Items.Add(new ShopItem { ShortName = "arrow.wooden", Amount = 64, BuyPrice = 100, SellPrice = 20 });
            _config.Categories.Add(ammoCat);

            // Medicine
            var medCat = new ShopCategory { Name = "Medicine" };
            medCat.Items.Add(new ShopItem { ShortName = "syringe.medical", Amount = 1, BuyPrice = 100, SellPrice = 20 });
            medCat.Items.Add(new ShopItem { ShortName = "largemedkit", Amount = 1, BuyPrice = 200, SellPrice = 40 });
            _config.Categories.Add(medCat);

            // Tools
            var toolCat = new ShopCategory { Name = "Tools" };
            toolCat.Items.Add(new ShopItem { ShortName = "mining.quarry", Amount = 1, BuyPrice = 12500, SellPrice = 2000, DisplayName = "Mining Quarry" });
            toolCat.Items.Add(new ShopItem { ShortName = "mining.pumpjack", Amount = 1, BuyPrice = 17500, SellPrice = 2500, DisplayName = "Pump Jack" });
            toolCat.Items.Add(new ShopItem { ShortName = "survey.charge", Amount = 1, BuyPrice = 1000, SellPrice = 100, DisplayName = "Survey Charge" });
            toolCat.Items.Add(new ShopItem { ShortName = "jackhammer", Amount = 1, BuyPrice = 1500, SellPrice = 300 });
            toolCat.Items.Add(new ShopItem { ShortName = "chainsaw", Amount = 1, BuyPrice = 1200, SellPrice = 240 });
            _config.Categories.Add(toolCat);

            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList) CuiHelper.DestroyUi(player, "NWG_Market_UI");
            SaveData();
        }

        private void OnServerSave() => SaveData();
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Market", _data);
        #endregion

        #region Economy Hook API
        [HookMethod("Balance")]
        public double Balance(ulong playerId) => _data.Balances.TryGetValue(playerId, out var bal) ? bal : _config.StartingBalance;

        [HookMethod("Withdraw")]
        public bool Withdraw(ulong playerId, double amount)
        {
            var bal = Balance(playerId);
            if (bal < amount) return false;
            _data.Balances[playerId] = bal - amount;
            return true;
        }

        [HookMethod("Deposit")]
        public bool Deposit(ulong playerId, double amount)
        {
            if (amount < 0) return false;
            _data.Balances[playerId] = Balance(playerId) + amount;
            return true;
        }
        #endregion

        #region Commands
        private void CmdShop(BasePlayer player) => ShowCategory(player, 0);
        
        private void CmdBalance(BasePlayer player) => SendReply(player, $"Balance: {_config.CurrencySymbol}{Balance(player.userID):N2}");

        [ConsoleCommand("market.buy")]
        private void ConsoleBuy(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int catIdx = arg.GetInt(0);
            int itemIdx = arg.GetInt(1);

            if (catIdx < 0 || catIdx >= _config.Categories.Count) return;
            var item = _config.Categories[catIdx].Items[itemIdx];

            if (Withdraw(player.userID, item.BuyPrice))
            {
                var giveItem = ItemManager.CreateByName(item.ShortName, item.Amount, item.SkinId);
                if (giveItem != null)
                {
                    player.GiveItem(giveItem);
                    SendReply(player, $"Purchased {item.Amount}x {item.ShortName}");
                }
                else Deposit(player.userID, item.BuyPrice); // Refund
            }
            else SendReply(player, "Insufficient funds.");
            
            ShowCategory(player, catIdx);
        }

        [ConsoleCommand("market.sell")]
        private void ConsoleSell(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int catIdx = arg.GetInt(0);
            int itemIdx = arg.GetInt(1);

            var item = _config.Categories[catIdx].Items[itemIdx];
            var def = ItemManager.FindItemDefinition(item.ShortName);
            if (def == null) return;

            if (player.inventory.GetAmount(def.itemid) >= item.Amount)
            {
                player.inventory.Take(null, def.itemid, item.Amount);
                Deposit(player.userID, item.SellPrice);
                SendReply(player, $"Sold {item.Amount}x {item.ShortName}");
            }
            ShowCategory(player, catIdx);
        }

        [ConsoleCommand("market.cat")]
        private void ConsoleCategory(ConsoleSystem.Arg arg) => ShowCategory(arg.Player(), arg.GetInt(0), arg.GetInt(1));

        [ConsoleCommand("market.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg) => CuiHelper.DestroyUi(arg.Player(), "NWG_Market_UI");

        [ConsoleCommand("market.sellall")]
        private void ConsoleSellAll(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int catIdx = arg.GetInt(0);
            if (catIdx < 0 || catIdx >= _config.Categories.Count) return;

            var cat = _config.Categories[catIdx];
            double totalEarned = 0;
            int totalSold = 0;

            foreach (var item in cat.Items)
            {
                if (item.SellPrice <= 0) continue;
                var def = ItemManager.FindItemDefinition(item.ShortName);
                if (def == null) continue;

                int available = player.inventory.GetAmount(def.itemid);
                int stacks = available / item.Amount;
                if (stacks <= 0) continue;

                int toTake = stacks * item.Amount;
                player.inventory.Take(null, def.itemid, toTake);
                double earned = stacks * item.SellPrice;
                totalEarned += earned;
                totalSold += toTake;
            }

            if (totalEarned > 0)
            {
                Deposit(player.userID, totalEarned);
                SendReply(player, $"Sold {totalSold} items for {_config.CurrencySymbol}{totalEarned:N0}");
            }
            else
            {
                SendReply(player, "Nothing to sell in this category.");
            }

            ShowCategory(player, catIdx);
        }

        [ConsoleCommand("market.sellallitem")]
        private void ConsoleSellAllItem(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int catIdx = arg.GetInt(0);
            int itemIdx = arg.GetInt(1);

            if (catIdx < 0 || catIdx >= _config.Categories.Count) return;
            var cat = _config.Categories[catIdx];
            if (itemIdx < 0 || itemIdx >= cat.Items.Count) return;

            var item = cat.Items[itemIdx];
            if (item.SellPrice <= 0) return;
            var def = ItemManager.FindItemDefinition(item.ShortName);
            if (def == null) return;

            int available = player.inventory.GetAmount(def.itemid);
            int stacks = available / item.Amount;
            if (stacks <= 0)
            {
                SendReply(player, $"You don't have enough {item.DisplayName ?? item.ShortName} to sell.");
                ShowCategory(player, catIdx);
                return;
            }

            int toTake = stacks * item.Amount;
            player.inventory.Take(null, def.itemid, toTake);
            double earned = stacks * item.SellPrice;
            Deposit(player.userID, earned);
            SendReply(player, $"Sold {toTake}x {item.DisplayName ?? item.ShortName} for {_config.CurrencySymbol}{earned:N0}");
            ShowCategory(player, catIdx);
        }
        #endregion

        #region UI
        private void ShowCategory(BasePlayer player, int catIndex, int page = 0)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, "NWG_Market_UI");
            
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.98" },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Market_UI");

            // Header
            elements.Add(new CuiPanel { Image = { Color = "0.15 0.15 0.15 1" }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, root);
            elements.Add(new CuiLabel { Text = { Text = _config.ShopTitle, FontSize = 24, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.5 1" } }, root);
            elements.Add(new CuiLabel { Text = { Text = $"BALANCE: <color=#b7d092>{_config.CurrencySymbol}{Balance(player.userID):N0}</color>", FontSize = 18, Align = TextAnchor.MiddleRight }, RectTransform = { AnchorMin = "0.5 0.92", AnchorMax = "0.9 1" } }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "market.close", Color = "0.8 0.2 0.2 0.8" },
                RectTransform = { AnchorMin = "0.93 0.93", AnchorMax = "0.99 0.99" },
                Text = { Text = "✕", FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, root);

            // Sidebar
            for (int i = 0; i < _config.Categories.Count; i++)
            {
                float y = 0.85f - (i * 0.06f);
                elements.Add(new CuiButton {
                    Button = { Command = $"market.cat {i} 0", Color = i == catIndex ? "0.4 0.6 0.2 0.8" : "0.15 0.15 0.15 0.7" },
                    RectTransform = { AnchorMin = $"0.01 {y-0.05f}", AnchorMax = $"0.17 {y}" },
                    Text = { Text = _config.Categories[i].Name.ToUpper(), FontSize = 12, Align = TextAnchor.MiddleCenter }
                }, root);
            }

            // Sell All Button (below category buttons)
            float sellAllY = 0.85f - (_config.Categories.Count * 0.06f) - 0.02f;
            elements.Add(new CuiButton {
                Button = { Command = $"market.sellall {catIndex}", Color = "0.7 0.3 0.1 0.9" },
                RectTransform = { AnchorMin = $"0.01 {sellAllY - 0.05f}", AnchorMax = $"0.17 {sellAllY}" },
                Text = { Text = "⚡ SELL ALL", FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
            }, root);

            // Grid
            var cat = _config.Categories[catIndex];
            int perPage = 12;
            int start = page * perPage;
            for (int i = 0; i < Math.Min(perPage, cat.Items.Count - start); i++)
            {
                var item = cat.Items[start + i];
                int r = i / 4, c = i % 4;
                float xMin = 0.2f + (c * 0.19f), yMax = 0.88f - (r * 0.26f);
                var pnl = elements.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.6" }, RectTransform = { AnchorMin = $"{xMin} {yMax-0.24f}", AnchorMax = $"{xMin+0.18f} {yMax}" } }, root);
                
                // Icon
                string icon = (string)ImageLibrary?.Call("GetImage", item.ShortName, item.SkinId) ?? "";
                elements.Add(new CuiElement { Parent = pnl, Components = { new CuiRawImageComponent { Png = icon }, new CuiRectTransformComponent { AnchorMin = "0.2 0.4", AnchorMax = "0.8 0.9" } } });
                
                // Name
                elements.Add(new CuiLabel { Text = { Text = item.DisplayName ?? item.ShortName.ToUpper(), FontSize = 10, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.25", AnchorMax = "1 0.45" } }, pnl);
                
                // Buttons
                bool hasBuy = item.BuyPrice > 0;
                bool hasSell = item.SellPrice > 0;
                if (hasBuy && hasSell)
                {
                    // 3-button layout: Buy | Sell x1 | Sell All
                    elements.Add(new CuiButton { Button = { Command = $"market.buy {catIndex} {start+i}", Color = "0.3 0.5 0.2 0.8" }, RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.34 0.2" }, Text = { Text = $"BUY\n{_config.CurrencySymbol}{item.BuyPrice:N0}", FontSize = 8 } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sell {catIndex} {start+i}", Color = "0.7 0.2 0.2 0.8" }, RectTransform = { AnchorMin = "0.36 0.05", AnchorMax = "0.65 0.2" }, Text = { Text = $"SELL\n{_config.CurrencySymbol}{item.SellPrice:N0}", FontSize = 8 } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sellallitem {catIndex} {start+i}", Color = "0.7 0.3 0.1 0.9" }, RectTransform = { AnchorMin = "0.67 0.05", AnchorMax = "0.97 0.2" }, Text = { Text = "SELL\nALL", FontSize = 8 } }, pnl);
                }
                else if (hasBuy)
                {
                    elements.Add(new CuiButton { Button = { Command = $"market.buy {catIndex} {start+i}", Color = "0.3 0.5 0.2 0.8" }, RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.2" }, Text = { Text = $"BUY {_config.CurrencySymbol}{item.BuyPrice:N0}", FontSize = 9 } }, pnl);
                }
                else if (hasSell)
                {
                    elements.Add(new CuiButton { Button = { Command = $"market.sell {catIndex} {start+i}", Color = "0.7 0.2 0.2 0.8" }, RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.48 0.2" }, Text = { Text = $"SELL\n{_config.CurrencySymbol}{item.SellPrice:N0}", FontSize = 9 } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sellallitem {catIndex} {start+i}", Color = "0.7 0.3 0.1 0.9" }, RectTransform = { AnchorMin = "0.52 0.05", AnchorMax = "0.95 0.2" }, Text = { Text = "SELL ALL", FontSize = 9 } }, pnl);
                }
            }

            CuiHelper.AddUi(player, elements);
        }
        #endregion
    }
}
