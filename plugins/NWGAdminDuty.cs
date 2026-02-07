using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG Admin Duty", "NWG Team", "1.0.0")]
    [Description("Allows staff to toggle admin powers.")]
    public class NWGAdminDuty : RustPlugin
    {
        private const string PermUsage = "nwgadminduty.use";
        private HashSet<ulong> _activeAdmins = new HashSet<ulong>();

        private void Init()
        {
            permission.RegisterPermission(PermUsage, this);
        }

        [ChatCommand("goadminduty")]
        private void CmdGoAdminDuty(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            // Allow hardcoded owners to bypass permission check initially
            bool isOwner = (player.UserIDString == "76561198049647064" || player.UserIDString == "76561198116726532");
            if (!isOwner && !permission.UserHasPermission(player.UserIDString, PermUsage))
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            if (_activeAdmins.Contains(player.userID))
            {
                // Disable Admin Duty
                _activeAdmins.Remove(player.userID);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdate();
                
                // Remove from admin group if we want strict separation
                // permission.RemoveUserGroup(player.UserIDString, "admin"); 

                player.ChatMessage("<color=#ff5555>You are now OFF Admin Duty.</color>");
                player.ChatMessage("Admin powers and God mode disabled.");
                
                // Auto-disable god/vanish if active
                player.ConsoleRun("god false");
                player.ConsoleRun("vanish false");
            }
            else
            {
                // Enable Admin Duty
                _activeAdmins.Add(player.userID);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdate();

                // Ensure they have permissions
                if (!permission.UserHasGroup(player.UserIDString, "admin"))
                    permission.AddUserGroup(player.UserIDString, "admin");
                
                permission.GrantUserPermission(player.UserIDString, "nwgcore.admin", this);

                player.ChatMessage("<color=#55ff55>You are now ON Admin Duty!</color>");
                player.ChatMessage("Access granted to /dungeon, /warp, /god, /vanish, etc.");
            }
        }
    }
}
