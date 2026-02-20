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

#region UI Constants
        private static class UIConstants
        {
            // Theme: Sage Green & Dark
            public const string PanelColor = "0.15 0.15 0.15 0.98"; 
            public const string HeaderColor = "0.1 0.1 0.1 1"; 
            public const string Primary = "0.718 0.816 0.573 1"; // Sage Green
            public const string Secondary = "0.851 0.325 0.31 1"; // Red/Rust
            public const string Accent = "1 0.647 0 1"; // Orange
            public const string Text = "0.867 0.867 0.867 1"; // Soft White
            
            public const string OverlayColor = "0.05 0.05 0.05 0.98";
            
            public const string ButtonInactive = "0.15 0.15 0.15 0.7";
            public const string ButtonActive = "0.718 0.816 0.573 0.8"; // Sage Green Transparent
            
            public const string BuyButton = "0.3 0.5 0.2 0.8"; 
            public const string SellButton = "0.7 0.2 0.2 0.8"; 
            public const string SellAllButton = "0.851 0.325 0.31 0.9"; // Red/Rust
            public const string ItemPanel = "0.1 0.1 0.1 0.6";
        }
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
        private readonly Dictionary<string, ItemDefinition> _itemDefCache = new Dictionary<string, ItemDefinition>();
        // Key: PlayerID, Value: Search Term
        private readonly Dictionary<ulong, string> _searchFilters = new Dictionary<ulong, string>();
#endregion

#region Lifecycle
        private void Init()
        {
            LoadConfigVariables();
            LoadData();
            
            permission.RegisterPermission("nwgmarket.admin", this);
            cmd.AddChatCommand(_config.ShopCommand, this, nameof(CmdShop));
            cmd.AddChatCommand(_config.BalanceCommand, this, nameof(CmdBalance));
            cmd.AddChatCommand("setbalance", this, nameof(CmdSetBalance));
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
            else
            {
                // Ensure Special Items exists for existing installations
                if (!_config.Categories.Any(c => c.Name == "Special Items"))
                {
                    var specCat = new ShopCategory { Name = "Special Items" };
                    specCat.Items.Add(new ShopItem { ShortName = "keycard_red", Amount = 1, BuyPrice = 15000, DisplayName = "Dungeon Keycard" });
                    _config.Categories.Add(specCat);
                    SaveConfig();
                    Puts("Added missing 'Special Items' category to NWG Market config.");
                }
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

            // Special
            var specCat = new ShopCategory { Name = "Special Items" };
            specCat.Items.Add(new ShopItem { ShortName = "keycard_red", Amount = 1, BuyPrice = 15000, DisplayName = "Dungeon Keycard" });
            _config.Categories.Add(specCat);

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
        
        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            _searchFilters.Remove(player.userID);
        }
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

        private void CmdBalance(BasePlayer player) => SendReply(player, GetMessage(Lang.Balance, player.UserIDString, _config.CurrencySymbol, Balance(player.userID)));

        private void CmdSetBalance(BasePlayer player, string command, string[] args)
        {
            if (player.net.connection.authLevel < 2 && !permission.UserHasPermission(player.UserIDString, "nwgmarket.admin"))
            {
                SendReply(player, "You do not have permission to use this command.");
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, "Usage: /setbalance <player name/ID> <amount>");
                return;
            }

            var target = FindPlayer(args[0]);
            if (target == null)
            {
                SendReply(player, $"Player '{args[0]}' not found.");
                return;
            }

            if (!double.TryParse(args[1], out var amount))
            {
                SendReply(player, "Invalid amount.");
                return;
            }

            _data.Balances[target.userID] = amount;
            SendReply(player, $"Set {target.displayName}'s balance to {amount:N0}");
            SaveData();
        }

        [ConsoleCommand("market.buy")]
        private void ConsoleBuy(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            int catIdx = arg.GetInt(0);
            int itemIdx = arg.GetInt(1);

            if (catIdx < 0 || catIdx >= _config.Categories.Count) return;
            var item = _config.Categories[catIdx].Items[itemIdx];

            // Verify item exists
            var def = GetItemDef(item.ShortName);
            if (def == null)
            {
                // Item invalid/removed from game
                return;
            }

            if (Withdraw(player.userID, item.BuyPrice))
            {
                var giveItem = ItemManager.Create(def, item.Amount, item.SkinId);
                if (giveItem != null)
                {
                    if (!string.IsNullOrEmpty(item.DisplayName)) giveItem.name = item.DisplayName;
                    player.GiveItem(giveItem);
                    SendReply(player, GetMessage(Lang.Purchased, player.UserIDString, item.Amount, item.DisplayName ?? item.ShortName));
                }
                else
                {
                    Deposit(player.userID, item.BuyPrice); // Refund
                    // Log error?
                }
            }
            else SendReply(player, GetMessage(Lang.InsufficientFunds, player.UserIDString));
            
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
            var def = GetItemDef(item.ShortName);
            if (def == null) return;

            if (player.inventory.GetAmount(def.itemid) >= item.Amount)
            {
                player.inventory.Take(null, def.itemid, item.Amount);
                Deposit(player.userID, item.SellPrice);
                SendReply(player, GetMessage(Lang.Sold, player.UserIDString, item.Amount, item.ShortName));
            }
            else
            {
                SendReply(player, GetMessage(Lang.NotEnoughItems, player.UserIDString, item.DisplayName ?? item.ShortName));
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
                var def = GetItemDef(item.ShortName);
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
                SendReply(player, GetMessage(Lang.SoldBulk, player.UserIDString, totalSold, _config.CurrencySymbol, totalEarned));
            }
            else
            {
                SendReply(player, GetMessage(Lang.NothingToSell, player.UserIDString));
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
            var def = GetItemDef(item.ShortName);
            if (def == null) return;

            int available = player.inventory.GetAmount(def.itemid);
            int stacks = available / item.Amount;
            if (stacks <= 0)
            {
                SendReply(player, GetMessage(Lang.NotEnoughItems, player.UserIDString, item.DisplayName ?? item.ShortName));
                ShowCategory(player, catIdx);
                return;
            }

            int toTake = stacks * item.Amount;
            player.inventory.Take(null, def.itemid, toTake);
            double earned = stacks * item.SellPrice;
            Deposit(player.userID, earned);
            SendReply(player, GetMessage(Lang.Sold, player.UserIDString, toTake, item.DisplayName ?? item.ShortName));
            ShowCategory(player, catIdx);
        }

        [ConsoleCommand("market.search")]
        private void ConsoleSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            
            string term = arg.GetString(0);
            if (string.IsNullOrWhiteSpace(term)) _searchFilters.Remove(player.userID);
            else _searchFilters[player.userID] = term;
            
            ShowCategory(player, 0); // Refresh UI, catIdx ignored in search mode mostly
        }

        [ConsoleCommand("market.clearsearch")]
        private void ConsoleClearSearch(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null)
            {
                _searchFilters.Remove(player.userID);
                ShowCategory(player, 0);
            }
        }
#endregion

#region UI
        private void ShowCategory(BasePlayer player, int catIndex, int page = 0)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, "NWG_Market_UI");
            
            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = UIConstants.OverlayColor },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Market_UI");

            // Header
            elements.Add(new CuiPanel { Image = { Color = UIConstants.HeaderColor }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, root);
            elements.Add(new CuiLabel { Text = { Text = _config.ShopTitle, Color = UIConstants.Primary, FontSize = 24, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.5 1" } }, root);
            
            var balanceText = GetMessage(Lang.UIBalance, player.UserIDString, _config.CurrencySymbol, Balance(player.userID));
            elements.Add(new CuiLabel { Text = { Text = balanceText, FontSize = 18, Align = TextAnchor.MiddleRight }, RectTransform = { AnchorMin = "0.5 0.92", AnchorMax = "0.9 1" } }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "market.close", Color = UIConstants.Secondary },
                RectTransform = { AnchorMin = "0.93 0.93", AnchorMax = "0.99 0.99" },
                Text = { Text = GetMessage(Lang.UIClose, player.UserIDString), FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, root);

            // Sidebar
            for (int i = 0; i < _config.Categories.Count; i++)
            {
                float y = 0.85f - (i * 0.06f);
                var btnColor = i == catIndex ? UIConstants.ButtonActive : UIConstants.ButtonInactive;
                
                elements.Add(new CuiButton {
                    Button = { Command = $"market.cat {i} 0", Color = btnColor },
                    RectTransform = { AnchorMin = $"0.01 {y-0.05f}", AnchorMax = $"0.17 {y}" },
                    Text = { Text = _config.Categories[i].Name.ToUpper(), Color = UIConstants.Primary, FontSize = 12, Align = TextAnchor.MiddleCenter }
                }, root);
            }

            // Sell All Button (below category buttons)
            float sellAllY = 0.85f - (_config.Categories.Count * 0.06f) - 0.02f;
            elements.Add(new CuiButton {
                Button = { Command = $"market.sellall {catIndex}", Color = UIConstants.SellAllButton }, // Use Accent or specific SellAll color
                RectTransform = { AnchorMin = $"0.01 {sellAllY - 0.05f}", AnchorMax = $"0.17 {sellAllY}" },
                Text = { Text = GetMessage(Lang.UISellAll, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" }
            }, root);

            // Search Bar (Top Center/Right)
            string currentSearch = _searchFilters.ContainsKey(player.userID) ? _searchFilters[player.userID] : "";
            bool isSearching = !string.IsNullOrEmpty(currentSearch);

            elements.Add(new CuiElement {
                Parent = root,
                Components = {
                    new CuiInputFieldComponent { Command = "market.search", Text = currentSearch, FontSize = 12, Align = TextAnchor.MiddleLeft, CharsLimit = 20, Color = UIConstants.Text },
                    new CuiRectTransformComponent { AnchorMin = "0.35 0.93", AnchorMax = "0.55 0.98" }
                }
            });
            // Search Placeholder/Label
            if (string.IsNullOrEmpty(currentSearch))
            {
                elements.Add(new CuiLabel { Text = { Text = GetMessage(Lang.UISearch, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "1 1 1 0.3" }, RectTransform = { AnchorMin = "0.355 0.93", AnchorMax = "0.55 0.98" } }, root);
            }
            // Clear Search Button
            if (isSearching)
            {
                elements.Add(new CuiButton {
                    Button = { Command = "market.clearsearch", Color = UIConstants.Secondary },
                    RectTransform = { AnchorMin = "0.56 0.93", AnchorMax = "0.62 0.98" },
                    Text = { Text = GetMessage(Lang.UIClear, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter }
                }, root);
            }


            // Grid Setup
            List<ShopItem> displayItems;
            if (isSearching)
            {
                displayItems = new List<ShopItem>();
                foreach (var c in _config.Categories)
                {
                    foreach (var item in c.Items)
                    {
                        if ((item.DisplayName ?? item.ShortName).Contains(currentSearch, StringComparison.OrdinalIgnoreCase))
                            displayItems.Add(item);
                    }
                }
            }
            else
            {
                // Safety check for category index
                if (catIndex < 0 || catIndex >= _config.Categories.Count) catIndex = 0;
                displayItems = _config.Categories[catIndex].Items;
            }

            int perPage = 12;
            int maxPage = (int)Math.Ceiling((double)displayItems.Count / perPage) - 1;
            if (page < 0) page = 0;
            if (page > maxPage) page = maxPage;
            if (page < 0) page = 0; // if count is 0, maxPage is -1

            int start = page * perPage;
            int end = Math.Min(start + perPage, displayItems.Count);

            // Pagination Controls (Bottom)
            if (maxPage > 0)
            {
                 // Prev
                 if (page > 0)
                 {
                     elements.Add(new CuiButton {
                        Button = { Command = $"market.cat {catIndex} {page-1}", Color = UIConstants.ButtonInactive },
                        RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.48 0.07" },
                        Text = { Text = GetMessage(Lang.UIPrev, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
                     }, root);
                 }
                 // Next
                 if (page < maxPage)
                 {
                     elements.Add(new CuiButton {
                        Button = { Command = $"market.cat {catIndex} {page+1}", Color = UIConstants.ButtonInactive },
                        RectTransform = { AnchorMin = "0.52 0.02", AnchorMax = "0.6 0.07" },
                        Text = { Text = GetMessage(Lang.UINext, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter }
                     }, root);
                 }
                 // Page Info
                 elements.Add(new CuiLabel { Text = { Text = $"{page+1}/{maxPage+1}", FontSize = 12, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.48 0.02", AnchorMax = "0.52 0.07" } }, root);
            }

            // Render Items
            for (int i = 0; i < (end - start); i++)
            {
                var item = displayItems[start + i];
                int r = i / 4, c = i % 4;
                float xMin = 0.2f + (c * 0.19f), yMax = 0.88f - (r * 0.26f);
                var pnl = elements.Add(new CuiPanel { Image = { Color = UIConstants.ItemPanel }, RectTransform = { AnchorMin = $"{xMin} {yMax-0.24f}", AnchorMax = $"{xMin+0.18f} {yMax}" } }, root);
                
                // Icon
                string icon = (string)ImageLibrary?.Call("GetImage", item.ShortName, item.SkinId) ?? "";
                elements.Add(new CuiElement { Parent = pnl, Components = { new CuiRawImageComponent { Png = icon }, new CuiRectTransformComponent { AnchorMin = "0.2 0.4", AnchorMax = "0.8 0.9" } } });
                
                // Name
                elements.Add(new CuiLabel { Text = { Text = item.DisplayName ?? item.ShortName.ToUpper(), FontSize = 10, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.25", AnchorMax = "1 0.45" } }, pnl);
                
                // Buttons
                bool hasBuy = item.BuyPrice > 0;
                bool hasSell = item.SellPrice > 0;

                int realCatIdx = isSearching ? -1 : catIndex; 
                // Wait, if searching, catIndex might be irrelevant for market.buy command?
                // The market.buy command expects (int catIdx, int itemIdx). 
                // If we are searching, we are displaying a flat list. The indexes won't match the original categories!
                // CRITICAL ISSUE: The buy/sell commands rely on (CategoryIndex, ItemIndex). 
                // With search, we are presenting a synthetic list.
                // FIX: We need a way to reference the item. Or we must lookup the original indices.
                // Lookup original indices:
                int originalCatIdx = -1;
                int originalItemIdx = -1;
                
                // Brute force find coordinates
                for(int ci=0; ci<_config.Categories.Count; ci++) {
                    int idx = _config.Categories[ci].Items.IndexOf(item);
                    if (idx != -1) {
                         originalCatIdx = ci;
                         originalItemIdx = idx;
                         break;
                    }
                }
                
                if (originalCatIdx == -1) continue; // Should not happen

                var buyText = GetMessage(Lang.UIBuy, player.UserIDString, _config.CurrencySymbol, item.BuyPrice);
                var sellText = GetMessage(Lang.UISell, player.UserIDString, _config.CurrencySymbol, item.SellPrice);
                var sellAllText = GetMessage(Lang.UISellAllItem, player.UserIDString);
                
                // Use original indices for commands
                if (hasBuy && hasSell)
                {
                    elements.Add(new CuiButton { Button = { Command = $"market.buy {originalCatIdx} {originalItemIdx}", Color = UIConstants.BuyButton }, RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.34 0.2" }, Text = { Text = buyText, FontSize = 8, Align = TextAnchor.MiddleCenter } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sell {originalCatIdx} {originalItemIdx}", Color = UIConstants.SellButton }, RectTransform = { AnchorMin = "0.36 0.05", AnchorMax = "0.65 0.2" }, Text = { Text = sellText, FontSize = 8, Align = TextAnchor.MiddleCenter } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sellallitem {originalCatIdx} {originalItemIdx}", Color = UIConstants.SellAllButton }, RectTransform = { AnchorMin = "0.67 0.05", AnchorMax = "0.97 0.2" }, Text = { Text = sellAllText, FontSize = 8, Align = TextAnchor.MiddleCenter } }, pnl);
                }
                else if (hasBuy)
                {
                    elements.Add(new CuiButton { Button = { Command = $"market.buy {originalCatIdx} {originalItemIdx}", Color = UIConstants.BuyButton }, RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.2" }, Text = { Text = buyText, FontSize = 9, Align = TextAnchor.MiddleCenter } }, pnl);
                }
                else if (hasSell)
                {
                    elements.Add(new CuiButton { Button = { Command = $"market.sell {originalCatIdx} {originalItemIdx}", Color = UIConstants.SellButton }, RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.48 0.2" }, Text = { Text = sellText, FontSize = 9, Align = TextAnchor.MiddleCenter } }, pnl);
                    elements.Add(new CuiButton { Button = { Command = $"market.sellallitem {originalCatIdx} {originalItemIdx}", Color = UIConstants.SellAllButton }, RectTransform = { AnchorMin = "0.52 0.05", AnchorMax = "0.95 0.2" }, Text = { Text = sellAllText, FontSize = 9, Align = TextAnchor.MiddleCenter } }, pnl);
                }
            }

            CuiHelper.AddUi(player, elements);
        }
#endregion

#region Localization
        private class Lang
        {
            public const string Balance = "Balance";
            public const string Purchased = "Purchased";
            public const string InsufficientFunds = "InsufficientFunds";
            public const string Sold = "Sold";
            public const string NothingToSell = "NothingToSell";
            public const string NotEnoughItems = "NotEnoughItems";
            public const string SoldBulk = "SoldBulk";
            public const string UIBalance = "UI.Balance";
            public const string UISellAll = "UI.SellAll";
            public const string UIBuy = "UI.Buy";
            public const string UISell = "UI.Sell";
            public const string UISellAllItem = "UI.SellAllItem";
            public const string UIClose = "UI.Close";
            public const string UISearch = "UI.Search";
            public const string UIClear = "UI.Clear";
            public const string UIPrev = "UI.Prev";
            public const string UINext = "UI.Next";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Balance] = "<color=#b7d092>[NWG]</color> Balance: <color=#b7d092>{0}{1:N2}</color>",
                [Lang.Purchased] = "<color=#b7d092>[NWG]</color> Purchased <color=#FFA500>{0}x {1}</color>",
                [Lang.InsufficientFunds] = "<color=#d9534f>[NWG]</color> Insufficient funds.",
                [Lang.Sold] = "<color=#b7d092>[NWG]</color> Sold <color=#FFA500>{0}x {1}</color>",
                [Lang.NothingToSell] = "<color=#d9534f>[NWG]</color> Nothing to sell in this category.",
                [Lang.NotEnoughItems] = "<color=#d9534f>[NWG]</color> You don't have enough <color=#FFA500>{0}</color> to sell.",
                [Lang.SoldBulk] = "<color=#b7d092>[NWG]</color> Sold <color=#FFA500>{0}</color> items for <color=#b7d092>{1}{2:N0}</color>",
                [Lang.UIBalance] = "BALANCE: <color=#b7d092>{0}{1:N0}</color>",
                [Lang.UISellAll] = "âš¡ SELL ALL",
                [Lang.UIBuy] = "BUY\n{0}{1:N0}",
                [Lang.UISell] = "SELL\n{0}{1:N0}",
                [Lang.UISellAllItem] = "SELL\nALL",
                [Lang.UIClose] = "âœ•",
                [Lang.UISearch] = "Search...",
                [Lang.UIClear] = "X",
                [Lang.UIPrev] = "<",
                [Lang.UINext] = ">"
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

#region Helpers
        private ItemDefinition GetItemDef(string shortname)
        {
            if (string.IsNullOrEmpty(shortname)) return null;
            if (_itemDefCache.TryGetValue(shortname, out var def)) return def;
            
            def = ItemManager.FindItemDefinition(shortname);
            if (def != null) _itemDefCache[shortname] = def;
            return def;
        }

        private BasePlayer FindPlayer(string nameOrId)
        {
            ulong id;
            if (ulong.TryParse(nameOrId, out id) && id > 76561197960265728)
                return BasePlayer.FindByID(id);

            return BasePlayer.Find(nameOrId);
        }
#endregion
    }
}
