using System;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NWGPermissionMigrator", "NWG", "1.0.0")]
    [Description("Migrates legacy NWG permission names to new plugin-prefixed names")]
    public class NWGPermissionMigrator : RustPlugin
    {
        private static readonly List<(string oldPrefix, string newPrefix)> PrefixMappings = new List<(string, string)>
        {
            ("nwg.bank.", "nwgbank."),
            ("nwg.bettertc.", "nwgbettertc."),
            ("bettertc.", "nwgbettertc."),
            ("nwg.buriedtreasure.", "nwgburiedtreasure."),
            ("nwg.crafts.", "nwgcrafts."),
            ("crafts.", "nwgcrafts."),
            ("nwg.economy.", "nwgeconomy."),
            ("nwg.rareminerals.", "nwgextractionrareminerals."),
            ("nwg.health.", "nwghealth."),
            ("nwg.helis.", "nwghelis."),
            ("nwg.instantairdrop.", "nwginstantairdrop."),
            ("nwg.kitcontroller.", "nwgkitcontroller."),
            ("kitcontroller.", "nwgkitcontroller."),
            ("nwg.kits.", "nwgkits."),
            ("nwg.paratroopers.", "nwgparatroopers."),
            ("paratroopers.", "nwgparatroopers."),
            ("nwg.raiders.", "nwgraiders."),
            ("nwg.teams.", "nwgteams."),
            ("nwg.trade.", "nwgtrade.")
        };

        [ConsoleCommand("nwg.perm.migrate")]
        private void CmdMigrate(ConsoleSystem.Arg arg)
        {
            var apply = arg.Args != null && arg.Args.Length > 0 && arg.Args[0].Equals("apply", StringComparison.OrdinalIgnoreCase);
            var report = Migrate(apply);

            if (!apply)
            {
                SendReply(arg, $"[NWGPermissionMigrator] Dry-run complete. {report.potential} grants can be migrated. Run: nwg.perm.migrate apply");
                return;
            }

            SendReply(arg, $"[NWGPermissionMigrator] Applied. User grants: {report.userApplied}, Group grants: {report.groupApplied}, Skipped existing: {report.skipped}, Failed: {report.failed}");
        }

        private (int potential, int userApplied, int groupApplied, int skipped, int failed) Migrate(bool apply)
        {
            var potential = 0;
            var userApplied = 0;
            var groupApplied = 0;
            var skipped = 0;
            var failed = 0;

            var groups = permission.GetGroups() ?? Array.Empty<string>();
            foreach (var group in groups)
            {
                var perms = permission.GetGroupPermissions(group, false) ?? Array.Empty<string>();
                foreach (var oldPerm in perms)
                {
                    var mapped = MapPermission(oldPerm);
                    if (mapped == null || mapped == oldPerm)
                        continue;

                    potential++;

                    if (permission.GroupHasPermission(group, mapped))
                    {
                        skipped++;
                        continue;
                    }

                    if (!apply)
                        continue;

                    try
                    {
                        permission.GrantGroupPermission(group, mapped, this);
                        groupApplied++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }

            var users = BasePlayer.activePlayerList.Select(p => p.UserIDString)
                .Concat(BasePlayer.sleepingPlayerList.Select(p => p.UserIDString))
                .Distinct()
                .ToList();

            foreach (var userId in users)
            {
                var perms = permission.GetUserPermissions(userId) ?? Array.Empty<string>();
                foreach (var oldPerm in perms)
                {
                    var mapped = MapPermission(oldPerm);
                    if (mapped == null || mapped == oldPerm)
                        continue;

                    potential++;

                    if (permission.UserHasPermission(userId, mapped))
                    {
                        skipped++;
                        continue;
                    }

                    if (!apply)
                        continue;

                    try
                    {
                        permission.GrantUserPermission(userId, mapped, this);
                        userApplied++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }

            return (potential, userApplied, groupApplied, skipped, failed);
        }

        private static string MapPermission(string permissionName)
        {
            if (string.IsNullOrEmpty(permissionName))
                return null;

            foreach (var (oldPrefix, newPrefix) in PrefixMappings)
            {
                if (permissionName.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    return newPrefix + permissionName.Substring(oldPrefix.Length);
            }

            return null;
        }
    }
}
