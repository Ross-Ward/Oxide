using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGWorld", "NWG Team", "4.3.0")]
    [Description("Global World Settings: Crafting, Workshop, and Virtual Quarry System.")]
    public class NWGWorld : RustPlugin
    {
#region Configuration
        private class PluginConfig
        {
            public float CraftingSpeedMultiplier = 1.0f;
            public bool InstantCraft = false;
            public float WorkshopRadius = 20.0f;
            public float VirtualQuarryTickRate = 60.0f;
            public int FuelPerDiesel = 5;
            public Dictionary<string, float> CraftSpeedOverrides = new Dictionary<string, float>();
            
            public Dictionary<string, List<YieldInfo>> QuarryYields = new Dictionary<string, List<YieldInfo>>
            {
                ["mining.quarry"] = new List<YieldInfo> { 
                    new YieldInfo { ShortName = "stones", Amount = 500 },
                    new YieldInfo { ShortName = "metal.ore", Amount = 250 },
                    new YieldInfo { ShortName = "sulfur.ore", Amount = 150 }
                },
                ["mining.pumpjack"] = new List<YieldInfo> {
                    new YieldInfo { ShortName = "crude.oil", Amount = 50 },
                    new YieldInfo { ShortName = "lowgradefuel", Amount = 20 }
                }
            };
        }

        private class YieldInfo { public string ShortName; public int Amount; }

        private PluginConfig _config;
#endregion

#region Data
        private class StoredData
        {
            public Dictionary<ulong, List<VirtualQuarry>> Quarries = new Dictionary<ulong, List<VirtualQuarry>>();
        }

        private class VirtualQuarry
        {
            public string Id;
            public string Type;
            public double LastTick;
            public int Fuel; 
            public Dictionary<string, int> Buffer = new Dictionary<string, int>();
        }

        private StoredData _data;
        private const string MainPanel = "NWG_QuarryUI";

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
            public const string Green = "0.4 0.8 0.2 1";
            public const string Red = "0.8 0.2 0.2 1";
        }
#endregion
#endregion

#region Lifecycle
        private void Init()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_World_Data") ?? new StoredData();
            
            int migrated = 0;
            foreach (var kvp in _data.Quarries)
            {
                foreach (var vq in kvp.Value)
                {
                    if (string.IsNullOrEmpty(vq.Id))
                    {
                        vq.Id = Guid.NewGuid().ToString().Substring(0, 8);
                        migrated++;
                    }
                }
            }
            if (migrated > 0) Puts($"[VQ] Migrated {migrated} quarries to the new ID system.");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Generating NWG World Defaults...");
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void OnServerInitialized()
        {
            ApplyCraftingSpeed();
            timer.Every(_config.VirtualQuarryTickRate, ProcessVirtualQuarries);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["UI.Title"] = "VIRTUAL QUARRY FARM",
                ["UI.NoQuarries"] = "NO VIRTUAL QUARRIES DEPLOYED",
                ["UI.DeployHeld"] = "DEPLOY HELD\n({0})",
                ["UI.HoldToDeploy"] = "HOLD QUARRY\nTO DEPLOY",
                ["UI.ClaimAll"] = "CLAIM ALL",
                ["UI.ClaimFilter"] = "CLAIM {0}",
                ["UI.FilterType"] = "FILTER BY TYPE",
                ["UI.Info"] = "INFO:\n- Sync every {0}s\n- Add Diesel for fuel",
                ["UI.Fuel"] = "FUEL: {0}",
                ["UI.Running"] = "Running...",
                ["UI.NoFuel"] = "No Fuel",
                ["UI.Inspect"] = "INSPECT",
                ["UI.Inspecting"] = "INSPECTING: {0}",
                ["UI.Back"] = "BACK",
                ["UI.Close"] = "CLOSE",
                ["UI.DieselRemaining"] = "DIESEL REMAINING: {0}",
                ["UI.BufferedRes"] = "BUFFERED RESOURCES",
                ["UI.NoBuffer"] = "NO RESOURCES IN BUFFER",
                ["UI.AddDiesel"] = "ADD 1x DIESEL",
                ["UI.CollectAll"] = "COLLECT ALL",
                ["UI.Processing"] = "Processed {0} Virtual Quarries.",
                
                ["Msg.Deployed"] = "Deployed virtual quarry!",
                ["Msg.NoDiesel"] = "No Diesel Fuel found!",
                ["Msg.DieselAdded"] = "Added Diesel! Remaining Units: {0}",
                ["Msg.ItemNotFound"] = "Item not found on this server!",
            }, this);
        }
        
        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);


        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, MainPanel);
            Interface.Oxide.DataFileSystem.WriteObject("NWG_World_Data", _data);
        }
#endregion

#region Crafting logic
        private void ApplyCraftingSpeed()
        {
            foreach (var bp in ItemManager.bpList)
            {
                if (bp == null) continue;
                if (_config.CraftSpeedOverrides.TryGetValue(bp.targetItem.shortname, out float forced)) bp.time = forced;
                else if (_config.InstantCraft) bp.time = 0f;
                else bp.time *= _config.CraftingSpeedMultiplier;
            }
        }

        private void OnTick()
        {
        }

        private Workbench FindNearbyWorkbench(BasePlayer player)
        {
            var workbenches = Facepunch.Pool.GetList<Workbench>();
            Vis.Entities(player.transform.position, _config.WorkshopRadius, workbenches, LayerMask.GetMask("Construction", "Deployed"));
            
            Workbench best = null;
            int maxLevel = 0;

            foreach (var wb in workbenches)
            {
                int level = GetWorkbenchLevel(wb);
                if (level > maxLevel)
                {
                    maxLevel = level;
                    best = wb;
                }
            }

            Facepunch.Pool.FreeList(ref workbenches);
            return best;
        }

        private int GetWorkbenchLevel(Workbench wb)
        {
            if (wb == null) return 0;
            // Workbench class usually has 'WorkbenchLevel' or similar in newer Rust versions, 
            // but since we can't inspect DLLs, we'll keep the string check as a fallback but try to be more precise.
            // Actually, we can assume standard prefabs:
            // "workbench1.deployed", "workbench2.deployed", "workbench3.deployed"
            
            if (wb.ShortPrefabName.Contains("workbench3")) return 3;
            if (wb.ShortPrefabName.Contains("workbench2")) return 2;
            if (wb.ShortPrefabName.Contains("workbench1")) return 1;

            return 0; 
        }
#endregion

#region Virtual Quarry System
        private void ProcessVirtualQuarries()
        {
            int totalProcessed = 0;
            foreach (var kvp in _data.Quarries)
            {
                foreach (var vq in kvp.Value)
                {
                    if (vq.Fuel <= 0) continue;

                    if (_config.QuarryYields.TryGetValue(vq.Type, out var yields))
                    {
                        vq.Fuel--;
                        foreach (var y in yields)
                        {
                            if (!vq.Buffer.ContainsKey(y.ShortName)) vq.Buffer[y.ShortName] = 0;
                            vq.Buffer[y.ShortName] += y.Amount;
                        }
                    }
                    totalProcessed++;
                }
            }
            if (totalProcessed > 0) Puts(string.Format(lang.GetMessage("UI.Processing", this), totalProcessed));
        }

        [ChatCommand("vquarry")]
        private void CmdVQuarry(BasePlayer player) => ShowQuarryUI(player, "all");

#region Virtual Quarry UI
        private void ShowQuarryUI(BasePlayer player) { ShowQuarryUI(player, "all"); }
        private void ShowQuarryUI(BasePlayer player, string filter)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, MainPanel);

            var e = new CuiElementContainer();
            var bg = e.Add(new CuiPanel
            {
                Image = { Color = UIConstants.OverlayColor },
                RectTransform = { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" },
                CursorEnabled = true
            }, "Overlay", MainPanel);

            // Header bar
            e.Add(new CuiPanel { Image = { Color = UIConstants.HeaderColor }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, bg);
            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.Title", player.UserIDString), FontSize = 22, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", Color = UIConstants.Primary }, RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.88 1" } }, bg);

            // Close button
            e.Add(new CuiButton { Button = { Close = MainPanel, Color = UIConstants.Secondary }, RectTransform = { AnchorMin = "0.9 0.93", AnchorMax = "0.98 0.99" }, Text = { Text = "X", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text } }, bg);

            // Left Panel
            var left = e.Add(new CuiPanel { Image = { Color = UIConstants.PanelColor }, RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.3 0.9" } }, bg);
            
            // Deploy Button
            var held = player.GetActiveItem();
            bool canDeploy = held != null && _config.QuarryYields.ContainsKey(held.info.shortname);
            string deployColor = canDeploy ? UIConstants.Green : UIConstants.ButtonInactive;
            string deployText = canDeploy ? GetMessage("UI.DeployHeld", player.UserIDString, held.info.displayName.english) : GetMessage("UI.HoldToDeploy", player.UserIDString);
            e.Add(new CuiButton
            {
                Button = { Command = "nwg_vq_deploy", Color = deployColor },
                RectTransform = { AnchorMin = "0.1 0.8", AnchorMax = "0.9 0.9" },
                Text = { Text = deployText, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, left);

            // Claim Button
            string claimText = filter == "all" ? GetMessage("UI.ClaimAll", player.UserIDString) : GetMessage("UI.ClaimFilter", player.UserIDString, filter.ToUpper());
            e.Add(new CuiButton
            {
                Button = { Command = "nwg_vq_claim " + filter, Color = UIConstants.ButtonActive },
                RectTransform = { AnchorMin = "0.1 0.68", AnchorMax = "0.9 0.78" },
                Text = { Text = claimText, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, left);

            // Filters
            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.FilterType", player.UserIDString), FontSize = 10, Color = "0.5 0.5 0.5 1", Align = TextAnchor.UpperCenter }, RectTransform = { AnchorMin = "0 0.62", AnchorMax = "1 0.65" } }, left);
            
            string[] filters = { "all", "quarry", "pumpjack" };
            for (int i = 0; i < filters.Length; i++)
            {
                string f = filters[i];
                float y = 0.55f - (i * 0.06f);
                string btnColor = filter == f ? UIConstants.Primary : UIConstants.ButtonInactive;
                e.Add(new CuiButton
                {
                    Button = { Command = "nwg_vq_filter " + f, Color = btnColor },
                    RectTransform = { AnchorMin = "0.1 " + y.ToString("F2"), AnchorMax = "0.9 " + (y + 0.05f).ToString("F2") },
                    Text = { Text = f.ToUpper(), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
                }, left);
            }

            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.Info", player.UserIDString, _config.VirtualQuarryTickRate), FontSize = 10, Color = "0.6 0.6 0.6 1", Align = TextAnchor.LowerLeft }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.2" } }, left);

            // Right Panel - Quarry List
            var right = e.Add(new CuiPanel { Image = { Color = UIConstants.PanelColor }, RectTransform = { AnchorMin = "0.32 0.02", AnchorMax = "0.98 0.9" } }, bg);
            
            if (!_data.Quarries.TryGetValue(player.userID, out var list) || list.Count == 0)
            {
                e.Add(new CuiLabel { Text = { Text = GetMessage("UI.NoQuarries", player.UserIDString), FontSize = 16, Color = "0.4 0.4 0.4 1", Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, right);
            }
            else
            {
                var filtered = list.Where(q => filter == "all" || q.Type.Contains(filter)).ToList();
                int max = Math.Min(filtered.Count, 10);
                for (int i = 0; i < max; i++)
                {
                    var q = filtered[i];
                    float y = 0.92f - (i * 0.085f);
                    string rowMin = "0.01 " + (y - 0.08f).ToString("F2");
                    string rowMax = "0.99 " + y.ToString("F2");
                    var row = e.Add(new CuiPanel { Image = { Color = "0.2 0.2 0.2 0.5" }, RectTransform = { AnchorMin = rowMin, AnchorMax = rowMax } }, right);
                    
                    e.Add(new CuiLabel { Text = { Text = q.Type.ToUpper().Replace("MINING.", ""), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.7 0.7 0.7 1" }, RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.25 1" } }, row);
                    
                    string fuelColor = q.Fuel > 0 ? UIConstants.Green : UIConstants.Red;
                    e.Add(new CuiLabel { Text = { Text = GetMessage("UI.Fuel", player.UserIDString, q.Fuel), FontSize = 10, Align = TextAnchor.MiddleLeft, Color = fuelColor }, RectTransform = { AnchorMin = "0.25 0", AnchorMax = "0.4 1" } }, row);

                    // Buffer summary
                    string buf = string.Join(", ", q.Buffer.Where(b => b.Value > 0).Select(b => b.Value + " " + PrettifyName(b.Key)));
                    if (string.IsNullOrEmpty(buf))
                        buf = q.Fuel > 0 ? $"<color=#666>{GetMessage("UI.Running", player.UserIDString)}</color>" : $"<color=#844>{GetMessage("UI.NoFuel", player.UserIDString)}</color>";
                    e.Add(new CuiLabel { Text = { Text = buf, FontSize = 9, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = "0.42 0", AnchorMax = "0.83 1" } }, row);

                    // Inspect button
                    if (!string.IsNullOrEmpty(q.Id))
                    {
                        e.Add(new CuiButton
                        {
                            Button = { Command = "nwg_vq_inspect " + q.Id, Color = UIConstants.ButtonInactive },
                            RectTransform = { AnchorMin = "0.85 0.1", AnchorMax = "0.98 0.9" },
                            Text = { Text = GetMessage("UI.Inspect", player.UserIDString), FontSize = 9, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
                        }, row);
                    }
                }
            }

            CuiHelper.AddUi(player, e);
        }

        private void ShowQuarryDetails(BasePlayer player, string id)
        {
            if (player == null) return;
            if (!_data.Quarries.TryGetValue(player.userID, out var list)) return;
            var q = list.FirstOrDefault(x => x.Id == id);
            if (q == null) return;

            CuiHelper.DestroyUi(player, MainPanel);

            var e = new CuiElementContainer();

            var bg = e.Add(new CuiPanel
            {
                Image = { Color = UIConstants.OverlayColor },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.8" },
                CursorEnabled = true
            }, "Overlay", MainPanel);

            // Header bar
            e.Add(new CuiPanel { Image = { Color = UIConstants.HeaderColor }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" } }, bg);
            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.Inspecting", player.UserIDString, q.Type.ToUpper().Replace("MINING.", "")), FontSize = 18, Align = TextAnchor.MiddleLeft, Color = UIConstants.Primary, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.05 0.9", AnchorMax = "0.6 1" } }, bg);

            // Back button
            e.Add(new CuiButton
            {
                Button = { Command = "nwg_vq_filter all", Color = UIConstants.ButtonInactive },
                RectTransform = { AnchorMin = "0.64 0.92", AnchorMax = "0.78 0.98" },
                Text = { Text = GetMessage("UI.Back", player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, bg);

            // Close button - uses Close property
            e.Add(new CuiButton
            {
                Button = { Close = MainPanel, Color = UIConstants.Secondary },
                RectTransform = { AnchorMin = "0.82 0.92", AnchorMax = "0.97 0.98" },
                Text = { Text = GetMessage("UI.Close", player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, bg);

            // Content
            var content = e.Add(new CuiPanel { Image = { Color = UIConstants.PanelColor }, RectTransform = { AnchorMin = "0.02 0.18", AnchorMax = "0.98 0.88" } }, bg);

            // Fuel status
            string fuelColor = q.Fuel > 0 ? UIConstants.Green : UIConstants.Red;
            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.DieselRemaining", player.UserIDString, q.Fuel), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = fuelColor }, RectTransform = { AnchorMin = "0.03 0.88", AnchorMax = "0.5 0.98" } }, content);
            e.Add(new CuiLabel { Text = { Text = GetMessage("UI.BufferedRes", player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.5 0.5 0.5 1" }, RectTransform = { AnchorMin = "0.03 0.78", AnchorMax = "0.5 0.86" } }, content);

            // Buffer items
            var bufferEntries = q.Buffer.Where(b => b.Value > 0).ToList();
            if (bufferEntries.Count == 0)
            {
                e.Add(new CuiLabel { Text = { Text = GetMessage("UI.NoBuffer", player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.3 0.3 0.3 1" }, RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.7" } }, content);
            }
            else
            {
                for (int idx = 0; idx < bufferEntries.Count; idx++)
                {
                    var b = bufferEntries[idx];
                    float yTop = 0.72f - (idx * 0.12f);
                    string rMin = "0.03 " + (yTop - 0.1f).ToString("F2");
                    string rMax = "0.97 " + yTop.ToString("F2");
                    var itemRow = e.Add(new CuiPanel { Image = { Color = "0.2 0.2 0.2 0.5" }, RectTransform = { AnchorMin = rMin, AnchorMax = rMax } }, content);
                    e.Add(new CuiLabel { Text = { Text = PrettifyName(b.Key), FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.8 0.8 0.8 1" }, RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.6 1" } }, itemRow);
                    e.Add(new CuiLabel { Text = { Text = "x" + b.Value.ToString("N0"), FontSize = 14, Align = TextAnchor.MiddleRight, Color = UIConstants.Green, Font = "robotocondensed-bold.ttf" }, RectTransform = { AnchorMin = "0.6 0", AnchorMax = "0.95 1" } }, itemRow);
                }
            }

            // Footer buttons
            e.Add(new CuiButton
            {
                Button = { Command = "nwg_vq_addfuel " + q.Id, Color = UIConstants.Green },
                RectTransform = { AnchorMin = "0.05 0.03", AnchorMax = "0.48 0.14" },
                Text = { Text = GetMessage("UI.AddDiesel", player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, bg);

            e.Add(new CuiButton
            {
                Button = { Command = "nwg_vq_claimone " + q.Id, Color = UIConstants.ButtonActive },
                RectTransform = { AnchorMin = "0.52 0.03", AnchorMax = "0.95 0.14" },
                Text = { Text = GetMessage("UI.CollectAll", player.UserIDString), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
            }, bg);

            CuiHelper.AddUi(player, e);
        }

        [ConsoleCommand("nwg_vq_inspect")]
        private void CC_VQInspect(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id)) return;
            ShowQuarryDetails(p, id);
        }

        [ConsoleCommand("nwg_vq_addfuel")]
        private void CC_VQAddFuel(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id)) return;
            if (!_data.Quarries.TryGetValue(p.userID, out var list)) return;
            var q = list.FirstOrDefault(x => x.Id == id);
            if (q == null) return;

            var dieselDef = ItemManager.FindItemDefinition("diesel_barrel");
            if (dieselDef == null)
            {
                p.ChatMessage(GetMessage("Msg.ItemNotFound", p.UserIDString));
                return;
            }

            var diesel = p.inventory.FindItemByItemID(dieselDef.itemid);
            if (diesel == null)
            {
                p.ChatMessage(GetMessage("Msg.NoDiesel", p.UserIDString));
                return;
            }

            diesel.UseItem(1);
            q.Fuel += _config.FuelPerDiesel;
            p.ChatMessage(GetMessage("Msg.DieselAdded", p.UserIDString, q.Fuel));
            ShowQuarryDetails(p, id);
        }

        [ConsoleCommand("nwg_vq_claimone")]
        private void CC_VQClaimOne(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            if (string.IsNullOrEmpty(id)) return;
            if (!_data.Quarries.TryGetValue(p.userID, out var list)) return;
            var q = list.FirstOrDefault(x => x.Id == id);
            if (q == null) return;

            foreach (var b in q.Buffer.ToList())
            {
                if (b.Value > 0)
                {
                    var item = ItemManager.CreateByName(b.Key, b.Value);
                    if (item != null) p.GiveItem(item);
                    q.Buffer[b.Key] = 0;
                }
            }
            ShowQuarryDetails(p, id);
        }

        private string PrettifyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Replace(".ore", "").Replace("metal.fragments", "Metal").Replace("high.quality.metal", "HQM").Replace("crude.oil", "Oil").Replace("lowgradefuel", "Fuel").Replace(".", " ").ToUpper();
        }

        [ConsoleCommand("nwg_vq_filter")]
        private void CC_VQFilter(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string f = arg.GetString(0, "all");
            ShowQuarryUI(p, f);
        }

        [ConsoleCommand("nwg_vq_close")]
        private void CC_VQClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            CuiHelper.DestroyUi(p, MainPanel);
        }

        [ConsoleCommand("nwg_vq_deploy")]
        private void CC_VQDeploy(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            var held = p.GetActiveItem();
            if (held != null && _config.QuarryYields.ContainsKey(held.info.shortname))
            {
                if (!_data.Quarries.ContainsKey(p.userID)) _data.Quarries[p.userID] = new List<VirtualQuarry>();
                _data.Quarries[p.userID].Add(new VirtualQuarry { 
                    Id = Guid.NewGuid().ToString().Substring(0, 8),
                    Type = held.info.shortname, 
                    LastTick = Facepunch.Math.Epoch.Current,
                    Fuel = 0
                });
                held.UseItem();
                p.ChatMessage(GetMessage("Msg.Deployed", p.UserIDString));
                ShowQuarryUI(p);
            }
        }

        [ConsoleCommand("nwg_vq_claim")]
        private void CC_VQClaim(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            if (!_data.Quarries.TryGetValue(p.userID, out var list)) return;
            string filter = arg.GetString(0, "all");
            
            foreach (var q in list.Where(x => filter == "all" || x.Type.Contains(filter)))
            {
                foreach (var b in q.Buffer.ToList())
                {
                    if (b.Value > 0)
                    {
                        var item = ItemManager.CreateByName(b.Key, b.Value);
                        if (item != null) p.GiveItem(item);
                        q.Buffer[b.Key] = 0;
                    }
                }
            }
            ShowQuarryUI(p, filter);
        }
#endregion
#endregion
    }
}
