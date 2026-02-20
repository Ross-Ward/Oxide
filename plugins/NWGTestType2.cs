using Oxide.Core.Libraries;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("NWG Test Type 2", "NWG", "1.0.0")]
    public class NWGTestType2 : RustPlugin
    {
        void Init()
        {
            var types = typeof(Permission).Assembly.GetTypes().Where(t => t.Name.Contains("UserData")).ToList();
            foreach (var t in types)
            {
                Puts($"Found UserData type: {t.FullName}");
            }
        }
    }
}
