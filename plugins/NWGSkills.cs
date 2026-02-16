// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("NWGSkills", "NWG Team", "1.0.0")]
    [Description("Gathering skill trees and blueprint unlocks.")]
    public class NWGSkills : RustPlugin
    {
#region Data
        private class PlayerData
        {
            public float WoodXP = 0;
            public float StoneXP = 0;
            public float OreXP = 0;
            public float CombatXP = 0;
            public float SurvivalXP = 0;
            public int SkillPoints = 0;
            public int Level = 1;
            public HashSet<string> UnlockedSkills = new HashSet<string>();
        }

#region Config
        private class SkillDef
        {
            public string Name; // Display Name
            public int Cost;
            public string ItemShortname; // Item to unlock BP for
        }

        private class PluginConfig
        {
            public Dictionary<string, SkillDef> Skills = new Dictionary<string, SkillDef>();
        }
        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            _config.Skills = new Dictionary<string, SkillDef>
            {
                ["unlock.chainsaw"] = new SkillDef { Name = "Chainsaw BP", Cost = 5, ItemShortname = "chainsaw" },
                ["unlock.jackhammer"] = new SkillDef { Name = "Jackhammer BP", Cost = 5, ItemShortname = "jackhammer" },
                ["unlock.quarry"] = new SkillDef { Name = "Quarry BP", Cost = 10, ItemShortname = "mining.quarry" },
                ["unlock.turbine"] = new SkillDef { Name = "Wind Turbine BP", Cost = 15, ItemShortname = "wind.turbine" }
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<PluginConfig>(); if (_config == null) LoadDefaultConfig(); }
            catch { LoadDefaultConfig(); }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
#endregion

#region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["LevelUp"] = "<color=#b7d092>[NWG]</color> <color=#b7d092>+1 Level!</color> (Level {0}) and +1 Skill Point in <color=#FFA500>{1}</color>!",
                ["UI.Title"] = "SURVIVAL SKILLS",
                ["UI.Points"] = "AVAILABLE POINTS: <color=#b7d092>{0}</color>",
                ["UI.Branch.Gather"] = "GATHERING",
                ["UI.Branch.Build"] = "CONSTRUCTION",
                ["UI.Branch.Adv"] = "ADVANCED",
                ["UI.Locked"] = "Locked...",
                ["Msg.Unlocked"] = "<color=#b7d092>[NWG]</color> Successfully unlocked <color=#FFA500>{0}</color>!",
                ["Msg.NoPoints"] = "<color=#d9534f>[NWG]</color> Not enough skill points.",
                ["Msg.AlreadyUnlocked"] = "<color=#d9534f>[NWG]</color> You already unlocked this skill.",
                ["Msg.Item precise"] = "<color=#b7d092>[NWG]</color> Blueprint for <color=#FFA500>{0}</color> unlocked!"
            }, this);
        }
        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

        public class UIConstants
        {
            public const string Panel = "0.15 0.15 0.15 0.98"; 
            public const string Header = "0.1 0.1 0.1 1"; 
            public const string Primary = "0.718 0.816 0.573"; // Sage Green (Raw RGB for alpha mixing)
            public const string Accent = "0.851 0.325 0.31"; // Red
            public const string Text = "0.867 0.867 0.867 1";
        }

        private Dictionary<ulong, PlayerData> _playerData = new Dictionary<ulong, PlayerData>();

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Skills", _playerData);
        private void LoadData() => _playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerData>>("NWG_Skills") ?? new Dictionary<ulong, PlayerData>();
#endregion

#region Lifecycle
        private void Init()
        {
            LoadData();
        }

        private void Unload() => SaveData();

        private void OnServerSave() => SaveData();

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!_playerData.ContainsKey(player.userID))
                _playerData[player.userID] = new PlayerData();
        }
#endregion

#region Gathering & Combat Hooks
        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null) return;

            string category = dispenser.gatherType.ToString();
            float xpGain = item.amount * 0.15f; // Buffed XP slightly

            var data = GetPlayerData(player.userID);
            string label = "";

            if (category == "Tree") { data.WoodXP += xpGain; label = "Woodcutting"; }
            else if (category == "Ore") { data.OreXP += xpGain; label = "Mining"; }
            else if (category == "Stone") { data.StoneXP += xpGain; label = "Quarrying"; }
            
            CheckLevelUp(player, data, label);
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var attacker = info?.Initiator as BasePlayer;
            if (attacker == null || entity == null) return;

            var data = GetPlayerData(attacker.userID);

            // Combat XP for hitting hostile NPCs or other players
            if (entity is BaseNpc || entity is BasePlayer && entity != attacker)
            {
                float xpGain = info.damageTypes.Total() * 0.5f;
                data.CombatXP += xpGain;
                CheckLevelUp(attacker, data, "Combat");
            }
            // Survival XP for hitting barrels, crates, etc.
            else if (entity is LootContainer || (entity.ShortPrefabName.Contains("barrel") || entity.ShortPrefabName.Contains("crate")))
            {
                float xpGain = info.damageTypes.Total() * 0.2f;
                data.SurvivalXP += xpGain;
                CheckLevelUp(attacker, data, "Survival");
            }
        }

        private void CheckLevelUp(BasePlayer player, PlayerData data, string label)
        {
            const float XP_PER_POINT = 1000f;
            bool leveled = false;

            if (data.WoodXP >= XP_PER_POINT) { data.WoodXP -= XP_PER_POINT; leveled = true; }
            if (data.StoneXP >= XP_PER_POINT) { data.StoneXP -= XP_PER_POINT; leveled = true; }
            if (data.OreXP >= XP_PER_POINT) { data.OreXP -= XP_PER_POINT; leveled = true; }
            if (data.CombatXP >= XP_PER_POINT) { data.CombatXP -= XP_PER_POINT; leveled = true; }
            if (data.SurvivalXP >= XP_PER_POINT) { data.SurvivalXP -= XP_PER_POINT; leveled = true; }

            if (leveled)
            {
                data.SkillPoints++;
                data.Level++;
                data.SkillPoints++;
                data.Level++;
                player.ChatMessage(GetMessage("LevelUp", player.UserIDString, data.Level, label));
            }
        }

        private PlayerData GetPlayerData(ulong uid)
        {
            if (!_playerData.TryGetValue(uid, out var data))
                _playerData[uid] = data = new PlayerData();
            return data;
        }
#endregion

#region UI
        [ChatCommand("skills")]
        private void CmdSkills(BasePlayer player)
        {
            ShowSkillsUI(player);
        }

        [ConsoleCommand("skills.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, "NWG_Skills_UI");
        }

        private void ShowSkillsUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "NWG_Skills_UI");
            var elements = new CuiElementContainer();
            var data = GetPlayerData(player.userID);

            var root = elements.Add(new CuiPanel {
                Image = { Color = UIConstants.Panel },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Skills_UI");

            // Header
            elements.Add(new CuiPanel {
                Image = { Color = UIConstants.Header },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, root);

            elements.Add(new CuiLabel {
                Text = { Text = GetMessage("UI.Title", player.UserIDString), FontSize = 24, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf", Color = UIConstants.Text },
                RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.5 1" }
            }, root);

            elements.Add(new CuiLabel {
                Text = { Text = GetMessage("UI.Points", player.UserIDString, data.SkillPoints), FontSize = 18, Align = TextAnchor.MiddleRight, Font = "robotocondensed-bold.ttf", Color = UIConstants.Text },
                RectTransform = { AnchorMin = "0.5 0.92", AnchorMax = "0.98 1" }
            }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "skills.close", Color = $"{UIConstants.Accent} 0.9" },
                RectTransform = { AnchorMin = "0.96 0.94", AnchorMax = "0.99 0.98" },
                Text = { Text = "âœ•", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, root);

            // --- BRANCHED TREE LAYOUT ---
            
            // Branch 1: Gathering (Left)
            float gatherX = 0.15f;
            AddBranchHeader(elements, root, GetMessage("UI.Branch.Gather", player.UserIDString), gatherX, 0.85f);
            AddSkillNodeTree(elements, root, "unlock.chainsaw", gatherX, 0.70f, player);
            AddSkillTreeLink(elements, root, gatherX, 0.65f, gatherX, 0.58f);
            AddSkillNodeTree(elements, root, "unlock.jackhammer", gatherX, 0.53f, player);

            // Branch 2: Construction (Center)
            float constrX = 0.50f;
            AddBranchHeader(elements, root, GetMessage("UI.Branch.Build", player.UserIDString), constrX, 0.85f);
            AddSkillNodeTree(elements, root, "unlock.quarry", constrX, 0.70f, player);
            AddSkillTreeLink(elements, root, constrX, 0.65f, constrX, 0.58f);
            AddSkillNodeTree(elements, root, "unlock.turbine", constrX, 0.53f, player);

            // Branch 3: Coming Soon (Right)
            float soonX = 0.85f;
            AddBranchHeader(elements, root, GetMessage("UI.Branch.Adv", player.UserIDString), soonX, 0.85f);
            // Example placeholder
            
            CuiHelper.AddUi(player, elements);
        }

        private void AddBranchHeader(CuiElementContainer container, string parent, string text, float x, float y)
        {
            container.Add(new CuiLabel {
                Text = { Text = text, Align = TextAnchor.MiddleCenter, FontSize = 14, Font = "robotocondensed-bold.ttf", Color = $"{UIConstants.Primary} 1" },
                RectTransform = { AnchorMin = $"{x - 0.12f} {y - 0.05f}", AnchorMax = $"{x + 0.12f} {y}" }
            }, parent);
        }

        private void AddSkillTreeLink(CuiElementContainer container, string parent, float x1, float y1, float x2, float y2)
        {
            container.Add(new CuiPanel {
                Image = { Color = "0.3 0.3 0.3 0.5" },
                RectTransform = { AnchorMin = $"{x1 - 0.002f} {y2}", AnchorMax = $"{x1 + 0.002f} {y1}" }
            }, parent);
        }

        private void AddSkillNodeTree(CuiElementContainer container, string parent, string id, float x, float y, BasePlayer player)
        {
            var data = GetPlayerData(player.userID);
            bool unlocked = data.UnlockedSkills.Contains(id);
            
            string name = id;
            int cost = 0;
            if (_config.Skills.TryGetValue(id, out var def))
            {
                name = def.Name;
                cost = def.Cost;
            }
            else
            {
                // Fallback for missing config or locked placeholders
                name = GetMessage("UI.Locked", player.UserIDString);
                cost = 999;
            }
            
            string color = unlocked ? $"{UIConstants.Primary} 0.8" : (data.SkillPoints >= cost ? "0.15 0.15 0.15 0.8" : "0.3 0.1 0.1 0.8");
            string btnText = unlocked ? $"<color=#b7d092>âœ“</color> {name}" : $"{name}\n<color=#999>{cost} pts</color>";
            string cmd = (unlocked || cost > 999) ? "" : $"skills.unlock {id} {cost}";

            container.Add(new CuiButton {
                Button = { Command = cmd, Color = color },
                RectTransform = { AnchorMin = $"{x - 0.1f} {y - 0.06f}", AnchorMax = $"{x + 0.1f} {y + 0.06f}" },
                Text = { Text = btnText, Align = TextAnchor.MiddleCenter, FontSize = 12, Font = "robotocondensed-bold.ttf", Color = UIConstants.Text }
            }, parent);
        }

        [ConsoleCommand("skills.unlock")]
        private void ConsoleUnlock(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string id = arg.GetString(0);

            var data = GetPlayerData(player.userID);
            if (data.UnlockedSkills.Contains(id)) 
            {
                player.ChatMessage(GetMessage("Msg.AlreadyUnlocked", player.UserIDString));
                return;
            }

            if (!_config.Skills.TryGetValue(id, out var def)) return;
            
            if (data.SkillPoints >= def.Cost)
            {
                data.SkillPoints -= def.Cost;
                data.UnlockedSkills.Add(id);
                UnlockBlueprint(player, def);
                player.ChatMessage(GetMessage("Msg.Unlocked", player.UserIDString, def.Name));
                ShowSkillsUI(player);
            }
            else
            {
                player.ChatMessage(GetMessage("Msg.NoPoints", player.UserIDString));
            }
        }

        private void UnlockBlueprint(BasePlayer player, SkillDef skill)
        {
            if (string.IsNullOrEmpty(skill.ItemShortname)) return;
            var itemDef = ItemManager.FindItemDefinition(skill.ItemShortname);
            if (itemDef != null)
            {
                 player.blueprints.Unlock(itemDef);
            }
        }

        [HookMethod("GetLevel")]
        public int API_GetLevel(ulong uid) => GetPlayerData(uid).Level;
#endregion
    }
}

