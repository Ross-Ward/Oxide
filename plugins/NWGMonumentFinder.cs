using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NWGMonumentFinder", "NWG Team", "1.0.0")]
    [Description("Internal monument discovery for NWG plugins.")]
    internal class NWGMonumentFinder : CovalencePlugin
    {
        #region Fields
        private static NWGMonumentFinder _pluginInstance;
        private static Configuration _pluginConfig;

        private const string MonumentMarkerPrefabShortName = "monument_marker.prefab";
        private const string PermissionFind = "nwgmonumentfinder.find";
        private const float DrawDuration = 30;

        private readonly FieldInfo DungeonBaseLinksFieldInfo = typeof(TerrainPath).GetField("DungeonBaseLinks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private Dictionary<MonumentInfo, string> _customMonumentNameTable = new();
        private Dictionary<MonumentInfo, NormalMonumentAdapter> _normalMonuments = new();
        private Dictionary<DungeonGridCell, TrainTunnelAdapter> _trainTunnels = new();
        private Dictionary<DungeonBaseLink, UnderwaterLabLinkAdapter> _labModules = new();
        private Dictionary<MonoBehaviour, BaseMonumentAdapter> _allMonuments = new();

        private Collider[] _colliderBuffer = new Collider[8];
        #endregion

        #region Hooks
        private void Init()
        {
            _pluginInstance = this;
            permission.RegisterPermission(PermissionFind, this);
            AddCovalenceCommand("nwgmf", nameof(CommandFind));
        }

        private void Unload()
        {
            _pluginConfig = null;
            _pluginInstance = null;
        }

        private void OnServerInitialized()
        {
            _customMonumentNameTable.Clear();
            _normalMonuments.Clear();
            _trainTunnels.Clear();
            _labModules.Clear();
            _allMonuments.Clear();

            foreach (var (prefabName, gameObjectList) in World.SpawnedPrefabs)
            {
                if (!IsMonumentMarker(gameObjectList.FirstOrDefault(), out _))
                    continue;

                foreach (var gameObject in gameObjectList)
                {
                    if (IsMonumentMarker(gameObject, out var monumentInfo))
                        _customMonumentNameTable[monumentInfo] = prefabName;
                }
            }

            if (DungeonBaseLinksFieldInfo != null && DungeonBaseLinksFieldInfo.GetValue(TerrainMeta.Path) is List<DungeonBaseLink> dungeonLinks)
            {
                foreach (var link in dungeonLinks)
                {
                    if (link.Type == DungeonBaseLinkType.End) continue;
                    var labLink = new UnderwaterLabLinkAdapter(link);
                    _labModules[link] = labLink;
                    _allMonuments[link] = labLink;
                }
            }

            foreach (var dungeonCell in TerrainMeta.Path.DungeonGridCells)
            {
                if (TrainTunnelAdapter.IgnoredPrefabs.Contains(dungeonCell.name)) continue;
                try
                {
                    var trainTunnel = new TrainTunnelAdapter(dungeonCell);
                    _trainTunnels[dungeonCell] = trainTunnel;
                    _allMonuments[dungeonCell] = trainTunnel;
                }
                catch (NotImplementedException) { }
            }

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                var normalMonument = new NormalMonumentAdapter(monument, _customMonumentNameTable);
                _normalMonuments[monument] = normalMonument;
                _allMonuments[monument] = normalMonument;
            }

            Puts($"[NWGMonumentFinder] Indexed {_allMonuments.Count} monument points.");
        }
        #endregion

        #region API
        [HookMethod("API_GetClosest")]
        private Dictionary<string, object> API_GetClosest(Vector3 position) => GetClosestMonumentForAPI(_allMonuments.Values, position);
        
        [HookMethod("API_GetClosestMonument")]
        private Dictionary<string, object> API_GetClosestMonument(Vector3 position) => GetClosestMonumentForAPI(_normalMonuments.Values, position);
        #endregion

        #region Commands
        private void CommandFind(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermissionFind)) { player.Reply("No permission."); return; }
            if (args.Length == 0) { player.Reply("/nwgmf closest - Shows closest monument"); return; }
            if (args[0].ToLower() == "closest") SubcommandClosest(player, command, args.Skip(1).ToArray());
        }

        private void SubcommandClosest(IPlayer player, string command, string[] args)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;

            var position = basePlayer.transform.position;
            var monument = GetClosestMonument(_allMonuments.Values, position);
            if (monument == null) { player.Reply("No monuments found."); return; }

            if (monument.IsInBounds(position))
            {
                var relativePosition = monument.InverseTransformPoint(position);
                player.Reply($"At monument: {monument.PrefabName} | Relative: {relativePosition}");
            }
            else
            {
                var closestPoint = monument.ClosestPointOnBounds(position);
                var distance = (position - closestPoint).magnitude;
                player.Reply($"Closest monument: {monument.PrefabName} | Distance: {distance:F2}m");
            }
        }
        #endregion

        #region Helpers
        private static bool IsMonumentMarker(GameObject gameObject, out MonumentInfo monumentInfo)
        {
            monumentInfo = null;
            return gameObject != null && gameObject.name.EndsWith(MonumentMarkerPrefabShortName) && gameObject.TryGetComponent(out monumentInfo);
        }

        private static T GetClosestMonument<T>(IEnumerable<T> monumentList, Vector3 position) where T : BaseMonumentAdapter
        {
            T closestMonument = null;
            var closestSqrDistance = float.MaxValue;
            foreach (var baseMonument in monumentList)
            {
                var currentSqrDistance = (position - baseMonument.ClosestPointOnBounds(position)).sqrMagnitude;
                if (currentSqrDistance < closestSqrDistance) { closestSqrDistance = currentSqrDistance; closestMonument = baseMonument; }
            }
            return closestMonument;
        }

        private static Dictionary<string, object> GetClosestMonumentForAPI(IEnumerable<BaseMonumentAdapter> monumentList, Vector3 position)
        {
            return GetClosestMonument(monumentList, position)?.APIResult;
        }

        private abstract class BaseMonumentAdapter
        {
            public MonoBehaviour Object { get; }
            public string PrefabName { get; protected set; }
            public string ShortName { get; protected set; }
            public string Alias { get; protected set; }
            public Vector3 Position { get; protected set; }
            protected Quaternion Rotation { get; set; }
            public OBB[] BoundingBoxes { get; protected set; }

            protected BaseMonumentAdapter(MonoBehaviour behavior)
            {
                Object = behavior;
                PrefabName = behavior.name;
                ShortName = behavior.name.Split('/').Last().Replace(".prefab", "");
                var transform = behavior.transform;
                Position = transform.position;
                Rotation = transform.rotation;
            }

            public Vector3 TransformPoint(Vector3 localPosition) => Position + Rotation * localPosition;
            public Vector3 InverseTransformPoint(Vector3 worldPosition) => Quaternion.Inverse(Rotation) * (worldPosition - Position);
            public bool IsInBounds(Vector3 position) => BoundingBoxes.Any(box => box.Contains(position));
            public Vector3 ClosestPointOnBounds(Vector3 position)
            {
                var bestPoint = Vector3.positiveInfinity;
                var bestDist = float.MaxValue;
                foreach (var box in BoundingBoxes)
                {
                    var p = box.ClosestPoint(position);
                    var d = (position - p).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; bestPoint = p; }
                }
                return bestPoint;
            }

            public virtual bool MatchesFilter(string filter, string shortName, string alias) => true;

            public Dictionary<string, object> APIResult => new Dictionary<string, object>
            {
                ["PrefabName"] = PrefabName,
                ["ShortName"] = ShortName,
                ["Alias"] = Alias,
                ["Position"] = Position,
                ["Rotation"] = Rotation,
                ["TransformPoint"] = new Func<Vector3, Vector3>(TransformPoint),
                ["InverseTransformPoint"] = new Func<Vector3, Vector3>(InverseTransformPoint),
                ["IsInBounds"] = new Func<Vector3, bool>(IsInBounds),
            };
        }

        private class NormalMonumentAdapter : BaseMonumentAdapter
        {
            public NormalMonumentAdapter(MonumentInfo info, Dictionary<MonumentInfo, string> names) : base(info)
            {
                BoundingBoxes = new[] { new OBB(Position, Rotation, info.Bounds) };
            }
        }

        private class TrainTunnelAdapter : BaseMonumentAdapter
        {
            public static readonly string[] IgnoredPrefabs = { "transition-sn-0", "transition-sn-1" };
            public TrainTunnelAdapter(DungeonGridCell cell) : base(cell)
            {
                BoundingBoxes = new[] { new OBB(Position, Rotation, new Bounds(Vector3.zero, new Vector3(20, 10, 20))) };
            }
        }

        private class UnderwaterLabLinkAdapter : BaseMonumentAdapter
        {
            public UnderwaterLabLinkAdapter(DungeonBaseLink link) : base(link)
            {
                BoundingBoxes = link.GetComponentsInChildren<DungeonVolume>().Select(v => new OBB(v.transform.position, v.transform.rotation, v.bounds)).ToArray();
            }
        }
        #endregion

        #region Config
        private class Configuration { }
        protected override void LoadDefaultConfig() => Config.WriteObject(new Configuration());
        #endregion
    }
}
