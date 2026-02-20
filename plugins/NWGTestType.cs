using System;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("NWGTestType", "Agent", "1.0.0")]
    class NWGTestType : RustPlugin
    {
        void Init()
        {
            var prop = typeof(BuildingPrivlidge).GetField("authorizedPlayers");
            if (prop != null)
                Puts($"authorizedPlayers Field type: {prop.FieldType}");
            
            var prop2 = typeof(BuildingPrivlidge).GetProperty("authorizedPlayers");
            if (prop2 != null)
                Puts($"authorizedPlayers Property type: {prop2.PropertyType}");
        }
    }
}
