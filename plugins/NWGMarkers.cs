using System;
using System.Collections.Generic;
using System.Text;
using Oxide.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWG Markers", "NWG Team", "1.0.0")]
    [Description("Create and manage custom map markers with full control over appearance and behavior.")]
    public class NWGMarkers : RustPlugin
    {
        #region Configuration
        private class PluginConfig
        {
            [JsonProperty("Default Marker Settings")]
            public MarkerDefaults Defaults = new MarkerDefaults();
            
            public class MarkerDefaults
            {
                [JsonProperty("Radius")]
                public float Radius = 0.4f;
                [JsonProperty("Alpha (Transparency)")]
                public float Alpha = 0.75f;
                [JsonProperty("Refresh Rate (seconds)")]
                public float RefreshRate = 30f;
                [JsonProperty("Color (Hex)")]
                public string Color = "00FFFF";
                [JsonProperty("Outline Color (Hex)")]
                public string OutlineColor = "00FFFFFF";
            }
        }
        private PluginConfig _config;
        #endregion

        #region State
        private const string GENERIC_PREFAB = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string VENDING_PREFAB = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
        private const string PERM_USE = "nwgmarkers.use";
        private const string PERM_ADMIN = "nwgmarkers.admin";
        
        private readonly List<CustomMapMarker> _markers = new List<CustomMapMarker>();
        private StringBuilder _sb;
        #endregion

        #region Data
        private StoredData _data;
        private class StoredData
        {
            public List<SavedMarker> Markers = new List<SavedMarker>();
        }
        
        private class SavedMarker
        {
            public string Name;
            public string DisplayName;
            public Vector3 Position;
            public float Radius;
            public float Alpha;
            public string Color;
            public string OutlineColor;
            public int Duration;
            public float RefreshRate;
        }
        
        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("NWGMarkers", _data);
        void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NWGMarkers"); }
            catch { _data = new StoredData(); }
            if (_data == null) _data = new StoredData();
        }
        #endregion

        #region Lifecycle
        void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_ADMIN, this);
            LoadConfigVars();
            LoadData();
        }

        void OnServerInitialized()
        {
            _sb = new StringBuilder();
            LoadSavedMarkers();
        }

        void Unload()
        {
            SaveData();
            RemoveAllMarkers();
            _sb = null;
        }

        void OnPlayerConnected(BasePlayer player)
        {
            foreach (var marker in _markers)
                if (marker != null) marker.UpdateMarkers();
        }

        void LoadConfigVars()
        {
            try { _config = Config.ReadObject<PluginConfig>(); if (_config == null) LoadDefaultConfig(); }
            catch { LoadDefaultConfig(); }
        }
        protected override void LoadDefaultConfig() { Puts("Creating new config for NWG Markers"); _config = new PluginConfig(); SaveConfig(); }
        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Commands
        [ChatCommand("marker")]
        void CmdMarker(BasePlayer player, string cmd, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERM_USE))
            {
                SendMsg(player, "NoPermission");
                return;
            }

            if (args == null || args.Length == 0)
            {
                SendMsg(player, "Usage");
                return;
            }

            switch (args[0].ToLower())
            {
                case "add":
                case "create":
                    if (args.Length < 9)
                    {
                        SendMsg(player, "UsageAdd");
                        return;
                    }
                    
                    var saved = new SavedMarker
                    {
                        Name = args[1],
                        Duration = Convert.ToInt32(args[2]),
                        RefreshRate = Convert.ToSingle(args[3]),
                        Radius = Convert.ToSingle(args[4]),
                        DisplayName = args[5],
                        Color = args[6],
                        OutlineColor = args[7],
                        Alpha = Convert.ToSingle(args[8]),
                        Position = player.transform.position
                    };

                    CreateCustomMarker(saved);
                    _data.Markers.Add(saved);
                    SaveData();
                    SendMsg(player, "MarkerAdded", saved.DisplayName, saved.Position);
                    break;

                case "remove":
                case "delete":
                    if (args.Length < 2)
                    {
                        SendMsg(player, "UsageRemove");
                        return;
                    }
                    RemoveCustomMarker(args[1], player);
                    break;

                case "list":
                    if (_data.Markers.Count == 0)
                    {
                        SendMsg(player, "NoMarkers");
                        return;
                    }
                    _sb.Clear();
                    _sb.AppendLine(Lang("MarkerList", player.UserIDString));
                    foreach (var m in _data.Markers)
                        _sb.AppendLine(Lang("MarkerListEntry", player.UserIDString, m.Name, m.DisplayName, m.Position));
                    player.ChatMessage(_sb.ToString());
                    break;

                default:
                    SendMsg(player, "Usage");
                    break;
            }
        }
        #endregion

        #region Marker Management
        T GetOrAddComponent<T>(GameObject obj) where T : Component
        {
            var comp = obj.GetComponent<T>();
            if (comp == null) comp = obj.AddComponent<T>();
            return comp;
        }

        void CreateMarker(Vector3 position, int duration, float refreshRate, string name, string displayName, 
            float radius, float alpha, string colorMarker, string colorOutline, bool playerPlaced = false)
        {
            var marker = new GameObject().AddComponent<CustomMapMarker>();
            marker.Name = name;
            marker.DisplayName = displayName;
            marker.Radius = radius;
            marker.Alpha = alpha;
            marker.Position = position;
            marker.Duration = duration;
            marker.RefreshRate = refreshRate;
            marker.PlayerPlaced = playerPlaced;
            ColorUtility.TryParseHtmlString($"#{colorMarker}", out marker.Color1);
            ColorUtility.TryParseHtmlString($"#{colorOutline}", out marker.Color2);
            _markers.Add(marker);
        }

        void CreateMarker(BaseEntity entity, int duration, float refreshRate, string name, string displayName,
            float radius, float alpha, string colorMarker, string colorOutline)
        {
            var marker = GetOrAddComponent<CustomMapMarker>(entity.gameObject);
            marker.Name = name;
            marker.DisplayName = displayName;
            marker.Radius = radius;
            marker.Alpha = alpha;
            marker.RefreshRate = refreshRate;
            marker.Parent = entity;
            marker.Position = entity.transform.position;
            marker.Duration = duration;
            ColorUtility.TryParseHtmlString($"#{colorMarker}", out marker.Color1);
            ColorUtility.TryParseHtmlString($"#{colorOutline}", out marker.Color2);
            _markers.Add(marker);
        }

        void CreateCustomMarker(SavedMarker def)
        {
            CreateMarker(def.Position, def.Duration, def.RefreshRate, def.Name, def.DisplayName,
                def.Radius, def.Alpha, def.Color, def.OutlineColor, true);
        }

        void RemoveMarker(string name)
        {
            foreach (var marker in _markers.ToArray())
                if (marker.Name == name) UnityEngine.Object.Destroy(marker);
        }

        void RemoveCustomMarker(string name, BasePlayer player = null)
        {
            int count = 0;
            foreach (var marker in _markers.ToArray())
            {
                if (marker.Name == name && marker.PlayerPlaced)
                {
                    UnityEngine.Object.Destroy(marker);
                    count++;
                }
            }
            _data.Markers.RemoveAll(x => x.Name == name);
            SaveData();
            if (player != null) SendMsg(player, "MarkersRemoved", count);
        }

        void RemoveAllMarkers()
        {
            foreach (var marker in _markers.ToArray())
                if (marker != null) UnityEngine.Object.Destroy(marker);
            _markers.Clear();
        }

        void LoadSavedMarkers()
        {
            foreach (var saved in _data.Markers)
                CreateCustomMarker(saved);
        }
        #endregion

        #region API
        void API_CreateMarker(Vector3 position, string name, int duration = 0, float refreshRate = 30f, 
            float radius = 0.4f, string displayName = "Marker", string colorMarker = "00FFFF", 
            string colorOutline = "00FFFFFF", float alpha = 0.75f)
        {
            CreateMarker(position, duration, refreshRate, name, displayName, radius, alpha, colorMarker, colorOutline);
        }

        void API_CreateMarker(BaseEntity entity, string name, int duration = 0, float refreshRate = 30f,
            float radius = 0.4f, string displayName = "Marker", string colorMarker = "00FFFF",
            string colorOutline = "00FFFFFF", float alpha = 0.75f)
        {
            CreateMarker(entity, duration, refreshRate, name, displayName, radius, alpha, colorMarker, colorOutline);
        }

        void API_RemoveMarker(string name) => RemoveMarker(name);
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "<color=#ff4444>[NWG Markers]</color> You don't have permission to use this command.",
                ["Usage"] = "<color=#00ffff>[NWG Markers]</color> Usage:\n" +
                           "  <color=#ffcc00>/marker add</color> <name> <duration> <refreshRate> <radius> <displayName> <color> <outlineColor> <alpha>\n" +
                           "  <color=#ffcc00>/marker remove</color> <name>\n" +
                           "  <color=#ffcc00>/marker list</color>",
                ["UsageAdd"] = "<color=#00ffff>[NWG Markers]</color> Usage:\n" +
                              "  <color=#ffcc00>/marker add</color> <name> <duration> <refreshRate> <radius> <displayName> <color> <outlineColor> <alpha>\n" +
                              "  Example: /marker add mymarker 0 30 0.4 \"My Location\" 00FFFF 00FFFFFF 0.75",
                ["UsageRemove"] = "<color=#00ffff>[NWG Markers]</color> Usage: <color=#ffcc00>/marker remove</color> <name>",
                ["MarkerAdded"] = "<color=#00ff00>[NWG Markers]</color> Marker '<color=#ffcc00>{0}</color>' added at {1}",
                ["MarkersRemoved"] = "<color=#00ff00>[NWG Markers]</color> Removed {0} marker(s).",
                ["NoMarkers"] = "<color=#ffcc00>[NWG Markers]</color> No custom markers found.",
                ["MarkerList"] = "<color=#00ffff>[NWG Markers]</color> Custom Markers:",
                ["MarkerListEntry"] = "  • <color=#ffcc00>{0}</color> - {1} at {2}"
            }, this);
        }

        string Lang(string key, string userId = null, params object[] args)
        {
            _sb.Clear();
            if (args != null && args.Length > 0)
            {
                _sb.AppendFormat(lang.GetMessage(key, this, userId), args);
                return _sb.ToString();
            }
            return lang.GetMessage(key, this, userId);
        }

        void SendMsg(BasePlayer player, string key, params object[] args)
        {
            string msg = Lang(key, player.UserIDString, args);
            player.ChatMessage(msg);
        }
        #endregion

        #region Custom Marker Component
        private class CustomMapMarker : MonoBehaviour
        {
            private VendingMachineMapMarker _vending;
            private MapMarkerGenericRadius _generic;
            
            public BaseEntity Parent;
            public string Name;
            public string DisplayName;
            public Vector3 Position;
            public float Radius;
            public float Alpha;
            public Color Color1;
            public Color Color2;
            public float RefreshRate;
            public int Duration;
            public bool PlayerPlaced;

            private bool _hasParent;

            void Start()
            {
                transform.position = Position;
                _hasParent = Parent != null;
                CreateMarkers();
            }

            void CreateMarkers()
            {
                _vending = GameManager.server.CreateEntity(VENDING_PREFAB, Position).GetComponent<VendingMachineMapMarker>();
                _vending.markerShopName = DisplayName;
                _vending.enableSaving = false;
                _vending.Spawn();

                _generic = GameManager.server.CreateEntity(GENERIC_PREFAB).GetComponent<MapMarkerGenericRadius>();
                _generic.color1 = Color1;
                _generic.color2 = Color2;
                _generic.radius = Radius;
                _generic.alpha = Alpha;
                _generic.enableSaving = false;
                _generic.SetParent(_vending);
                _generic.Spawn();

                if (Duration > 0)
                    Invoke(nameof(DestroyMarkers), Duration);

                UpdateMarkers();

                if (RefreshRate > 0f)
                {
                    if (_hasParent)
                        InvokeRepeating(nameof(UpdatePosition), RefreshRate, RefreshRate);
                    else
                        InvokeRepeating(nameof(UpdateMarkers), RefreshRate, RefreshRate);
                }
            }

            void UpdatePosition()
            {
                if (_hasParent)
                {
                    if (!Parent.IsValid())
                    {
                        Destroy(this);
                        return;
                    }
                    Vector3 pos = Parent.transform.position;
                    transform.position = pos;
                    _vending.transform.position = pos;
                }
                UpdateMarkers();
            }

            public void UpdateMarkers()
            {
                if (_vending != null && _vending.IsValid()) _vending.SendNetworkUpdate();
                if (_generic != null && _generic.IsValid()) _generic.SendUpdate();
            }

            void DestroyMarkers()
            {
                if (_vending != null && _vending.IsValid()) _vending.Kill();
                if (_generic != null && _generic.IsValid()) _generic.Kill();
            }

            void OnDestroy()
            {
                DestroyMarkers();
            }
        }
        #endregion
    }
}
