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
    [Info("NWGQuests", "NWG Team", "1.0.0")]
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
#endregion

#region Config
        private class PluginConfig
        {
            public List<QuestDef> Quests = new List<QuestDef>();
        }
        private PluginConfig _config;

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            _config.Quests = new List<QuestDef>
            {
                new QuestDef {
                    ID = "q_collector",
                    Title = "The Grand Collector",
                    CashReward = 5000,
                    Stages = new List<QuestStage> {
                        new QuestStage { Description = "Gather 1000 Wood", TargetType = "gather", TargetID = "Wood", RequiredAmount = 1000 },
                        new QuestStage { Description = "Gather 500 Stone", TargetType = "gather", TargetID = "Stone", RequiredAmount = 500 }
                    }
                },
                new QuestDef {
                    ID = "q_slayer",
                    Title = "Zombie Slayer",
                    CashReward = 7500,
                    Stages = new List<QuestStage> {
                        new QuestStage { Description = "Kill 5 Zombies", TargetType = "kill", TargetID = "murderer", RequiredAmount = 5 }
                    }
                }
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

#region Stored Data
        private Dictionary<ulong, PlayerQuestData> _playerData = new Dictionary<ulong, PlayerQuestData>();
        // _quests removed, use _config.Quests

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWG_Quests", _playerData);
        private void LoadData() => _playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerQuestData>>("NWG_Quests") ?? new Dictionary<ulong, PlayerQuestData>();
#endregion

#region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["UI.Title"] = "AVAILABLE QUESTS",
                ["UI.Completed"] = "COMPLETED",
                ["UI.InProgress"] = "IN PROGRESS: {0}/{1}",
                ["UI.ClickToStart"] = "CLICK TO START",
                ["Msg.Advanced"] = "<color=#b7d092>[NWG] QUEST ADVANCED:</color> {0}",
                ["Msg.Completed"] = "<color=#b7d092>[NWG] QUEST COMPLETED!</color> You finished <color=#FFA500>'{0}'</color> and earned <color=#b7d092>${1}</color>!",
                ["Msg.Started"] = "<color=#b7d092>[NWG]</color> Quest Started: <color=#FFA500>{0}</color>"
            }, this);
        }
        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

        public class UIConstants
        {
            public const string Panel = "0.15 0.15 0.15 0.98"; 
            public const string Header = "0.1 0.1 0.1 1"; 
            public const string Primary = "0.718 0.816 0.573 1"; // Sage Green
            public const string Secondary = "0.851 0.325 0.31 1"; // Red
            public const string Button = "0.2 0.2 0.2 0.8";
            public const string Text = "0.867 0.867 0.867 1";
        }

#region Lifecycle
        private void Init()
        {
            LoadData();
        }

        private void Unload() => SaveData();


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

            var quest = _config.Quests.FirstOrDefault(q => q.ID == data.ActiveQuestID);
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
                player.ChatMessage(GetMessage("Msg.Advanced", player.UserIDString, quest.Stages[data.CurrentStageIndex].Description));
            }
        }

        private void CompleteQuest(BasePlayer player, PlayerQuestData data, QuestDef quest)
        {
            data.CompletedQuests.Add(quest.ID);
            data.ActiveQuestID = null;
            
            // Give Reward (Call NWGMarket)
            Interface.CallHook("DepositMoney", player.UserIDString, quest.CashReward);
            
            player.ChatMessage(GetMessage("Msg.Completed", player.UserIDString, quest.Title, quest.CashReward));
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
                Image = { Color = UIConstants.Panel },
                RectTransform = { AnchorMin = "0.2 0.2", AnchorMax = "0.8 0.8" },
                CursorEnabled = true
            }, "Overlay", "NWG_Quest_UI");

            // Header
            elements.Add(new CuiPanel {
                Image = { Color = UIConstants.Header },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, root);

            elements.Add(new CuiLabel {
                Text = { Text = GetMessage("UI.Title", player.UserIDString), FontSize = 20, Align = TextAnchor.MiddleCenter, Font = "robotocondensed-bold.ttf", Color = UIConstants.Primary },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, root);

            float y = 0.8f;
            foreach (var quest in _config.Quests)
            {
                var data = GetPlayerData(player.userID);
                bool completed = data.CompletedQuests.Contains(quest.ID);
                bool active = data.ActiveQuestID == quest.ID;
                
                string status = completed ? GetMessage("UI.Completed", player.UserIDString) : (active ? GetMessage("UI.InProgress", player.UserIDString, data.CurrentProgress, quest.Stages[data.CurrentStageIndex].RequiredAmount) : GetMessage("UI.ClickToStart", player.UserIDString));
                string btnText = $"{quest.Title}\n<size=12>{status}</size>";
                string color = completed ? "0.3 0.3 0.3 0.8" : (active ? UIConstants.Primary.Replace("1", "0.8") : UIConstants.Button); // Dim if completed, Primary if active, Dark if avail
                string cmd = (completed || active) ? "" : $"quests.start {quest.ID}";

                elements.Add(new CuiButton {
                    Button = { Command = cmd, Color = color },
                    RectTransform = { AnchorMin = $"0.1 {y - 0.08f}", AnchorMax = $"0.9 {y}" },
                    Text = { Text = btnText, Align = TextAnchor.MiddleCenter, Color = UIConstants.Text }
                }, root);
                y -= 0.1f;
            }

            CuiHelper.AddUi(player, elements);
        }
#endregion

#region Commands
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
            
            player.ChatMessage(GetMessage("Msg.Started", player.UserIDString, qid));
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

