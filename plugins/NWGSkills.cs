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
    [Info("NWG Skills", "NWG Team", "1.0.0")]
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
            public HashSet<string> UnlockedSkills = new HashSet<string>();
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
            
            CheckLeveUp(player, data, label);
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
                CheckLeveUp(attacker, data, "Combat");
            }
            // Survival XP for hitting barrels, crates, etc.
            else if (entity is LootContainer || (entity.ShortPrefabName.Contains("barrel") || entity.ShortPrefabName.Contains("crate")))
            {
                float xpGain = info.damageTypes.Total() * 0.2f;
                data.SurvivalXP += xpGain;
                CheckLeveUp(attacker, data, "Survival");
            }
        }

        private void CheckLeveUp(BasePlayer player, PlayerData data, string label)
        {
            const float XP_PER_POINT = 1000f;

            if (data.WoodXP >= XP_PER_POINT) { data.WoodXP -= XP_PER_POINT; data.SkillPoints++; player.ChatMessage($"<color=#b7d092>[NWG Skills]</color> +1 Point in {label}!"); }
            if (data.OreXP >= XP_PER_POINT) { data.OreXP -= XP_PER_POINT; data.SkillPoints++; player.ChatMessage($"<color=#b7d092>[NWG Skills]</color> +1 Point in {label}!"); }
            if (data.StoneXP >= XP_PER_POINT) { data.StoneXP -= XP_PER_POINT; data.SkillPoints++; player.ChatMessage($"<color=#b7d092>[NWG Skills]</color> +1 Point in {label}!"); }
            if (data.CombatXP >= XP_PER_POINT) { data.CombatXP -= XP_PER_POINT; data.SkillPoints++; player.ChatMessage($"<color=#b7d092>[NWG Skills]</color> +1 Point in {label}!"); }
            if (data.SurvivalXP >= XP_PER_POINT) { data.SurvivalXP -= XP_PER_POINT; data.SkillPoints++; player.ChatMessage($"<color=#b7d092>[NWG Skills]</color> +1 Point in {label}!"); }
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
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" },
                CursorEnabled = true
            }, "Overlay", "NWG_Skills_UI");

            // Header
            elements.Add(new CuiPanel {
                Image = { Color = "0.4 0.6 0.2 0.3" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, root);

            elements.Add(new CuiLabel {
                Text = { Text = "SURVIVAL SKILLS", FontSize = 24, Align = TextAnchor.MiddleLeft, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.02 0.92", AnchorMax = "0.5 1" }
            }, root);

            elements.Add(new CuiLabel {
                Text = { Text = $"AVAILABLE POINTS: <color=#b7d092>{data.SkillPoints}</color>", FontSize = 18, Align = TextAnchor.MiddleRight, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0.5 0.92", AnchorMax = "0.98 1" }
            }, root);

            // Close Button
            elements.Add(new CuiButton {
                Button = { Command = "skills.close", Color = "0.8 0.1 0.1 0.9" },
                RectTransform = { AnchorMin = "0.96 0.94", AnchorMax = "0.99 0.98" },
                Text = { Text = "✕", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, root);

            // --- BRANCHED TREE LAYOUT ---
            
            // Branch 1: Gathering (Left)
            float gatherX = 0.15f;
            AddBranchHeader(elements, root, "GATHERING", gatherX, 0.85f);
            AddSkillNodeTree(elements, root, "Chainsaw BP", "unlock.chainsaw", 5, gatherX, 0.70f, player);
            AddSkillTreeLink(elements, root, gatherX, 0.65f, gatherX, 0.58f);
            AddSkillNodeTree(elements, root, "Jackhammer BP", "unlock.jackhammer", 5, gatherX, 0.53f, player);

            // Branch 2: Construction (Center)
            float constrX = 0.50f;
            AddBranchHeader(elements, root, "CONSTRUCTION", constrX, 0.85f);
            AddSkillNodeTree(elements, root, "Quarry BP", "unlock.quarry", 10, constrX, 0.70f, player);
            AddSkillTreeLink(elements, root, constrX, 0.65f, constrX, 0.58f);
            AddSkillNodeTree(elements, root, "Wind Turbine BP", "unlock.turbine", 15, constrX, 0.53f, player);

            // Branch 3: Coming Soon (Right)
            float soonX = 0.85f;
            AddBranchHeader(elements, root, "ADVANCED", soonX, 0.85f);
            AddSkillNodeTree(elements, root, "Locked...", "locked", 999, soonX, 0.70f, player);

            CuiHelper.AddUi(player, elements);
        }

        private void AddBranchHeader(CuiElementContainer container, string parent, string text, float x, float y)
        {
            container.Add(new CuiLabel {
                Text = { Text = text, Align = TextAnchor.MiddleCenter, FontSize = 14, Font = "robotocondensed-bold.ttf", Color = "0.4 0.6 0.2 1" },
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

        private void AddSkillNodeTree(CuiElementContainer container, string parent, string name, string id, int cost, float x, float y, BasePlayer player)
        {
            var data = GetPlayerData(player.userID);
            bool unlocked = data.UnlockedSkills.Contains(id);
            string color = unlocked ? "0.4 0.6 0.2 0.8" : (data.SkillPoints >= cost ? "0.15 0.15 0.15 0.8" : "0.3 0.1 0.1 0.8");
            string btnText = unlocked ? $"<color=#b7d092>✓</color> {name}" : $"{name}\n<color=#999>{cost} pts</color>";
            string cmd = (unlocked || id == "locked") ? "" : $"skills.unlock {id} {cost}";

            container.Add(new CuiButton {
                Button = { Command = cmd, Color = color },
                RectTransform = { AnchorMin = $"{x - 0.1f} {y - 0.06f}", AnchorMax = $"{x + 0.1f} {y + 0.06f}" },
                Text = { Text = btnText, Align = TextAnchor.MiddleCenter, FontSize = 12, Font = "robotocondensed-bold.ttf" }
            }, parent);
        }

        [ConsoleCommand("skills.unlock")]
        private void ConsoleUnlock(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string id = arg.GetString(0);
            int cost = arg.GetInt(1);

            var data = GetPlayerData(player.userID);
            if (data.SkillPoints >= cost)
            {
                data.SkillPoints -= cost;
                data.UnlockedSkills.Add(id);
                UnlockBlueprint(player, id);
                player.ChatMessage($"Successfully unlocked {id}!");
                ShowSkillsUI(player);
            }
            else
            {
                player.ChatMessage("Not enough skill points.");
            }
        }

        private void UnlockBlueprint(BasePlayer player, string id)
        {
            // Map ID to actual blueprint. Simplified for demo.
            if (id == "unlock.chainsaw") player.blueprints.Unlock(ItemManager.FindItemDefinition("chainsaw"));
            if (id == "unlock.jackhammer") player.blueprints.Unlock(ItemManager.FindItemDefinition("jackhammer"));
            if (id == "unlock.quarry") player.blueprints.Unlock(ItemManager.FindItemDefinition("mining.quarry"));
        }
        #endregion
    }
}
