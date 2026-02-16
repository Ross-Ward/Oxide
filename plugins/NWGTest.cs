using Oxide.Plugins;

namespace Oxide.Plugins
{
    [Info("NWGTest", "NWG Team", "1.0.0")]
    public class NWGTest : RustPlugin
    {
        private void Init()
        {
            Puts("NWG Test Plugin Initialized!");
            
            if (Config == null)
            {
                Puts("Config property is NULL. Attempting manual load...");
                LoadConfig();
            }

            if (Config != null)
            {
                Puts("Config is now available.");
                Config.Settings.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore;
            }
            else
            {
                Puts("Config is STILL NULL. Initialization may be incomplete.");
            }
        }

        private void Loaded()
        {
            Puts("NWG Test Plugin Loaded!");
        }
    }
}

