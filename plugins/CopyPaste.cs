//If debug is defined it will add a stopwatch to the paste and copydata which can be used to profile copying and pasting.
// #define DEBUG

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;
using ProtoBuf;
using UnityEngine;
using Graphics = System.Drawing.Graphics;
using WrapMode = System.Drawing.Drawing2D.WrapMode;

#if DEBUG
using System.Diagnostics;
#endif

// ReSharper disable SpecifyACultureInStringConversionExplicitly

/*
 * CREDITS
 *
 * Orange - Saving ContainerIOEntity
 * UIP88 - Turrets fix
 * bsdinis - Wire fix
 * nivex - Ownership option, sign fix
 * bmgjet - Wallpapers, pattern firework, industrial
 * DezLife - CCTV fix
 * Wulf - Skipping 4.1.24 :D
 * 
 */

namespace Oxide.Plugins
{
    [Info("Copy Paste", "misticos", "4.2.5")]
    [Description("Copy and paste buildings to save them or move them")]
    public class CopyPaste : CovalencePlugin
    {
        // ReSharper disable once Unity.IncorrectMonoBehaviourInstantiation
        private readonly Item _emptyItem = new Item { info = new ItemDefinition() };
        private readonly IPlayer _consolePlayer = new RustConsolePlayer();

        private int _copyLayer =
                LayerMask.GetMask("Construction", "Prevent Building", "Construction Trigger", "Trigger", "Deployed",
                    "Default", "Ragdoll"),
            _groundLayer = LayerMask.GetMask("Terrain", "Default"),
            _rayCopy = LayerMask.GetMask("Construction", "Deployed", "Tree", "Resource", "Prevent Building"),
            _rayPaste = LayerMask.GetMask("Construction", "Deployed", "Tree", "Terrain", "World", "Water",
                "Prevent Building");

        private string _copyPermission = "copypaste.copy",
            _listPermission = "copypaste.list",
            _pastePermission = "copypaste.paste",
            _pastebackPermission = "copypaste.pasteback",
            _undoPermission = "copypaste.undo",
            _subDirectory = "copypaste/";

        private readonly HashSet<string> _dlcPrefabs = new();
        private readonly HashSet<int> _dlcItemIds = new();
        private readonly HashSet<ulong> _paidSkinIds = new();
        private readonly Dictionary<string, ItemDefinition> _prefabToItemDef = new();
        private readonly Dictionary<ItemDefinition, string> _itemDefToPrefab = new();
        private bool _pasteReady;
        private readonly List<PasteData> _pendingPastes = new();

        private Dictionary<string, Stack<List<BaseEntity>>> _lastPastes =
            new Dictionary<string, Stack<List<BaseEntity>>>();

        private Dictionary<string, SignSize> _signSizes = new Dictionary<string, SignSize>
        {
            { "photoframe.landscape", new SignSize(320, 240) },
            { "photoframe.large", new SignSize(320, 240) },
            { "photoframe.portrait", new SignSize(320, 240) },
            { "sign.pictureframe.landscape", new SignSize(256, 192) },
            { "sign.pictureframe.tall", new SignSize(128, 512) },
            { "sign.pictureframe.portrait", new SignSize(205, 256) },
            { "sign.pictureframe.xxl", new SignSize(1024, 512) },
            { "sign.pictureframe.xl", new SignSize(512, 512) },
            { "sign.small.wood", new SignSize(256, 128) },
            { "sign.medium.wood", new SignSize(512, 256) },
            { "sign.large.wood", new SignSize(512, 256) },
            { "sign.huge.wood", new SignSize(1024, 256) },
            { "sign.hanging.banner.large", new SignSize(256, 1024) },
            { "sign.pole.banner.large", new SignSize(256, 1024) },
            { "sign.post.single", new SignSize(256, 128) },
            { "sign.post.double", new SignSize(512, 512) },
            { "sign.post.town", new SignSize(512, 256) },
            { "sign.post.town.roof", new SignSize(512, 256) },
            { "sign.hanging", new SignSize(256, 512) },
            { "sign.hanging.ornate", new SignSize(512, 256) },
            { "sign.neon.xl.animated", new SignSize(256, 256) },
            { "sign.neon.xl", new SignSize(256, 256) },
            { "sign.neon.125x215.animated", new SignSize(256, 128) },
            { "sign.neon.125x215", new SignSize(256, 128) },
            { "sign.neon.125x125", new SignSize(128, 128) },
        };

        private HashSet<Type> ItemModAssociatedEntities = new()
        {
            typeof(PaintedItemStorageEntity),
            typeof(PhotoEntity),
            typeof(SignContent),
            typeof(HeadEntity),
            typeof(PagerEntity),
            typeof(MobileInventoryEntity),
            typeof(Cassette)
        };

        private List<BaseEntity.Slot> _checkSlots = new List<BaseEntity.Slot>
        {
            BaseEntity.Slot.Lock,
            BaseEntity.Slot.UpperModifier,
            BaseEntity.Slot.MiddleModifier,
            BaseEntity.Slot.LowerModifier
        };

        public enum CopyMechanics
        {
            Building,
            Proximity
        }

        private class SignSize
        {
            public int Width;
            public int Height;

            public SignSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        public enum SkinsMode
        {
            NoSkins = 0,
            AllSkins = 1,
            NoPaidSkins = 2,
            AllowSpecifiedOnly = 3,
            BlockSpecifiedOnly = 4
        }

        //Config

        private ConfigData _config;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Copy Options")]
            public CopyOptions Copy { get; set; }

            [JsonProperty(PropertyName = "Paste Options")]
            public PasteOptions Paste { get; set; }

            [JsonProperty(PropertyName =
                "Amount of entities to paste per batch. Use to tweak performance impact of pasting")]
            [DefaultValue(15)]
            public int PasteBatchSize = 15;

            [JsonProperty(PropertyName =
                "Amount of entities to copy per batch. Use to tweak performance impact of copying")]
            [DefaultValue(100)]
            public int CopyBatchSize = 100;

            [JsonProperty(PropertyName =
                "Amount of entities to undo per batch. Use to tweak performance impact of undoing")]
            [DefaultValue(15)]
            public int UndoBatchSize = 15;

            [JsonProperty(PropertyName =
                "Prevent These Prefabs From Spawning", ObjectCreationHandling = ObjectCreationHandling.Replace),
            DefaultValue(typeof(List<string>), "")]
            public List<string> BlockedPrefabs = new();

            [JsonProperty(PropertyName = "Enable data saving feature")]
            [DefaultValue(true)]
            public bool DataSaving = true;

            public class CopyOptions
            {
                [JsonProperty(PropertyName = "Check radius from each entity (true/false)")]
                [DefaultValue(true)]
                public bool EachToEach { get; set; } = true;

                [JsonProperty(PropertyName = "Share (true/false)")]
                [DefaultValue(false)]
                public bool Share { get; set; } = false;

                [JsonProperty(PropertyName = "Tree (true/false)")]
                [DefaultValue(false)]
                public bool Tree { get; set; } = false;

                [JsonProperty(PropertyName = "Default radius to look for entities from block")]
                [DefaultValue(3.0f)]
                public float Radius { get; set; } = 3.0f;
            }

            public class PasteOptions
            {
                [JsonProperty(PropertyName = "Auth (true/false)")]
                [DefaultValue(false)]
                public bool Auth { get; set; } = false;

                [JsonProperty(PropertyName = "Deployables (true/false)")]
                [DefaultValue(true)]
                public bool Deployables { get; set; } = true;

                [JsonProperty(PropertyName = "Inventories (true/false)")]
                [DefaultValue(true)]
                public bool Inventories { get; set; } = true;

                [JsonProperty(PropertyName = "Vending Machines (true/false)")]
                [DefaultValue(true)]
                public bool VendingMachines { get; set; } = true;

                [JsonProperty(PropertyName = "Stability (true/false)")]
                [DefaultValue(true)]
                public bool Stability { get; set; } = true;

                [JsonProperty(PropertyName = "EntityOwner (true/false)")]
                [DefaultValue(true)]
                public bool EntityOwner { get; set; } = true;

                [JsonProperty(PropertyName = "DLC items and deployables (true/false)")]
                [DefaultValue(true)]
                public bool Dlc { get; set; } = true;

                [JsonProperty(PropertyName = "Skins (0=no skins, 1=all, 2=no paid skins, 3=allow specified only, 4=block specified only)")]
                [DefaultValue((int)CopyPaste.SkinsMode.AllSkins)]
                public int SkinsMode { get; set; } = (int)CopyPaste.SkinsMode.AllSkins;

                [JsonProperty(PropertyName = "Specified Skins (skin id, like 2601577757, or item shortname for redirected skins, like hazmatsuit.spacesuit)")]
                public List<object> SpecifiedSkins { get; set; } = new();

                [JsonIgnore]
                public List<ulong> SpecifiedSkinIds = new();

                [JsonIgnore]
                public List<string> SpecifiedSkinRedirects = new();

            }

        }

        private void LoadVariables()
        {
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;

            _config = Config.ReadObject<ConfigData>();

            _config.BlockedPrefabs ??= new();
            _config.Paste.SpecifiedSkins ??= new();

            if (!IsValidSkinsMode(_config.Paste.SkinsMode))
            {
                PrintWarning("Invalid config value specified for 'Skins', resetting to default of 1 (all skins)");
                _config.Paste.SkinsMode = (int)SkinsMode.AllSkins;
            }

            for (var i = 0; i < _config.Paste.SpecifiedSkins.Count; i++)
            {
                var str = _config.Paste.SpecifiedSkins[i].ToString();
                if (string.IsNullOrEmpty(str))
                    continue;

                if (UInt64.TryParse(str, out var skinid))
                    _config.Paste.SpecifiedSkinIds.Add(skinid);
                else if (ItemManager.FindItemDefinition(str) != null)
                    _config.Paste.SpecifiedSkinRedirects.Add(str);
                else
                    PrintWarning($"Ignoring invalid item shortname in 'Specified Skins': {str}");
            }

            Config.WriteObject(_config, true);
        }

        protected override void LoadDefaultConfig()
        {
            var configData = new ConfigData
            {
                Copy = new ConfigData.CopyOptions(),
                Paste = new ConfigData.PasteOptions()
            };

            Config.WriteObject(configData, true);
        }

        private bool IsValidSkinsMode(int mode) => Enum.IsDefined(typeof(SkinsMode), mode);

        //Hooks

        private void Init()
        {
            permission.RegisterPermission(_copyPermission, this);
            permission.RegisterPermission(_listPermission, this);
            permission.RegisterPermission(_pastePermission, this);
            permission.RegisterPermission(_pastebackPermission, this);
            permission.RegisterPermission(_undoPermission, this);

            var compiledLangs = new Dictionary<string, Dictionary<string, string>>();

            foreach (var line in _messages)
            {
                foreach (var translate in line.Value)
                {
                    if (!compiledLangs.ContainsKey(translate.Key))
                        compiledLangs[translate.Key] = new Dictionary<string, string>();

                    compiledLangs[translate.Key][line.Key] = translate.Value;
                }
            }

            foreach (var cLangs in compiledLangs)
            {
                lang.RegisterMessages(cLangs.Value, this, cLangs.Key);
            }
        }

        private void OnServerInitialized()
        {
            LoadVariables();

            Vis.colBuffer = new Collider[8192 * 16];

            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            ProcessItemDefinitions();
        }

        // Heavily influenced by k1lly0u's Player DLC API plugin
        private void ProcessItemDefinitions()
        {
            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                timer.In(3f, ProcessItemDefinitions);
                return;
            }

            const string WORKSHOP_ID = "workshopid";
            foreach (Steamworks.InventoryDef inventoryDef in Steamworks.SteamInventory.Definitions)
            {
                if (ulong.TryParse(inventoryDef.GetProperty(WORKSHOP_ID), out ulong skinId))
                    _paidSkinIds.Add(skinId);
            }

            for (int i = 0; i < ItemSkinDirectory.Instance.skins.Length; i++)
            {
                ItemSkinDirectory.Skin skin = ItemSkinDirectory.Instance.skins[i];
                _paidSkinIds.Add((ulong)skin.id);
            }

            foreach (ItemDefinition itemDef in ItemManager.itemList)
            {
                string prefabPath = GetPrefabPathFromItemDef(itemDef);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    _prefabToItemDef[prefabPath] = itemDef;
                    _itemDefToPrefab[itemDef] = prefabPath;
                }

                if (IsDlcItem(itemDef))
                {
                    _dlcItemIds.Add(itemDef.itemid);
                    if (!string.IsNullOrEmpty(prefabPath))
                        _dlcPrefabs.Add(prefabPath);
                }
            }

            Puts($"Skin detection initialized: {_paidSkinIds.Count} official skins, {_dlcItemIds.Count} DLC items. Processing {_pendingPastes.Count} queued paste(s).");

            _pasteReady = true;
            for (int i = 0; i < _pendingPastes.Count; i++)
            {
                var pasteData = _pendingPastes[i];
                timer.Once(i * 0.1f, () => PasteLoop(pasteData));
            }
            _pendingPastes.Clear();
        }

        private string GetPrefabPathFromItemDef(ItemDefinition def)
        {
            if (def.TryGetComponent<ItemModDeployable>(out var deployable))
                return deployable.entityPrefab.resourcePath;

            if (def.TryGetComponent<ItemModEntity>(out var entity))
                return entity.entityPrefab.resourcePath;

            if (def.TryGetComponent<ItemModEntityReference>(out var entityRef))
                return entityRef.entityPrefab.resourcePath;

            return null;
        }

        public static bool IsDlcItem(ItemDefinition definition)
        {
            var bp = definition.Blueprint;
            var parent = definition.Parent ?? definition.isRedirectOf;
            var parentBp = parent?.Blueprint;
            return
                (definition.steamItem is not null && definition.steamItem.id != 0) ||
                (definition.steamDlc is not null && definition.steamDlc.dlcAppID != 0) ||
                (bp is not null && bp.NeedsSteamDLC) ||
                (parentBp is not null && parentBp.NeedsSteamDLC) ||
                definition.isRedirectOf is not null;
        }

        public ulong FilterSkinId(PasteData pasteData, ulong skinId)
        {
            if (skinId == 0)
                return skinId;

            return pasteData.SkinsMode switch
            {
                SkinsMode.NoSkins => 0,
                SkinsMode.NoPaidSkins when _paidSkinIds.Contains(skinId) => 0,
                SkinsMode.AllowSpecifiedOnly when !_config.Paste.SpecifiedSkinIds.Contains(skinId) => 0,
                SkinsMode.BlockSpecifiedOnly when _config.Paste.SpecifiedSkinIds.Contains(skinId) => 0,
                _ => skinId
            };
        }

        public bool GetItemDefinitionForPrefab(string prefabPath, out ItemDefinition def, bool useRedirect = true)
        {
            def = null;

            if (!_prefabToItemDef.TryGetValue(prefabPath, out def))
                return false;

            if (useRedirect && def?.isRedirectOf != null)
                def = def.isRedirectOf;

            return def != null;
        }

        private bool ShouldRedirectForSkinsMode(PasteData pasteData, ItemDefinition itemDef)
        {
            var useRedirect = pasteData.SkinsMode == SkinsMode.NoSkins ||
                              pasteData.SkinsMode == SkinsMode.NoPaidSkins;

            if (pasteData.SkinsMode == SkinsMode.AllowSpecifiedOnly ||
                pasteData.SkinsMode == SkinsMode.BlockSpecifiedOnly)
            {
                bool isInSpecifiedList = _config.Paste.SpecifiedSkinRedirects.Contains(itemDef.shortname);

                if (pasteData.SkinsMode == SkinsMode.AllowSpecifiedOnly && !isInSpecifiedList)
                    useRedirect = true;
                else if (pasteData.SkinsMode == SkinsMode.BlockSpecifiedOnly && isInSpecifiedList)
                    useRedirect = true;
            }

            return useRedirect;
        }
        #region API

        private bool IsPasteReady() => _pasteReady;

        private object TryCopyFromSteamId(ulong userId, string filename, string[] args, Action callback = null)
        {
            var player = players.FindPlayerById(userId.ToString())?.Object as BasePlayer;
            if (player == null)
                return Lang("NOT_FOUND_PLAYER");

            RaycastHit hit;

            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 1000f, _rayCopy))
                return Lang("NO_ENTITY_RAY", player.UserIDString);

            var entity = hit.GetEntity();
            if (!entity.IsValid())
                return Lang("NO_ENTITY_RAY", player.UserIDString);

            return TryCopy(hit.point, entity.GetNetworkRotation().eulerAngles, filename,
                DegreeToRadian(player.GetNetworkRotation().eulerAngles.y), args, player.IPlayer, callback);
        }

        private object TryPasteFromSteamId(ulong userId, string filename, string[] args, Action callback = null,
            Action<BaseEntity> callbackSpawned = null)
        {
            var player = players.FindPlayerById(userId.ToString())?.Object as BasePlayer;
            if (player == null)
                return Lang("NOT_FOUND_PLAYER");

            RaycastHit hit;

            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 1000f, _rayPaste))
                return Lang("NO_ENTITY_RAY", player.UserIDString);

            return TryPaste(hit.point, filename, player.IPlayer,
                DegreeToRadian(player.GetNetworkRotation().eulerAngles.y),
                args, callback: callback, callbackSpawned: callbackSpawned).Item1;
        }

        private object TryPasteFromVector3(Vector3 pos, float rotationCorrection, string filename, string[] args,
            Action callback = null, Action<BaseEntity> callbackSpawned = null)
        {
            return TryPaste(pos, filename, _consolePlayer, rotationCorrection, args, callback: callback,
                callbackSpawned: callbackSpawned).Item1;
        }

        private ValueTuple<object, Action> TryPasteFromVector3Cancellable(Vector3 pos, float rotationCorrection,
            string filename, string[] args,
            Action callback = null, Action<BaseEntity> callbackSpawned = null)
        {
            var result = TryPaste(pos, filename, _consolePlayer, rotationCorrection, args, callback: callback,
                callbackSpawned: callbackSpawned);

            var pasteData = result.Item2;

            return new ValueTuple<object, Action>(result.Item1, () => pasteData.Cancelled = true);
        }

        #endregion

        //Other methods

        private object CheckCollision(HashSet<Dictionary<string, object>> entities, Vector3 startPos, float radius)
        {
            foreach (var entityobj in entities)
            {
                if (Physics.CheckSphere((Vector3)entityobj["position"], radius, _copyLayer))
                    return Lang("BLOCKING_PASTE");
            }

            return true;
        }

        private bool CheckPlaced(string prefabname, Vector3 pos, Quaternion rot)
        {
            const float maxDiff = 0.01f;

            var ents = Pool.Get<List<BaseEntity>>();
            try
            {
                Vis.Entities(pos, maxDiff, ents);

                foreach (var ent in ents)
                {
                    if (ent.IsDestroyed || ent.PrefabName != prefabname)
                        continue;

                    if (Vector3.Distance(ent.transform.position, pos) > maxDiff)
                    {
                        continue;
                    }

                    if (Vector3.Distance(ent.transform.rotation.eulerAngles, rot.eulerAngles) > maxDiff)
                    {
                        continue;
                    }

                    return true;
                }

                return false;
            }
            finally
            {
                Pool.FreeUnmanaged(ref ents);
            }
        }

        private void RemoveEntity(BaseEntity entity)
        {
            if (!entity.IsValid() || entity.IsDestroyed)
                return;

            // Cleanup the hotspot beloning to the node.
            var ore = entity as OreResourceEntity;
            if (ore != null)
            {
                ore.CleanupBonus();
            }

            var io = entity as IOEntity;
            if (io != null)
            {
                io.ClearConnections();
            }

            var autoTurret = entity as AutoTurret;
            if (autoTurret != null)
            {
                AutoTurret.interferenceUpdateList.Remove(autoTurret);
            }

            entity.Kill();
        }

        private void UndoLoop(HashSet<BaseEntity> entities, IPlayer player, int count = 0)
        {
            for (var i = entities.Count - 1; i >= 0; i--)
            {
                var baseEntity = entities.ElementAt(i);
                if (baseEntity is IItemContainerEntity)
                {
                    RemoveEntity(baseEntity);
                    entities.Remove(baseEntity);
                }
            }

            // Take an amount of entities from the entity list (defined in config) and kill them. Will be repeated for every tick until there are no entities left.
            entities
                .Take(_config.UndoBatchSize)
                .ToList()
                .ForEach(p =>
                {
                    entities.Remove(p);
                    RemoveEntity(p);
                });

            // If it gets stuck in infinite loop break the loop.
            if (count != 0 && entities.Count != 0 && entities.Count == count)
            {
                player?.Reply("Undo cancelled because of infinite loop.");
                return;
            }

            if (entities.Count > 0)
                NextTick(() => UndoLoop(entities, player, entities.Count));
            else if (player != null)
            {
                player.Reply(Lang("UNDO_SUCCESS", player.Id));

                if (_lastPastes.ContainsKey(player.Id) && _lastPastes[player.Id].Count == 0)
                    _lastPastes.Remove(player.Id);
            }
        }

        private void Copy(Vector3 sourcePos, Vector3 sourceRot, string filename, float rotationCorrection,
            CopyMechanics copyMechanics, float range, bool saveTree, bool saveShare, bool eachToEach, IPlayer player,
            Action callback)
        {
            var currentLayer = _copyLayer;

            if (saveTree)
                currentLayer |= LayerMask.GetMask("Tree");

            var copyData = new CopyData
            {
                Filename = filename,
                CurrentLayer = currentLayer,
                RotCor = rotationCorrection,
                Range = range,
                SaveShare = saveShare,
                SaveTree = saveTree,
                CopyMechanics = copyMechanics,
                EachToEach = eachToEach,
                SourcePos = sourcePos,
                SourceRot = sourceRot,
                Player = player,
                BasePlayer = player.Object as BasePlayer,
                Callback = callback
            };

            copyData.CheckFrom.Push(sourcePos);

            NextTick(() => CopyLoop(copyData));
        }

        // Main loop for copy, will fetch all the data needed. Is called every tick untill copy is done (can't find any entities)
        private void CopyLoop(CopyData copyData)
        {
            var checkFrom = copyData.CheckFrom;
            var houseList = copyData.HouseList;
            var buildingId = copyData.BuildingId;
            var copyMechanics = copyData.CopyMechanics;
            var batchSize = checkFrom.Count < _config.CopyBatchSize ? checkFrom.Count : _config.CopyBatchSize;
            var range = copyData.Range;

            /*
                BUILDING BLOCK DETECTION FIX:
                Rust building blocks (foundations, walls, etc.) are 3m x 3m. When the plugin's detection range is
                exactly 3.0f (default) and starting from one building block, Vis.Entities() detection extends precisely
                to the edge of adjacent blocks but doesn't cross their boundaries.

                This creates an edge case where connected building blocks aren't always detected (depending on the
                block's grade and or skin) when there are no deployables positioned such that they would fall within 3m
                range of adjacent building blocks (which would otherwise cause those blocks to be detected).

                By adding a tiny amount (0.001f) to the range when it's exactly 3.0f, we ensure Vis.Entities() detection
                slightly overlaps adjacent blocks, properly capturing the entire connected structure without
                significantly changing the intended detection range.
            */
            if (range == 3.0f)
                range += 0.001f;

            for (var i = 0; i < batchSize; i++)
            {
                if (checkFrom.Count == 0)
                    break;

                var list = Pool.Get<List<BaseEntity>>();
                try
                {
                    Vis.Entities(checkFrom.Pop(), range, list, copyData.CurrentLayer);

                    foreach (var entity in list)
                    {
                        // Skip entities that are already in the list
                        if (!entity.IsValid() || entity.HasParent())
                            continue;
                        
                        // Skip metal detector flags
                        if (entity.GetComponent<MetalDetectorSource>() != null)
                            continue;
                        
                        if (!houseList.Add(entity))
                            continue;

                        if (copyMechanics == CopyMechanics.Building)
                        {
                            var buildingBlock = entity.GetComponentInParent<BuildingBlock>();

                            if (buildingBlock != null)
                            {
                                if (buildingId == 0)
                                    buildingId = buildingBlock.buildingID;

                                if (buildingId != buildingBlock.buildingID)
                                    continue;
                            }
                        }

                        var transform = entity.transform;
                        if (copyData.EachToEach)
                            checkFrom.Push(transform.position);

                        if (entity.GetComponent<BaseLock>() != null)
                            continue;
                        
                        copyData.RawData.Add(EntityData(entity, transform.position,
                            transform.rotation.eulerAngles / Mathf.Rad2Deg, copyData));
                    }
                }
                finally
                {
                    Pool.FreeUnmanaged(ref list);
                }

                copyData.BuildingId = buildingId;
            }

            if (checkFrom.Count > 0)
            {
                NextTick(() => CopyLoop(copyData));
            }
            else
            {
                var path = _subDirectory + copyData.Filename;
                var datafile = Interface.Oxide.DataFileSystem.GetFile(path);

                datafile.Clear();

                var sourcePos = copyData.SourcePos;

                datafile["default"] = new Dictionary<string, object>
                {
                    {
                        "position", new Dictionary<string, object>
                        {
                            { "x", sourcePos.x.ToString() },
                            { "y", sourcePos.y.ToString() },
                            { "z", sourcePos.z.ToString() }
                        }
                    },
                    { "rotationy", copyData.SourceRot.y.ToString() },
                    { "rotationdiff", copyData.RotCor.ToString() }
                };

                datafile["entities"] = copyData.RawData;
                datafile["protocol"] = new Dictionary<string, object>
                {
                    { "items", 2 },
                    { "version", Version }
                };

                Interface.Oxide.DataFileSystem.SaveDatafile(path);

                copyData.Player.Reply(Lang("COPY_SUCCESS", copyData.Player.Id, copyData.Filename));

                copyData.Callback?.Invoke();

                Interface.CallHook("OnCopyFinished", copyData.RawData, copyData.Filename, copyData.Player, copyData.SourcePos);
            }
        }

        private float DegreeToRadian(float angle)
        {
            return (float)(Math.PI * angle / 180.0f);
        }

        private Dictionary<string, object> EntityData(BaseEntity entity, Vector3 entPos, Vector3 entRot,
            CopyData copyData)
        {
            var normalizedPos = NormalizePosition(copyData.SourcePos, entPos, copyData.RotCor);
            var isChild = entity.HasParent();

            entRot.y -= copyData.RotCor;

            var data = new Dictionary<string, object>
            {
                { "prefabname", entity.PrefabName },
                { "skinid", entity.skinID },
                { "flags", TryCopyFlags(entity) },
                {
                    "pos", new Dictionary<string, object>
                    {
                        { "x", isChild ? entity.transform.localPosition.x.ToString() : normalizedPos.x.ToString() },
                        { "y", isChild ? entity.transform.localPosition.y.ToString() : normalizedPos.y.ToString() },
                        { "z", isChild ? entity.transform.localPosition.z.ToString() : normalizedPos.z.ToString() }
                    }
                },
                {
                    "rot", new Dictionary<string, object>
                    {
                        { "x", isChild ? entity.transform.localRotation.eulerAngles.x.ToString() : entRot.x.ToString() },
                        { "y", isChild ? entity.transform.localRotation.eulerAngles.y.ToString() : entRot.y.ToString() },
                        { "z", isChild ? entity.transform.localRotation.eulerAngles.z.ToString() : entRot.z.ToString() }
                    }
                },
                { "ownerid", entity.OwnerID }
            };

            if (entity.HasParent())
            {
                if (entity.parentBone != 0)
                {
                    data.Add("parentbone", StringPool.Get(entity.parentBone));
                }

                if (GetSlot(entity.GetParentEntity(), entity, out BaseEntity.Slot? theslot) && theslot != null)
                {
                    data.Add("slot", (int)theslot);
                }
            }

            if (entity.children != null && entity.children.Count > 0)
            {
                var children = new List<object>();
                foreach (var child in entity.children)
                {
                    if (!child.IsValid())
                        continue;
                    
                    children.Add(EntityData(child, child.transform.position, child.transform.rotation.eulerAngles, copyData));
                }

                if( children.Count > 0 )
                    data.Add("children", children);
            }

            var growableEntity = entity as GrowableEntity;
            if (growableEntity != null)
            {
                var genes = GrowableGeneEncoding.EncodeGenesToInt(growableEntity.Genes);
                if (genes > 0)
                {
                    data.Add("genes", genes);
                }

                var perent = growableEntity.GetParentEntity();
                if (perent != null)
                {
                    data.Add("hasParent", true);
                }
            }

            // TryCopySlots(entity, data, copyData.SaveShare);

            var codeLock = entity.GetComponent<CodeLock>();
            if (codeLock != null)
            {
                data.Add("code", codeLock.code);

                if (copyData.SaveShare)
                    data.Add("whitelistPlayers", codeLock.whitelistPlayers);

                if (codeLock.guestCode != null && codeLock.guestCode.Length == 4)
                {
                    data.Add("guestCode", codeLock.guestCode);

                    if (copyData.SaveShare)
                        data.Add("guestPlayers", codeLock.guestPlayers);
                }
            }
            
            var keyLock = entity.GetComponent<KeyLock>();
            if (keyLock != null)
            {
                data.Add("code", keyLock.keyCode.ToString());
                data.Add("firstKeyCreated", keyLock.firstKeyCreated);
            }
            
            var buildingblock = entity as BuildingBlock;

            if (buildingblock != null)
            {
                data.Add("grade", buildingblock.grade);
                if (buildingblock.customColour != 0)
                {
                    data.Add("customColour", buildingblock.customColour);
                }

                if (buildingblock.HasWallpaper(0))
                {
                    data.Add("wallpaperID", buildingblock.wallpaperID);
                    data.Add("wallpaperHealth", buildingblock.wallpaperHealth);
                    data.Add("wallpaperRotation", buildingblock.wallpaperRotation);
                }

                if (buildingblock.HasWallpaper(1))
                {
                    data.Add("wallpaperID2", buildingblock.wallpaperID2);
                    data.Add("wallpaperHealth2", buildingblock.wallpaperHealth2);
                    data.Add("wallpaperRotation2", buildingblock.wallpaperRotation2);
                }
            }

            var container = entity as IItemContainerEntity;
            if (container != null && container.inventory != null)
            {
                ExtractInventory(data, container.inventory, copyData);
            }

            var iSignage = entity as ISignage;
            if (iSignage != null)
            {
                ExtractTextures(data, iSignage.GetTextureCRCs(), entity, iSignage.FileType);
            }

            var photoEntity = entity as PhotoEntity;
            if (photoEntity != null)
            {
                ExtractTextures(data, photoEntity.GetContentCRCs, entity, FileStorage.Type.jpg);
            }

            var photoFrame = entity as PhotoFrame;
            if (photoFrame != null && photoFrame._photoEntity.uid.IsValid)
            {
                data.Add("photoEntity", photoFrame._photoEntity.uid.Value);
            }

            var signContent = entity as SignContent;
            if (signContent != null)
            {
                ExtractTextures(data, signContent.GetContentCRCs, entity, signContent.FileType);
            }

            var paintedItemStorageEntity = entity as PaintedItemStorageEntity;
            if (paintedItemStorageEntity != null)
            {
                ExtractTextures(data, paintedItemStorageEntity.GetContentCRCs, entity, FileStorage.Type.png);
            }

            var lights = entity as ChristmasLights;
            if (lights != null)
            {
                data.Add("animationStyle", lights.animationStyle);
            }

            var stringLights = entity as StringLights;
            if (stringLights != null)
            {
                var lightsPointsList = new List<Dictionary<string, object>>();
                foreach (var pointEntry in stringLights.points)
                {
                    lightsPointsList.Add(new Dictionary<string, object>
                    {
                        { "normal", pointEntry.normal },
                        { "point", NormalizePosition(copyData.SourcePos, pointEntry.point, copyData.RotCor) },
                        { "slack", pointEntry.slack },
                    });
                }
                data.Add("points", lightsPointsList);
            }

            var chandelier = entity as Chandelier;
            if (chandelier != null)
            {
                data.Add("chandelierLength", chandelier.ChandelierLength);
            }

            var orientableLight = entity as OrientableLight;
            if (orientableLight != null)
            {
                data.Add("pitchAmount", orientableLight.pitchAmount);
                data.Add("yawAmount", orientableLight.yawAmount);
            }

            var mannequin = entity as Mannequin;
            if (mannequin != null)
            {
                data.Add("poseIndex", mannequin.PoseIndex);
            }

            if (copyData.SaveShare)
            {
                var sleepingBag = entity as SleepingBag;

                if (sleepingBag != null)
                {
                    data.Add("sleepingbag", new Dictionary<string, object>
                    {
                        { "niceName", sleepingBag.niceName },
                        { "deployerUserID", sleepingBag.deployerUserID },
                        { "isPublic", sleepingBag.IsPublic() }
                    });
                }

                var cupboard = entity as BuildingPrivlidge;

                if (cupboard != null)
                {
                    data.Add("cupboard", new Dictionary<string, object>
                    {
                        { "authorizedPlayers", cupboard.authorizedPlayers.ToList() }
                    });
                }

                var autoTurret = entity as AutoTurret;

                if (autoTurret != null)
                {
                    data.Add("autoturret", new Dictionary<string, object>
                    {
                        { "authorizedPlayers", autoTurret.authorizedPlayers.ToList() }
                    });
                }
            }

            var firework = entity as PatternFirework;
            if (firework != null && firework?.Design != null && firework?.Design?.stars != null)
            {
                data.Add("patternfirework", new Dictionary<string, object>
                {
                    { "editedBy", firework.Design.editedBy },
                    { "stars", SerializeStarPattern(firework.Design.stars) },
                });
            }

            var tinCanAlarm = entity as TinCanAlarm;
            if (tinCanAlarm != null)
            {
                if (tinCanAlarm.endPoint != Vector3.zero)
                {
                    data.Add("endPoint", NormalizePosition(copyData.SourcePos, tinCanAlarm.endPoint, copyData.RotCor));
                }
            }

            var cctvRc = entity as CCTV_RC;
            if (cctvRc != null)
            {
                data.Add("cctv", new Dictionary<string, object>
                {
                    { "yaw", cctvRc.yawAmount },
                    { "pitch", cctvRc.pitchAmount },
                    { "rcIdentifier", cctvRc.rcIdentifier }
                });
            }

            var computerStation = entity as ComputerStation;
            if (computerStation != null)
            {
                data.Add("bookmarks", computerStation.GenerateControlBookmarkString());
            }

            var wantedPoster = entity as WantedPoster;
            if (wantedPoster != null)
            {
                data.Add("wantedPoster", new Dictionary<string, object>
                {
                    { "playerId", wantedPoster.playerId },
                    { "playerName", wantedPoster.playerName }
                });
            }

            var headEntity = entity as HeadEntity;
            if (headEntity != null)
            {
                ExtractHeadData(data, headEntity.CurrentTrophyData);
            }

            var huntingTrophy = entity as HuntingTrophy;
            if (huntingTrophy != null)
            {
                ExtractHeadData(data, huntingTrophy.CurrentTrophyData);
            }

            // Save RF frequencies for non-IOEntity RF objects (IOEntity frequencies are handled separately)
            var rfObject = entity as IRFObject;
            if (rfObject != null && entity is not IOEntity)
            {
                var frequency = rfObject.GetFrequency();
                if (frequency > 0)
                    data.Add("frequency", frequency);
            }

            var cassette = entity as Cassette;
            if (cassette != null)
            {
                ExtractCassette(data, cassette);
            }

            var elevator = entity as Elevator;
            if (elevator != null)
            {
                data.Add("Floor", elevator.Floor);
            }

            var weaponRack = entity as WeaponRack;
            if (weaponRack != null)
            {
                var gridSlots = new List<object>();
                foreach (var weaponRackGridSlot in weaponRack.gridSlots)
                {
                    if (weaponRackGridSlot != null && weaponRackGridSlot.Used)
                    {
                        gridSlots.Add(new Dictionary<string, object>
                        {
                            { "GridSlotIndex", weaponRackGridSlot.GridSlotIndex },
                            { "InventoryIndex", weaponRackGridSlot.InventoryIndex },
                            { "Rotation", weaponRackGridSlot.Rotation },
                        });
                    }
                }

                data.Add("gridSlots", gridSlots);
            }

            var commandBlock = entity as global::CommandBlock;
            if (commandBlock != null)
            {
                data.Add("currentCommand", commandBlock.currentCommand);
                data.Add("lastPlayerID", commandBlock.lastPlayerID);
            }

            var phoneController = entity.GetComponent<PhoneController>();
            if (phoneController != null && phoneController.savedVoicemail != null && phoneController.savedVoicemail.Count > 0)
            {
                var savedVoicemail = new List<object>();
                foreach (var voicemail in phoneController.savedVoicemail)
                {
                    var oggByte = FileStorage.server.Get(voicemail.audioId, FileStorage.Type.ogg, entity.net.ID);
                    if (oggByte != null)
                    {
                        savedVoicemail.Add(new Dictionary<string, object>
                        {
                            { "audio", Convert.ToBase64String(oggByte) },
                            { "userName", voicemail.userName }
                        });
                    }
                }

                data.Add("savedVoicemail", savedVoicemail);
            }

            var vendingMachine = entity as VendingMachine;

            if (vendingMachine != null)
            {
                var sellOrders = new List<object>();

                foreach (var vendItem in vendingMachine.sellOrders.sellOrders)
                {
                    sellOrders.Add(new Dictionary<string, object>
                    {
                        { "itemToSellID", vendItem.itemToSellID },
                        { "itemToSellAmount", vendItem.itemToSellAmount },
                        { "currencyID", vendItem.currencyID },
                        { "currencyAmountPerItem", vendItem.currencyAmountPerItem },
                        { "inStock", vendItem.inStock },
                        { "currencyIsBP", vendItem.currencyIsBP },
                        { "itemToSellIsBP", vendItem.itemToSellIsBP }
                    });
                }

                data.Add("vendingmachine", new Dictionary<string, object>
                {
                    { "shopName", vendingMachine.shopName },
                    { "isBroadcasting", vendingMachine.IsBroadcasting() },
                    { "sellOrders", sellOrders }
                });
            }

            var ridableHorse2 = entity as RidableHorse;
            if (ridableHorse2 != null)
            {
                data.Add("currentBreedIndex", ridableHorse2.currentBreedIndex);
                if (ridableHorse2.IsTowing && ridableHorse2.towingEntityId.IsValid)
                    data.Add("towingEntityId", ridableHorse2.towingEntityId.Value);
            }

            if (entity is FarmableAnimal farmableAnimal && farmableAnimal.IsValid() && !farmableAnimal.IsDestroyed)
            {
                data["hunger"] = farmableAnimal.AnimalHunger;
                data["thirst"] = farmableAnimal.AnimalThirst;
                data["love"] = farmableAnimal.AnimalLove;
                data["sunlight"] = farmableAnimal.AnimalSunlight;
                data["animalName"] = farmableAnimal.AnimalName;
            }

            if (entity is ChickenCoop chickenCoop && chickenCoop.IsValid() && !chickenCoop.IsDestroyed)
            {
                if (chickenCoop.HasFlag(BaseEntity.Flags.Reserved1))
                {
                    var timeUntilHatches = Facepunch.Pool.Get<List<string>>();
                    for (var i = 0; i < chickenCoop.Animals.Count; i++)
                    {
                        var animal = chickenCoop.Animals[i];
                        if (animal.TimeUntilHatch > 0.0)
                            timeUntilHatches.Add(animal.TimeUntilHatch.ToString());
                    }
                    if (timeUntilHatches.Count > 0)
                        data.Add("timeUntilHatches", timeUntilHatches.ToArray());

                    Facepunch.Pool.FreeUnmanaged(ref timeUntilHatches);
                }
            }

            var constructableEntity = entity as ConstructableEntity;
            if (constructableEntity != null && constructableEntity.currentMaterials.Length > 0)
            {
                data.Add("currentMaterials", constructableEntity.currentMaterials.ToArray());
                data.Add("health", constructableEntity.Health());
            }

            var boomBox = entity.GetComponent<BoomBox>();
            if (boomBox != null)
                ExtractBoomBox(data, boomBox);

            var ioEntity = entity as IOEntity;

            if (ioEntity.IsValid() && !ioEntity.IsDestroyed)
            {
                var ioData = new Dictionary<string, object>();
                var inputs = ioEntity.inputs.Select(input => new Dictionary<string, object>
                    {
                        { "connectedID", input.connectedTo.entityRef.uid.Value },
                        { "connectedToSlot", input.connectedToSlot },
                        { "niceName", input.niceName },
                        { "wireColour", input.wireColour },
                        { "type", (int)input.type }
                    })
                    .Cast<object>()
                    .ToList();

                ioData.Add("inputs", inputs);

                var outputs = new List<object>();
                foreach (var output in ioEntity.outputs)
                {
                    var ioConnection = new Dictionary<string, object>
                    {
                        { "connectedID", output.connectedTo.entityRef.uid.Value },
                        { "connectedToSlot", output.connectedToSlot },
                        { "niceName", output.niceName },
                        { "wireColour", output.wireColour },
                        { "type", (int)output.type },
                        { "linePoints", output.linePoints?.ToList() ?? new List<Vector3>() },
                        { "lineAnchors", output.lineAnchors != null ? GetLineAnchors(output.lineAnchors, ioEntity) : new List<object>() },
                        { "slackLevels", output.slackLevels?.ToList() ?? new List<float>() },
                    };

                    outputs.Add(ioConnection);
                }

                ioData.Add("outputs", outputs);
                ioData.Add("oldID", ioEntity.net.ID.Value);
                var electricalBranch = ioEntity as ElectricalBranch;
                if (electricalBranch != null)
                {
                    ioData.Add("branchAmount", electricalBranch.branchAmount);
                }

                var counter = ioEntity as PowerCounter;
                if (counter != null)
                {
                    ioData.Add("targetNumber", counter.GetTarget());
                    ioData.Add("counterNumber", counter.counterNumber);
                }

                var timerSwitch = ioEntity as TimerSwitch;
                if (timerSwitch != null)
                {
                    ioData.Add("timerLength", timerSwitch.timerLength);
                }

                var rfBroadcaster = ioEntity as IRFObject;
                if (rfBroadcaster != null)
                {
                    ioData.Add("frequency", rfBroadcaster.GetFrequency());
                }

                var seismicSensor = ioEntity as SeismicSensor;
                if (seismicSensor != null)
                {
                    ioData.Add("range", seismicSensor.range);
                }

                var conveyor = ioEntity as IndustrialConveyor;
                if (conveyor != null)
                {
                    ioData.Add("industrialconveyormode", (int)conveyor.mode);
                    ioData.Add("industrialconveyorfilteritems", SerializeConveyorFilter(conveyor.filterItems));
                }

                var audioVisual = ioEntity as AudioVisualisationEntity;
                if (audioVisual != null)
                {
                    ioData.Add("colour", (int)audioVisual.currentColour);
                    ioData.Add("volumeSensitivity", (int)audioVisual.currentVolumeSensitivity);
                    ioData.Add("speed", (int)audioVisual.currentSpeed);
                    ioData.Add("gradient", audioVisual.currentGradient);
                    if (audioVisual.connectedTo.IsValid(true))
                        ioData.Add("connectedTo", audioVisual.connectedTo.uid.Value);
                }

                var digitalClock = ioEntity as DigitalClock;
                if (digitalClock != null)
                {
                    var alarms = new List<object>();
                    foreach (var alarm in digitalClock.alarms)
                    {
                        alarms.Add(new Dictionary<string, object>
                        {
                            { "time", alarm.time },
                            { "active", alarm.active },
                        });
                    }

                    ioData.Add("muted", digitalClock.muted);
                    ioData.Add("alarms", alarms);
                }

                data.Add("IOEntity", ioData);
            }

            if (entity is StorageContainer || entity is Door || entity is ITowing || (isChild && entity is not IOEntity))
            {
                data.Add("oldID", entity.net.ID.Value);
            }

            return data;
        }
        
        private List<object> GetLineAnchors(IOEntity.LineAnchor[] lineAnchors, IOEntity ioEntity)
        {
            var anchors = new List<object>();
            foreach (var anchor in lineAnchors)
            {
                anchors.Add(new Dictionary<string, object>
                {
                    { "position", new Dictionary<string, object>
                        {
                            { "x", anchor.position.x.ToString() },
                            { "y", anchor.position.y.ToString() },
                            { "z", anchor.position.z.ToString() }
                        }
                    },
                    { "index", anchor.index },
                    { "boneName", anchor.boneName },
                    { "entityRefID", anchor.entityRef.uid.Value }
                });
            }

            return anchors;
        }

        private object FindBestHeight(HashSet<Dictionary<string, object>> entities, Vector3 startPos)
        {
            var maxHeight = 0f;

            foreach (var entity in entities)
            {
                var prefab = (string)entity["prefabname"];
                if (prefab.Contains("/foundation/") || prefab.Contains("/foundation.triangle/"))
                {
                    var foundHeight = GetGround((Vector3)entity["position"]);

                    if (foundHeight != null)
                    {
                        var height = (Vector3)foundHeight;

                        if (height.y > maxHeight)
                            maxHeight = height.y;
                    }
                }
            }

            maxHeight += 1f;

            return maxHeight;
        }

        private bool FindRayEntity(Vector3 sourcePos, Vector3 sourceDir, out Vector3 point, out BaseEntity entity,
            int rayLayer)
        {
            RaycastHit hitinfo;
            entity = null;
            point = Vector3.zero;

            if (!Physics.Raycast(sourcePos, sourceDir, out hitinfo, 1000f, rayLayer))
                return false;

            entity = hitinfo.GetEntity();
            point = hitinfo.point;

            return true;
        }

        private byte[] FixSignage(ISignage iSignage, byte[] imageBytes)
        {
            var sign = iSignage as Signage;

            if (sign == null || !_signSizes.ContainsKey(sign.ShortPrefabName))
                return imageBytes;

            var size = Math.Max(sign.paintableSources.Length, 1);
            if (sign.textureIDs == null || sign.textureIDs.Length != size)
            {
                Array.Resize(ref sign.textureIDs, size);
            }

            return ImageResize(imageBytes, _signSizes[sign.ShortPrefabName].Width,
                _signSizes[sign.ShortPrefabName].Height);
        }

        private object GetGround(Vector3 pos)
        {
            RaycastHit hitInfo;
            pos += new Vector3(0, 100, 0);

            if (Physics.Raycast(pos, Vector3.down, out hitInfo, 200, _groundLayer))
                return hitInfo.point;

            return null;
        }

        private int GetItemId(int itemId)
        {
            if (ReplaceItemId.ContainsKey(itemId))
                return ReplaceItemId[itemId];

            return itemId;
        }

        private string GetPrefabName(string prefabName)
        {
            if (ReplacePrefab.TryGetValue(prefabName, out string replacementPrefab))
                return replacementPrefab;

            return prefabName;
        }

        private bool HasAccess(IPlayer player, string permName)
        {
            return player.IsAdmin || player.HasPermission(permName);
        }

        private static bool IsPNG(byte[] imageBytes)
        {
            return imageBytes is { Length: >= 8 } &&
                   imageBytes[0] == 0x89 && imageBytes[1] == 0x50 &&
                   imageBytes[2] == 0x4E && imageBytes[3] == 0x47 &&
                   imageBytes[4] == 0x0D && imageBytes[5] == 0x0A &&
                   imageBytes[6] == 0x1A && imageBytes[7] == 0x0A;
        }

        private byte[] ImageResize(byte[] imageBytes, int width, int height)
        {
            if (imageBytes == null || imageBytes.Length == 0 || width <= 0 || height <= 0)
                return imageBytes;

            var sourceStream = new MemoryStream(imageBytes, writable: false);
            using var src = new Bitmap(sourceStream);

            var output = new MemoryStream();
            if (src.Width == width && src.Height == height)
            {
                if (IsPNG(imageBytes))
                    return imageBytes;

                src.Save(output, ImageFormat.Png);
            }
            else
            {
                using var dest = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                dest.SetResolution(src.HorizontalResolution, src.VerticalResolution);

                using var wrap = new ImageAttributes();
                wrap.SetWrapMode(WrapMode.TileFlipXY);

                var destRect = new Rectangle(0, 0, width, height);

                using var g = Graphics.FromImage(dest);
                g.DrawImage(src, destRect, 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);

                dest.Save(output, ImageFormat.Png);
            }

            return output.ToArray();
        }

        private string Lang(string key, string userId = null, params object[] args) =>
            string.Format(lang.GetMessage(key, this, userId), args);

        private Vector3 NormalizePosition(Vector3 initialPos, Vector3 currentPos, float diffRot)
        {
            var transformedPos = currentPos - initialPos;
            var newX = transformedPos.x * (float)Math.Cos(-diffRot) +
                       transformedPos.z * (float)Math.Sin(-diffRot);
            var newZ = transformedPos.z * (float)Math.Cos(-diffRot) -
                       transformedPos.x * (float)Math.Sin(-diffRot);

            transformedPos.x = newX;
            transformedPos.z = newZ;

            return transformedPos;
        }

        private PasteData Paste(ICollection<Dictionary<string, object>> entities, Dictionary<string, object> protocol,
            bool ownership, Vector3 startPos, IPlayer player, bool stability, float rotationCorrection,
            float heightAdj, bool auth, Action callback, Action<BaseEntity> callbackSpawned, string filename,
            bool checkPlaced, bool enableSaving = true, bool? dlc = null, int? skinsMode = null)
        {
            //Settings

            var isItemReplace = !protocol.ContainsKey("items");

            var eulerRotation = new Vector3(0f, rotationCorrection * Mathf.Rad2Deg, 0f);
            var quaternionRotation = Quaternion.Euler(eulerRotation);
            
            // Parse VersionNumber
            var version = protocol.ContainsKey("version") ? protocol["version"] as Dictionary<string, object> : null;
            
            VersionNumber vNumber = default;
            if (version != null)
                vNumber = new VersionNumber((int)version["Major"], (int)version["Minor"], (int)version["Patch"]);
            
            var pasteData = new PasteData
            {
                HeightAdj = heightAdj,
                IsItemReplace = isItemReplace,
                Entities = entities,
                Player = player,
                BasePlayer = player.Object as BasePlayer,
                QuaternionRotation = quaternionRotation,
                StartPos = startPos,
                Stability = stability,
                Auth = auth,
                Ownership = ownership,
                CallbackFinished = callback,
                CallbackSpawned = callbackSpawned,
                Filename = filename,
                CheckPlaced = checkPlaced,
                Version = vNumber,
                EnableSaving = enableSaving,
                Dlc = dlc ?? _config.Paste.Dlc,
                SkinsMode = (SkinsMode)(skinsMode.HasValue && IsValidSkinsMode(skinsMode.Value)
                    ? skinsMode.Value
                    : _config.Paste.SkinsMode)
            };

            if (!_pasteReady)
            {
                PrintWarning($"Pasting '{filename}' has been queued: waiting for Steam definitions");
                _pendingPastes.Add(pasteData);
            }
            else
                NextTick(() => PasteLoop(pasteData));

            return pasteData;
        }

        private void PasteLoop(PasteData pasteData)
        {
            if (pasteData.Cancelled)
            {
                UndoLoop(new HashSet<BaseEntity>(pasteData.PastedEntities), pasteData.Player,
                    pasteData.PastedEntities.Count);
                
                return;
            }

            var entities = pasteData.Entities;
            var todo = entities.Take(_config.PasteBatchSize).ToArray();

            foreach (var data in todo)
            {
                entities.Remove(data);

                PasteEntity(data, pasteData);
            }

            if (entities.Count > 0)
                NextTick(() => PasteLoop(pasteData));
            else
            {

                // Adjust IOEntity positions to fix alignment issues for older file versions
                if (pasteData.Version < new VersionNumber(4, 2, 0))
                    pasteData.checkPosition = Pool.Get<List<IOEntity>>();

                foreach (var ioData in pasteData.EntityLookup.Values.ToArray())
                    ProgressIOEntity(ioData, pasteData);

                if (pasteData.checkPosition != null)
                {
                    AdjustIOEntityPositions(pasteData);
                    Pool.FreeUnmanaged(ref pasteData.checkPosition);
                }

                foreach (var keyPair in pasteData.ItemsWithSubEntity)
                {
                    SetItemSubEntity(pasteData, keyPair.Value, keyPair.Key);
                }

                foreach (var entity in pasteData.StabilityEntities)
                {
                    entity.grounded = false;
                    entity.InitializeSupports();
                    entity.UpdateStability();
                }

                foreach (var adapter in pasteData.industrialStorageAdaptors)
                {
                    if (adapter == null) { continue; }
                    if (!adapter.HasParent())
                    {
                        List<BaseEntity> ents = Facepunch.Pool.Get<List<BaseEntity>>();
                        Vis.Entities(adapter.transform.position + (adapter.transform.up * -0.2f), 0.01f, ents);
                        if (ents.Count > 0)
                        {
                            adapter.SetParent(ents[0], true, true);
                        }
                        Facepunch.Pool.FreeUnmanaged(ref ents);
                    }
                    adapter.MarkDirtyForceUpdateOutputs();
                    adapter.SendNetworkUpdateImmediate();
                    adapter.RefreshIndustrialPreventBuilding();
                    adapter.NotifyIndustrialNetworkChanged();
                }

                pasteData.FinalProcessingActions.ForEach(action => action());

                pasteData.Player.Reply(Lang("PASTE_SUCCESS", pasteData.Player.Id));
#if DEBUG
                pasteData.Player.Reply($"Stopwatch took: {pasteData.Sw.Elapsed.TotalMilliseconds} ms");
#endif

                if (!_lastPastes.ContainsKey(pasteData.Player.Id))
                    _lastPastes[pasteData.Player.Id] = new Stack<List<BaseEntity>>();

                _lastPastes[pasteData.Player.Id].Push(pasteData.PastedEntities);

                pasteData.CallbackFinished?.Invoke();

                Interface.CallHook("OnPasteFinished", pasteData.PastedEntities, pasteData.Filename, pasteData.Player, pasteData.StartPos);
            }
        }

        private void FindAndAssignTargetDoor(DoorManipulator doorManipulator)
        {
            if (!doorManipulator.IsValid() || doorManipulator.IsDestroyed)
                return;

            Transform manipulatorTransform = doorManipulator.transform;
            List<Door> doors = Pool.Get<List<Door>>();
            Vis.Entities(manipulatorTransform.position, 1f, doors, 2097152, QueryTriggerInteraction.Ignore);
            Door foundDoor = null;
            float closestDistance = float.PositiveInfinity;
            foreach (Door door in doors)
            {
                if (door.IsValid() && !door.IsDestroyed && !door.IsOnMovingObject())
                {
                    float distance = Vector3.Distance(door.transform.position, manipulatorTransform.position);
                    if (distance < closestDistance)
                    {
                        foundDoor = door;
                        closestDistance = distance;
                    }
                }
            }
            Pool.FreeUnmanaged(ref doors);

            if (foundDoor.IsValid())
            {
                doorManipulator.SetParent(foundDoor, true);
                doorManipulator.SetTargetDoor(foundDoor);
            }
        }

        private void AdjustIOEntityPositions(PasteData pasteData)
        {
            Dictionary<IOEntity, OriginalTransforms> originalTransforms = Pool.Get<Dictionary<IOEntity, OriginalTransforms>>();
            List<IOEntity> emptyOutputs = Pool.Get<List<IOEntity>>();

            // First pass: Adjust entities that have outputs
            for (int i = 0; i < pasteData.checkPosition.Count; i++)
            {
                var ioEntity = pasteData.checkPosition[i];
                if (!AdjustIOEntityPosition(ioEntity, originalTransforms, true))
                {
                    // Didn't have any outputs, queue for input check
                    emptyOutputs.Add(ioEntity);
                }
            }

            // Second pass: Adjust entities that didn't have any outputs
            for (int i = 0; i < emptyOutputs.Count; i++)
                AdjustIOEntityPosition(emptyOutputs[i], originalTransforms, false);

            // Third pass: Adjust the line points based on the new positions
            foreach (var (ioEntity, originalTransform) in originalTransforms)
                AdjustLinePointPositions(ioEntity, originalTransform);

            Pool.FreeUnmanaged(ref originalTransforms);
            Pool.FreeUnmanaged(ref emptyOutputs);
        }

        private bool GetConnectedIOEntity(IOEntity.IOSlot ioSlot, bool isCurrentSlotInput, out IOEntity connectedIOEntity, out IOEntity.IOSlot connectedIOSlot)
        {
            connectedIOEntity = null;
            connectedIOSlot = null;

            if (ioSlot == null || ioSlot.connectedTo == null || ioSlot.connectedToSlot < 0)
                return false;

            IOEntity ioEntity = ioSlot.connectedTo.Get();
            if (!ioEntity.IsValid() || ioEntity.IsDestroyed)
                return false;

            IOEntity.IOSlot[] ioEntitySlots = isCurrentSlotInput ? ioEntity.outputs : ioEntity.inputs;
            if (ioEntitySlots == null || ioSlot.connectedToSlot >= ioEntitySlots.Length)
                return false;

            connectedIOEntity = ioEntity;
            connectedIOSlot = ioEntitySlots[ioSlot.connectedToSlot];
            return true;
        }

        private bool AdjustIOEntityPosition(IOEntity ioEntity, Dictionary<IOEntity, OriginalTransforms> originalTransforms, bool checkOutputs)
        {
            Transform transform = ioEntity.transform;

            void ApplyPositionCorrection(Vector3 linePoint, Vector3 handlePosition)
            {
                Vector3 localDiff = linePoint - handlePosition;
                Vector3 worldDiff = transform.TransformDirection(localDiff.normalized) * localDiff.magnitude;
                float magnitude = worldDiff.magnitude;
                if (magnitude >= 0.5f && magnitude <= 1.5f)
                {
                    originalTransforms.Add(ioEntity, new OriginalTransforms(transform, worldDiff));
                    transform.position += worldDiff;
                }
            }

            if (checkOutputs)
            {
                if (ioEntity.outputs == null)
                    return false;

                for (int i = 0; i < ioEntity.outputs.Length; i++)
                {
                    IOEntity.IOSlot ioOutput = ioEntity.outputs[i];
                    if (ioOutput == null || ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                        continue;

                    Vector3 linePoint = ioOutput.linePoints[ioOutput.linePoints.Length - 1];
                    if (linePoint == Vector3.zero)
                        continue;

                    ApplyPositionCorrection(linePoint, ioOutput.handlePosition);
                    return true;
                }
            }
            else
            {
                if (ioEntity.inputs == null)
                    return false;

                for (int i = 0; i < ioEntity.inputs.Length; i++)
                {
                    IOEntity.IOSlot ioInput = ioEntity.inputs[i];
                    if (!GetConnectedIOEntity(ioInput, true, out IOEntity outputIoEntity, out IOEntity.IOSlot ioOutput))
                        continue;

                    if (ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                        continue;

                    Vector3 linePoint = ioOutput.linePoints[0];
                    if (linePoint == Vector3.zero)
                        continue;

                    Vector3 localLinePoint;
                    if (originalTransforms.TryGetValue(outputIoEntity, out var origTransform))
                    {
                        Vector3 scaledLinePoint = new Vector3(
                            origTransform.localScale.x * linePoint.x,
                            origTransform.localScale.y * linePoint.y,
                            origTransform.localScale.z * linePoint.z
                        );
                        Vector3 rotatedLinePoint = origTransform.rotation * scaledLinePoint;
                        Vector3 worldLinePoint = origTransform.position + rotatedLinePoint;

                        localLinePoint = transform.InverseTransformPoint(worldLinePoint);
                    }
                    else
                    {
                        localLinePoint = transform.InverseTransformPoint(outputIoEntity.transform.TransformPoint(linePoint));
                    }

                    ApplyPositionCorrection(localLinePoint, ioInput.handlePosition);
                    return true;
                }
            }

            return false;
        }

        private void AdjustLinePointPositions(IOEntity ioEntity, OriginalTransforms originalTransform)
        {
            Transform transform = ioEntity.transform;
            Vector3 diff = originalTransform.diff;

            if (ioEntity.outputs != null)
            {
                for (int i = 0; i < ioEntity.outputs.Length; i++)
                {
                    IOEntity.IOSlot ioOutput = ioEntity.outputs[i];
                    if (!GetConnectedIOEntity(ioOutput, false, out IOEntity inputIoEntity, out IOEntity.IOSlot ioInput))
                        continue;

                    if (ioOutput.linePoints != null)
                    {
                        ioOutput.originPosition = transform.position;
                        int max = ioOutput.linePoints.Length - 1;
                        for (int x = 0; x < ioOutput.linePoints.Length; x++)
                        {
                            if (ioOutput.linePoints[x] == Vector3.zero)
                                continue;

                            if (x == 0)
                                ioOutput.linePoints[x] = transform.InverseTransformPoint(inputIoEntity.transform.TransformPoint(inputIoEntity.inputs[ioOutput.connectedToSlot].handlePosition));
                            else if (x == max)
                                ioOutput.linePoints[x] = ioOutput.handlePosition;
                            else
                                ioOutput.linePoints[x] -= diff;
                        }
                    }
                }
            }

            if (ioEntity.inputs != null)
            {
                for (int i = 0; i < ioEntity.inputs.Length; i++)
                {
                    IOEntity.IOSlot ioInput = ioEntity.inputs[i];
                    if (!GetConnectedIOEntity(ioInput, true, out IOEntity outputIoEntity, out IOEntity.IOSlot ioOutput))
                        continue;

                    if (ioOutput.linePoints == null || ioOutput.linePoints.Length == 0)
                        continue;

                    if (ioOutput.linePoints[0] == Vector3.zero)
                        continue;

                    ioOutput.linePoints[0] = outputIoEntity.transform.InverseTransformPoint(transform.TransformPoint(ioInput.handlePosition));
                }
            }

            ioEntity.SendNetworkUpdate();
            ioEntity.RefreshIndustrialPreventBuilding();
        }

        private void PasteEntity(Dictionary<string, object> data, PasteData pasteData, BaseEntity parent = null)
        {
            bool isChild = parent != null;

            var prefabname = GetPrefabName((string)data["prefabname"]);
#if DEBUG
            Puts($"{nameof(PasteLoop)}: Entity {prefabname}");
#endif
            
            var skinid = data.ContainsKey("skinid")
                ? FilterSkinId(pasteData, ulong.Parse(data["skinid"].ToString()))
                : 0;

            if (!pasteData.Dlc || pasteData.SkinsMode != SkinsMode.AllSkins)
            {
                string redirectPrefab = null;
                if (GetItemDefinitionForPrefab(prefabname, out var itemDef, useRedirect: false) &&
                    itemDef.isRedirectOf != null)
                    _itemDefToPrefab.TryGetValue(itemDef.isRedirectOf, out redirectPrefab);

                if (!pasteData.Dlc && _dlcPrefabs.Contains(prefabname))
                {
                    if (redirectPrefab == null || _dlcPrefabs.Contains(redirectPrefab))
                        return;

                    prefabname = redirectPrefab;
                    skinid = 0;
                }
                else if (pasteData.SkinsMode != SkinsMode.AllSkins && redirectPrefab != null)
                {
                    if (ShouldRedirectForSkinsMode(pasteData, itemDef))
                    {
                        prefabname = redirectPrefab;
                        skinid = 0;
                    }
                }
            }

            var pos = isChild ? Vector3.zero : (Vector3)data["position"];
            var rot = isChild ? Quaternion.identity : (Quaternion)data["rotation"];
            var localPos = isChild ? (Vector3)data["position"] : Vector3.zero;
            var localRot = isChild ? (Quaternion)data["rotation"] : Quaternion.identity;
                
            var ownerId = pasteData.BasePlayer?.userID ?? 0;
            if (data.ContainsKey("ownerid"))
            {
#if DEBUG
                Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1077");
#endif
                ownerId = Convert.ToUInt64(data["ownerid"]);
            }

            if (pasteData.CheckPlaced && CheckPlaced(prefabname, pos, rot))
                return;

            if (prefabname.Contains("pillar"))
                return;

            // Used to copy locks for no reason in previous versions (is included in the slots info so no need to copy locks) so just skipping them.
            if (prefabname.Contains("locks") && pasteData.Version < new VersionNumber(4, 2, 0))
                return;

            if (_config.BlockedPrefabs.Exists(prefabname.Contains))
                return;

            BaseEntity entity = null;

            // Check to see if this child is already spawned
            if (isChild && parent.children != null)
            {
                foreach (var child in parent.children)
                {
                    if (child == null || child.IsDestroyed)
                        continue;

                    // Skip associated entities that can have multiple instances and always have localPosition set to Vector3.zero
                    if (ItemModAssociatedEntities.Contains(child.GetType()) && child.transform.localPosition == Vector3.zero)
                        continue;

                    if (child.PrefabName == prefabname && (child.transform.localPosition - localPos).sqrMagnitude < 0.001f)
                    {
                        entity = child;
                        break;
                    }
                }
            }

            if (entity == null)
                entity = GameManager.server.CreateEntity(GetPrefabName(prefabname), pos, rot);

            if (entity == null)
                return;
            
            var transform = entity.transform;
            
            // If the entity is a child, set the parent and the local position and rotation.
            if (isChild)
            {
                if (!entity.isSpawned)
                {
                    entity.gameObject.Identity();
                    if (data.ContainsKey("parentbone"))
                        entity.SetParent(parent, data["parentbone"].ToString());
                    else
                        entity.SetParent(parent);

                    // Skip OnDeployed() for entities that don't properly handle null "deployedBy" or "fromItem.info"
                    if (entity is not CustomDoorManipulator && entity is not AutoTurret &&
                        entity is not GrowableEntity && entity is not Signage)
                    {
                        entity.OnDeployed(parent, null, _emptyItem);
                    }


                    transform.localPosition = localPos;
                    transform.localRotation = localRot;
                }
            }
            // If the entity is not a child, set the position and rotation.
            else
            {
                transform.position = pos;
                transform.rotation = rot;
            }

            if (pasteData.BasePlayer != null)
                entity.SendMessage("SetDeployedBy", pasteData.BasePlayer, SendMessageOptions.DontRequireReceiver);

            if (pasteData.Ownership)
                entity.OwnerID = ownerId;

            var buildingBlock = entity as BuildingBlock;
            if (buildingBlock != null)
            {
                buildingBlock.blockDefinition = PrefabAttribute.server.Find<Construction>(buildingBlock.prefabID);
                var grade = (BuildingGrade.Enum)Convert.ToInt32(data["grade"]);
                if (skinid != 0ul && !HasGrade(buildingBlock, grade, skinid))
                    skinid = 0ul;
                buildingBlock.SetGrade(grade);
                if (!pasteData.Stability)
                    buildingBlock.grounded = true;
            }

            var decayEntity = entity as DecayEntity;
            if (decayEntity != null)
            {
                if (pasteData.BuildingId == 0)
                    pasteData.BuildingId = BuildingManager.server.NewBuildingID();

                decayEntity.AttachToBuilding(pasteData.BuildingId);
            }

            var stabilityEntity = entity as StabilityEntity;
            if (stabilityEntity != null)
            {
                if (!stabilityEntity.grounded)
                {
                    stabilityEntity.grounded = true;
                    pasteData.StabilityEntities.Add(stabilityEntity);
                }
            }

            if (data.TryGetValue("oldID", out var oldIDString))
            {
                ulong oldID = Convert.ToUInt64(oldIDString);
                if (!pasteData.EntityLookup.ContainsKey(oldID))
                {
                    pasteData.EntityLookup.Add(oldID, new Dictionary<string, object>
                    {
                        { "entity", entity }
                    });
                }
            }

            entity.skinID = skinid;

            if (!pasteData.EnableSaving)
            {
                entity.EnableSaving(false);
            }

            if (entity is ModularCar modularCar && data.TryGetValue("children", out var childrenObj))
            {
                // If there are children present, disable the default spawn settings to prevent stacking modules
                // on top of each other, which could cause the vehicle to be destroyed
                var children = childrenObj as List<object>;
                if (children != null && children.Count > 0)
                    modularCar.spawnSettings.useSpawnSettings = false;
            }

            if (!entity.isSpawned)
                entity.Spawn();

            if (entity.net == null || entity.IsDestroyed)
                return;

            var baseCombat = entity as BaseCombatEntity;
            if (buildingBlock != null)
            {
                buildingBlock.SetHealthToMax();
                buildingBlock.UpdateSkin();
                buildingBlock.SendNetworkUpdate();
                buildingBlock.ResetUpkeepTime();
                object customColour;
                if (data.TryGetValue("customColour", out customColour))
                    buildingBlock.SetCustomColour(Convert.ToUInt32(customColour));

                object rawValue;
                for (int side = 0; side <= 1; side++)
                {
                    string idKey = side == 0 ? "wallpaperID" : "wallpaperID2";

                    if (data.TryGetValue(idKey, out rawValue))
                    {
                        ulong wallpaperId = Convert.ToUInt64(rawValue);
                        if (wallpaperId == 0UL)
                            continue;

                        int currentSide = side;
                        float rotation = 0f;
                        float health = BuildingBlock.WALLPAPER_MAXHEALTH;

                        string rotationKey = currentSide == 0 ? "wallpaperRotation" : "wallpaperRotation2";
                        if (data.TryGetValue(rotationKey, out rawValue))
                            rotation = Convert.ToSingle(rawValue);

                        string healthKey = currentSide == 0 ? "wallpaperHealth" : "wallpaperHealth2";
                        if (data.TryGetValue(healthKey, out rawValue))
                            health = Convert.ToSingle(rawValue);

                        // Defer wallpaper until all building blocks are pasted.
                        // Interior wallpaper (side 1) must be "inside" (fully enclosed)
                        // or it will despawn on the next stability tick
                        pasteData.FinalProcessingActions.Add(() =>
                        {
                            if (buildingBlock == null || !buildingBlock.IsValid() || buildingBlock.IsDestroyed)
                                return;

                            buildingBlock.SetWallpaper(wallpaperId, currentSide, rotation);

                            if (currentSide == 0)
                                buildingBlock.wallpaperHealth = health;
                            else
                                buildingBlock.wallpaperHealth2 = health;
                        });
                    }
                }
            }
            else if (baseCombat != null)
                baseCombat.SetHealth(baseCombat.MaxHealth());

            var firework = entity as PatternFirework;
            if (firework != null && data.ContainsKey("patternfirework"))
            {
                if (firework?.Design == null)
                {
                    firework.Design = new ProtoBuf.PatternFirework.Design();
                    firework.Design.stars = new List<ProtoBuf.PatternFirework.Star>();
                }
                var pattern = (Dictionary<string, object>)data["patternfirework"];
                object editedBy;
                if (data.TryGetValue("editedBy", out editedBy))
                    firework.Design.editedBy = Convert.ToUInt32(pattern["editedBy"]);

                firework.Design.stars = DeSerializeStarPattern(pattern["stars"].ToString());
                firework.SendNetworkUpdate();
            }

            // This needs to stay for the old configs to load properly but is unused because of the new 'children' system.
            pasteData.PastedEntities.AddRange(TryPasteSlots(entity, data, pasteData));

            if (isChild && data.ContainsKey( "slot" ))
            {
                var slot = (BaseEntity.Slot) Convert.ToInt32(data["slot"]);
                if (parent.HasSlot( slot ))
                {
                    parent.SetSlot( slot, entity );
                }
            }
            
            TryPasteLocks(entity, data, pasteData);
            
            var autoTurret = entity as AutoTurret;
            if (autoTurret != null)
            {
                var authorizedPlayers = new List<ulong>();

                if (data.ContainsKey("autoturret"))
                {
#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1305");
#endif
                    var autoTurretData = data["autoturret"] as Dictionary<string, object>;
                    authorizedPlayers = (autoTurretData["authorizedPlayers"] as List<object>)
                        .Select(Convert.ToUInt64).ToList();
                }

                if (pasteData.BasePlayer != null && !authorizedPlayers.Contains(pasteData.BasePlayer.userID) &&
                    pasteData.Auth)
                    authorizedPlayers.Add(pasteData.BasePlayer.userID);

                foreach (var userId in authorizedPlayers)
                {
                    autoTurret.authorizedPlayers.Add(userId);
                }

                autoTurret.SendNetworkUpdate();
            }

            var box = entity as IItemContainerEntity;
            if (box != null)
            {
                if (box.inventory == null)
                {
                    if (entity is StorageContainer storageContainer)
                    {
                        storageContainer.CreateInventory(true);
                    }
                    else if (entity is IndustrialCrafter crafter)
                    {
                        crafter.CreateInventory(true);
                    }
                    else if (entity is ContainerIOEntity containerIo)
                    {
                        containerIo.CreateInventory(true);
                        containerIo.OnInventoryFirstCreated(containerIo.inventory);
                    }
                    else
                    {
                        Puts("WARNING: New IItemContainerEntity container '{0}' not supported", entity);
                    }
                }
                else
                {
                    box.inventory.Clear();
                }

                if (box.inventory != null)
                {
                    PopulateInventory(pasteData, data, entity, box.inventory);

                    if (autoTurret != null)
                    {
                        autoTurret.UpdateAttachedWeapon();
                    }

                    entity.SendNetworkUpdate();
                }
            }

            var iSignage = entity as ISignage;
            if (iSignage != null && data.ContainsKey("sign"))
            {
                var signData = data["sign"] as Dictionary<string, object>;

                if (signData.ContainsKey("amount") || signData.ContainsKey("texture") || signData.ContainsKey("texture0"))
                {
                    int amount = signData.ContainsKey("amount") && int.TryParse(signData["amount"].ToString(), out var parsedAmount) ? parsedAmount : 1;

                    uint[] newTextureIDs = new uint[amount];

                    for (var num = 0; num < amount; num++)
                    {
                        string textureKey = $"texture{num}";
                        if (amount == 1 && signData.ContainsKey("texture"))
                        {
                            textureKey = "texture";
                        }

                        if (signData.ContainsKey(textureKey))
                        {
                            var imageBytes = FixSignage(iSignage, Convert.FromBase64String(signData[textureKey].ToString()));
                            newTextureIDs[num] = FileStorage.server.Store(imageBytes, iSignage.FileType, entity.net.ID);
                        }
                        else
                        {
                            newTextureIDs[num] = 0;
                        }
                    }

                    iSignage.SetTextureCRCs(newTextureIDs);
                }

                if (iSignage is Signage sign)
                {
                    if (Convert.ToBoolean(signData["locked"]))
                        sign.SetFlag(BaseEntity.Flags.Locked, true);

                    sign.SendNetworkUpdate();
                }
            }

            var photoEntity = entity as PhotoEntity;
            if (photoEntity != null && data.ContainsKey("sign"))
            {
                var signData = data["sign"] as Dictionary<string, object>;

                if (signData.ContainsKey("amount"))
                {
                    int amount;
                    if (int.TryParse(signData["amount"].ToString(), out amount))
                    {
                        for (var num = 0; num < amount; num++)
                        {
                            string textureKey = $"texture{num}";

                            if (signData.ContainsKey(textureKey))
                            {
                                var imageBytes = Convert.FromBase64String(signData[textureKey].ToString());
                                photoEntity.SetImageData(0UL, imageBytes);
                            }
                        }
                    }
                }
            }

            var signContent = entity as SignContent;
            if (signContent != null && data.ContainsKey("sign"))
            {
                var signData = data["sign"] as Dictionary<string, object>;

                if (signData.ContainsKey("amount"))
                {
                    int amount;
                    if (int.TryParse(signData["amount"].ToString(), out amount))
                    {
                        uint[] newTextureIDs = new uint[amount];

                        for (var num = 0; num < amount; num++)
                        {
                            string textureKey = $"texture{num}";

                            if (signData.ContainsKey(textureKey))
                            {
                                var imageBytes = Convert.FromBase64String(signData[textureKey].ToString());
                                newTextureIDs[num] = FileStorage.server.Store(imageBytes, signContent.FileType, entity.net.ID);
                            }
                            else
                            {
                                newTextureIDs[num] = 0;
                            }
                        }

                        signContent.textureIDs = newTextureIDs;
                    }
                }
            }

            var paintedItemStorageEntity = entity as PaintedItemStorageEntity;
            if (paintedItemStorageEntity != null && data.ContainsKey("sign"))
            {
                var signData = data["sign"] as Dictionary<string, object>;

                if (signData.ContainsKey("amount"))
                {
                    int amount;
                    if (int.TryParse(signData["amount"].ToString(), out amount))
                    {
                        for (var num = 0; num < amount; num++)
                        {
                            if (signData.ContainsKey($"texture{num}"))
                            {
                                var imageBytes = Convert.FromBase64String(signData[$"texture{num}"].ToString());

                                if (ImageProcessing.IsValidPNG(imageBytes, 512, 512))
                                    paintedItemStorageEntity._currentImageCrc = FileStorage.server.Store(imageBytes, FileStorage.Type.png, paintedItemStorageEntity.net.ID);
                            }
                        }
                    }
                }
            }

            var lights = entity as ChristmasLights;
            if (lights != null)
            {
                if (data.ContainsKey("animationStyle"))
                {
                    lights.animationStyle = (ChristmasLights.AnimationType)data["animationStyle"];
                }
            }

            var stringLights = entity as StringLights;
            if (stringLights != null)
            {
                if (data.ContainsKey("points"))
                {
                    var points = data["points"] as List<object>;
                    if (points != null && points.Count > 0)
                    {
                        foreach (Dictionary<string, object> pointEntry in points)
                        {
                            var normal = (Dictionary<string, object>)pointEntry["normal"];
                            var point = (Dictionary<string, object>)pointEntry["point"];

                            var adjustedPoint = pasteData.QuaternionRotation * new Vector3(Convert.ToSingle(point["x"]),
                                Convert.ToSingle(point["y"]),
                                Convert.ToSingle(point["z"])) + pasteData.StartPos;

                            adjustedPoint.y += pasteData.HeightAdj;

                            float slack = 0f;
                            if (pointEntry.TryGetValue("slack", out var rawSlack))
                                slack = Convert.ToSingle(rawSlack);

                            stringLights.points.Add(new StringLights.PointEntry
                            {
                                normal = new Vector3(Convert.ToSingle(normal["x"]), Convert.ToSingle(normal["y"]),
                                    Convert.ToSingle(normal["z"])),
                                point = adjustedPoint,
                                slack = slack
                            });
                        }
                    }
                }
            }

            var chandelier = entity as Chandelier;
            if (chandelier != null)
            {
                if (data.ContainsKey("chandelierLength"))
                {
                    chandelier.SetChandelierLength(Convert.ToSingle(data["chandelierLength"]));
                }
            }

            var orientableLight = entity as OrientableLight;
            if (orientableLight != null)
            {
                if (data.ContainsKey("pitchAmount"))
                {
                    orientableLight.pitchAmount = Convert.ToSingle(data["pitchAmount"]);
                }

                if (data.ContainsKey("yawAmount"))
                {
                    orientableLight.yawAmount = Convert.ToSingle(data["yawAmount"]);
                }
            }

            var mannequin = entity as Mannequin;
            if (mannequin != null)
            {
                if (data.ContainsKey("poseIndex"))
                {
                    mannequin.PoseIndex = Convert.ToInt32(data["poseIndex"]);
                }
            }

            var sleepingBag = entity as SleepingBag;
            if (sleepingBag != null && data.ContainsKey("sleepingbag"))
            {
                var bagData = data["sleepingbag"] as Dictionary<string, object>;

                sleepingBag.niceName = bagData["niceName"].ToString();
                var deployerUserID = ulong.Parse(bagData["deployerUserID"].ToString());
                if (sleepingBag.deployerUserID != deployerUserID)
                {
                    var oldUser = sleepingBag.deployerUserID;
                    sleepingBag.deployerUserID = deployerUserID;
                    SleepingBag.OnBagChangedOwnership(sleepingBag, oldUser);
                }
                sleepingBag.SetPublic(Convert.ToBoolean(bagData["isPublic"]));
            }

            var cupboard = entity as BuildingPrivlidge;
            if (cupboard != null)
            {
                var authorizedPlayers = new List<ulong>();

                if (data.ContainsKey("cupboard"))
                {
#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1521");
#endif
                    var cupboardData = data["cupboard"] as Dictionary<string, object>;
                    authorizedPlayers = (cupboardData["authorizedPlayers"] as List<object>).Select(Convert.ToUInt64)
                        .ToList();
                }

                if (pasteData.BasePlayer != null && !authorizedPlayers.Contains(pasteData.BasePlayer.userID) &&
                    pasteData.Auth)
                    authorizedPlayers.Add(pasteData.BasePlayer.userID);

                foreach (var userId in authorizedPlayers)
                {
                    cupboard.authorizedPlayers.Add(userId);
                }

                cupboard.SendNetworkUpdate();
            }

            var tinCanAlarm = entity as TinCanAlarm;
            if (tinCanAlarm != null)
            {
                if (data.ContainsKey("endPoint"))
                {
                    var endPoint = (Dictionary<string, object>)data["endPoint"];
                    var adjustedEndPoint = pasteData.QuaternionRotation * new Vector3(Convert.ToSingle(endPoint["x"]),
                        Convert.ToSingle(endPoint["y"]),
                        Convert.ToSingle(endPoint["z"])) + pasteData.StartPos;

                    adjustedEndPoint.y += pasteData.HeightAdj;

                    tinCanAlarm.endPoint = adjustedEndPoint;
                    tinCanAlarm.SendNetworkUpdate();
                }
            }

            var cctvRc = entity as CCTV_RC;
            if (cctvRc != null && data.ContainsKey("cctv"))
            {
                var cctv = (Dictionary<string, object>)data["cctv"];
                cctvRc.yawAmount = Convert.ToSingle(cctv["yaw"]);
                cctvRc.pitchAmount = Convert.ToSingle(cctv["pitch"]);
                cctvRc.rcIdentifier = cctv["rcIdentifier"].ToString();
                cctvRc.SendNetworkUpdate();
            }

            var computerStation = entity as ComputerStation;
            if (computerStation != null && data.ContainsKey("bookmarks"))
            {
                var bookmarks = data["bookmarks"] as string;
                foreach (string text in bookmarks.Split(ComputerStation.BookmarkSplit, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ComputerStation.IsValidIdentifier(text))
                    {
                        computerStation.controlBookmarks.Add(text);
                    }
                }
            }

            var wantedPoster = entity as WantedPoster;
            if (wantedPoster != null && data.ContainsKey("wantedPoster"))
            {
                var poster = (Dictionary<string, object>)data["wantedPoster"];
                wantedPoster.playerId = Convert.ToUInt64(poster["playerId"]);
                wantedPoster.playerName = poster["playerName"].ToString();
                wantedPoster.SendNetworkUpdate();
            }

            var headEntity = entity as HeadEntity;
            if (headEntity != null && data.ContainsKey("currentTrophyData"))
            {
                if (headEntity.CurrentTrophyData == null)
                    headEntity.CurrentTrophyData = Pool.Get<HeadData>();

                PopulateHeadData(data, headEntity.CurrentTrophyData);
            }

            var huntingTrophy = entity as HuntingTrophy;
            if (huntingTrophy != null && data.ContainsKey("currentTrophyData"))
            {
                if (huntingTrophy.CurrentTrophyData == null)
                    huntingTrophy.CurrentTrophyData = Pool.Get<HeadData>();

                PopulateHeadData(data, huntingTrophy.CurrentTrophyData);
            }

            var pagerEntity = entity as PagerEntity;
            if (pagerEntity != null && data.ContainsKey("frequency"))
            {
                pagerEntity.ChangeFrequency(Convert.ToInt32(data["frequency"]));
                pagerEntity.SendNetworkUpdate();
            }

            var rfTimedExplosive = entity as RFTimedExplosive;
            if (rfTimedExplosive != null && data.ContainsKey("frequency"))
            {
                rfTimedExplosive.SetFrequency(Convert.ToInt32(data["frequency"]));
                rfTimedExplosive.SetFuse(0f);
            }

            var cassette = entity as Cassette;
            if (cassette != null)
            {
                PopulateCassette(data, cassette);
            }

            var mobileInventoryEntity = entity as MobileInventoryEntity;
            if (mobileInventoryEntity != null)
            {
                mobileInventoryEntity.SetFlag(MobileInventoryEntity.Ringing, false);
            }

            var elevator = entity as Elevator;
            if (elevator != null && data.ContainsKey("Floor"))
            {
                elevator.Floor = Convert.ToInt32(data["Floor"]);
            }

            var weaponRack = entity as WeaponRack;
            if (weaponRack != null && data.ContainsKey("gridSlots"))
            {
                var gridSlots = (List<object>)data["gridSlots"];
                if (gridSlots != null)
                {
                    foreach (Dictionary<string, object> gridSlot in gridSlots)
                    {
                        if (gridSlot != null)
                        {
                            int gridSlotIndex = Convert.ToInt32(gridSlot["GridSlotIndex"]);
                            int inventoryIndex = Convert.ToInt32(gridSlot["InventoryIndex"]);
                            int rotation = Convert.ToInt32(gridSlot["Rotation"]);

                            var item = weaponRack.inventory.GetSlot(inventoryIndex);
                            if (item != null)
                            {
                                var slot = weaponRack.gridSlots[inventoryIndex];
                                if (slot != null)
                                {
                                    slot.SetItem(item, item.info, gridSlotIndex, rotation);
                                    weaponRack.SetGridCellContents(slot, false);
                                }
                            }
                        }
                    }
                }
            }

            var commandBlock = entity as global::CommandBlock;
            if (commandBlock != null)
            {
                object rawValue;
                if (data.TryGetValue("currentCommand", out rawValue) &&
                    rawValue is string rawCommand && !string.IsNullOrEmpty(rawCommand))
                    commandBlock.currentCommand = rawCommand;

                if (data.TryGetValue("lastPlayerID", out rawValue))
                    commandBlock.lastPlayerID = Convert.ToUInt64(rawValue);
            }

            var phoneController = entity.GetComponent<PhoneController>();
            if (phoneController != null && data.ContainsKey("savedVoicemail"))
            {
                // Requires the cassette item subentity to exist, delay processing until the very end
                pasteData.FinalProcessingActions.Add(() =>
                {
                    var savedVoicemail = (List<object>)data["savedVoicemail"];
                    if (savedVoicemail != null)
                    {
                        foreach (Dictionary<string, object> voicemail in savedVoicemail)
                        {
                            if (voicemail != null)
                            {
                                byte[] audioData = Convert.FromBase64String(voicemail["audio"].ToString());
                                phoneController.SaveVoicemail(audioData, voicemail["userName"].ToString());
                            }
                        }
                    }
                });
            }

            var vendingMachine = entity as VendingMachine;
            if (vendingMachine != null && data.ContainsKey("vendingmachine"))
            {
                var vendingData = data["vendingmachine"] as Dictionary<string, object>;

                vendingMachine.shopName = vendingData["shopName"].ToString();
                vendingMachine.SetFlag(BaseEntity.Flags.Reserved4,
                    Convert.ToBoolean(vendingData["isBroadcasting"]));

                var sellOrders = vendingData["sellOrders"] as List<object>;

                foreach (var orderPreInfo in sellOrders)
                {
                    var orderInfo = orderPreInfo as Dictionary<string, object>;

                    if (!orderInfo.ContainsKey("inStock"))
                    {
                        orderInfo["inStock"] = 0;
                        orderInfo["currencyIsBP"] = false;
                        orderInfo["itemToSellIsBP"] = false;
                    }

                    int itemToSellId = Convert.ToInt32(orderInfo["itemToSellID"]),
                        currencyId = Convert.ToInt32(orderInfo["currencyID"]);

                    if (pasteData.IsItemReplace)
                    {
                        itemToSellId = GetItemId(itemToSellId);
                        currencyId = GetItemId(currencyId);
                    }

                    vendingMachine.sellOrders.sellOrders.Add(new ProtoBuf.VendingMachine.SellOrder
                    {
                        ShouldPool = false,
                        itemToSellID = itemToSellId,
                        itemToSellAmount = Convert.ToInt32(orderInfo["itemToSellAmount"]),
                        currencyID = currencyId,
                        currencyAmountPerItem = Convert.ToInt32(orderInfo["currencyAmountPerItem"]),
                        inStock = Convert.ToInt32(orderInfo["inStock"]),
                        currencyIsBP = Convert.ToBoolean(orderInfo["currencyIsBP"]),
                        itemToSellIsBP = Convert.ToBoolean(orderInfo["itemToSellIsBP"])
                    });
                }

                vendingMachine.FullUpdate();
            }

            var ridableHorse2 = entity as RidableHorse;
            if (ridableHorse2 != null)
            {
                if (data.TryGetValue("currentBreedIndex", out var currentBreedIndexObj))
                    ridableHorse2.SetBreed(Convert.ToInt32(currentBreedIndexObj));

                if (data.TryGetValue("towingEntityId", out var towingEntityIdObj))
                {
                    // Try to find the ITowing entity after everything has pasted
                    pasteData.FinalProcessingActions.Add(() =>
                    {
                        if (pasteData.EntityLookup.TryGetValue(Convert.ToUInt64(towingEntityIdObj), out var result) &&
                            result.TryGetValue("entity", out var newEntityObj))
                        {
                            if (newEntityObj is BaseEntity newEntity && newEntity.IsValid() && !newEntity.IsDestroyed &&
                                newEntity is ITowing iTowing)
                            {
                                newEntity.SetFlag(BaseEntity.Flags.Reserved14, false);
                                ridableHorse2.towingEntityId = newEntity.net.ID;
                                ridableHorse2.towableEntity = iTowing;
                                ridableHorse2.TowAttach();
                            }
                        }
                    });
                }
            }

            var constructableEntity = entity as ConstructableEntity;
            if (constructableEntity != null)
            {
                if (data.TryGetValue("currentMaterials", out var currentMaterialsObj))
                {
                    var currentMaterials = currentMaterialsObj as List<object>;
                    if (currentMaterials != null)
                    {
                        for (var i = 0; i < currentMaterials.Count; i++)
                            constructableEntity.currentMaterials[i] = Convert.ToInt32(currentMaterials[i]);
                    }
                }

                if (data.TryGetValue("health", out var health))
                    constructableEntity.SetHealth(Mathf.Min(Convert.ToSingle(health), constructableEntity.MaxHealth()));

                constructableEntity.SendNetworkUpdate();
                constructableEntity.UpdateState();
            }

            var ioEntity = entity as IOEntity;
            if (ioEntity.IsValid() && !ioEntity.IsDestroyed)
            {
                var ioData = new Dictionary<string, object>();

                if (data.ContainsKey("IOEntity"))
                {
                    ioData = data["IOEntity"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                }

                ioData.Add("entity", ioEntity);
                ioData.Add("newId", ioEntity.net.ID.Value);

                object oldIdObject;
                if (ioData.TryGetValue("oldID", out oldIdObject))
                {
#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1619");
#endif
                    var oldId = Convert.ToUInt64(oldIdObject);
                    pasteData.EntityLookup.Add(oldId, ioData);
                }
            }
            
            var flagsData = new Dictionary<string, object>();

            if (data.ContainsKey("flags"))
                flagsData = data["flags"] as Dictionary<string, object>;

            var flags = new Dictionary<BaseEntity.Flags, bool>();

            foreach (var flagData in flagsData)
            {
                BaseEntity.Flags baseFlag;
                if (Enum.TryParse(flagData.Key, out baseFlag))
                    flags.Add(baseFlag, Convert.ToBoolean(flagData.Value));
            }

            foreach (var flag in flags)
            {
                entity.SetFlag(flag.Key, flag.Value);
            }
            
            if (data.TryGetValue("boomBox", out var boomBoxObj) &&
                boomBoxObj is Dictionary<string, object> boomBoxData && boomBoxData != null &&
                entity.TryGetComponent<BoomBox>(out var boomBox))
            {
                PopulateBoomBox(boomBoxData, boomBox);
                if (boomBox.IsOn())
                {
                    boomBox.ServerTogglePlay(false);
                    pasteData.FinalProcessingActions.Add(() =>
                    {
                        if (boomBox == null)
                            return;

                        boomBox.Invoke(() =>
                        {
                            if (!boomBox.baseEntity.IsValid() || boomBox.baseEntity.IsDestroyed)
                                return;

                            if (!boomBox.HasFlag(BoomBox.HasCassette))
                                boomBox.baseEntity.ClientRPC<string>(RpcTarget.NetworkGroup("OnRadioIPChanged"), boomBox.CurrentRadioIp);

                            boomBox.ServerTogglePlay(true);

                            foreach (var connectedSpeaker in pasteData.ConnectedSpeakers) {
                                if (connectedSpeaker.IsValid() && !connectedSpeaker.IsDestroyed)
                                {
                                    connectedSpeaker.SetFlag(BaseEntity.Flags.Reserved8, false);
                                    connectedSpeaker.SetFlag(BaseEntity.Flags.Reserved8, true);
                                }
                            }
                        }, 1f);
                    });
                }
            }

            var connectedSpeaker = entity as ConnectedSpeaker;
            if (connectedSpeaker != null && connectedSpeaker.HasFlag(BaseEntity.Flags.Reserved8))
                pasteData.ConnectedSpeakers.Add(connectedSpeaker);

            var industrialCrafter = entity as IndustrialCrafter;
            if (industrialCrafter != null)
            {
                industrialCrafter.SetFlag(IndustrialCrafter.Crafting, false);
            }

            if (data.ContainsKey("children"))
            {
                var children = data["children"] as List<object>;

                if (children != null)
                {
                    foreach (var child in children)
                    {
                        var childData = child as Dictionary<string, object>;
                        if (childData == null)
                            continue;

                        PasteEntity(childData, pasteData, entity);
                    }
                }
            }
            
            var photoFrame = entity as PhotoFrame;
            if (photoFrame != null && data.ContainsKey("photoEntity"))
            {
                if (pasteData.EntityLookup.TryGetValue(Convert.ToUInt64(data["photoEntity"]), out var objData))
                {
                    if (objData["entity"] is BaseEntity baseEntity && baseEntity.IsValid() && !baseEntity.IsDestroyed && baseEntity.net.ID.IsValid)
                    {
                        photoFrame._photoEntity.uid = baseEntity.net.ID;
                    }
                }
            }

            var baseOven = entity as BaseOven;
            if (baseOven != null && baseOven.IsOn())
            {
                baseOven.StartCooking();
            }

            if (entity is IndustrialStorageAdaptor)
            {
                pasteData.industrialStorageAdaptors.Add(entity as IndustrialStorageAdaptor);
            }

            var mixingTable = entity as MixingTable;
            if (mixingTable != null && mixingTable.IsOn())
            {
                List<Item> orderedContainerItems = mixingTable.GetOrderedContainerItems(mixingTable.inventory, out var itemsAreContiguous);
                mixingTable.currentRecipe = RecipeDictionary.GetMatchingRecipeAndQuantity(mixingTable.Recipes, orderedContainerItems, out var quantity);
                mixingTable.currentQuantity = quantity;
                if (mixingTable.currentRecipe == null || !itemsAreContiguous)
                {
                    mixingTable.StopMixing();
                    return;
                }
                mixingTable.RemainingMixTime = mixingTable.currentRecipe.MixingDuration * mixingTable.currentQuantity;
                mixingTable.TotalMixTime = mixingTable.RemainingMixTime;
                if (mixingTable.RemainingMixTime == 0.0)
                {
                    mixingTable.ProduceItem(mixingTable.currentRecipe, mixingTable.currentQuantity);
                }
                else
                {
                    mixingTable.InvokeRepeating(mixingTable.TickMix, 1f, 1f);
                }
            }

            if (entity is FarmableAnimal farmableAnimal)
            {
                if (data.TryGetValue("hunger", out var hunger))
                    farmableAnimal.AnimalHunger = Convert.ToSingle(hunger);
                if (data.TryGetValue("thirst", out var thirst))
                    farmableAnimal.AnimalThirst = Convert.ToSingle(thirst);
                if (data.TryGetValue("love", out var love))
                    farmableAnimal.AnimalLove = Convert.ToSingle(love);
                if (data.TryGetValue("sunlight", out var sunlight))
                    farmableAnimal.AnimalSunlight = Convert.ToSingle(sunlight);
                if (data.TryGetValue("animalName", out var animalName))
                    farmableAnimal.AnimalName = (string) animalName;

                if (parent != null && parent is ChickenCoop chickenCoopParent &&
                    chickenCoopParent.ChickenPrefab.resourceID == entity.prefabID)
                {
                    ChickenCoop.AnimalStatus animalStatus = new ChickenCoop.AnimalStatus();
                    animalStatus.SpawnedAnimal.Set(farmableAnimal);
                    chickenCoopParent.Animals.Add(animalStatus);
                }

                farmableAnimal.SendNetworkUpdate();
            }

            if (entity is ChickenCoop chickenCoop)
            {
                if (entity.HasFlag(BaseEntity.Flags.Reserved1) &&
                    data.TryGetValue("timeUntilHatches", out var timeUntilHatchesObj))
                {
                    var timeUntilHatches = timeUntilHatchesObj as List<object>;
                    if (timeUntilHatches != null && timeUntilHatches.Count > 0)
                    {
                        for (var i = 0; i < timeUntilHatches.Count; i++)
                        {
                            chickenCoop.Animals.Add(new ChickenCoop.AnimalStatus()
                            {
                                TimeUntilHatch = (TimeUntil) Convert.ToSingle(timeUntilHatches[i])
                            });
                        }
                        if (!chickenCoop.IsInvoking(new Action(chickenCoop.CheckEggHatchState)))
                            chickenCoop.InvokeRepeating(new Action(chickenCoop.CheckEggHatchState), 10f, 10f);
                        chickenCoop.SendNetworkUpdate();
                    }
                }
            }

            pasteData.PastedEntities.Add(entity);
            pasteData.CallbackSpawned?.Invoke(entity);
        }

        private void ProgressIOEntity(Dictionary<string, object> ioData, PasteData pasteData)
        {
            if (!ioData.ContainsKey("entity"))
                return;

            var ioEntity = ioData["entity"] as IOEntity;

            if (!ioEntity.IsValid() || ioEntity.IsDestroyed)
                return;

            List<object> inputs = null;
            if (ioData.ContainsKey("inputs"))
                inputs = ioData["inputs"] as List<object>;

            var electricalBranch = ioEntity as ElectricalBranch;
            if (electricalBranch != null && ioData.ContainsKey("branchAmount"))
            {
                electricalBranch.branchAmount = Convert.ToInt32(ioData["branchAmount"]);
            }

            var counter = ioEntity as PowerCounter;
            if (counter != null)
            {
                if (ioData.ContainsKey("targetNumber"))
                    counter.targetCounterNumber = Convert.ToInt32(ioData["targetNumber"]);

                object counterNumber;
                counter.SetCounterNumber(ioData.TryGetValue("counterNumber", out counterNumber) ?
                    Convert.ToInt32(counterNumber) :
                    0);
            }

            var timerSwitch = ioEntity as TimerSwitch;
            if (timerSwitch != null && ioData.ContainsKey("timerLength"))
            {
                timerSwitch.timerLength = Convert.ToSingle(ioData["timerLength"]);
                if(timerSwitch.IsOn())
                {
                    timerSwitch.SetFlag(BaseEntity.Flags.On, false);
                    timerSwitch.SwitchPressed();
                }
            }

            var rfBroadcaster = ioEntity as RFBroadcaster;
            if (rfBroadcaster != null && ioData.ContainsKey("frequency"))
            {
                int newFrequency = Convert.ToInt32(ioData["frequency"]);
                if (ioEntity.IsPowered())
                    RFManager.AddBroadcaster(newFrequency, rfBroadcaster);
                rfBroadcaster.frequency = newFrequency;
                rfBroadcaster.MarkDirty();
            }

            var rfReceiver = ioEntity as RFReceiver;
            if (rfReceiver != null && ioData.ContainsKey("frequency"))
            {
                int newFrequency = Convert.ToInt32(ioData["frequency"]);
                RFManager.AddListener(newFrequency, rfReceiver);
                rfReceiver.frequency = newFrequency;
                rfReceiver.MarkDirty();
            }

            var seismicSensor = ioEntity as SeismicSensor;
            if (seismicSensor != null && ioData.ContainsKey("range"))
            {
                seismicSensor.SetRange(Convert.ToInt32(ioData["range"]));
            }

            var doorManipulator = ioEntity as CustomDoorManipulator;
            if (doorManipulator != null)
            {
                Door door = doorManipulator.GetParentEntity() as Door;
                if (door != null)
                {
                    doorManipulator.SetTargetDoor(door);
                }
                else
                {
                    pasteData.FinalProcessingActions.Add(() => FindAndAssignTargetDoor(doorManipulator));
                }
            }

            var conveyor = ioEntity as IndustrialConveyor;
            if (conveyor != null && ioData.ContainsKey("industrialconveyormode"))
            {
                object mode;
                if (ioData.TryGetValue("industrialconveyormode", out mode))
                    conveyor.mode = (IndustrialConveyor.ConveyorMode)Convert.ToInt32(mode);

                conveyor.filterItems = DeSerializeConveyorFilter(ioData["industrialconveyorfilteritems"].ToString());
                conveyor.SendNetworkUpdate();
            }

            var audioVisual = ioEntity as AudioVisualisationEntity;
            if (audioVisual != null)
            {
                if (ioData.TryGetValue("colour", out object audioObj))
                    audioVisual.currentColour = (AudioVisualisationEntity.LightColour) Convert.ToInt32(audioObj);
                if (ioData.TryGetValue("volumeSensitivity", out audioObj))
                    audioVisual.currentVolumeSensitivity = (AudioVisualisationEntity.VolumeSensitivity) Convert.ToInt32(audioObj);
                if (ioData.TryGetValue("speed", out audioObj))
                    audioVisual.currentSpeed = (AudioVisualisationEntity.Speed) Convert.ToInt32(audioObj);
                if (ioData.TryGetValue("gradient", out audioObj))
                    audioVisual.currentGradient = Convert.ToInt32(audioObj);
                if (ioData.TryGetValue("connectedTo", out audioObj))
                {
                    var oldId = Convert.ToUInt64(audioObj);
                    if (oldId != 0 && pasteData.EntityLookup.TryGetValue(oldId, out var newConnectedTo) && newConnectedTo.TryGetValue("newId", out audioObj))
                        audioVisual.connectedTo.uid = new NetworkableId(Convert.ToUInt64(audioObj));
                }
            }

            var digitalClock = ioEntity as DigitalClock;
            if (digitalClock != null)
            {
                if (ioData.ContainsKey("muted"))
                {
                    digitalClock.muted = Convert.ToBoolean(ioData["muted"]);
                }

                if (ioData.ContainsKey("alarms") && ioData["alarms"] is List<object> alarms)
                {
                    foreach (Dictionary<string, object> alarm in alarms)
                    {
                        if (alarm != null && alarm.ContainsKey("time") && alarm.ContainsKey("active"))
                        {
                            digitalClock.alarms.Add(new DigitalClock.Alarm(TimeSpan.Parse(alarm["time"].ToString()),
                                Convert.ToBoolean(alarm["active"])));
                        }
                    }
                }

                digitalClock.MarkDirty();
                digitalClock.SendNetworkUpdate();
            }

            if (inputs != null && inputs.Count > 0)
            {
                for (var index = 0; index < inputs.Count; index++)
                {
                    var input = inputs[index] as Dictionary<string, object>;
                    object oldIdObject;

                    if (!input.TryGetValue("connectedID", out oldIdObject))
                        continue;

#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1712");
#endif
                    var oldId = Convert.ToUInt64(oldIdObject);

                    if (oldId != 0 && pasteData.EntityLookup.ContainsKey(oldId))
                    {
                        if (index >= ioEntity.inputs.Length)
                            continue;

                        if (ioEntity.inputs[index] == null)
                            ioEntity.inputs[index] = new IOEntity.IOSlot();

                        var ioConnection = pasteData.EntityLookup[oldId];
                        if (ioConnection.ContainsKey("newId"))
                        {
#if DEBUG
            Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1719");
#endif
                            ioEntity.inputs[index].connectedTo.entityRef.uid =
                                new NetworkableId(Convert.ToUInt64(ioConnection["newId"]));
                        }
                    }
                }
            }

            List<object> outputs = null;
            if (ioData.ContainsKey("outputs"))
                outputs = ioData["outputs"] as List<object>;

            if (outputs != null && outputs.Count > 0)
            {
                for (var index = 0; index < outputs.Count; index++)
                {
                    var output = outputs[index] as Dictionary<string, object>;
#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 1744");
#endif
                    var oldId = Convert.ToUInt64(output["connectedID"]);

                    if (oldId != 0 && pasteData.EntityLookup.ContainsKey(oldId))
                    {
                        if (ioEntity.outputs[index] == null)
                            ioEntity.outputs[index] = new IOEntity.IOSlot();

                        var ioConnection = pasteData.EntityLookup[oldId];

                        if( ioConnection.ContainsKey( "newId" ) )
                        {
                            var ioOutput = ioEntity.outputs[index];
                            var ioEntity2 = ioConnection["entity"] as IOEntity;

                            if (!ioEntity2.IsValid() || ioEntity2.IsDestroyed)
                                continue;

                            var connectedToSlot = Convert.ToInt32( output["connectedToSlot"] );

                            if (connectedToSlot >= ioEntity2.inputs.Length)
                                continue;

                            var ioInput = ioEntity2.inputs[connectedToSlot];

                            ioOutput.connectedTo = new IOEntity.IORef();
                            ioOutput.connectedTo.Set( ioEntity2 );
                            ioOutput.connectedToSlot = connectedToSlot;
                            ioOutput.type = (IOEntity.IOType) Convert.ToInt32( output["type"] );
                            ioOutput.niceName = output["niceName"] as string;
                            ioOutput.connectedTo.Init();

                            ioInput.connectedTo = new IOEntity.IORef();
                            ioInput.connectedTo.Set( ioEntity );
                            ioInput.connectedToSlot = index;
                            ioInput.connectedTo.Init();

                            ioOutput.worldSpaceLineEndRotation =
                                ioEntity2.transform.TransformDirection( ioInput.handleDirection );
                            ioOutput.originPosition = ioEntity.transform.position;
                            ioOutput.originRotation = ioEntity.transform.rotation.eulerAngles;

                            if( output.TryGetValue( "wireColour", out var wireColour ) )
                            {
                                var color = (WireTool.WireColour) Convert.ToInt32( wireColour );
                                ioInput.wireColour = color;
                                ioOutput.wireColour = color;
                            }

                            if (output.ContainsKey( "linePoints" ) && output["linePoints"] is List<object> linePoints)
                            {
                                ioOutput.linePoints = new Vector3[linePoints.Count];
                                for( var i = 0; i < linePoints.Count; i++ )
                                {
                                    var linePoint = linePoints[i] as Dictionary<string, object>;
                                    ioOutput.linePoints[i] = new Vector3(
                                        Convert.ToSingle( linePoint["x"] ),
                                        Convert.ToSingle( linePoint["y"] ),
                                        Convert.ToSingle( linePoint["z"] ) );
                                }
                            }
                            
                            if (output.ContainsKey("slackLevels") && output["slackLevels"] is List<object> slackLevels)
                            {
                                ioOutput.slackLevels = new float[slackLevels.Count];
                                for (var i = 0; i < slackLevels.Count; i++)
                                {
                                    ioOutput.slackLevels[i] = Convert.ToSingle(slackLevels[i]);
                                }
                            }
                            else
                            {
                                ioOutput.slackLevels = new float[ioOutput.linePoints.Count()];
                                for (var i = 0; i < ioOutput.slackLevels.Count(); i++)
                                    ioOutput.slackLevels[i] = 0f;
                            }

                            if (output.ContainsKey("lineAnchors") && output["lineAnchors"] is List<object> lineAnchors)
                            {
                                ioOutput.lineAnchors = new IOEntity.LineAnchor[lineAnchors.Count];
                                for (var i = 0; i < lineAnchors.Count; i++)
                                {
                                    var lineAnchor = lineAnchors[i] as Dictionary<string, object>;
                                    var pos = (Dictionary<string, object>)lineAnchor["position"];

                                    if (pasteData.EntityLookup.TryGetValue(Convert.ToUInt64(lineAnchor["entityRefID"]), out var data))
                                    {
                                        var door = data["entity"] as Door;
                                        if (door.IsValid())
                                        {
                                            ioOutput.lineAnchors[i] = new IOEntity.LineAnchor
                                            {
                                                entityRef = new EntityRef<Door>(door.net.ID),
                                                position = new Vector3(Convert.ToSingle(pos["x"]), Convert.ToSingle(pos["y"]),
                                                    Convert.ToSingle(pos["z"])),
                                                index = Convert.ToInt32(lineAnchor["index"]),
                                                boneName = lineAnchor["boneName"] as string
                                            };                                            
                                        }
                                    }
                                }
                            }

                            ioEntity2.SendNetworkUpdate();
                        }
                    }
                }

                if (pasteData.checkPosition != null)
                    pasteData.checkPosition.Add(ioEntity);
            }

            ioEntity.MarkDirty();
            ioEntity.UpdateOutputs();
            ioEntity.SendNetworkUpdate();
            ioEntity.RefreshIndustrialPreventBuilding();
        }

        private void SetItemSubEntity(PasteData pasteData, Item item, ulong oldId)
        {
            if (item != null && oldId != 0 && pasteData.EntityLookup.TryGetValue(oldId, out var data))
            {
                if (data["entity"] is BaseEntity subEntity && subEntity.IsValid() && !subEntity.IsDestroyed && subEntity.net.ID.IsValid)
                {
                    InitializeItemInstanceData(item);
                    item.instanceData.subEntity = subEntity.net.ID;
                }
            }
        }

        private void InitializeItemInstanceData(Item item)
        {
            if (item.instanceData == null)
            {
                item.instanceData = new ProtoBuf.Item.InstanceData()
                {
                    ShouldPool = false
                };
            }
        }

        private void ExtractInventory(Dictionary<string, object> data, ItemContainer inventory, CopyData copyData)
        {
            var itemlist = new List<object>();

            foreach (var item in inventory.itemList)
            {
                var itemdata = new Dictionary<string, object>
                {
                    { "condition", item.condition.ToString() },
                    { "maxCondition", item.maxCondition.ToString() },
                    { "id", item.info.itemid },
                    { "amount", item.amount },
                    { "skinid", item.skin },
                    { "fuel", item.fuel },
                    { "position", item.position },
                    { "blueprintTarget", item.blueprintTarget },
                    { "blueprintAmount", item.blueprintAmount },
                    { "dataInt", item.instanceData?.dataInt ?? 0 },
                    { "dataFloat", item.instanceData?.dataFloat ?? 0f }
                };

                if (item.instanceData != null)
                {
                    if (item.instanceData.subEntity != default(NetworkableId))
                    {
                        itemdata.Add("subEntity", item.instanceData.subEntity.Value);
                    }

                    // RF timed explosives
                    if (item.instanceData.dataInt > 0 && item.info != null && item.info.Blueprint != null &&
                        item.info.Blueprint.workbenchLevelRequired == 3)
                    {
                        itemdata.Add("IsOn", item.IsOn());
                    }
                }

                if (item.HasItemOwnership())
                    itemdata.Add("ownershipShares", item.ownershipShares.ToArray());

                if (!string.IsNullOrEmpty(item.name))
                    itemdata["name"] = item.name;

                if (!string.IsNullOrEmpty(item.text))
                    itemdata["text"] = item.text;

                var heldEnt = item.GetHeldEntity();

                if (heldEnt != null)
                {
                    var projectiles = heldEnt.GetComponent<BaseProjectile>();

                    if (projectiles != null)
                    {
                        var magazine = projectiles.primaryMagazine;

                        if (magazine != null)
                        {
                            itemdata.Add("magazine", new Dictionary<string, object>
                        {
                            { magazine.ammoType.itemid.ToString(), magazine.contents }
                        });
                        }
                    }

                    if (heldEnt.children != null && heldEnt.children.Count > 0)
                    {
                        var children = new List<object>();
                        foreach (var child in heldEnt.children)
                        {
                            if (!child.IsValid())
                                continue;

                            children.Add(EntityData(child, child.transform.position,
                                child.transform.rotation.eulerAngles, copyData));
                        }

                        if (children.Count > 0)
                            itemdata["children"] = children;
                    }

                    if (heldEnt is HeldBoomBox heldBoomBox && heldBoomBox.BoxController != null)
                        ExtractBoomBox(itemdata, heldBoomBox.BoxController);
                }

                if (item.contents != null)
                {
                    if (item.contents.capacity > 0 && item.info != null && item.info.HasComponent<ItemModContainerArmorSlot>())
                        itemdata["armorSlotCapacity"] = item.contents.capacity;

                    if (item.contents.itemList != null && item.contents.itemList.Count > 0)
                    {
                        var itemContents = new Dictionary<string, object>();
                        ExtractInventory(itemContents, item.contents, copyData);
                        if (itemContents.ContainsKey("items"))
                            itemdata["items"] = itemContents["items"];
                    }
                }

                itemlist.Add(itemdata);
            }

            data.Add("items", itemlist);
        }

        private void PopulateInventory(PasteData pasteData, Dictionary<string,object> data, BaseEntity entity, ItemContainer inventory)
        {
            var items = new List<object>();

            if (data.ContainsKey("items"))
                items = data["items"] as List<object>;

            object getObj;

            foreach (var itemDef in items)
            {
                var item = itemDef as Dictionary<string, object>;
                var itemid = Convert.ToInt32(item["id"]);
                var itemskin = item.ContainsKey("skinid") ? FilterSkinId(pasteData, ulong.Parse(item["skinid"].ToString())) : 0;

                var def = ItemManager.FindItemDefinition(itemid);
                if (!pasteData.Dlc && itemid != 0 && _dlcItemIds.Contains(itemid))
                {
                    if (def?.isRedirectOf == null || _dlcItemIds.Contains(def.isRedirectOf.itemid))
                        continue;

                    itemid = def.isRedirectOf.itemid;
                    itemskin = 0;
                }
                else if (pasteData.SkinsMode != SkinsMode.AllSkins && def?.isRedirectOf != null)
                {
                    if (ShouldRedirectForSkinsMode(pasteData, def))
                    {
                        itemid = def.isRedirectOf.itemid;
                        itemskin = 0;
                    }
                }

                var itemamount = Convert.ToInt32(item["amount"]);
                var dataInt = item.ContainsKey("dataInt") ? Convert.ToInt32(item["dataInt"]) : 0;
                var dataFloat = item.TryGetValue("dataFloat", out getObj) ? Convert.ToSingle(getObj) : 0f;

                if (itemid == 0 || itemamount == 0)
                    continue;

                var growableEntity = entity as GrowableEntity;
                if (growableEntity != null)
                {
                    if (data.ContainsKey("genes"))
                    {
                        var genesData = (int)data["genes"];

                        if (genesData > 0)
                        {
                            GrowableGeneEncoding.DecodeIntToGenes(genesData, growableEntity.Genes);
                        }
                    }

                    if (data.ContainsKey("hasParent"))
                    {
                        var isParented = (bool)data["hasParent"];

                        if (isParented)
                        {
                            RaycastHit hitInfo;

                            if (Physics.Raycast(growableEntity.transform.position, Vector3.down, out hitInfo,
                                    .5f, Rust.Layers.DefaultDeployVolumeCheck))
                            {
                                var parentEntity = hitInfo.GetEntity();
                                if (parentEntity != null)
                                {
                                    growableEntity.SetParent(parentEntity, true);
                                }
                            }
                        }
                    }
                }

                if (pasteData.IsItemReplace)
                    itemid = GetItemId(itemid);

                var targetPos = -1;
                if (item.ContainsKey("position"))
                    targetPos = Convert.ToInt32(item["position"]);

                if (entity is BaseOven ov && ov.visualFood && targetPos >= ov._inputSlotIndex &&
                    targetPos < ov._inputSlotIndex + ov.inputSlots &&
                    ItemManager.FindItemDefinition(itemid)?.ItemModCookable == null)
                    continue;

                var i = ItemManager.CreateByItemID(itemid, itemamount, itemskin);

                if (i != null)
                {
                    if (i.hasCondition)
                    {
                        if (item.TryGetValue("maxCondition", out getObj))
                        {
                            float maxCondition = Convert.ToSingle(getObj);
                            if (maxCondition > 0f)
                                i.maxCondition = maxCondition;
                        }

                        if (item.TryGetValue("condition", out getObj))
                            i.condition = Convert.ToSingle(getObj);
                    }

                    if (item.TryGetValue("text", out object obj) && obj is string str1 && !string.IsNullOrEmpty(str1))
                        i.text = str1;

                    if (item.TryGetValue("name", out obj) && obj is string str2 && !string.IsNullOrEmpty(str2))
                        i.name = str2;

                    if (item.TryGetValue("fuel", out getObj))
                    {
                        float fuel = Convert.ToSingle(getObj);
                        if (fuel > 0)
                            i.fuel = fuel;
                    }

                    if (item.ContainsKey("blueprintTarget"))
                    {
                        var blueprintTarget = Convert.ToInt32(item["blueprintTarget"]);

                        if (pasteData.IsItemReplace)
                            blueprintTarget = GetItemId(blueprintTarget);

                        if (blueprintTarget != 0)
                            i.blueprintTarget = blueprintTarget;
                    }

                    if (item.TryGetValue("blueprintAmount", out getObj))
                    {
                        var blueprintAmount = Convert.ToInt32(getObj);
                        if (blueprintAmount != 0)
                            i.blueprintAmount = blueprintAmount;
                    }

                    if (dataInt != 0)
                    {
                        InitializeItemInstanceData(i);
                        i.instanceData.dataInt = dataInt;
                    }

                    if (dataFloat != 0f)
                    {
                        InitializeItemInstanceData(i);
                        i.instanceData.dataFloat = dataFloat;
                    }

                    if (item.ContainsKey("IsOn"))
                    {
                        i.SetFlag(Item.Flag.IsOn, Convert.ToBoolean(item["IsOn"]));
                    }

                    if (item.ContainsKey("subEntity"))
                    {
                        // Needs to be processed after all of the children are spawned
                        var oldId = Convert.ToUInt64(item["subEntity"]);
                        if (oldId != 0)
                            pasteData.ItemsWithSubEntity.Add(oldId, i);
                    }

                    if (item.TryGetValue("armorSlotCapacity", out var armorSlotCapacityObj))
                    {
                        var armorSlotCapacity = Convert.ToInt32(armorSlotCapacityObj);
                        if (armorSlotCapacity > 0 && i.info != null && i.info.TryGetComponent<ItemModContainerArmorSlot>(out var armorSlot))
                            armorSlot.CreateAtCapacity(armorSlotCapacity, i);
                    }
                    else if (i.info != null && i.info.isWearable &&
                             item.TryGetValue("items", out var rawSlotItems) &&
                             rawSlotItems is List<object> { Count: > 0 } slotItems &&
                             i.info.TryGetComponent<ItemModContainerArmorSlot>(out var armorSlot))
                    {
                        armorSlot.CreateAtCapacity(slotItems.Count, i);
                    }

                    if (item.TryGetValue("ownershipShares", out var ownershipSharesObj))
                    {
                        var ownershipShares = ownershipSharesObj as List<object>;
                        if (ownershipShares != null && ownershipShares.Count > 0)
                        {
                            i.InitializeItemOwnership();
                            if (i.ownershipShares != null)
                            {
                                i.ownershipShares.Clear();
                                for (var num = 0; num < ownershipShares.Count; num++)
                                {
                                    var ownershipShare = ownershipShares[num] as Dictionary<string, object>;
                                    if (ownershipShare == null)
                                        continue;

                                    var itemOwnershipShare = new ItemOwnershipShare();

                                    if (ownershipShare.TryGetValue("username", out var username))
                                        itemOwnershipShare.username = (string) username;
                                    if (ownershipShare.TryGetValue("reason", out var reason))
                                        itemOwnershipShare.reason = (string) reason;
                                    if (ownershipShare.TryGetValue("amount", out var amount))
                                        itemOwnershipShare.amount = Convert.ToInt32(amount);

                                    if (itemOwnershipShare.IsValid())
                                        i.ownershipShares.Add(itemOwnershipShare);
                                }
                            }
                        }
                    }

                    if (item.ContainsKey("items"))
                    {
                        PopulateInventory(pasteData, item, null, i.contents);
                    }

                    var heldent = i.GetHeldEntity();

                    if (heldent != null)
                    {
                        if (item.ContainsKey("magazine"))
                        {
                            var projectiles = heldent.GetComponent<BaseProjectile>();

                            if (projectiles != null)
                            {
                                var magazine = item["magazine"] as Dictionary<string, object>;
                                var ammotype = int.Parse(magazine.Keys.ToArray()[0]);
                                var ammoamount = int.Parse(magazine[ammotype.ToString()].ToString());

                                if (pasteData.IsItemReplace)
                                    ammotype = GetItemId(ammotype);

                                projectiles.primaryMagazine.ammoType = ItemManager.FindItemDefinition(ammotype);
                                projectiles.primaryMagazine.contents = ammoamount;
                            }
                        }

                        if (item.ContainsKey("children"))
                        {
                            PreLoadChildrenData(item);

                            var children = item["children"] as List<object>;
                            if (children != null)
                            {
                                foreach (var child in children)
                                {
                                    var childData = child as Dictionary<string, object>;
                                    if (childData == null)
                                        continue;

                                    PasteEntity(childData, pasteData, heldent);
                                }
                            }
                        }

                        if (item.TryGetValue("boomBox", out var boomBoxObj) &&
                            boomBoxObj is Dictionary<string, object> boomBoxData && boomBoxData != null &&
                            heldent is HeldBoomBox heldBoomBox)
                        {
                            PopulateBoomBox(boomBoxData, heldBoomBox.BoxController);
                        }
                    }

                    var heldEntity = i.GetHeldEntity();
                    if (heldEntity != null && heldEntity is Detonator detonator)
                    {
                        detonator.frequency = dataInt;
                        if ( detonator.IsOn() )
                            RFManager.AddBroadcaster(detonator.frequency, detonator);
                    }

                    i.position = targetPos;

                    if (entity is WaterCatcher waterCatcher)
                    {
                        waterCatcher.Invoke(() => {
                            if (waterCatcher != null && !waterCatcher.IsDestroyed)
                                waterCatcher.inventory.AddItem(i.info, i.amount);
                        }, 1f);
                    }
                    else
                    {
                        inventory.Insert(i);
                    }
                }
            }
        }

        void ExtractTextures(Dictionary<string, object> data, uint[] textureIDs, BaseEntity entity, FileStorage.Type type)
        {
            if (textureIDs != null && textureIDs.Length > 0)
            {
                var signData = new Dictionary<string, object>();

                if (entity is Signage sign && sign != null)
                    signData.Add("locked", sign.IsLocked());

                for (var num = 0; num < textureIDs.Length; num++)
                {
                    var textureId = textureIDs[num];
                    if (textureId == 0)
                        continue;

                    var imageByte = FileStorage.server.Get(textureId, type, entity.net.ID);
                    if (imageByte != null)
                    {
                        signData.Add($"texture{num}", Convert.ToBase64String(imageByte));
                    }
                }

                signData["amount"] = textureIDs.Length;
                data.Add("sign", signData);
            }
        }

        void ExtractBoomBox(Dictionary<string, object> data, BoomBox boomBox)
        {
            if (!string.IsNullOrEmpty(boomBox.CurrentRadioIp))
            {
                data.Add("boomBox", new Dictionary<string, object>()
                {
                    { "radioIp", boomBox.CurrentRadioIp },
                    { "radioBy", boomBox.AssignedRadioBy }
                });
            }
        }

        void PopulateBoomBox(Dictionary<string, object> data, BoomBox boomBox)
        {
            if (boomBox != null)
            {
                if (data.TryGetValue("radioIp", out var boomBoxObj))
                {
                    var radioIp = boomBoxObj as string;
                    if (!string.IsNullOrEmpty(radioIp) && BoomBox.IsStationValid(radioIp))
                        boomBox.CurrentRadioIp = radioIp;
                }
                if (data.TryGetValue("radioBy", out boomBoxObj))
                    boomBox.AssignedRadioBy = Convert.ToUInt64(boomBoxObj);
            }
        }

        void ExtractCassette(Dictionary<string, object> data, Cassette cassette)
        {
            if (cassette.IsValid())
            {
                uint[] contentCRCs = cassette.GetContentCRCs;
                if (contentCRCs != null && contentCRCs.Length == 1)
                {
                    var oggByte = FileStorage.server.Get(contentCRCs[0], FileStorage.Type.ogg, cassette.net.ID);
                    if (oggByte != null)
                    {
                        data.Add("audio", Convert.ToBase64String(oggByte));
                    }
                }
            }
        }

        void PopulateCassette(Dictionary<string, object> data, Cassette cassette)
        {
            if (data.ContainsKey("audio"))
            {
                var contentCRC = FileStorage.server.Store(Convert.FromBase64String(data["audio"].ToString()), FileStorage.Type.ogg, cassette.net.ID);
                cassette.SetAudioId(contentCRC, 0);
            }
        }

        void ExtractHeadData(Dictionary<string, object> data, HeadData headData)
        {
            if (headData != null)
            {
                data.Add("currentTrophyData", new Dictionary<string, object>
                {
                    { "entitySource", headData.entitySource },
                    { "playerName", headData.playerName },
                    { "playerId", headData.playerId },
                    { "clothing", headData.clothing },
                    { "count", headData.count },
                    { "horseBreed", headData.horseBreed }
                });
            }
        }

        void PopulateHeadData(Dictionary<string,object> data, HeadData headData)
        {
            if (data.ContainsKey("currentTrophyData"))
            {
                var headDataData = (Dictionary<string, object>)data["currentTrophyData"];

                var clothing = Pool.Get<List<int>>();
                if (headDataData["clothing"] is List<object> clothingData)
                {
                    foreach (var clothingItem in clothingData)
                    {
                        clothing.Add(Convert.ToInt32(clothingItem));
                    }
                }
                if (clothing.Count == 0)
                    Pool.FreeUnmanaged(ref clothing);

                headData.entitySource = Convert.ToUInt32(headDataData["entitySource"]);
                headData.playerName = headDataData["playerName"] as string;
                headData.playerId = Convert.ToUInt64(headDataData["playerId"]);
                headData.clothing = clothing;
                headData.count = Convert.ToUInt32(headDataData["count"]);
                headData.horseBreed = Convert.ToInt32(headDataData["horseBreed"]);
            }
        }

        private HashSet<Dictionary<string, object>> PreLoadData(List<object> entities, Vector3 startPos,
            float rotationCorrection, bool deployables, bool inventories, bool auth, bool vending)
        {
            var eulerRotation = new Vector3(0f, rotationCorrection, 0f);
            var quaternionRotation = Quaternion.Euler(eulerRotation * Mathf.Rad2Deg);
            var preloaddata = new HashSet<Dictionary<string, object>>();

            foreach (Dictionary<string, object> entity in entities)
            {
                if (!deployables && !entity.ContainsKey("grade"))
                    continue;

                var pos = (Dictionary<string, object>)entity["pos"];
                var rot = (Dictionary<string, object>)entity["rot"];

                entity.Add("position",
                    quaternionRotation * new Vector3(Convert.ToSingle(pos["x"]), Convert.ToSingle(pos["y"]),
                        Convert.ToSingle(pos["z"])) + startPos);
                entity.Add("rotation",
                    Quaternion.Euler((eulerRotation + new Vector3(Convert.ToSingle(rot["x"]),
                        Convert.ToSingle(rot["y"]), Convert.ToSingle(rot["z"]))) * Mathf.Rad2Deg));

                if (!inventories && entity.ContainsKey("items"))
                    entity["items"] = new List<object>();

                if (!vending && entity["prefabname"].ToString().Contains("vendingmachine"))
                    entity.Remove("vendingmachine");

                PreLoadChildrenData(entity);

                preloaddata.Add(entity);
            }

            return preloaddata;
        }

        private void PreLoadChildrenData(Dictionary<string, object> entity)
        {
            if (entity.ContainsKey("children"))
            {
                var children = entity["children"] as List<object>;

                if (children == null)
                    return;

                // Set the (local) position and rotation of the children
                foreach (var child in children)
                {
                    var childData = child as Dictionary<string, object>;
                    if (childData == null)
                        continue;

                    var childPos = (Dictionary<string, object>)childData["pos"];
                    var childRot = (Dictionary<string, object>)childData["rot"];

                    childData.Add("position",
                        new Vector3(Convert.ToSingle(childPos["x"]), Convert.ToSingle(childPos["y"]),
                            Convert.ToSingle(childPos["z"])));
                    childData.Add("rotation",
                        Quaternion.Euler(new Vector3(Convert.ToSingle(childRot["x"]),
                            Convert.ToSingle(childRot["y"]), Convert.ToSingle(childRot["z"]))));

                    // Recursively process the child's children
                    PreLoadChildrenData(childData);
                }
            }
        }

        private object TryCopy(Vector3 sourcePos, Vector3 sourceRot, string filename, float rotationCorrection,
            string[] args, IPlayer player, Action callback)
        {
            bool saveShare = _config.Copy.Share, saveTree = _config.Copy.Tree, eachToEach = _config.Copy.EachToEach;
            var copyMechanics = CopyMechanics.Proximity;
            var radius = _config.Copy.Radius;

            for (var i = 0;; i += 2)
            {
                if (i >= args.Length)
                    break;

                var valueIndex = i + 1;

                if (valueIndex >= args.Length)
                    return Lang("SYNTAX_COPY");

                var param = args[i].ToLower();

                switch (param)
                {
                    case "e":
                    case "each":
                        if (!bool.TryParse(args[valueIndex], out eachToEach))
                            return Lang("SYNTAX_BOOL", null, param);

                        break;

                    case "m":
                    case "method":
                        switch (args[valueIndex].ToLower())
                        {
                            case "b":
                            case "building":
                                copyMechanics = CopyMechanics.Building;
                                break;

                            case "p":
                            case "proximity":
                                copyMechanics = CopyMechanics.Proximity;
                                break;
                        }

                        break;

                    case "r":
                    case "radius":
                        if (!float.TryParse(args[valueIndex], out radius))
                            return Lang("SYNTAX_RADIUS");

                        break;

                    case "s":
                    case "share":
                        if (!bool.TryParse(args[valueIndex], out saveShare))
                            return Lang("SYNTAX_BOOL", null, param);

                        break;

                    case "t":
                    case "tree":
                        if (!bool.TryParse(args[valueIndex], out saveTree))
                            return Lang("SYNTAX_BOOL", null, param);

                        break;

                    default:
                        return Lang("SYNTAX_COPY");
                }
            }

            Copy(sourcePos, sourceRot, filename, rotationCorrection, copyMechanics, radius, saveTree, saveShare,
                eachToEach, player, callback);

            return true;
        }

        private bool GetSlot(BaseEntity parent, BaseEntity child, out BaseEntity.Slot? slot)
        {
            slot = null;
            
            for (int s = 0; s < (int)BaseEntity.Slot.Count; s++)
            {
                var slotEnum = (BaseEntity.Slot)s;
                
                if (parent.HasSlot( slotEnum ) && parent.GetSlot( slotEnum ) == child)
                {
                    slot = slotEnum;
                    return true;
                }
            }
            
            return false;
        }
        
        // private void TryCopySlots(BaseEntity ent, IDictionary<string, object> housedata, bool saveShare)
        // {
        //     foreach (var slot in _checkSlots)
        //     {
        //         if (!ent.HasSlot(slot))
        //             continue;
        //
        //         var slotEntity = ent.GetSlot(slot);
        //
        //         if (slotEntity == null)
        //             continue;
        //
        //         var codedata = new Dictionary<string, object>
        //         {
        //             { "prefabname", slotEntity.PrefabName },
        //             { "flags", TryCopyFlags(ent) }
        //         };
        //
        //         if (slotEntity.GetComponent<CodeLock>())
        //         {
        //             var codeLock = slotEntity.GetComponent<CodeLock>();
        //
        //             codedata.Add("code", codeLock.code);
        //
        //             if (saveShare)
        //                 codedata.Add("whitelistPlayers", codeLock.whitelistPlayers);
        //
        //             if (codeLock.guestCode != null && codeLock.guestCode.Length == 4)
        //             {
        //                 codedata.Add("guestCode", codeLock.guestCode);
        //
        //                 if (saveShare)
        //                     codedata.Add("guestPlayers", codeLock.guestPlayers);
        //             }
        //         }
        //         else if (slotEntity.GetComponent<KeyLock>())
        //         {
        //             var keyLock = slotEntity.GetComponent<KeyLock>();
        //             var code = keyLock.keyCode;
        //
        //             if (keyLock.firstKeyCreated)
        //                 code |= 0x80;
        //
        //             codedata.Add("ownerId", keyLock.OwnerID.ToString());
        //             codedata.Add("code", code.ToString());
        //         }
        //
        //         var slotName = slot.ToString().ToLower();
        //
        //         housedata.Add(slotName, codedata);
        //     }
        // }

        private Dictionary<string, object> TryCopyFlags(BaseEntity entity)
        {
            var flags = new Dictionary<string, object>();

            foreach (BaseEntity.Flags flag in Enum.GetValues(typeof(BaseEntity.Flags)))
            {
                if (!_config.DataSaving || entity.HasFlag(flag))
                    flags.Add(flag.ToString(), entity.HasFlag(flag));
            }

            return flags;
        }

        private ValueTuple<object, PasteData> TryPaste(Vector3 startPos, string filename, IPlayer player,
            float rotationCorrection,
            string[] args, bool autoHeight = true, Action callback = null,
            Action<BaseEntity> callbackSpawned = null)
        {
            var userId = player?.Id;

            var path = _subDirectory + filename;

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path))
                return new ValueTuple<object, PasteData>(Lang("FILE_NOT_EXISTS", userId), null);

            var data = Interface.Oxide.DataFileSystem.GetDatafile(path);

            if (data["default"] == null || data["entities"] == null)
                return new ValueTuple<object, PasteData>(Lang("FILE_BROKEN", userId), null);

            float heightAdj = 0f, blockCollision = 0f;
            int skinsMode = _config.Paste.SkinsMode;
            bool auth = _config.Paste.Auth,
                inventories = _config.Paste.Inventories,
                deployables = _config.Paste.Deployables,
                vending = _config.Paste.VendingMachines,
                stability = _config.Paste.Stability,
                ownership = _config.Paste.EntityOwner,
                dlc = _config.Paste.Dlc,
                checkPlaced = true, enableSaving = true;

            for (var i = 0;; i += 2)
            {
                if (i >= args.Length)
                    break;

                var valueIndex = i + 1;

                if (valueIndex >= args.Length)
                    return new ValueTuple<object, PasteData>(Lang("SYNTAX_PASTE_OR_PASTEBACK", userId), null);

                var param = args[i].ToLower();

                switch (param)
                {
                    case "a":
                    case "auth":
                        if (!bool.TryParse(args[valueIndex], out auth))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "b":
                    case "blockcollision":
                        if (!float.TryParse(args[valueIndex], out blockCollision))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BLOCKCOLLISION", userId), null);

                        break;

                    case "d":
                    case "deployables":
                        if (!bool.TryParse(args[valueIndex], out deployables))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "h":
                    case "height":
                        if (!float.TryParse(args[valueIndex], out heightAdj))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_HEIGHT", userId), null);

                        break;

                    case "i":
                    case "inventories":
                        if (!bool.TryParse(args[valueIndex], out inventories))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "s":
                    case "stability":
                        if (!bool.TryParse(args[valueIndex], out stability))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "v":
                    case "vending":
                        if (!bool.TryParse(args[valueIndex], out vending))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "o":
                    case "entityowner":
                        if (!bool.TryParse(args[valueIndex], out ownership))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "cp":
                    case "checkplaced":
                        if (!bool.TryParse(args[valueIndex], out checkPlaced))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "autoheight":
                        if (!bool.TryParse(args[valueIndex], out autoHeight))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "position":
                        startPos = args[valueIndex].ToVector3();
                        break;

                    case "rotation":
                        if (!float.TryParse(args[valueIndex], out rotationCorrection))
                            return new ValueTuple<object, PasteData>(Lang("SYNTAX_FLOAT", userId, param), null);

                        break;

                    case "enablesaving":
                        if (!bool.TryParse(args[valueIndex], out enableSaving))
                            return new(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "dlc":
                        if (!bool.TryParse(args[valueIndex], out dlc))
                            return new(Lang("SYNTAX_BOOL", userId, param), null);

                        break;

                    case "skins":
                        if (!Int32.TryParse(args[valueIndex], out skinsMode) || !IsValidSkinsMode(skinsMode))
                            return new(Lang("SYNTAX_SKINSMODE", userId, param), null);

                        break;

                    default:
                        return new ValueTuple<object, PasteData>(Lang("SYNTAX_PASTE_OR_PASTEBACK", userId), null);
                }
            }

            startPos.y += heightAdj;

            var preloadData = PreLoadData(data["entities"] as List<object>, startPos, rotationCorrection, deployables, inventories, auth, vending);

            if (autoHeight)
            {
                var bestHeight = FindBestHeight(preloadData, startPos);

                if (bestHeight is string)
                    return new ValueTuple<object, PasteData>(bestHeight, null);

                heightAdj += (float)bestHeight - startPos.y;

                foreach (var entity in preloadData)
                {
                    var pos = (Vector3)entity["position"];
                    pos.y += heightAdj;

                    entity["position"] = pos;
                }
            }

            if (blockCollision > 0f)
            {
                var collision = CheckCollision(preloadData, startPos, blockCollision);

                if (collision is string)
                    return new ValueTuple<object, PasteData>(collision, null);
            }

            var protocol = new Dictionary<string, object>();

            if (data["protocol"] != null)
                protocol = data["protocol"] as Dictionary<string, object>;

            var pasteData = Paste(preloadData, protocol, ownership, startPos, player, stability, rotationCorrection,
                autoHeight ? heightAdj : 0, auth, callback, callbackSpawned, filename, checkPlaced, enableSaving,
                dlc, skinsMode);

            return new ValueTuple<object, PasteData>(true, pasteData);
        }

        private void TryPasteLocks(BaseEntity entity, Dictionary<string, object> data, PasteData pasteData)
        {
            if (entity.GetComponent<CodeLock>())
            {
                var code = (string)data["code"];

                if (!string.IsNullOrEmpty(code))
                {
                    var codeLock = entity.GetComponent<CodeLock>();
                    codeLock.code = code;
                    codeLock.hasCode = true;

                    if (pasteData.Auth && pasteData.BasePlayer != null)
                        codeLock.whitelistPlayers.Add(pasteData.BasePlayer.userID);

                    if (data.ContainsKey("whitelistPlayers"))
                    {
                        foreach (var userId in (List<object>)data["whitelistPlayers"])
                        {
#if DEBUG
                            Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 2206");
#endif
                            codeLock.whitelistPlayers.Add(Convert.ToUInt64(userId));
                        }
                    }

                    if (data.ContainsKey("guestCode"))
                    {
                        var guestCode = (string)data["guestCode"];

                        codeLock.guestCode = guestCode;
                        codeLock.hasGuestCode = true;

                        if (data.ContainsKey("guestPlayers"))
                        {
                            foreach (var userId in (List<object>)data["guestPlayers"])
                            {
#if DEBUG
                                Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 2224");
#endif
                                codeLock.guestPlayers.Add(Convert.ToUInt64(userId));
                            }
                        }
                    }

                    codeLock.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }
            else if (entity.GetComponent<KeyLock>())
            {
                var code = Convert.ToInt32(data["code"]);
                var keyLock = entity.GetComponent<KeyLock>();

                if (data.ContainsKey("firstKeyCreated"))
                {
                    keyLock.keyCode = code;
                    keyLock.firstKeyCreated = Convert.ToBoolean(data["firstKeyCreated"]);
                }
                else
                {
                    if ((code & 0x80) != 0)
                    {
                        keyLock.keyCode = code & 0x7F;
                        keyLock.firstKeyCreated = true;
                        keyLock.SetFlag(BaseEntity.Flags.Locked, true);
                    }
                }

                if (pasteData.Ownership && data.ContainsKey("ownerId"))
                {
#if DEBUG
                    Puts($"{nameof(PasteLoop)}: Convert.ToUInt64 2249");
#endif
                    keyLock.OwnerID = Convert.ToUInt64(data["ownerId"]);
                }
            }
        }
        
        private List<BaseEntity> TryPasteSlots(BaseEntity ent, Dictionary<string, object> structure,
            PasteData pasteData)
        {
            var entitySlots = new List<BaseEntity>();

            foreach (var slot in _checkSlots)
            {
                var slotName = slot.ToString().ToLower();

                if (!ent.HasSlot(slot) || !structure.ContainsKey(slotName))
                    continue;

                var slotData = structure[slotName] as Dictionary<string, object>;
                var slotEntity = GameManager.server.CreateEntity(GetPrefabName((string)slotData?["prefabname"]), Vector3.zero);
                if (slotEntity == null)
                    continue;

                slotEntity.gameObject.Identity();
                slotEntity.SetParent(ent, slotName);
                slotEntity.OnDeployed(ent, null, _emptyItem);

                if (!pasteData.EnableSaving)
                {
                    slotEntity.EnableSaving(false);
                }

                slotEntity.Spawn();

                ent.SetSlot(slot, slotEntity);

                entitySlots.Add(slotEntity);

                if (slotName == "lock" && slotData.ContainsKey("code"))
                    TryPasteLocks(slotEntity, slotData, pasteData);

                pasteData.CallbackSpawned?.Invoke(ent);
            }

            return entitySlots;
        }

        private List<IndustrialConveyor.ItemFilter> DeSerializeConveyorFilter(string itemstring)
        {
            List<IndustrialConveyor.ItemFilter> itemFilters = new List<IndustrialConveyor.ItemFilter>();
            try
            {
                foreach (string datapoint in Encoding.ASCII.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(itemstring))).Split('\\'))
                {
                    if (datapoint != null && !string.IsNullOrEmpty(datapoint))
                    {
                        string[] info = datapoint.Split('/');
                        if (info.Length == 6)
                        {
                            IndustrialConveyor.ItemFilter item = new IndustrialConveyor.ItemFilter();
                            if (info[0] != "-1")
                            {
                                item.TargetItem = ItemManager.FindItemDefinition(Convert.ToInt32(info[0]));
                            }
                            item.MaxAmountInOutput = Convert.ToInt32(info[1]);
                            item.BufferAmount = Convert.ToInt32(info[2]);
                            item.MinAmountInInput = Convert.ToInt32(info[3]);
                            if (info[4] != "-1")
                            {
                                item.TargetCategory = (ItemCategory)Convert.ToInt32(info[4]);
                            }
                            item.IsBlueprint = Convert.ToBoolean(info[5]);
                            itemFilters.Add(item);
                        }
                    }
                }
            }
            catch { Puts("DeSerializeConveyorFilter Failed!"); }
            return itemFilters;
        }

        private string SerializeConveyorFilter(List<IndustrialConveyor.ItemFilter> filterItems, string itemstring = "")
        {
            if (filterItems?.Count > 0)
            {
                foreach (var item in filterItems) { itemstring += (item.TargetItem ? item.TargetItem.itemid : "-1") + "/" + item.MaxAmountInOutput + "/" + item.BufferAmount + "/" + item.MinAmountInInput + "/" + (item.TargetCategory.HasValue ? (int) item.TargetCategory.Value : "-1") + "/" + item.IsBlueprint + "\\"; }
                return Convert.ToBase64String(Facepunch.Utility.Compression.Compress(Encoding.ASCII.GetBytes(itemstring)));
            }
            return itemstring;
        }

        private List<ProtoBuf.PatternFirework.Star> DeSerializeStarPattern(string stars)
        {
            List<ProtoBuf.PatternFirework.Star> starlist = new List<ProtoBuf.PatternFirework.Star>();
            try
            {
                foreach (string datapoint in Encoding.ASCII.GetString(Facepunch.Utility.Compression.Uncompress(Convert.FromBase64String(stars))).Split('\\'))
                {
                    if (datapoint != null && !string.IsNullOrEmpty(datapoint))
                    {
                        string[] info = datapoint.Split('/');
                        if (info.Length == 6)
                        {
                            ProtoBuf.PatternFirework.Star star = new ProtoBuf.PatternFirework.Star();
                            star.position = new Vector2(Convert.ToSingle(info[0]), Convert.ToSingle(info[1]));
                            star.color = new UnityEngine.Color(Convert.ToSingle(info[2]), Convert.ToSingle(info[3]), Convert.ToSingle(info[4]), Convert.ToSingle(info[5]));
                            starlist.Add(star);
                        }
                    }
                }
            }
            catch { Puts("DeSerializeStarPattern Failed!"); }
            return starlist;
        }

        private string SerializeStarPattern(List<ProtoBuf.PatternFirework.Star> stars, string starstring = "")
        {
            if (stars?.Count > 0)
            {
                foreach (var star in stars) { starstring += star.position.x + "/" + star.position.y + "/" + star.color.r + "/" + star.color.g + "/" + star.color.b + "/" + star.color.a + "\\"; }
                return Convert.ToBase64String(Facepunch.Utility.Compression.Compress(Encoding.ASCII.GetBytes(starstring)));
            }
            return starstring;
        }

        private object TryPasteBack(string filename, IPlayer player, string[] args)
        {
            var path = _subDirectory + filename;

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path))
                return Lang("FILE_NOT_EXISTS", player?.Id);

            var data = Interface.Oxide.DataFileSystem.GetDatafile(path);

            if (data["default"] == null || data["entities"] == null)
                return Lang("FILE_BROKEN", player?.Id);

            var defaultdata = data["default"] as Dictionary<string, object>;
            var pos = defaultdata?["position"] as Dictionary<string, object>;
            var rotationCorrection = Convert.ToSingle(defaultdata?["rotationdiff"]);
            var startPos = new Vector3(Convert.ToSingle(pos?["x"]), Convert.ToSingle(pos?["y"]),
                Convert.ToSingle(pos?["z"]));

            return TryPaste(startPos, filename, player, rotationCorrection, args, false).Item1;
        }

        private static bool HasGrade(BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            foreach (var constructionGrade in block.blockDefinition.grades)
            {
                var baseGrade = constructionGrade.gradeBase;
                if (baseGrade.type == grade && baseGrade.skin == skin)
                    return true;
            }

            return false;
        }
        
        [Command("copy")]
        private void CmdCopy(IPlayer player, string command, string[] args)
        {
            if (!HasAccess(player, _copyPermission))
            {
                player.Reply(Lang("NO_ACCESS", player.Id));
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(Lang("SYNTAX_COPY", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            var savename = args[0];
            var success =
                TryCopyFromSteamId(basePlayer == null ? 0ul : basePlayer.userID, savename,
                    args.Skip(1).ToArray()) as string;

            if (!string.IsNullOrEmpty(success))
                player.Reply(success);
        }

        [Command("paste")]
        private void CmdPaste(IPlayer player, string command, string[] args)
        {
            if (!HasAccess(player, _pastePermission))
            {
                player.Reply(Lang("NO_ACCESS", player.Id));
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(Lang("SYNTAX_PASTE_OR_PASTEBACK", player.Id));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            var success =
                TryPasteFromSteamId(basePlayer == null ? 0ul : basePlayer.userID, args[0],
                    args.Skip(1).ToArray()) as string;

            if (!string.IsNullOrEmpty(success))
                player.Reply(success);
        }

        [Command("copylist")]
        private void CmdList(IPlayer player, string command, string[] args)
        {
            if (!HasAccess(player, _listPermission))
            {
                player.Reply(Lang("NO_ACCESS", player.Id));
                return;
            }

            var files = Interface.Oxide.DataFileSystem.GetFiles(_subDirectory);

            var fileList = new List<string>();

            foreach (var file in files)
            {
                var strFileParts = file.Split('/');
                var justfile = strFileParts[strFileParts.Length - 1].Replace(".json", "");
                fileList.Add(justfile);
            }

            player.Reply(Lang("AVAILABLE_STRUCTURES", player.Id));
            player.Reply(string.Join(", ", fileList.ToArray()));
        }

        [Command("pasteback")]
        private void CmdPasteBack(IPlayer player, string command, string[] args)
        {
            if (!HasAccess(player, _pastebackPermission))
            {
                player.Reply(Lang("NO_ACCESS", player.Id));
                return;
            }

            if (args.Length < 1)
            {
                player.Reply(Lang("SYNTAX_PASTEBACK", player.Id));
                return;
            }

            var success = TryPasteBack(args[0], player, args.Skip(1).ToArray()) as string;
            if (!string.IsNullOrEmpty(success))
                player.Reply(success);
        }

        [Command("undo")]
        private void CmdUndo(IPlayer player, string command, string[] args)
        {
            if (!HasAccess(player, _undoPermission))
            {
                player.Reply(Lang("NO_ACCESS", player.Id));
                return;
            }

            if (!_lastPastes.ContainsKey(player.Id))
            {
                player.Reply(Lang("NO_PASTED_STRUCTURE", player.Id));
                return;
            }

            var entities = new HashSet<BaseEntity>(_lastPastes[player.Id].Pop().ToList());

            UndoLoop(entities, player);
        }

        private static readonly Dictionary<string, string> ReplacePrefab = new Dictionary<string, string>
        {
            { "assets/rust.ai/nextai/testridablehorse.prefab", "assets/content/vehicles/horse/ridablehorse.prefab" },
            { "assets/content/vehicles/horse/ridablehorse2.prefab", "assets/content/vehicles/horse/ridablehorse.prefab" },
            { "assets/prefabs/deployable/windmill/windmillsmall/electric.windmill.small.prefab", "assets/prefabs/deployable/windmill/electric.windmill.small.prefab"}
        };

        //Replace between old ItemID to new ItemID

        private static readonly Dictionary<int, int> ReplaceItemId = new Dictionary<int, int>
        {
            { -1461508848, 1545779598 },
            { 2115555558, 588596902 },
            { -533875561, 785728077 },
            { 1621541165, 51984655 },
            { -422893115, -1691396643 },
            { 815896488, -1211166256 },
            { 805088543, -1321651331 },
            { 449771810, 605467368 },
            { 1152393492, 1712070256 },
            { 1578894260, -742865266 },
            { 1436532208, 1638322904 },
            { 542276424, -1841918730 },
            { 1594947829, -17123659 },
            { -1035059994, -1685290200 },
            { 1818890814, -1036635990 },
            { 1819281075, -727717969 },
            { 1685058759, -1432674913 },
            { 93029210, 1548091822 },
            { -1565095136, 352130972 },
            { -1775362679, 215754713 },
            { -1775249157, 14241751 },
            { -1280058093, -1023065463 },
            { -420273765, -1234735557 },
            { 563023711, -2139580305 },
            { 790921853, -262590403 },
            { -337261910, -2072273936 },
            { 498312426, -1950721390 },
            { 504904386, 1655650836 },
            { -1221200300, -559599960 },
            { 510887968, 15388698 },
            { -814689390, 866889860 },
            { 1024486167, 1382263453 },
            { 2021568998, 609049394 },
            { 97329, 1099314009 },
            { 1046072789, -582782051 },
            { 97409, -1273339005 },
            { -1480119738, -1262185308 },
            { 1611480185, 1931713481 },
            { -1386464949, 1553078977 },
            { 93832698, 1776460938 },
            { -1063412582, -586342290 },
            { -1887162396, -996920608 },
            { -55660037, 1588298435 },
            { 919780768, 1711033574 },
            { -365801095, 1719978075 },
            { 68998734, 613961768 },
            { -853695669, 1443579727 },
            { 271534758, 833533164 },
            { -770311783, -180129657 },
            { -1192532973, 1424075905 },
            { -307490664, 1525520776 },
            { 707427396, 602741290 },
            { 707432758, -761829530 },
            { -2079677721, 1783512007 },
            { -1342405573, -1316706473 },
            { -139769801, 1946219319 },
            { -1043746011, -700591459 },
            { 2080339268, 1655979682 },
            { -171664558, -1941646328 },
            { 1050986417, -1557377697 },
            { -1693683664, 1789825282 },
            { 523409530, 1121925526 },
            { 1300054961, 634478325 },
            { -2095387015, 1142993169 },
            { 1428021640, 1104520648 },
            { 94623429, 1534542921 },
            { 1436001773, -1938052175 },
            { 1711323399, 1973684065 },
            { 1734319168, -1848736516 },
            { -1658459025, -1440987069 },
            { -726947205, -751151717 },
            { -341443994, 363467698 },
            { 1540879296, 2009734114 },
            { 94756378, -858312878 },
            { 3059095, 204391461 },
            { 3059624, 1367190888 },
            { 2045107609, -778875547 },
            { 583366917, 998894949 },
            { 2123300234, 1965232394 },
            { 1983936587, -321733511 },
            { 1257201758, -97956382 },
            { -1144743963, 296519935 },
            { -1144542967, -113413047 },
            { -1144334585, -2022172587 },
            { 1066729526, -1101924344 },
            { -1598790097, 1390353317 },
            { -933236257, 1221063409 },
            { -1575287163, -1336109173 },
            { -2104481870, -2067472972 },
            { -1571725662, 1353298668 },
            { 1456441506, 1729120840 },
            { 1200628767, -1112793865 },
            { -778796102, 1409529282 },
            { 1526866730, 674734128 },
            { 1925723260, -1519126340 },
            { 1891056868, 1401987718 },
            { 1295154089, -1878475007 },
            { 498591726, 1248356124 },
            { 1755466030, -592016202 },
            { 726730162, 798638114 },
            { -1034048911, -1018587433 },
            { 252529905, 274502203 },
            { 471582113, -1065444793 },
            { -1138648591, 16333305 },
            { 305916740, 649305914 },
            { 305916742, 649305916 },
            { 305916744, 649305918 },
            { 1908328648, -1535621066 },
            { -2078972355, 1668129151 },
            { -533484654, 989925924 },
            { 1571660245, 1569882109 },
            { 1045869440, -1215753368 },
            { 1985408483, 528668503 },
            { 97513422, 304481038 },
            { 1496470781, -196667575 },
            { 1229879204, 952603248 },
            { -1722829188, 936496778 },
            { 1849912854, 1948067030 },
            { -1266285051, 1413014235 },
            { -1749787215, -1000573653 },
            { 28178745, -946369541 },
            { -505639592, -1999722522 },
            { 1598149413, -1992717673 },
            { -1779401418, -691113464 },
            { -57285700, -335089230 },
            { 98228420, 479143914 },
            { 1422845239, 999690781 },
            { 277631078, -1819763926 },
            { 115739308, 1366282552 },
            { -522149009, -690276911 },
            { 3175989, -1899491405 },
            { 718197703, -746030907 },
            { 384204160, 1840822026 },
            { -1308622549, 143803535 },
            { -217113639, -2124352573 },
            { -1580059655, -265876753 },
            { -1832205789, 1070894649 },
            { 305916741, 649305917 },
            { 936777834, 3222790 },
            { -1224598842, 200773292 },
            { -1976561211, -1506397857 },
            { -1406876421, 1675639563 },
            { -1397343301, -23994173 },
            { 1260209393, 850280505 },
            { -1035315940, 1877339384 },
            { -1381682752, 1714496074 },
            { 696727039, -1022661119 },
            { -2128719593, -803263829 },
            { -1178289187, -1903165497 },
            { 1351172108, 1181207482 },
            { -450738836, -1539025626 },
            { -966287254, -324675402 },
            { 340009023, 671063303 },
            { 124310981, -1478212975 },
            { 1501403549, -2094954543 },
            { 698310895, -1252059217 },
            { 523855532, 1266491000 },
            { 2045246801, -886280491 },
            { 583506109, -237809779 },
            { -148163128, 794356786 },
            { -132588262, -1773144852 },
            { -1666761111, 196700171 },
            { -465236267, 442289265 },
            { -1211618504, 1751045826 },
            { 2133577942, -1982036270 },
            { -1014825244, -682687162 },
            { -991829475, 1536610005 },
            { -642008142, -1709878924 },
            { 661790782, 1272768630 },
            { -1440143841, -1780802565 },
            { 569119686, 1746956556 },
            { 1404466285, -1102429027 },
            { -1616887133, -48090175 },
            { -1167640370, -1163532624 },
            { -1284735799, 1242482355 },
            { -1278649848, -1824943010 },
            { 776005741, 1814288539 },
            { 108061910, -316250604 },
            { 255101535, -1663759755 },
            { -51678842, 1658229558 },
            { -789202811, 254522515 },
            { 516382256, -132516482 },
            { 50834473, 1381010055 },
            { -975723312, 1159991980 },
            { 1908195100, -850982208 },
            { -1097452776, -110921842 },
            { 146685185, -1469578201 },
            { -1716193401, -1812555177 },
            { 193190034, -2069578888 },
            { 371156815, -852563019 },
            { 3343606, -1966748496 },
            { 825308669, -1137865085 },
            { 830965940, -586784898 },
            { 1662628660, -163828118 },
            { 1662628661, -163828117 },
            { 1662628662, -163828112 },
            { -1832205788, 1070894648 },
            { -1832205786, 1070894646 },
            { 1625090418, 181590376 },
            { -1269800768, -874975042 },
            { 429648208, -1190096326 },
            { -1832205787, 1070894647 },
            { -1832205785, 1070894645 },
            { 107868, 696029452 },
            { 997973965, -2012470695 },
            { -46188931, -702051347 },
            { -46848560, -194953424 },
            { -2066726403, -989755543 },
            { -2043730634, 1873897110 },
            { 1325935999, -1520560807 },
            { -225234813, -78533081 },
            { -202239044, -1509851560 },
            { -322501005, 1422530437 },
            { -1851058636, 1917703890 },
            { -1828062867, -1162759543 },
            { -1966381470, -1130350864 },
            { 968732481, 1391703481 },
            { 991728250, -242084766 },
            { -253819519, 621915341 },
            { -1714986849, 1827479659 },
            { -1691991080, 813023040 },
            { 179448791, -395377963 },
            { 431617507, -1167031859 },
            { 688032252, 69511070 },
            { -1059362949, -4031221 },
            { 1265861812, 1110385766 },
            { 374890416, 317398316 },
            { 1567404401, 1882709339 },
            { -1057402571, 95950017 },
            { -758925787, -1130709577 },
            { -1411620422, 1052926200 },
            { 88869913, -542577259 },
            { -2094080303, 1318558775 },
            { 843418712, -1962971928 },
            { -1569356508, -1405508498 },
            { -1569280852, 1478091698 },
            { 449769971, 1953903201 },
            { 590532217, -2097376851 },
            { 3387378, 1414245162 },
            { 1767561705, 1992974553 },
            { 106433500, 237239288 },
            { -1334615971, -1778159885 },
            { -135651869, 1722154847 },
            { -1595790889, 1850456855 },
            { -459156023, -1695367501 },
            { 106434956, -1779183908 },
            { -578028723, -1302129395 },
            { -586116979, 286193827 },
            { -1379225193, -75944661 },
            { -930579334, 649912614 },
            { 548699316, 818877484 },
            { 142147109, 1581210395 },
            { 148953073, 1903654061 },
            { 102672084, 980333378 },
            { 640562379, -1651220691 },
            { -1732316031, -1622660759 },
            { -2130280721, 756517185 },
            { -1725510067, -722241321 },
            { 1974032895, -1673693549 },
            { -225085592, -567909622 },
            { 509654999, 1898094925 },
            { 466113771, -1511285251 },
            { 2033918259, 1373971859 },
            { 2069925558, -1736356576 },
            { -1026117678, 803222026 },
            { 1987447227, -1861522751 },
            { 540154065, -544317637 },
            { 1939428458, 176787552 },
            { -288010497, -2002277461 },
            { -847065290, 1199391518 },
            { 3506021, 963906841 },
            { 649603450, 442886268 },
            { 3506418, 1414245522 },
            { 569935070, -1104881824 },
            { 113284, -1985799200 },
            { 1916127949, -277057363 },
            { -1775234707, -1978999529 },
            { -388967316, 1326180354 },
            { 2007564590, -575483084 },
            { -1705696613, 177226991 },
            { 670655301, -253079493 },
            { 1148128486, -1958316066 },
            { -141135377, 567235583 },
            { 109266897, -932201673 },
            { -527558546, 2087678962 },
            { -1745053053, -904863145 },
            { 1223860752, 573926264 },
            { -419069863, 1234880403 },
            { -1617374968, -1994909036 },
            { 2057749608, 1950721418 },
            { 24576628, -2025184684 },
            { -1659202509, 1608640313 },
            { 2107229499, -1549739227 },
            { 191795897, -765183617 },
            { -1009492144, 795371088 },
            { 2077983581, -1367281941 },
            { 378365037, 352499047 },
            { -529054135, -1199897169 },
            { -529054134, -1199897172 },
            { 486166145, -1023374709 },
            { 1628490888, 23352662 },
            { 1498516223, 1205607945 },
            { -632459882, -1647846966 },
            { -626812403, -845557339 },
            { 385802761, -1370759135 },
            { 2117976603, 121049755 },
            { 1338515426, -996185386 },
            { -1455694274, 98508942 },
            { 1579245182, 2070189026 },
            { -587434450, 1521286012 },
            { -163742043, 1542290441 },
            { -1224714193, -1832422579 },
            { 644359987, 826309791 },
            { -1962514734, -143132326 },
            { -705305612, 1153652756 },
            { -357728804, -1819233322 },
            { -698499648, -1138208076 },
            { 1213686767, -1850571427 },
            { 386382445, -855748505 },
            { 1859976884, 553887414 },
            { 960793436, 996293980 },
            { 1001265731, 2048317869 },
            { 1253290621, -1754948969 },
            { 470729623, -1293296287 },
            { 1051155022, -369760990 },
            { 865679437, -1878764039 },
            { 927253046, -1039528932 },
            { 109552593, 1796682209 },
            { -2092529553, 1230323789 },
            { 691633666, -363689972 },
            { -2055888649, 1629293099 },
            { 621575320, -41440462 },
            { -2118132208, 1602646136 },
            { -1127699509, 1540934679 },
            { -685265909, -92759291 },
            { 552706886, -1100422738 },
            { 1835797460, -1021495308 },
            { -892259869, 642482233 },
            { -1623330855, -465682601 },
            { -1616524891, 1668858301 },
            { 789892804, 171931394 },
            { -1289478934, -1583967946 },
            { -892070738, -2099697608 },
            { -891243783, -1581843485 },
            { 889398893, -1157596551 },
            { -1625468793, 1397052267 },
            { 1293049486, 1975934948 },
            { 1369769822, 559147458 },
            { 586484018, 1079279582 },
            { 110115790, 593465182 },
            { 1490499512, 1523195708 },
            { 3552619, 2019042823 },
            { 1471284746, 73681876 },
            { 456448245, -1758372725 },
            { 110547964, 795236088 },
            { 1588977225, -1667224349 },
            { 918540912, -209869746 },
            { -471874147, 1686524871 },
            { 205978836, 1723747470 },
            { -1044400758, -129230242 },
            { -2073307447, -1331212963 },
            { 435230680, 2106561762 },
            { -864578046, 223891266 },
            { 1660607208, 935692442 },
            { 260214178, -1478445584 },
            { -1847536522, 198438816 },
            { -496055048, -967648160 },
            { -1792066367, 99588025 },
            { 562888306, -956706906 },
            { -427925529, -1429456799 },
            { 995306285, 1451568081 },
            { -378017204, -1117626326 },
            { 447918618, -148794216 },
            { 313836902, 1516985844 },
            { 1175970190, -796583652 },
            { 525244071, -148229307 },
            { -1021702157, -819720157 },
            { -402507101, 671706427 },
            { -1556671423, -1183726687 },
            { 61936445, -1614955425 },
            { 112903447, -1779180711 },
            { 1817873886, -1100168350 },
            { 1824679850, -132247350 },
            { -1628526499, -1863559151 },
            { 547302405, -119235651 },
            { 1840561315, 2114754781 },
            { -460592212, -1379835144 },
            { 3655341, -151838493 },
            { 1554697726, 418081930 },
            { -1883959124, 832133926 },
            { -481416622, 1524187186 },
            { -481416621, -41896755 },
            { -481416620, -1607980696 },
            { -1151126752, 1058261682 },
            { -1926458555, 794443127 }
        };

        //Languages phrases

        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>
            {
                {
                    "FILE_NOT_EXISTS", new Dictionary<string, string>
                    {
                        { "en", "File does not exist" },
                        { "ru", "Файл не существует" },
                        { "nl", "Bestand bestaat niet." }
                    }
                },
                {
                    "FILE_BROKEN", new Dictionary<string, string>
                    {
                        { "en", "Something went wrong during pasting because of a error in the file." },
                        { "ru", "Файл поврежден, вставка невозможна" },
                        { "nl", "Er is iets misgegaan tijdens het plakken door een beschadigd bestand." }
                    }
                },
                {
                    "NO_ACCESS", new Dictionary<string, string>
                    {
                        { "en", "You don't have the permissions to use this command" },
                        { "ru", "У вас нет прав доступа к данной команде" },
                        { "nl", "U heeft geen toestemming/permissie om dit commando te gebruiken." }
                    }
                },
                {
                    "SYNTAX_PASTEBACK", new Dictionary<string, string>
                    {
                        {
                            "en", "Syntax: /pasteback <Target Filename> <options values>\n" +
                                  "height XX - Adjust the height\n" +
                                  "vending - Information and sellings in vending machine\n" +
                                  "stability <true/false> - Whether or not to disable stability\n" +
                                  "deployables <true/false> - Whether or not to copy deployables\n" +
                                  "auth <true/false> - Whether or not to copy lock and cupboard whitelists\n" +
                                  "position <x,y,z> - Override position\n" +
                                  "rotation <X> - Override rotation\n" +
                                  "dlc <true/false> - false to exclude DLC items and deployables\n" +
                                  "skins <0-4> - 0=no skins, 1=all, 2=no paid skins, 3=allow specified only, 4=block specified only"
                        },
                        {
                            "ru", "Синтаксис: /pasteback <Название Объекта> <опция значение>\n" +
                                  "height XX - Высота от земли\n" +
                                  "vending - Информация и товары в торговом автомате"
                        },
                        {
                            "nl", "Syntax: /pasteback <Bestandsnaam> <opties waarden>\n" +
                                  "height XX - Pas de hoogte aan \n" +
                                  "vending <true/false> - Informatie en inventaris van \"vending machines\" kopiëren\n" +
                                  "stability <true/false> - of de stabiliteit van het gebouw uitgezet moet worden\n" +
                                  "deployables <true/false> - of de \"deployables\" gekopiërd moeten worden\n" +
                                  "auth <true/false> - Of authorisatie op sloten en tool cupboards gekopiërd moet worden"
                        }
                    }
                },
                {
                    "SYNTAX_PASTE_OR_PASTEBACK", new Dictionary<string, string>
                    {
                        {
                            "en", "Syntax: /paste or /pasteback <Target Filename> <options values>\n" +
                                  "height XX - Adjust the height\n" +
                                  "autoheight true/false - sets best height, carefull of the steep\n" +
                                  "blockcollision XX - blocks the entire paste if something the new building collides with something\n" +
                                  "deployables true/false - false to remove deployables\n" +
                                  "inventories true/false - false to ignore inventories\n" +
                                  "vending - Information and sellings in vending machine\n" +
                                  "stability <true/false> - Whether or not to disable stability on the building\n" +
                                  "position <x,y,z> - Override position\n" +
                                  "rotation <X> - Override rotation\n" +
                                  "dlc <true/false> - false to exclude DLC items and deployables\n" +
                                  "skins <0-4> - 0=no skins, 1=all, 2=no paid skins, 3=allow specified only, 4=block specified only"
                        },
                        {
                            "ru", "Синтаксис: /paste or /pasteback <Название Объекта> <опция значение>\n" +
                                  "height XX - Высота от земли\n" +
                                  "autoheight true/false - автоматически подобрать высоту от земли\n" +
                                  "blockcollision XX - блокировать вставку, если что-то этому мешает\n" +
                                  "deployables true/false - false для удаления предметов\n" +
                                  "inventories true/false - false для игнорирования копирования инвентаря\n" +
                                  "vending - Информация и товары в торговом автомате"
                        },
                        {
                            "nl", "Syntax: /paste of /pasteback <Bestandsnaam> <opties waarden>\n" +
                                  "height XX - Pas de hoogte aan \n" +
                                  "autoheight true/false - probeert de optimale hoogte te vinden om gebouw te plaatsen. Werkt optimaal op vlakke grond.\n" +
                                  "vending true/false - Informatie en inventaris van \"vending machines\" kopiëren\n" +
                                  "stability <true/false> - of de stabiliteit van het gebouw uitgezet moet worden\n" +
                                  "deployables <true/false> - of de \"deployables\" gekopiërd moeten worden\n" +
                                  "auth <true/false> - Of authorisatie op sloten en tool cupboards gekopiërd moet worden"
                        }
                    }
                },
                {
                    "PASTEBACK_SUCCESS", new Dictionary<string, string>
                    {
                        { "en", "You've successfully placed back the structure" },
                        { "ru", "Постройка успешно вставлена на старое место" },
                        { "nl", "Het gebouw is succesvol teruggeplaatst." }
                    }
                },
                {
                    "PASTE_SUCCESS", new Dictionary<string, string>
                    {
                        { "en", "You've successfully pasted the structure" },
                        { "ru", "Постройка успешно вставлена" },
                        { "nl", "Het gebouw is succesvol geplaatst." }
                    }
                },
                {
                    "SYNTAX_COPY", new Dictionary<string, string>
                    {
                        {
                            "en", "Syntax: /copy <Target Filename> <options values>\n" +
                                  "radius XX (default 3) - The radius in which to search for the next object (performs this search from every other object)\n" +
                                  "method proximity/building (default proximity) - Building only copies objects which are part of the building, proximity copies everything (within the radius)\n" +
                                  "deployables true/false (saves deployables or not) - Whether to save deployables\n" +
                                  "inventories true/false (saves inventories or not) - Whether to save inventories of found objects with inventories."
                        },
                        {
                            "ru", "Синтаксис: /copy <Название Объекта> <опция значение>\n" +
                                  "radius XX (default 3)\n" +
                                  "method proximity/building (по умолчанию proximity)\n" +
                                  "deployables true/false (сохранять предметы или нет)\n" +
                                  "inventories true/false (сохранять инвентарь или нет)"
                        },
                        {
                            "nl", "Syntax: /copy <Bestandsnaam> <opties waarden>\n" +
                                  "radius XX (standaard 3) - De radius waarin copy paste naar het volgende object zoekt\n" +
                                  "method proximity/building (standaard proximity) - Building kopieërd alleen objecten die bij het gebouw horen, proximity kopieërd alles wat gevonden is\n" +
                                  "deployables true/false (saves deployables or not) - Of de data van gevonden \"deployables\" opgeslagen moet worden\n" +
                                  "inventories true/false (saves inventories or not) - Of inventarissen van objecten (kisten, tool cupboards, etc) opgeslagen moet worden"
                        }
                    }
                },
                {
                    "NO_ENTITY_RAY", new Dictionary<string, string>
                    {
                        { "en", "Couldn't ray something valid in front of you" },
                        { "ru", "Не удалось найти какой-либо объект перед вами" },
                        { "nl", "U kijkt niet naar een geschikt object om een kopie op te starten." }
                    }
                },
                {
                    "COPY_SUCCESS", new Dictionary<string, string>
                    {
                        { "en", "The structure was successfully copied as {0}" },
                        { "ru", "Постройка успешно скопирована под названием: {0}" },
                        { "nl", "Gebouw is succesvol gekopieërd" }
                    }
                },
                {
                    "NO_PASTED_STRUCTURE", new Dictionary<string, string>
                    {
                        { "en", "You must paste structure before undoing it" },
                        { "ru", "Вы должны вставить постройку перед тем, как отменить действие" },
                        {
                            "nl",
                            "U moet eerst een gebouw terugplaatsen alvorens deze ongedaan gemaakt kan worden (duhh)"
                        }
                    }
                },
                {
                    "UNDO_SUCCESS", new Dictionary<string, string>
                    {
                        { "en", "You've successfully undid what you pasted" },
                        { "ru", "Вы успешно снесли вставленную постройку" },
                        { "nl", "Laatse geplaatste gebouw is succesvol ongedaan gemaakt." }
                    }
                },
                {
                    "NOT_FOUND_PLAYER", new Dictionary<string, string>
                    {
                        { "en", "Couldn't find the player" },
                        { "ru", "Не удалось найти игрока" },
                        { "nl", "Speler niet gevonden." }
                    }
                },
                {
                    "SYNTAX_SKINSMODE", new Dictionary<string, string>
                    {
                        { "en", "Option {0} must be <0-4> 0=no skins, 1=all, 2=no paid skins, 3=allow specified only, 4=block specified only" },
                        { "ru", "Опция {0} должна быть <0-4> 0=без скинов, 1=все, 2=без платных скинов, 3=только разрешенные, 4=блокировать указанные" },
                        { "nl", "Optie {0} moet <0-4> zijn 0=geen skins, 1=alle, 2=geen betaalde skins, 3=alleen toegestane, 4=blokkeer opgegeven" }
                    }
                },
                {
                    "SYNTAX_BOOL", new Dictionary<string, string>
                    {
                        { "en", "Option {0} must be true/false" },
                        { "ru", "Опция {0} принимает значения true/false" },
                        { "nl", "Optie {0} moet true of false zijn" }
                    }
                },
                {
                    "SYNTAX_FLOAT", new Dictionary<string, string>
                    {
                        { "en", "Option {0} must be a decimal" },
                        { "ru", "Опция {0} принимает только числовые значения с точкой" },
                        { "nl", "Optie {0} moet een decimal zijn" }
                    }
                },
                {
                    "SYNTAX_HEIGHT", new Dictionary<string, string>
                    {
                        { "en", "Option height must be a number" },
                        { "ru", "Опция height принимает только числовые значения" },
                        { "nl", "De optie height accepteert alleen nummers" }
                    }
                },
                {
                    "SYNTAX_BLOCKCOLLISION", new Dictionary<string, string>
                    {
                        { "en", "Option blockcollision must be a number, 0 will deactivate the option" },
                        {
                            "ru",
                            "Опция blockcollision принимает только числовые значения, 0 позволяет отключить проверку"
                        },
                        { "nl", "Optie blockcollision accepteert alleen nummers, 0 schakelt deze functionaliteit uit" }
                    }
                },
                {
                    "SYNTAX_RADIUS", new Dictionary<string, string>
                    {
                        { "en", "Option radius must be a number" },
                        { "ru", "Опция radius принимает только числовые значения" },
                        { "nl", "Optie height accepteert alleen nummers" }
                    }
                },
                {
                    "BLOCKING_PASTE", new Dictionary<string, string>
                    {
                        { "en", "Something is blocking the paste" },
                        { "ru", "Что-то препятствует вставке" },
                        { "nl", "Iets blokkeert het plaatsen van dit gebouw" }
                    }
                },
                {
                    "AVAILABLE_STRUCTURES", new Dictionary<string, string>
                    {
                        { "ru", "<color=orange>Доступные постройки:</color>" },
                        { "en", "<color=orange>Available structures:</color>" },
                        { "nl", "Beschikbare bestanden om te plaatsen zijn:" }
                    }
                }
            };

        public class CopyData
        {
            public IPlayer Player;
            public BasePlayer BasePlayer;
            public Stack<Vector3> CheckFrom = new Stack<Vector3>();
            public HashSet<BaseEntity> HouseList = new HashSet<BaseEntity>();
            public List<object> RawData = new List<object>();
            public Vector3 SourcePos;
            public Vector3 SourceRot;
            public Action Callback;

            public string Filename;
            public int CurrentLayer;
            public float RotCor;
            public float Range;
            public bool SaveTree;
            public bool SaveShare;
            public CopyMechanics CopyMechanics;
            public bool EachToEach;
            public uint BuildingId = 0;

#if DEBUG
            public Stopwatch Sw = new Stopwatch();
#endif
        }

        public class PasteData
        {
            public ICollection<Dictionary<string, object>> Entities;
            public List<BaseEntity> PastedEntities = new List<BaseEntity>();
            public string Filename;

            public Dictionary<ulong, Dictionary<string, object>> EntityLookup =
                new Dictionary<ulong, Dictionary<string, object>>();

            public Dictionary<ulong, Item> ItemsWithSubEntity = new Dictionary<ulong, Item>();
            public List<Action> FinalProcessingActions = new List<Action>();
            public IPlayer Player;
            public BasePlayer BasePlayer;
            public List<StabilityEntity> StabilityEntities = new List<StabilityEntity>();
            public List<IndustrialStorageAdaptor> industrialStorageAdaptors = new List<IndustrialStorageAdaptor>();
            public List<ConnectedSpeaker> ConnectedSpeakers = new List<ConnectedSpeaker>();
            public List<IOEntity> checkPosition;
            public Quaternion QuaternionRotation;
            public Action CallbackFinished;
            public Action<BaseEntity> CallbackSpawned;

            public bool Auth;
            public Vector3 StartPos;
            public float HeightAdj;
            public bool Stability;
            public bool IsItemReplace;
            public bool Ownership;
            public bool CheckPlaced = true;
            public bool EnableSaving = true;
            public bool Dlc = true;
            public SkinsMode SkinsMode = SkinsMode.AllSkins;

            public bool Cancelled = false;

            public uint BuildingId = 0;
            
            public VersionNumber Version { get; set; }

#if DEBUG
            public Stopwatch Sw = new Stopwatch();
#endif
        }

        public struct OriginalTransforms
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 localScale;
            public Vector3 diff;

            public OriginalTransforms(Transform transform, Vector3 localDiff)
            {
                position = transform.position;
                rotation = transform.rotation;
                localScale = transform.localScale;
                diff = transform.InverseTransformDirection(localDiff);
            }
        }

        private VersionNumber ParseVersionNumber(string versionString)
        {
            string[] array = versionString.Split(new char[1] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            int major = int.Parse(array[0]);
            int minor = int.Parse(array[1]);
            int patch = int.Parse(array[2]);
            return new VersionNumber(major, minor, patch);
        }
    }
}
