// Forced Recompile: 2026-02-07 11:15
using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NWGRacing", "NWG Team", "1.0.0")]
    [Description("Organized racing events with checkpoints and rewards.")]
    public class NWGRacing : RustPlugin
    {
        private class RaceSession
        {
            public List<ulong> Participants = new List<ulong>();
            public List<Vector3> Checkpoints = new List<Vector3>();
            public bool IsActive = false;
            public float StartTime;
            public Timer CheckTimer;
        }

        private RaceSession _currentRace;

#region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["RaceStarted"] = "<color=#b7d092>[NWG]</color> <color=#b7d092>A new race has started!</color> Get to the marked finish line fast!",
                ["RaceEnded"] = "<color=#d9534f>[NWG]</color> Race ended due to time.",
                ["RaceWon"] = "<color=#b7d092>[NWG]</color> <color=#FFA500>{0}</color> <color=#b7d092>WON</color> the race in <color=#FFA500>{1:F2}s</color>!",
                ["NoPermission"] = "<color=#d9534f>[NWG]</color> You do not have permission to start a race."
            }, this);
        }

        private string GetMessage(string key, string userId, params object[] args) => string.Format(lang.GetMessage(key, this, userId), args);
#endregion

        [ChatCommand("startrace")]
        private void CmdRaceStart(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) { player.ChatMessage(GetMessage("NoPermission", player.UserIDString)); return; }
            
            _currentRace = new RaceSession
            {
                IsActive = true,
                StartTime = Time.realtimeSinceStartup
            };

            // Simplified: Race starts at current admin pos, finish is 500m away
            _currentRace.Checkpoints.Add(player.transform.position + (player.transform.forward * 500));
            
            // Start check loop
            _currentRace.CheckTimer = timer.Every(0.5f, CheckRaceLoop);

            foreach (var p in BasePlayer.activePlayerList)
            {
                p.ChatMessage(GetMessage("RaceStarted", p.UserIDString));
                // Draw a marker (simplified)
                p.SendConsoleCommand("ddraw.sphere", 60f, Color.green, _currentRace.Checkpoints[0], 5f);
            }

            timer.Once(300f, EndRace);
        }

        private void EndRace()
        {
            if (_currentRace == null || !_currentRace.IsActive) return;
            _currentRace.IsActive = false;
            _currentRace.CheckTimer?.Destroy();
            Puts(GetMessage("RaceEnded", null));
        }

        private void CheckRaceLoop()
        {
            if (_currentRace == null || !_currentRace.IsActive || _currentRace.Checkpoints.Count == 0) return;
            
            var finishLine = _currentRace.Checkpoints[0];

            foreach(var player in BasePlayer.activePlayerList)
            {
                if (Vector3.Distance(player.transform.position, finishLine) < 10f)
                {
                    WinRace(player);
                    break;
                }
            }
        }

        private void WinRace(BasePlayer player)
        {
            _currentRace.CheckTimer?.Destroy();
            float timeTaken = Time.realtimeSinceStartup - _currentRace.StartTime;
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                p.ChatMessage(GetMessage("RaceWon", p.UserIDString, player.displayName, timeTaken));
            }

            // Reward (Simplified: Giving scrap)
            player.GiveItem(ItemManager.CreateByName("scrap", 500));
        }
    }
}

