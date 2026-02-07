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
    [Info("NWG Racing", "NWG Team", "1.0.0")]
    [Description("Organized racing events with checkpoints and rewards.")]
    public class NWGRacing : RustPlugin
    {
        private class RaceSession
        {
            public List<ulong> Participants = new List<ulong>();
            public List<Vector3> Checkpoints = new List<Vector3>();
            public bool IsActive = false;
            public float StartTime;
        }

        private RaceSession _currentRace;

        [ChatCommand("race.start")]
        private void CmdRaceStart(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;
            
            _currentRace = new RaceSession
            {
                IsActive = true,
                StartTime = Time.realtimeSinceStartup
            };

            // Simplified: Race starts at current admin pos, finish is 500m away
            _currentRace.Checkpoints.Add(player.transform.position + (player.transform.forward * 500));
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                p.ChatMessage("<color=#51CF66>[NWG RACE]</color> A new race has started! Get to the marked finish line fast!");
                // Draw a marker (simplified)
                p.SendConsoleCommand("ddraw.sphere", 60f, Color.green, _currentRace.Checkpoints[0], 5f);
            }

            timer.Once(300f, EndRace);
        }

        private void EndRace()
        {
            if (_currentRace == null || !_currentRace.IsActive) return;
            _currentRace.IsActive = false;
            Puts("[NWG Race] Race ended due to time.");
        }

        private void OnPlayerMove(BasePlayer player)
        {
            if (_currentRace == null || !_currentRace.IsActive) return;

            foreach (var cp in _currentRace.Checkpoints)
            {
                if (Vector3.Distance(player.transform.position, cp) < 10f)
                {
                    WinRace(player);
                    break;
                }
            }
        }

        private void WinRace(BasePlayer player)
        {
            _currentRace.IsActive = false;
            float timeTaken = Time.realtimeSinceStartup - _currentRace.StartTime;
            
            foreach (var p in BasePlayer.activePlayerList)
            {
                p.ChatMessage($"<color=#51CF66>[NWG RACE]</color> {player.displayName} WON the race in {timeTaken:F2}s!");
            }

            // Reward (Simplified: Giving scrap)
            player.GiveItem(ItemManager.CreateByName("scrap", 500));
        }
    }
}
