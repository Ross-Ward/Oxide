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
    [Info("NWG Market", "NWG Team", "3.0.0")]
    [Description("In-game Shop and Economy for NWG.")]
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
            public string IconUrl; // Optional
            public List<ShopItem> Items = new List<ShopItem>();
        }

        private class ShopItem
        {
            public string ShortName;
            public string DisplayName; // Optional override
            public ulong SkinId;
            public int Amount = 1;
            public double BuyPrice; // 0 to disable buy
            public double SellPrice; // 0 to disable sell
            public string Command; // Optional command to run
            public string ImageUrl; // Optional override
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

            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_Market");
            }
            catch
            {
                _data = new StoredData();
            }
            if (_data == null) _data = new StoredData();
            
            cmd.AddChatCommand(_config.ShopCommand, this, nameof(CmdShop));
            cmd.AddChatCommand(_config.BalanceCommand, this, nameof(CmdBalance));
        }

        private void LoadConfigVariables()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null || _config.Categories.Count == 0)
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
            Puts("Creating new configuration file for NWG Market");
            _config = new PluginConfig();

            // Resources
            var resCat = new ShopCategory { Name = "Resources" };
            resCat.Items.Add(new ShopItem { ShortName = "wood", Amount = 1000, BuyPrice = 100, SellPrice = 50 });
            resCat.Items.Add(new ShopItem { ShortName = "stones", Amount = 1000, BuyPrice = 200, SellPrice = 100 });
            resCat.Items.Add(new ShopItem { ShortName = "metal.fragments", Amount = 1000, BuyPrice = 300, SellPrice = 150 });
            resCat.Items.Add(new ShopItem { ShortName = "metal.refined", Amount = 100, BuyPrice = 500, SellPrice = 250 });
            resCat.Items.Add(new ShopItem { ShortName = "sulfur", Amount = 1000, BuyPrice = 400, SellPrice = 200 });
            resCat.Items.Add(new ShopItem { ShortName = "cloth", Amount = 100, BuyPrice = 100, SellPrice = 50 });
            resCat.Items.Add(new ShopItem { ShortName = "leather", Amount = 100, BuyPrice = 200, SellPrice = 100 });
            resCat.Items.Add(new ShopItem { ShortName = "charcoal", Amount = 1000, BuyPrice = 100, SellPrice = 50 });
            _config.Categories.Add(resCat);
            
            // Weapons
            var weapCat = new ShopCategory { Name = "Weapons" };
            weapCat.Items.Add(new ShopItem { ShortName = "rifle.ak", Amount = 1, BuyPrice = 1500, SellPrice = 500 });
            weapCat.Items.Add(new ShopItem { ShortName = "rifle.semi", Amount = 1, BuyPrice = 1000, SellPrice = 300 });
            weapCat.Items.Add(new ShopItem { ShortName = "smg.thompson", Amount = 1, BuyPrice = 800, SellPrice = 250 });
            weapCat.Items.Add(new ShopItem { ShortName = "pistol.semi", Amount = 1, BuyPrice = 500, SellPrice = 150 });
            weapCat.Items.Add(new ShopItem { ShortName = "shotgun.pump", Amount = 1, BuyPrice = 700, SellPrice = 200 });
            weapCat.Items.Add(new ShopItem { ShortName = "bow.hunting", Amount = 1, BuyPrice = 100, SellPrice = 30 });
            weapCat.Items.Add(new ShopItem { ShortName = "crossbow", Amount = 1, BuyPrice = 250, SellPrice = 70 });
            weapCat.Items.Add(new ShopItem { ShortName = "pistol.revolver", Amount = 1, BuyPrice = 300, SellPrice = 100 });
            _config.Categories.Add(weapCat);

            // Ammo
            var ammoCat = new ShopCategory { Name = "Ammunition" };
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.rifle", Amount = 128, BuyPrice = 100, SellPrice = 50 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.pistol", Amount = 128, BuyPrice = 80, SellPrice = 40 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.shotgun", Amount = 64, BuyPrice = 60, SellPrice = 30 });
            ammoCat.Items.Add(new ShopItem { ShortName = "ammo.rifle.explosive", Amount = 64, BuyPrice = 500, SellPrice = 200 });
            ammoCat.Items.Add(new ShopItem { ShortName = "arrow.wooden", Amount = 64, BuyPrice = 50, SellPrice = 20 });
            _config.Categories.Add(ammoCat);

            // Armor
            var armorCat = new ShopCategory { Name = "Armor" };
            armorCat.Items.Add(new ShopItem { ShortName = "hazmatsuit", Amount = 1, BuyPrice = 500, SellPrice = 150 });
            armorCat.Items.Add(new ShopItem { ShortName = "metal.facemask", Amount = 1, BuyPrice = 1000, SellPrice = 300 });
            armorCat.Items.Add(new ShopItem { ShortName = "metal.plate.torso", Amount = 1, BuyPrice = 1200, SellPrice = 350 });
            armorCat.Items.Add(new ShopItem { ShortName = "roadsign.jacket", Amount = 1, BuyPrice = 600, SellPrice = 200 });
            armorCat.Items.Add(new ShopItem { ShortName = "roadsign.kilt", Amount = 1, BuyPrice = 500, SellPrice = 150 });
            armorCat.Items.Add(new ShopItem { ShortName = "coffeecan.helmet", Amount = 1, BuyPrice = 400, SellPrice = 120 });
            _config.Categories.Add(armorCat);

            // Tools
            var toolCat = new ShopCategory { Name = "Tools" };
            toolCat.Items.Add(new ShopItem { ShortName = "jackhammer", Amount = 1, BuyPrice = 1500, SellPrice = 200 });
            toolCat.Items.Add(new ShopItem { ShortName = "chainsaw", Amount = 1, BuyPrice = 1200, SellPrice = 150 });
            toolCat.Items.Add(new ShopItem { ShortName = "axe.salvaged", Amount = 1, BuyPrice = 300, SellPrice = 50 });
            toolCat.Items.Add(new ShopItem { ShortName = "pickaxe.salvaged", Amount = 1, BuyPrice = 300, SellPrice = 50 });
            _config.Categories.Add(toolCat);

            // Medicine
            var medCat = new ShopCategory { Name = "Medicine" };
            medCat.Items.Add(new ShopItem { ShortName = "syringe.medical", Amount = 1, BuyPrice = 50, SellPrice = 10 });
            medCat.Items.Add(new ShopItem { ShortName = "largemedkit", Amount = 1, BuyPrice = 200, SellPrice = 40 });
            _config.Categories.Add(medCat);

            // Components
            var compCat = new ShopCategory { Name = "Components" };
            compCat.Items.Add(new ShopItem { ShortName = "gears", Amount = 1, BuyPrice = 50, SellPrice = 25 });
            compCat.Items.Add(new ShopItem { ShortName = "spring", Amount = 1, BuyPrice = 100, SellPrice = 50 });
            compCat.Items.Add(new ShopItem { ShortName = "metalpipe", Amount = 1, BuyPrice = 80, SellPrice = 40 });
            compCat.Items.Add(new ShopItem { ShortName = "riflebody", Amount = 1, BuyPrice = 500, SellPrice = 250 });
            compCat.Items.Add(new ShopItem { ShortName = "techparts", Amount = 1, BuyPrice = 400, SellPrice = 200 });
            _config.Categories.Add(compCat);

            // Services
            var cmdCat = new ShopCategory { Name = "Services" };
            cmdCat.Items.Add(new ShopItem { 
                ShortName = "raidevent.private", 
                DisplayName = "Start Private Raid",
                Command = "nwg.dungeon start private {steamid}",
                BuyPrice = 2500, 
                SellPrice = 0,
                ImageUrl = "https://i.imgur.com/example_raid.png"
            });
            cmdCat.Items.Add(new ShopItem { 
                ShortName = "raidevent.group", 
                DisplayName = "Start Group Raid",
                Command = "nwg.dungeon start group {steamid}",
                BuyPrice = 5000, 
                SellPrice = 0 
            });
            _config.Categories.Add(cmdCat);

            SaveConfig();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "NWG_Market_UI");
            }
            SaveData();
        }

        private void OnServerSave() => SaveData();
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Market", _data);
        #endregion

        #region Economy API
        [HookMethod("Balance")]
        public double Balance(ulong playerId)
        {
            return _data.Balances.TryGetValue(playerId, out var bal) ? bal : _config.StartingBalance;
        }

        [HookMethod("Deposit")]
        public bool Deposit(ulong playerId, double amount)
        {
            if (amount < 0) return false;
            var bal = Balance(playerId);
            _data.Balances[playerId] = bal + amount;
            return true;
        }

        [HookMethod("Withdraw")]
        public bool Withdraw(ulong playerId, double amount)
        {
            if (amount < 0) return false;
            var bal = Balance(playerId);
            if (bal < amount) return false;
            _data.Balances[playerId] = bal - amount;
            return true;
        }
        
        // String overloads for legacy compatibility
        [HookMethod("Balance")] public double Balance(string id) => Balance(ulong.Parse(id));
        [HookMethod("Deposit")] public bool Deposit(string id, double a) => Deposit(ulong.Parse(id), a);
        [HookMethod("Withdraw")] public bool Withdraw(string id, double a) => Withdraw(ulong.Parse(id), a);

        #endregion

        #region Commands
        private void CmdBalance(BasePlayer player, string command, string[] args)
        {
            var bal = Balance(player.userID);
            SendReply(player, $"Your Balance: {_config.CurrencySymbol}{bal:N2}");
        }

        private void CmdShop(BasePlayer player, string command, string[] args)
        {
            ShowCategory(player, 0);
        }
        
        [ConsoleCommand("market.buy")]
        private void ConsoleBuy(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            int catIdx = arg.GetInt(0);
            int itemIdx = arg.GetInt(1);

            if (catIdx < 0 || catIdx >= _config.Categories.Count) return;
            var cat = _config.Categories[catIdx];
            if (itemIdx < 0 || itemIdx >= cat.Items.Count) return;
            var item = cat.Items[itemIdx];

            if (item.BuyPrice <= 0) return;

            if (Withdraw(player.userID, item.BuyPrice))
            {
                bool success = true;

                if (!string.IsNullOrEmpty(item.Command))
                {
                    string cmd = item.Command
                        .Replace("{steamid}", player.UserIDString)
                        .Replace("{username}", player.displayName);
                        
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, cmd);
                    SendReply(player, $"Purchased {item.DisplayName ?? item.ShortName}!");
                }
                else
                {
                    var giveItem = ItemManager.CreateByName(item.ShortName, item.Amount, item.SkinId);
                    if (giveItem != null)
                    {
                        player.GiveItem(giveItem);
                        SendReply(player, $"Bought {item.Amount}x {item.ShortName} for {_config.CurrencySymbol}{item.BuyPrice}");
                    }
                    else
                    {
                        success = false;
                    }
                }

                if (!success)
                {
                    Deposit(player.userID, item.BuyPrice); // Refund
                    SendReply(player, "Error creating item.");
                }
            }
            else
            {
                SendReply(player, "Insufficient funds.");
            }
            
            // Refresh UI
            ShowCategory(player, catIdx); 
        }

        [ConsoleCommand("market.sell")]
        private void ConsoleSell(ConsoleSystem.Arg arg)
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
            
            int amountAvailable = player.inventory.GetAmount(def.itemid);
            if (amountAvailable < item.Amount)
            {
                SendReply(player, $"Need {item.Amount}x {item.ShortName}.");
                return;
            }
            
            player.inventory.Take(null, def.itemid, item.Amount);
            Deposit(player.userID, item.SellPrice);
            SendReply(player, $"Sold {item.Amount}x {item.ShortName} for {_config.CurrencySymbol}{item.SellPrice}");
            
            ShowCategory(player, catIdx);
        }
        
        [ConsoleCommand("market.cat")]
        private void ConsoleCategory(ConsoleSystem.Arg arg)
        {
             var player = arg.Player();
             if (player == null) return;
             int catIdx = arg.GetInt(0);
             int page = arg.GetInt(1);
             ShowCategory(player, catIdx, page);
        }

        [ConsoleCommand("market.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, "NWG_Market_UI");
        }

        [ConsoleCommand("market.deposit")]
        private void ConsoleAdminDeposit(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            
            var target = arg.GetPlayerOrSleeper(0);
            if (target == null) { SendReply(arg, "Player not found."); return; }
            
            double amount = (double)arg.GetFloat(1);
            if (amount < 0) { SendReply(arg, "Invalid amount."); return; }

            Deposit(target.userID, amount);
            SendReply(arg, $"Deposited {amount:N2} to {target.displayName}. New balance: {Balance(target.userID):N2}");
        }

        [ConsoleCommand("market.setbalance")]
        private void ConsoleAdminSetBalance(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;
            
            var target = arg.GetPlayerOrSleeper(0);
            if (target == null) { SendReply(arg, "Player not found."); return; }
            
            double amount = (double)arg.GetFloat(1);
            if (amount < 0) { SendReply(arg, "Invalid amount."); return; }

            _data.Balances[target.userID] = amount;
            SendReply(arg, $"Set {target.displayName}'s balance to {amount:N2}");
        }
        #endregion

        #region UI
        private void ShowCategory(BasePlayer player, int catIndex, int page = 0)
        {
            CuiHelper.DestroyUi(player, "NWG_Market_UI");
            
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Market_UI");

            // Header Background
            elements.Add(new CuiPanel {
                Image = { Color = "0.4 0.6 0.2 0.3" },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, root);

            // Header Title
            elements.Add(new CuiLabel {
                Text = { Text = _config.ShopTitle, FontSize = 28, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.5 1" }
            }, root);

            // Balance
            elements.Add(new CuiLabel {
                Text = { Text = $"Balance: <color=#b7d092>{_config.CurrencySymbol}{Balance(player.userID):N2}</color>", FontSize = 18, Align = TextAnchor.MiddleRight, Font = "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0.5 0.9", AnchorMax = "0.92 1" }
            }, root);

            // Sidebar (Left)
            elements.Add(new CuiPanel {
                Image = { Color = "0.1 0.1 0.1 0.5" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.18 0.9" }
            }, root);

            for(int i=0; i<_config.Categories.Count; i++)
            {
                var c = _config.Categories[i];
                bool active = (i == catIndex);
                string color = active ? "0.4 0.6 0.2 0.8" : "0.2 0.2 0.2 0.5";
                
                elements.Add(new CuiButton {
                    Button = { Command = $"market.cat {i} 0", Color = color },
                    RectTransform = { AnchorMin = $"0.01 {0.82f - (i+1)*0.07f}", AnchorMax = $"0.17 {0.82f - i*0.07f}" },
                    Text = { Text = c.Name.ToUpper(), Align = TextAnchor.MiddleCenter, FontSize = 11, Font = "robotocondensed-bold.ttf" } 
                }, root);
            }

            // Items (Grid)
            if (catIndex >= 0 && catIndex < _config.Categories.Count)
            {
                var cat = _config.Categories[catIndex];
                int itemsPerPage = 12;
                int cols = 4;
                float startX = 0.20f;
                float startY = 0.85f;
                float width = 0.18f;
                float height = 0.19f;
                float xGap = 0.015f;
                float yGap = 0.02f;

                int startIndex = page * itemsPerPage;
                int endIndex = Math.Min(startIndex + itemsPerPage, cat.Items.Count);

                for(int i=startIndex; i<endIndex; i++)
                {
                    var item = cat.Items[i];
                    int localIdx = i - startIndex;
                    int r = localIdx / cols;
                    int c = localIdx % cols;
                    
                    float xMin = startX + c * (width + xGap);
                    float yMax = startY - r * (height + yGap);
                    float xMax = xMin + width;
                    float yMin = yMax - height;

                    var pnl = elements.Add(new CuiPanel {
                        Image = { Color = "0.15 0.15 0.15 0.8" },
                        RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                    }, root);

                    // Item Display Name
                    elements.Add(new CuiLabel {
                        Text = { Text = item.DisplayName ?? item.ShortName, FontSize = 12, Align = TextAnchor.UpperCenter, Font = "robotocondensed-bold.ttf" },
                        RectTransform = { AnchorMin = "0 0.8", AnchorMax = "1 0.98" }
                    }, pnl);

                    // Buy Button
                    if (item.BuyPrice > 0)
                    {
                        elements.Add(new CuiButton {
                            Button = { Command = $"market.buy {catIndex} {i}", Color = "0.4 0.6 0.2 0.9" },
                            RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.48 0.35" },
                            Text = { Text = $"BUY\n{_config.CurrencySymbol}{item.BuyPrice:N0}", FontSize = 10, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                        }, pnl);
                    }
                    
                    // Sell Button
                    if (item.SellPrice > 0)
                    {
                        elements.Add(new CuiButton {
                            Button = { Command = $"market.sell {catIndex} {i}", Color = "0.8 0.2 0.2 0.9" },
                            RectTransform = { AnchorMin = "0.52 0.05", AnchorMax = "0.95 0.35" },
                            Text = { Text = $"SELL\n{_config.CurrencySymbol}{item.SellPrice:N0}", FontSize = 10, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                        }, pnl);
                    }
                }

                // Pagination Buttons
                if (page > 0)
                {
                    elements.Add(new CuiButton {
                        Button = { Command = $"market.cat {catIndex} {page - 1}", Color = "0.2 0.2 0.2 0.8" },
                        RectTransform = { AnchorMin = "0.2 0.02", AnchorMax = "0.3 0.07" },
                        Text = { Text = "BACK", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                    }, root);
                }

                if (endIndex < cat.Items.Count)
                {
                    elements.Add(new CuiButton {
                        Button = { Command = $"market.cat {catIndex} {page + 1}", Color = "0.2 0.2 0.2 0.8" },
                        RectTransform = { AnchorMin = "0.88 0.02", AnchorMax = "0.98 0.07" },
                        Text = { Text = "NEXT", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
                    }, root);
                }
            }

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "market.close", Color = "0.8 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.95 0.925", AnchorMax = "0.985 0.975" },
                Text = { Text = "✕", FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, root);

            CuiHelper.AddUi(player, elements);
        }
        #endregion
    }
}
