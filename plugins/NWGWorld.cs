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
    [Info("NWGWorld", "NWG Team", "4.2.0")]
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
            public int FuelPerDiesel = 5; // One diesel fuel gives 5 ticks of operation
            public Dictionary<string, float> CraftSpeedOverrides = new Dictionary<string, float>();
            
            // Virtual Quarry Yields (per tick)
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
        #endregion

        #region Lifecycle
        private void Init()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWG_World_Data") ?? new StoredData();
            
            // Migration: Assign IDs to old quarries and log it
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

        private void Unload() => Interface.Oxide.DataFileSystem.WriteObject("NWG_World_Data", _data);
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
            // Workbench proximity logic will be implemented via CanCraft hook in a future update.
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
            if (wb == null || wb.ShortPrefabName == null) return 0;
            if (wb.ShortPrefabName.Contains("3")) return 3;
            if (wb.ShortPrefabName.Contains("2")) return 2;
            if (wb.ShortPrefabName.Contains("1")) return 1;
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
            if (totalProcessed > 0) Puts($"[NWG World] Processed {totalProcessed} Virtual Quarries.");
        }

        [ChatCommand("vquarry")]
        private void CmdVQuarry(BasePlayer player) => ShowQuarryUI(player, "all");

        #region Virtual Quarry UI
        private void ShowQuarryUI(BasePlayer player, string filter = "all")
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, "NWG_QuarryUI");

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.08 0.08 0.1 0.98" },
                RectTransform = { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" },
                CursorEnabled = true
            }, "Overlay", "NWG_QuarryUI");

            // Header
            elements.Add(new CuiPanel { Image = { Color = "0.12 0.12 0.15 1" }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, root);
            elements.Add(new CuiLabel { Text = { Text = "VIRTUAL QUARRY FARM", FontSize = 22, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", Color = "0.35 0.6 1 1" }, RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" } }, root);

            // Left Panel - Actions
            var left = elements.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.6" }, RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.3 0.9" } }, root);
            
            // Filters
            elements.Add(new CuiLabel { Text = { Text = "FILTER BY TYPE", FontSize = 10, Color = "0.5 0.5 0.5 1", Align = TextAnchor.UpperCenter }, RectTransform = { AnchorMin = "0 0.62", AnchorMax = "1 0.65" } }, left);
            
            string[] filters = { "all", "quarry", "pumpjack" };
            for(int i = 0; i < filters.Length; i++)
            {
                string f = filters[i];
                float y = 0.55f - (i * 0.06f);
                elements.Add(new CuiButton {
                    Button = { Command = $"nwg_vq_filter {f}", Color = filter == f ? "0.35 0.6 1 0.4" : "0.2 0.2 0.2 0.6" },
                    RectTransform = { AnchorMin = $"0.1 {y}", AnchorMax = $"0.9 {y+0.05f}" },
                    Text = { Text = f.ToUpper(), FontSize = 9, Align = TextAnchor.MiddleCenter }
                }, left);
            }

            // Deploy Button
            var held = player.GetActiveItem();
            bool canDeploy = held != null && _config.QuarryYields.ContainsKey(held.info.shortname);
            elements.Add(new CuiButton {
                Button = { Command = "nwg_vq_deploy", Color = canDeploy ? "0.3 0.5 0.2 0.8" : "0.2 0.2 0.2 0.5" },
                RectTransform = { AnchorMin = "0.1 0.8", AnchorMax = "0.9 0.9" },
                Text = { Text = canDeploy ? $"DEPLOY HELD\n({held.info.displayName.english})" : "HOLD QUARRY\nTO DEPLOY", FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, left);

            // Claim Button
            elements.Add(new CuiButton {
                Button = { Command = $"nwg_vq_claim {filter}", Color = "0.2 0.4 0.6 0.8" },
                RectTransform = { AnchorMin = "0.1 0.68", AnchorMax = "0.9 0.78" },
                Text = { Text = filter == "all" ? "CLAIM ALL" : $"CLAIM {filter.ToUpper()}", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, left);

            elements.Add(new CuiLabel { Text = { Text = "INFO:\n- Sync every 60s\n- No fuel required", FontSize = 10, Color = "0.6 0.6 0.6 1", Align = TextAnchor.LowerLeft }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.2" } }, left);

            // Right Panel - List
            var right = elements.Add(new CuiPanel { Image = { Color = "0.15 0.15 0.15 0.8" }, RectTransform = { AnchorMin = "0.32 0.02", AnchorMax = "0.98 0.9" } }, root);
            
            if (!_data.Quarries.TryGetValue(player.userID, out var list) || list.Count == 0)
            {
                elements.Add(new CuiLabel { Text = { Text = "NO VIRTUAL QUARRIES DEPLOYED", FontSize = 16, Color = "0.4 0.4 0.4 1", Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, right);
            }
            else
            {
                var filtered = list.Where(q => filter == "all" || q.Type.Contains(filter)).ToList();
                for (int i = 0; i < Math.Min(filtered.Count, 10); i++)
                {
                    var q = filtered[i];
                    float y = 0.92f - (i * 0.085f);
                    var row = elements.Add(new CuiPanel { Image = { Color = "0.2 0.2 0.2 0.5" }, RectTransform = { AnchorMin = $"0.01 {y-0.08f}", AnchorMax = $"0.99 {y}" } }, right);
                    
                    elements.Add(new CuiLabel { Text = { Text = q.Type.ToUpper().Replace("MINING.", ""), FontSize = 11, Align = TextAnchor.MiddleLeft, Color = "0.7 0.7 0.7 1" }, RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.25 1" } }, row);
                    
                    string fuelColor = q.Fuel > 0 ? "0.4 0.8 0.2 1" : "0.8 0.2 0.2 1";
                    elements.Add(new CuiLabel { Text = { Text = $"FUEL: {q.Fuel}", FontSize = 10, Align = TextAnchor.MiddleLeft, Color = fuelColor }, RectTransform = { AnchorMin = "0.25 0", AnchorMax = "0.4 1" } }, row);

                    string cmd = string.IsNullOrEmpty(q.Id) ? "" : $"nwg_vq_inspect {q.Id}";
                    elements.Add(new CuiButton {
                        Button = { Command = cmd, Color = "0.35 0.6 1 0.4" },
                        RectTransform = { AnchorMin = "0.85 0.1", AnchorMax = "0.98 0.9" },
                        Text = { Text = "INSPECT", FontSize = 9, Align = TextAnchor.MiddleCenter }
                    }, row);

                    string buf = string.Join(", ", q.Buffer.Where(b => b.Value > 0).Select(b => $"{b.Value} {PrettifyName(b.Key)}"));
                    if (string.IsNullOrEmpty(buf)) buf = q.Fuel > 0 ? "<color=#444>Running...</color>" : "<color=#633>Out of Fuel</color>";
                    elements.Add(new CuiLabel { Text = { Text = buf, FontSize = 9, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = "0.42 0", AnchorMax = "0.83 1" } }, row);
                }
            }

            // Close
            elements.Add(new CuiButton {
                Button = { Command = "nwg_vq_close", Color = "0.7 0.2 0.2 0.8" },
                RectTransform = { AnchorMin = "0.96 0.93", AnchorMax = "0.99 0.99" },
                Text = { Text = "✕", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, root);

            CuiHelper.AddUi(player, elements);
        }

        private void ShowQuarryDetails(BasePlayer player, string id)
        {
            if (player == null) return;
            if (!_data.Quarries.TryGetValue(player.userID, out var list)) return;
            var q = list.FirstOrDefault(x => x.Id == id);
            if (q == null) return;

            CuiHelper.DestroyUi(player, "NWG_QuarryUI");

            var elements = new CuiElementContainer();
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.1 0.1 0.12 0.98" },
                RectTransform = { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                CursorEnabled = true
            }, "Overlay", "NWG_QuarryUI");

            elements.Add(new CuiLabel { Text = { Text = $"INSPECTING {q.Type.ToUpper().Replace("MINING.", "")}", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.35 0.6 1 1" }, RectTransform = { AnchorMin = "0 0.85", AnchorMax = "1 1" } }, root);

            var content = elements.Add(new CuiPanel { Image = { Color = "0.2 0.2 0.2 0.4" }, RectTransform = { AnchorMin = "0.05 0.2", AnchorMax = "0.95 0.8" } }, root);

            elements.Add(new CuiLabel { Text = { Text = $"Diesel Units: {q.Fuel}\nBuffer Content:", FontSize = 12, Align = TextAnchor.UpperLeft }, RectTransform = { AnchorMin = "0.05 0.7", AnchorMax = "0.95 0.95" } }, content);
            
            string buf = string.Join("\n", q.Buffer.Where(b => b.Value > 0).Select(b => $"- {b.Value}x {PrettifyName(b.Key)}"));
            if (string.IsNullOrEmpty(buf)) buf = "Clean";
            elements.Add(new CuiLabel { Text = { Text = buf, FontSize = 11, Align = TextAnchor.UpperLeft, Color = "0.7 0.7 0.7 1" }, RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.65" } }, content);

            // Add Diesel Button
            elements.Add(new CuiButton {
                Button = { Command = $"nwg_vq_addfuel {q.Id}", Color = "0.3 0.5 0.2 0.8" },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.48 0.15" },
                Text = { Text = "DEPLOY 1x DIESEL", FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, root);

            // Claim Specific Button
            elements.Add(new CuiButton {
                Button = { Command = $"nwg_vq_claimone {q.Id}", Color = "0.2 0.4 0.6 0.8" },
                RectTransform = { AnchorMin = "0.52 0.05", AnchorMax = "0.95 0.15" },
                Text = { Text = "CLAIM BUFFER", FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, root);

            elements.Add(new CuiButton {
                Button = { Command = "nwg_vq_filter all", Color = "0.4 0.4 0.4 0.8" },
                RectTransform = { AnchorMin = "0.9 0.92", AnchorMax = "0.98 0.98" },
                Text = { Text = "BACK", FontSize = 9, Align = TextAnchor.MiddleCenter }
            }, root);

            // Close
            elements.Add(new CuiButton {
                Button = { Command = "nwg_vq_close", Color = "0.7 0.2 0.2 0.8" },
                RectTransform = { AnchorMin = "0.9 0.85", AnchorMax = "0.98 0.98" },
                Text = { Text = "✕", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, root);

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("nwg_vq_inspect")]
        public void CC_VQInspect(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            Puts($"[VQ Debug] CC_VQInspect: ID {id}");
            ShowQuarryDetails(p, id);
        }

        [ConsoleCommand("nwg_vq_addfuel")]
        public void CC_VQAddFuel(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            Puts($"[VQ Debug] CC_VQAddFuel: ID {id}");
            if (!_data.Quarries.TryGetValue(p.userID, out var list)) return;
            var q = list.FirstOrDefault(x => x.Id == id);
            if (q == null) return;

            var dieselDef = ItemManager.FindItemDefinition("diesel_fuel");
            if (dieselDef == null) return;

            var diesel = p.inventory.FindItemByItemID(dieselDef.itemid);
            if (diesel == null)
            {
                p.ChatMessage("No Diesel Fuel found!");
                return;
            }

            diesel.UseItem(1);
            q.Fuel += _config.FuelPerDiesel;
            p.ChatMessage($"Added Diesel! Units: {q.Fuel}");
            ShowQuarryDetails(p, id);
        }

        [ConsoleCommand("nwg_vq_claimone")]
        public void CC_VQClaimOne(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            string id = arg.GetString(0);
            Puts($"[VQ Debug] CC_VQClaimOne: ID {id}");
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
        public void CC_VQFilter(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            string f = arg.GetString(0, "all");
            Puts($"[VQ Debug] CC_VQFilter: {f}");
            ShowQuarryUI(p, f);
        }

        [ConsoleCommand("nwg_vq_close")]
        public void CC_VQClose(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            Puts($"[VQ Debug] CC_VQClose");
            CuiHelper.DestroyUi(p, "NWG_QuarryUI");
        }

        [ConsoleCommand("nwg_vq_deploy")]
        public void CC_VQDeploy(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            Puts($"[VQ Debug] CC_VQDeploy");
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
                p.ChatMessage("Deployed virtual quarry!");
                ShowQuarryUI(p);
            }
        }

        [ConsoleCommand("nwg_vq_claim")]
        public void CC_VQClaim(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null || !_data.Quarries.TryGetValue(p.userID, out var list)) return;
            string filter = arg.GetString(0, "all");
            Puts($"[VQ Debug] CC_VQClaim: {filter}");
            
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
