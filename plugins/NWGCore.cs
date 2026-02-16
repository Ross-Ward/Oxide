using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGCore", "NWG Team", "3.1.0")]
    [Description("Central Hub for NWG Plugin Suite. Provides performance services and shared infrastructure.")]
    public class NWGCore : RustPlugin
    {
#region Static Access
        public static NWGCore Instance { get; private set; }
        
        // Configuration Constants
        public const string AdminPermission = "nwgcore.admin";
        public const string PluginPrefix = "<color=#b7d092>[NWG]</color>";
        
        // Public API for other plugins to access services
        public T GetService<T>() where T : class => ServiceContainer.Get<T>();
#endregion

#region Service Container
        public class ServiceContainer
        {
            private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

            public static void Register<T>(T service) where T : class
            {
                if (_services.ContainsKey(typeof(T)))
                    _services[typeof(T)] = service;
                else
                    _services.Add(typeof(T), service);
            }

            public static T Get<T>() where T : class
            {
                if (_services.TryGetValue(typeof(T), out var service))
                    return service as T;
                return null;
            }

            public static void Clear() => _services.Clear();
            
            public static IEnumerable<object> GetAll() => _services.Values;
        }
#endregion

#region Interfaces
        public interface IService
        {
            void OnServerInitialized();
            void OnUnload();
        }
        
        public interface IEntityTracker
        {
            int GetCount<T>() where T : BaseEntity;
            List<T> GetEntities<T>() where T : BaseEntity;
        }
#endregion

#region Services Implementation

        /// <summary>
        /// High-Performance Entity Tracker. 
        /// Replaces laggy FindObjectsOfType with Event-Driven caching.
        /// </summary>
        public class EntityTrackerService : IService, IEntityTracker
        {
            // Cache specific types we care about (Bradleys, Helis, Planes, Drops)
            private readonly HashSet<BradleyAPC> _bradleys = new HashSet<BradleyAPC>();
            private readonly HashSet<PatrolHelicopter> _helis = new HashSet<PatrolHelicopter>();
            private readonly HashSet<CargoPlane> _planes = new HashSet<CargoPlane>();
            private readonly HashSet<SupplyDrop> _drops = new HashSet<SupplyDrop>();
            
            // Generic cache storage for unified access
            private readonly Dictionary<Type, object> _cacheMap = new Dictionary<Type, object>();

            public EntityTrackerService()
            {
                // Register caches
                _cacheMap[typeof(BradleyAPC)] = (object)_bradleys;
                _cacheMap[typeof(PatrolHelicopter)] = (object)_helis;
                _cacheMap[typeof(CargoPlane)] = (object)_planes;
                _cacheMap[typeof(SupplyDrop)] = (object)_drops;
            }

            public void OnServerInitialized()
            {
                // Initial Population (Only done once on reload)
                RepopulateCache<BradleyAPC>(_bradleys);
                RepopulateCache<PatrolHelicopter>(_helis);
                RepopulateCache<CargoPlane>(_planes);
                RepopulateCache<SupplyDrop>(_drops);
            }

            public void OnUnload()
            {
                _bradleys.Clear();
                _helis.Clear();
                _planes.Clear();
                _drops.Clear();
            }

            // --- Public API ---

            public int GetCount<T>() where T : BaseEntity
            {
                if (_cacheMap.TryGetValue(typeof(T), out var collection))
                {
                    if (collection is HashSet<T> set) return set.Count;
                }
                return 0;
            }

            public List<T> GetEntities<T>() where T : BaseEntity
            {
                if (_cacheMap.TryGetValue(typeof(T), out var collection))
                {
                    if (collection is HashSet<T> set)
                        return set.ToList();
                }
                return new List<T>();
            }

            // --- Internal Update Logic ---

            public void RegisterEntity(BaseEntity entity)
            {
                if (entity == null) return;
                
                if (entity is BradleyAPC apc) _bradleys.Add(apc);
                else if (entity is PatrolHelicopter heli) _helis.Add(heli);
                else if (entity is CargoPlane plane) _planes.Add(plane);
                else if (entity is SupplyDrop drop) _drops.Add(drop);
            }

            public void UnregisterEntity(BaseEntity entity)
            {
                if (entity == null) return;

                if (entity is BradleyAPC apc) _bradleys.Remove(apc);
                else if (entity is PatrolHelicopter heli) _helis.Remove(heli);
                else if (entity is CargoPlane plane) _planes.Remove(plane);
                else if (entity is SupplyDrop drop) _drops.Remove(drop);
            }

            private void RepopulateCache<T>(HashSet<T> cache) where T : BaseEntity
            {
                cache.Clear();
                foreach (var entity in UnityEngine.Object.FindObjectsOfType<T>())
                {
                    if (entity != null && !entity.IsDestroyed)
                        cache.Add(entity);
                }
            }
        }

#endregion

#region Plugin Lifecycle

        private void Init()
        {
            Instance = this;
            ServiceContainer.Clear();
            
            // Register Services
            ServiceContainer.Register<IEntityTracker>(new EntityTrackerService());
            
            permission.RegisterPermission(AdminPermission, this);
            Puts("[NWG Core] Services Registered.");
        }

        private void OnServerInitialized()
        {
            // Initialize all registered services
            foreach (var service in ServiceContainer.GetAll().OfType<IService>())
            {
                try
                {
                    service.OnServerInitialized();
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to initialize service {service.GetType().Name}: {ex}");
                }
            }

            // Ensure admin group has root permission
            if (!permission.GroupExists("admin")) permission.CreateGroup("admin", "Default Admin Group", 0);
            if (!permission.GroupHasPermission("admin", AdminPermission))
            {
                permission.GrantGroupPermission("admin", AdminPermission, this);
                Puts($"[NWG Core] Granted '{AdminPermission}' to 'admin' group.");
            }
            
            Puts($"[NWG Core] Ready. Tracker monitoring {GetEntityStatusString()}");
        }

        private void Unload()
        {
            foreach (var service in ServiceContainer.GetAll().OfType<IService>())
            {
                service.OnUnload();
            }
            ServiceContainer.Clear();
            Instance = null;
        }

#endregion

#region Hooks (Event Wiring)

        // Sync native Rust admins to Oxide Groups/Permissions
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;

            if (player.IsAdmin || player.IsDeveloper)
            {
                // Ensure native admin flag
                if (!player.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    player.SendNetworkUpdate();
                }

                // Ensure Oxide 'admin' group membership
                if (!permission.UserHasGroup(player.UserIDString, "admin"))
                {
                    permission.AddUserGroup(player.UserIDString, "admin");
                }

                // Ensure core NWG admin permission
                if (!permission.UserHasPermission(player.UserIDString, AdminPermission))
                {
                    permission.GrantUserPermission(player.UserIDString, AdminPermission, this);
                }
            }
        
            // Entity Tracking (BasePlayer is a BaseEntity)
            var tracker = ServiceContainer.Get<EntityTrackerService>();
            tracker?.RegisterEntity(player);
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
             if (entity is BaseEntity baseEntity)
             {
                 var tracker = ServiceContainer.Get<EntityTrackerService>(); // Use concrete type for speed
                 tracker?.RegisterEntity(baseEntity);
             }
        }

        private void OnEntityKilled(BaseNetworkable entity)
        {
            if (entity is BaseEntity baseEntity)
            {
                var tracker = ServiceContainer.Get<EntityTrackerService>();
                tracker?.UnregisterEntity(baseEntity);
            }
        }

#endregion

#region Helpers

        private string GetEntityStatusString()
        {
            var tracker = ServiceContainer.Get<IEntityTracker>();
            if (tracker == null) return "Tracker Error";

            return $"Bradleys: {tracker.GetCount<BradleyAPC>()}, " +
                   $"Helis: {tracker.GetCount<PatrolHelicopter>()}, " +
                   $"Planes: {tracker.GetCount<CargoPlane>()}, " +
                   $"Drops: {tracker.GetCount<SupplyDrop>()}";
        }

#endregion

#region Lang & Config Helpers
        
        [HookMethod("FormatMessage")]
        public string API_FormatMessage(string message)
        {
            return $"{PluginPrefix} {message}";
        }

        [HookMethod("SendColoredMessage")]
        public void API_SendColoredMessage(BasePlayer player, string message)
        {
            player?.ChatMessage(API_FormatMessage(message));
        }

        // Shared Config pattern interface
        public interface INWGConfig
        {
            void SetDefaults();
        }

#endregion
        
#region UI Theme
        public static class Theme
        {
            public const string Primary = "0.718 0.816 0.573 1";   // Sage Green #b7d092
            public const string Secondary = "0.851 0.325 0.31 1"; // Red/Rust #d9534f
            public const string Accent = "1 0.647 0 1";           // Orange #FFA500
            public const string Panel = "0.15 0.15 0.15 0.98";    // Dark Background
            public const string Text = "0.867 0.867 0.867 1";     // Soft White #DDDDDD
            
            public static Dictionary<string, string> GetThemeDict()
            {
                return new Dictionary<string, string>
                {
                    ["Primary"] = Primary,
                    ["Secondary"] = Secondary,
                    ["Accent"] = Accent,
                    ["Panel"] = Panel,
                    ["Text"] = Text
                };
            }
        }
#endregion

#region API Accessors (For External Plugins using Call())
        
        [HookMethod("GetTheme")]
        public Dictionary<string, string> API_GetTheme()
        {
            return Theme.GetThemeDict();
        }
        
        // Example: int count = (int)NWGCore.Call("GetEntityCount", "BradleyAPC");
        [HookMethod("GetEntityCount")]
        public int API_GetEntityCount(string typeName)
        {
            var tracker = ServiceContainer.Get<IEntityTracker>();
            if (tracker == null) return 0;
            
            switch (typeName.ToLower())
            {
                case "bradleyapc": return tracker.GetCount<BradleyAPC>();
                case "patrolhelicopter": return tracker.GetCount<PatrolHelicopter>();
                case "cargoplane": return tracker.GetCount<CargoPlane>();
                case "supplydrop": return tracker.GetCount<SupplyDrop>();
                default: return 0;
            }
        }

        [HookMethod("GetEntities")]
        public List<BaseEntity> API_GetEntities(string typeName)
        {
            var tracker = ServiceContainer.Get<IEntityTracker>();
            if (tracker == null) return new List<BaseEntity>();
            
            switch (typeName.ToLower())
            {
                case "bradleyapc": return tracker.GetEntities<BradleyAPC>().Cast<BaseEntity>().ToList();
                case "patrolhelicopter": return tracker.GetEntities<PatrolHelicopter>().Cast<BaseEntity>().ToList();
                case "cargoplane": return tracker.GetEntities<CargoPlane>().Cast<BaseEntity>().ToList();
                case "supplydrop": return tracker.GetEntities<SupplyDrop>().Cast<BaseEntity>().ToList();
                default: return new List<BaseEntity>();
            }
        }

        [HookMethod("GetGrid")]
        public string API_GetGrid(Vector3 pos)
        {
            return GetGrid(pos);
        }
        
        [HookMethod("GetGridRef")]
        public string API_GetGridRef(Vector3 pos)
        {
             return GetGrid(pos);
        }

        public string GetGrid(Vector3 pos)
        {
            float worldSize = ConVar.Server.worldsize;
            float offset = worldSize / 2f;
            const float cellSize = 150f;
            int maxCell = (int)(worldSize / cellSize) - 1;
            int x = Mathf.Clamp(Mathf.FloorToInt((pos.x + offset) / cellSize), 0, maxCell);
            int z = Mathf.Clamp(Mathf.FloorToInt((pos.z + offset) / cellSize), 0, maxCell);
            
            string col = "";
            int cx = x;
            do
            {
                col = (char)('A' + cx % 26) + col;
                cx = cx / 26 - 1;
            } while (cx >= 0);
            
            return $"{col}{z}";
        }
        
#endregion
    }
}
