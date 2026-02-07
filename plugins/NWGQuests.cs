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
    [Info("NWG Quests", "NWG Team", "1.0.0")]
    [Description("Multi-stage quests with rewards.")]
    public class NWGQuests : RustPlugin
    {
        #region Data
        private class QuestDef
        {
            public string ID;
            public string Title;
            public List<QuestStage> Stages = new List<QuestStage>();
            public double CashReward;
        }

        private class QuestStage
        {
            public string Description;
            public string TargetType; // "gather", "kill", "visit"
            public string TargetID; // "wood", "murderer", "outpost"
            public int RequiredAmount;
        }

        private class PlayerQuestData
        {
            public string ActiveQuestID;
            public int CurrentStageIndex;
            public int CurrentProgress;
            public HashSet<string> CompletedQuests = new HashSet<string>();
        }

        private Dictionary<ulong, PlayerQuestData> _playerData = new Dictionary<ulong, PlayerQuestData>();
        private List<QuestDef> _quests = new List<QuestDef>();

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Quests", _playerData);
        private void LoadData() => _playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerQuestData>>("NWG_Quests") ?? new Dictionary<ulong, PlayerQuestData>();
        #endregion

        #region Lifecycle
        private void Init()
        {
            LoadData();
            SetupQuests();
        }

        private void Unload() => SaveData();

        private void SetupQuests()
        {
            _quests.Add(new QuestDef {
                ID = "q_collector",
                Title = "The Grand Collector",
                CashReward = 5000,
                Stages = new List<QuestStage> {
                    new QuestStage { Description = "Gather 1000 Wood", TargetType = "gather", TargetID = "Wood", RequiredAmount = 1000 },
                    new QuestStage { Description = "Gather 500 Stone", TargetType = "gather", TargetID = "Stone", RequiredAmount = 500 }
                }
            });

            _quests.Add(new QuestDef {
                ID = "q_slayer",
                Title = "Zombie Slayer",
                CashReward = 7500,
                Stages = new List<QuestStage> {
                    new QuestStage { Description = "Kill 5 Zombies", TargetType = "kill", TargetID = "murderer", RequiredAmount = 5 }
                }
            });
        }
        #endregion

        #region Hooks
        private void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            if (player == null) return;
            CheckProgress(player, "gather", item.info.displayName.english, item.amount);
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var attacker = info?.Initiator as BasePlayer;
            if (attacker == null || entity == null) return;
            CheckProgress(attacker, "kill", entity.ShortPrefabName, 1);
        }

        private void CheckProgress(BasePlayer player, string type, string id, int amount)
        {
            if (!_playerData.TryGetValue(player.userID, out var data) || string.IsNullOrEmpty(data.ActiveQuestID)) return;

            var quest = _quests.FirstOrDefault(q => q.ID == data.ActiveQuestID);
            if (quest == null) return;

            var stage = quest.Stages[data.CurrentStageIndex];
            if (stage.TargetType == type && (stage.TargetID == id || id.Contains(stage.TargetID)))
            {
                data.CurrentProgress += amount;
                if (data.CurrentProgress >= stage.RequiredAmount)
                {
                    AdvanceQuest(player, data, quest);
                }
            }
        }

        private void AdvanceQuest(BasePlayer player, PlayerQuestData data, QuestDef quest)
        {
            data.CurrentStageIndex++;
            data.CurrentProgress = 0;

            if (data.CurrentStageIndex >= quest.Stages.Count)
            {
                CompleteQuest(player, data, quest);
            }
            else
            {
                player.ChatMessage($"<color=#b7d092>QUEST ADVANCED:</color> {quest.Stages[data.CurrentStageIndex].Description}");
            }
        }

        private void CompleteQuest(BasePlayer player, PlayerQuestData data, QuestDef quest)
        {
            data.CompletedQuests.Add(quest.ID);
            data.ActiveQuestID = null;
            
            // Give Reward (Call NWGMarket)
            Interface.CallHook("DepositMoney", player.UserIDString, quest.CashReward);
            
            player.ChatMessage($"<color=#b7d092>QUEST COMPLETED!</color> You finished '{quest.Title}' and earned ${quest.CashReward}!");
        }
        #endregion

        #region UI
        [ChatCommand("quests")]
        private void CmdQuests(BasePlayer player)
        {
            ShowQuestUI(player);
        }

        private void ShowQuestUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "NWG_Quest_UI");
            var elements = new CuiElementContainer();
            
            var root = elements.Add(new CuiPanel {
                Image = { Color = "0.05 0.05 0.05 0.95" },
                RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" },
                CursorEnabled = true
            }, "Overlay", "NWG_Quest_UI");

            // Header
            elements.Add(new CuiLabel {
                Text = { Text = "NWG QUESTS", FontSize = 20, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, root);

            float y = 0.8f;
            foreach (var quest in _quests)
            {
                var data = GetPlayerData(player.userID);
                bool completed = data.CompletedQuests.Contains(quest.ID);
                bool active = data.ActiveQuestID == quest.ID;
                
                string btnText = completed ? $"{quest.Title} (COMPLETED)" : (active ? $"{quest.Title} (PROG: {data.CurrentProgress}/{quest.Stages[data.CurrentStageIndex].RequiredAmount})" : quest.Title);
                string color = completed ? "0.3 0.3 0.3 0.8" : (active ? "0.4 0.6 0.2 0.8" : "0.2 0.4 0.6 0.8");
                string cmd = (completed || active) ? "" : $"quests.start {quest.ID}";

                elements.Add(new CuiButton {
                    Button = { Command = cmd, Color = color },
                    RectTransform = { AnchorMin = $"0.1 {y - 0.08f}", AnchorMax = $"0.9 {y}" },
                    Text = { Text = btnText, Align = TextAnchor.MiddleCenter }
                }, root);
                y -= 0.1f;
            }

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("quests.start")]
        private void ConsoleStart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            string qid = arg.GetString(0);
            
            var data = GetPlayerData(player.userID);
            data.ActiveQuestID = qid;
            data.CurrentStageIndex = 0;
            data.CurrentProgress = 0;
            
            player.ChatMessage($"Quest Started: {qid}");
            ShowQuestUI(player);
        }

        private PlayerQuestData GetPlayerData(ulong uid)
        {
            if (!_playerData.TryGetValue(uid, out var data))
                _playerData[uid] = data = new PlayerQuestData();
            return data;
        }
        #endregion
    }
}
