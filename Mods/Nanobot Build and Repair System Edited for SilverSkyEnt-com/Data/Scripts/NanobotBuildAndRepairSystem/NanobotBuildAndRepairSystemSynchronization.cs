namespace SpaceEquipmentLtd.NanobotBuildAndRepairSystem
{
   using System;
   using System.Collections.Generic;
   using System.ComponentModel;
   using System.Xml.Serialization;

   using VRage.Game;
   using VRage.Game.ModAPI;
   using VRage.ModAPI;
   using VRage.ObjectBuilders;
   using VRageMath;

   using ProtoBuf;
   using Sandbox.Game.EntityComponents;
   using Sandbox.ModAPI;

   using SpaceEquipmentLtd.Utils;

   [ProtoContract(UseProtoMembersOnly = true)]
   public class SyncEntityId
   {
      [ProtoMember(1)]
      public long EntityId { get; set; }
      [ProtoMember(2)]
      public long GridId { get; set; }
      [ProtoMember(3)]
      public Vector3I Position { get; set; }

      public override string ToString()
      {
         return string.Format("EntityId={0}, GridId={1}, Position={2}", EntityId, GridId, Position);
      }

      public static SyncEntityId GetSyncId(object item)
      {
         if (item == null) return null;
         var slimBlock = item as IMySlimBlock;
         if (slimBlock != null)
         {
            if (slimBlock.FatBlock != null)
            {
               return new SyncEntityId() { EntityId = slimBlock.FatBlock.EntityId, GridId = slimBlock.CubeGrid != null ? slimBlock.CubeGrid.EntityId : 0, Position = slimBlock.Position };
            }
            else if (slimBlock.CubeGrid != null)
            {
               return new SyncEntityId() { EntityId = 0, GridId = slimBlock.CubeGrid.EntityId, Position = slimBlock.Position };
            }
         }
         var entity = item as IMyEntity;
         if (entity != null)
         {
            return new SyncEntityId() { EntityId = entity.EntityId };
         }

         var position = item as Vector3D?;
         if (position != null)
         {
            return new SyncEntityId() { Position = new Vector3I((int)position.Value.X, (int)position.Value.Y, (int)position.Value.Z) };
         }

         return null;
      }

      public static object GetItem(SyncEntityId id)
      {
         if (id == null) return null;

         if (id.EntityId != 0)
         {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(id.EntityId, out entity))
            {
               return entity;
            }
         }
         if (id.GridId != 0)
         {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(id.GridId, out entity))
            {
               var grid = entity as IMyCubeGrid;
               return grid != null ? grid.GetCubeBlock(id.Position) : null;
            }
         }
         if (id.Position != null)
         {
            return id.Position;
         }
         return null;
      }

      public static IMySlimBlock GetItemAsSlimBlock(SyncEntityId id)
      {
         var item = GetItem(id);
         var slimBlock = item as IMySlimBlock;
         if (slimBlock != null) return slimBlock;

         var block = item as IMyCubeBlock;
         if (block != null) return block.SlimBlock;

         return null;
      }

      public static T GetItemAs<T>(SyncEntityId id) where T : class
      {
         return GetItem(id) as T;
      }
   }

   [ProtoContract(UseProtoMembersOnly = true)]
   public class SyncTargetEntityData
   {
      [ProtoMember(1)]
      public SyncEntityId Entity { get; set; }
      [ProtoMember(2)]
      public double Distance { get; set; }
   }

   [ProtoContract(UseProtoMembersOnly = true)]
   public class SyncComponents
   {
      [ProtoMember(1)]
      public SerializableDefinitionId Component { get; set; }
      [ProtoMember(2)]
      public int Amount { get; set; }
   }

   /// <summary>
   /// The settings for Mod
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncModSettings
   {
      private const int CurrentSettingsVersion = 4;
      [XmlElement]
      public int Version { get; set; }

      [ProtoMember(1), XmlElement]
      public Logging.Level LogLevel { get; set; }

      [XmlIgnore]
      public TimeSpan SourcesAndTargetsUpdateInterval { get; set; }

      [XmlIgnore]
      public TimeSpan FriendlyDamageTimeout { get; set; }

      [XmlIgnore]
      public TimeSpan FriendlyDamageCleanup { get; set; }

      [ProtoMember(2), XmlElement]
      public int Range { get; set; }

      [ProtoMember(3), XmlElement]
      public long SourcesAndTargetsUpdateIntervalTicks
      {
         get { return SourcesAndTargetsUpdateInterval.Ticks; }
         set { SourcesAndTargetsUpdateInterval = new TimeSpan(value); }
      }

      [ProtoMember(4), XmlElement]
      public long FriendlyDamageTimeoutTicks
      {
         get { return FriendlyDamageTimeout.Ticks; }
         set { FriendlyDamageTimeout = new TimeSpan(value); }
      }

      [ProtoMember(5), XmlElement]
      public long FriendlyDamageCleanupTicks
      {
         get { return FriendlyDamageCleanup.Ticks; }
         set { FriendlyDamageCleanup = new TimeSpan(value); }
      }

      [ProtoMember(8), XmlElement]
      public float MaximumRequiredElectricPowerTransport { get; set; }

      [ProtoMember(10), XmlElement]
      public SyncModSettingsWelder Welder { get; set; }

      public SyncModSettings()
      {
         LogLevel = Logging.Level.Error; //Default
         SourcesAndTargetsUpdateInterval = TimeSpan.FromSeconds(10);
         FriendlyDamageTimeout = TimeSpan.FromSeconds(60);
         FriendlyDamageCleanup = TimeSpan.FromSeconds(10);
         Range = NanobotBuildAndRepairSystemBlock.WELDER_RANGE_DEFAULT_IN_M;
         MaximumRequiredElectricPowerTransport = NanobotBuildAndRepairSystemBlock.WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT;
         Welder = new SyncModSettingsWelder();
      }

      public static SyncModSettings Load()
      {
         var world = false;
         SyncModSettings settings = null;
         try
         {
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               world = true;
               using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
               {
                  settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                  Mod.Log.Write("NanobotBuildAndRepairSystemSettings: Loaded from world file.");
               }
            }
            else if (MyAPIGateway.Utilities.FileExistsInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
               {
                  settings = MyAPIGateway.Utilities.SerializeFromXML<SyncModSettings>(reader.ReadToEnd());
                  Mod.Log.Write("NanobotBuildAndRepairSystemSettings: Loaded from local storage.");
               }
            }

            if (settings != null)
            {
               var adjusted = false;
               if (settings.Version < CurrentSettingsVersion)
               {
                  Mod.Log.Write("NanobotBuildAndRepairSystemSettings: Settings have old version: {0} update to {1}", settings.Version, CurrentSettingsVersion);
                  switch (settings.Version)
                  {
                     case 0:
                        settings.LogLevel = Logging.Level.Error;
                        break;
                  }

                  if (settings.Welder.AllowedSearchModes == 0) settings.Welder.AllowedSearchModes = SearchModes.Grids | SearchModes.BoundingBox;
                  if (settings.Welder.AllowedWorkModes == 0) settings.Welder.AllowedWorkModes = WorkModes.WeldBeforeGrind | WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck;
                  if (settings.Welder.WeldingMultiplier == 0) settings.Welder.WeldingMultiplier = 1;
                  if (settings.Welder.GrindingMultiplier == 0) settings.Welder.GrindingMultiplier = 1;

                  adjusted = true;
                  settings.Version = CurrentSettingsVersion;
               }
               if (settings.Range > NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MAX_IN_M)
               {
                  settings.Range = NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MAX_IN_M;
                  adjusted = true;
               }
               else if (settings.Range < NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M)
               {
                  settings.Range = NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M;
                  adjusted = true;
               }

               if (settings.Welder.WeldingMultiplier < NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MIN)
               {
                  settings.Welder.WeldingMultiplier = NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MIN;
                  adjusted = true;
               }
               else if (settings.Welder.WeldingMultiplier >= NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MAX)
               {
                  settings.Welder.WeldingMultiplier = NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MAX;
                  adjusted = true;
               }

               if (settings.Welder.GrindingMultiplier < NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MIN)
               {
                  settings.Welder.GrindingMultiplier = NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MIN;
                  adjusted = true;
               }
               else if (settings.Welder.GrindingMultiplier >= NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MAX)
               {
                  settings.Welder.GrindingMultiplier = NanobotBuildAndRepairSystemBlock.WELDING_GRINDING_MULTIPLIER_MAX;
                  adjusted = true;
               }

               Mod.Log.Write(Logging.Level.Info, "NanobotBuildAndRepairSystemSettings: Settings {0} {1} ", settings.Welder.GrindingMultiplier, settings);
               if (adjusted) Save(settings, world);
            }
            else
            {
               settings = new SyncModSettings() { Version = CurrentSettingsVersion };
               //Save(settings, world); don't save file with default values
            }
         }
         catch (Exception ex)
         {
            Mod.Log.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemSettings: Exception while loading: {0}", ex);
         }

         return settings;
      }

      public static void Save(SyncModSettings settings, bool world)
      {
         if (world)
         {
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
            }
         }
         else
         {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("ModSettings.xml", typeof(SyncModSettings)))
            {
               writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
            }
         }
      }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncModSettingsWelder
   {
      [ProtoMember(1), XmlElement]
      public float MaximumRequiredElectricPowerWelding { get; set; }

      [ProtoMember(2), XmlElement]
      public float MaximumRequiredElectricPowerGrinding { get; set; }

      [ProtoMember(10), XmlElement]
      public float WeldingMultiplier { get; set; }

      [ProtoMember(11), XmlElement]
      public float GrindingMultiplier { get; set; }

      [ProtoMember(90), XmlElement]
      public SearchModes AllowedSearchModes { get; set; }
      [ProtoMember(91), XmlElement]
      public SearchModes SearchModeDefault { get; set; }

      [ProtoMember(101), XmlElement]
      public bool AllowBuildFixed { get; set; }

      [ProtoMember(102), XmlElement]
      public bool AllowBuildDefault { get; set; }

      [ProtoMember(105), XmlElement]
      public WorkModes AllowedWorkModes { get; set; }
      [ProtoMember(106), XmlElement]
      public WorkModes WorkModeDefault { get; set; }

      [ProtoMember(110), XmlElement]
      public bool UseIgnoreColorFixed { get; set; }
      [ProtoMember(111), XmlElement]
      public bool UseIgnoreColorDefault { get; set; }
      [ProtoMember(112), XmlArray]
      public float[] IgnoreColorDefault { get; set; }

      [ProtoMember(115), XmlElement]
      public bool UseGrindColorFixed { get; set; }
      [ProtoMember(116), XmlElement]
      public bool UseGrindColorDefault { get; set; }
      [ProtoMember(117), XmlArray]
      public float[] GrindColorDefault { get; set; }

      [ProtoMember(118), XmlElement]
      public bool UseGrindJanitorFixed { get; set; }
      [ProtoMember(119), XmlElement]
      public AutoGrindRelation UseGrindJanitorDefault { get; set; }
      [ProtoMember(120), XmlElement]
      public AutoGrindOptions GrindJanitorOptionsDefault { get; set; }

      [ProtoMember(125), XmlElement]
      public bool ShowAreaFixed { get; set; }

      [ProtoMember(130), XmlElement]
      public bool AreaSizeFixed { get; set; }

      [ProtoMember(140), XmlElement]
      public bool PriorityFixed { get; set; }
      [ProtoMember(144), XmlElement]
      public bool CollectPriorityFixed { get; set; }

      [ProtoMember(145), XmlElement]
      public bool PushIngotOreImmediatelyFixed { get; set; }
      [ProtoMember(146), XmlElement]
      public bool PushIngotOreImmediatelyDefault { get; set; }

      [ProtoMember(147), XmlElement]
      public bool PushComponentImmediatelyFixed { get; set; }
      [ProtoMember(148), XmlElement]
      public bool PushComponentImmediatelyDefault { get; set; }

      [ProtoMember(149), XmlElement]
      public bool PushItemsImmediatelyFixed { get; set; }
      [ProtoMember(150), XmlElement]
      public bool PushItemsImmediatelyDefault { get; set; }

      [ProtoMember(156), XmlElement]
      public bool CollectIfIdleFixed { get; set; }
      [ProtoMember(157), XmlElement]
      public bool CollectIfIdleDefault { get; set; }

      [ProtoMember(160), XmlElement]
      public bool SoundVolumeFixed { get; set; }
      [ProtoMember(161), XmlElement]
      public float SoundVolumeDefault { get; set; }

      [ProtoMember(170), XmlElement]
      public bool ScriptControllFixed { get; set; }

      public SyncModSettingsWelder()
      {
         MaximumRequiredElectricPowerWelding = NanobotBuildAndRepairSystemBlock.WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT;
         MaximumRequiredElectricPowerGrinding = NanobotBuildAndRepairSystemBlock.WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT;

         WeldingMultiplier = 1f;
         GrindingMultiplier = 1f;

         AllowedSearchModes = SearchModes.Grids | SearchModes.BoundingBox;
         SearchModeDefault = SearchModes.Grids;

         AllowBuildFixed = false;
         AllowBuildDefault = true;

         AllowedWorkModes = WorkModes.GrindBeforeWeld | WorkModes.GrindIfWeldGetStuck | WorkModes.WeldBeforeGrind;
         WorkModeDefault = WorkModes.WeldBeforeGrind;

         UseIgnoreColorFixed = false;
         UseIgnoreColorDefault = true;
         IgnoreColorDefault = new float[] { 321f, 100f, 51f };

         UseGrindColorFixed = false;
         UseGrindColorDefault = true;
         GrindColorDefault = new float[] { 321f, 100f, 50f };

         UseGrindJanitorFixed = false;
         UseGrindJanitorDefault = AutoGrindRelation.Enemies | AutoGrindRelation.NoOwnership;
         GrindJanitorOptionsDefault = 0;

         ShowAreaFixed = false;
         AreaSizeFixed = false;
         PriorityFixed = false;
         CollectPriorityFixed = false;

         PushIngotOreImmediatelyFixed = false;
         PushIngotOreImmediatelyDefault = true;
         PushItemsImmediatelyFixed = false;
         PushItemsImmediatelyDefault = true;
         PushComponentImmediatelyFixed = false;
         PushComponentImmediatelyDefault = false;

         CollectIfIdleDefault = false;

         SoundVolumeFixed = false;
         SoundVolumeDefault = NanobotBuildAndRepairSystemBlock.WELDER_SOUND_VOLUME / 2;

         ScriptControllFixed = false;
      }
   }

   /// <summary>
   /// The settings for Block
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncBlockSettings
   {
      [Flags]
      public enum Settings
      {
         AllowBuild = 0x00000001,
         ShowArea = 0x00000002,
         ScriptControlled = 0x00000004,
         UseIgnoreColor = 0x00000010,
         UseGrindColor = 0x00000020,
         GrindNearFirst = 0x00000100,
         GrindSmallestGridFirst = 0x00000200,
         ComponentCollectIfIdle = 0x00010000,
         PushIngotOreImmediately = 0x00020000,
         PushComponentImmediately = 0x00040000,
         PushItemsImmediately = 0x00080000
      }
      private Settings _Flags;
      private BoundingBoxD _AreaBoundingBox;
      private Vector3 _IgnoreColor;
      private Vector3 _GrindColor;
      private AutoGrindRelation _UseGrindJanitorOn;
      private AutoGrindOptions _GrindJanitorOptions;
      private int _AreaWidthLeft;
      private int _AreaWidthRight;
      private int _AreaHeightTop;
      private int _AreaHeightBottom;
      private int _AreaDepthFront;
      private int _AreaDepthRear;
      private string _WeldPriority;
      private string _GrindPriority;
      private string _ComponentCollectPriority;
      private float _SoundVolume;
      private SearchModes _SearchMode;
      private WorkModes _WorkMode;
      private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedWeldingBlock;
      private VRage.Game.ModAPI.Ingame.IMySlimBlock _CurrentPickedGrindingBlock;
      private TimeSpan _LastStored;
      private TimeSpan _LastTransmitted;

      private void SetFlags(bool set, Settings setting)
      {
         if (set != ((_Flags & setting) != 0))
         {
            _Flags = (_Flags & ~setting) | (set ? setting : 0);
            Changed = 3u;
         }
      }

      [XmlIgnore]
      public uint Changed { get; private set; }

      [ProtoMember(5), XmlElement]
      public Settings Flags
      {
         get
         {
            return _Flags;
         }
         set
         {
            if (_Flags != value)
            {
               _Flags = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(20), XmlElement]
      public SearchModes SearchMode
      {
         get
         {
            return _SearchMode;
         }
         set
         {
            if (_SearchMode != value)
            {
               _SearchMode = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(25), XmlElement]
      public WorkModes WorkMode
      {
         get
         {
            return _WorkMode;
         }
         set
         {
            if (_WorkMode != value)
            {
               _WorkMode = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(31), XmlElement]
      public Vector3 IgnoreColor
      {
         get
         {
            return _IgnoreColor;
         }
         set
         {
            if (_IgnoreColor != value)
            {
               _IgnoreColor = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(36), XmlElement]
      public Vector3 GrindColor
      {
         get
         {
            return _GrindColor;
         }
         set
         {
            if (_GrindColor != value)
            {
               _GrindColor = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(39), XmlElement]
      public AutoGrindRelation UseGrindJanitorOn
      {
         get
         {
            return _UseGrindJanitorOn;
         }
         set
         {
            if (_UseGrindJanitorOn != value)
            {
               _UseGrindJanitorOn = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(40), XmlElement]
      public AutoGrindOptions GrindJanitorOptions
      {
         get
         {
            return _GrindJanitorOptions;
         }
         set
         {
            if (_GrindJanitorOptions != value)
            {
               _GrindJanitorOptions = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(41), XmlElement]
      public int AreaWidthLeft
      {
         get
         {
            return _AreaWidthLeft;
         }
         set
         {
            if (_AreaWidthLeft != value)
            {
               _AreaWidthLeft = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(42), XmlElement]
      public int AreaWidthRight
      {
         get
         {
            return _AreaWidthRight;
         }
         set
         {
            if (_AreaWidthRight != value)
            {
               _AreaWidthRight = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(43), XmlElement]
      public int AreaHeightTop
      {
         get
         {
            return _AreaHeightTop;
         }
         set
         {
            if (_AreaHeightTop != value)
            {
               _AreaHeightTop = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(44), XmlElement]
      public int AreaHeightBottom
      {
         get
         {
            return _AreaHeightBottom;
         }
         set
         {
            if (_AreaHeightBottom != value)
            {
               _AreaHeightBottom = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(45), XmlElement]
      public int AreaDepthFront
      {
         get
         {
            return _AreaDepthFront;
         }
         set
         {
            if (_AreaDepthFront != value)
            {
               _AreaDepthFront = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(46), XmlElement]
      public int AreaDepthRear
      {
         get
         {
            return _AreaDepthRear;
         }
         set
         {
            if (_AreaDepthRear != value)
            {
               _AreaDepthRear = value;
               Changed = 3u;
               RecalcAreaBoundigBox();
            }
         }
      }

      [ProtoMember(61), XmlElement]
      public string WeldPriority
      {
         get
         {
            return _WeldPriority;
         }
         set
         {
            if (_WeldPriority != value)
            {
               _WeldPriority = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(62), XmlElement]
      public string GrindPriority
      {
         get
         {
            return _GrindPriority;
         }
         set
         {
            if (_GrindPriority != value)
            {
               _GrindPriority = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(65), XmlElement]
      public string ComponentCollectPriority
      {
         get
         {
            return _ComponentCollectPriority;
         }
         set
         {
            if (_ComponentCollectPriority != value)
            {
               _ComponentCollectPriority = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(80), XmlElement]
      public float SoundVolume
      {
         get
         {
            return _SoundVolume;
         }
         set
         {
            if (_SoundVolume != value)
            {
               _SoundVolume = value;
               Changed = 3u;
            }
         }
      }

      [XmlIgnore]
      public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedWeldingBlock
      {
         get
         {
            return _CurrentPickedWeldingBlock;
         }
         set
         {
            if (_CurrentPickedWeldingBlock != value)
            {
               _CurrentPickedWeldingBlock = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(100), XmlElement]
      public SyncEntityId CurrentPickedWeldingBlockSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentPickedWeldingBlock);
         }
         set
         {
            CurrentPickedWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
         }
      }

      [XmlIgnore]
      public VRage.Game.ModAPI.Ingame.IMySlimBlock CurrentPickedGrindingBlock
      {
         get
         {
            return _CurrentPickedGrindingBlock;
         }
         set
         {
            if (_CurrentPickedGrindingBlock != value)
            {
               _CurrentPickedGrindingBlock = value;
               Changed = 3u;
            }
         }
      }

      [ProtoMember(105), XmlElement]
      public SyncEntityId CurrentPickedGrindingBlockSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentPickedGrindingBlock);
         }
         set
         {
            CurrentPickedGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
         }
      }

      [XmlIgnore]
      public int MaximumRange { get; private set; }
      [XmlIgnore]
      public float TransportSpeed { get; private set; }
      [XmlIgnore]
      public float MaximumRequiredElectricPowerWelding { get; private set; }
      [XmlIgnore]
      public float MaximumRequiredElectricPowerGrinding { get; private set; }
      [XmlIgnore]
      public float MaximumRequiredElectricPowerTransport { get; private set; }

      internal BoundingBoxD AreaBoundingBox
      {
         get
         {
            return _AreaBoundingBox;
         }
      }

      public SyncBlockSettings() : this(null)
      {

      }

      public SyncBlockSettings(NanobotBuildAndRepairSystemBlock system)
      {
         _WeldPriority = string.Empty;
         _GrindPriority = string.Empty;
         _ComponentCollectPriority = string.Empty;
         CheckLimits(system, true);

         Changed = 0;
         _LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
         _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;

         RecalcAreaBoundigBox();
      }

      public void TrySave(IMyEntity entity, Guid guid)
      {
         if ((Changed & 2u) == 0) return;
         if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastStored) < TimeSpan.FromSeconds(20)) return;
         Save(entity, guid);
      }

      public void Save(IMyEntity entity, Guid guid)
      {
         if (entity.Storage == null)
         {
            entity.Storage = new MyModStorageComponent();
         }

         var storage = entity.Storage;
         storage[guid] = MyAPIGateway.Utilities.SerializeToXML(this);
         Changed = (Changed & ~2u);
         _LastStored = MyAPIGateway.Session.ElapsedPlayTime;
      }

      public static SyncBlockSettings Load(IMyEntity entity, Guid guid, NanobotBuildAndRepairSystemBlockPriorityHandling blockWeldPriority, NanobotBuildAndRepairSystemBlockPriorityHandling blockGrindPriority, NanobotBuildAndRepairSystemComponentPriorityHandling componentCollectPriority)
      {
         var storage = entity.Storage;
         string data;
         SyncBlockSettings settings = null;
         if (storage != null && storage.TryGetValue(guid, out data))
         {
            try
            {
               //Fix changed names
               data = data.Replace("GrindColorNearFirst", "GrindNearFirst");
               settings = MyAPIGateway.Utilities.SerializeFromXML<SyncBlockSettings>(data);
               if (settings != null)
               {
                  settings.RecalcAreaBoundigBox();
                  //Retrieve current settings or default if WeldPriority/GrindPriority/ComponentCollectPriority was empty
                  blockWeldPriority.SetEntries(settings.WeldPriority);
                  settings.WeldPriority = blockWeldPriority.GetEntries();

                  blockGrindPriority.SetEntries(settings.GrindPriority);
                  settings.GrindPriority = blockGrindPriority.GetEntries();

                  componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
                  settings.ComponentCollectPriority = componentCollectPriority.GetEntries();

                  settings.Changed = 0;
                  settings._LastStored = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(60));
                  settings._LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
                  return settings;
               }
            }
            catch (Exception ex)
            {
               Mod.Log.Write("SyncBlockSettings: Exception: " + ex);
            }
         }

         var control = entity as IMyTerminalBlock;
         var system = control != null ? control.GameLogic.GetAs<NanobotBuildAndRepairSystemBlock>() : null;
         settings = new SyncBlockSettings(system);
         blockWeldPriority.SetEntries(settings.WeldPriority);
         blockGrindPriority.SetEntries(settings.GrindPriority);
         componentCollectPriority.SetEntries(settings.ComponentCollectPriority);
         settings.Changed = 0;
         return settings;
      }

      public void AssignReceived(SyncBlockSettings newSettings, NanobotBuildAndRepairSystemBlockPriorityHandling weldPriority, NanobotBuildAndRepairSystemBlockPriorityHandling grindPriority, NanobotBuildAndRepairSystemComponentPriorityHandling componentCollectPriority)
      {
         _Flags = newSettings._Flags;
         _IgnoreColor = newSettings.IgnoreColor;
         _GrindColor = newSettings.GrindColor;
         _UseGrindJanitorOn = newSettings.UseGrindJanitorOn;
         _GrindJanitorOptions = newSettings.GrindJanitorOptions;

         _AreaWidthLeft = newSettings.AreaWidthLeft;
         _AreaWidthRight = newSettings.AreaWidthRight;
         _AreaHeightTop = newSettings.AreaHeightTop;
         _AreaHeightBottom = newSettings.AreaHeightBottom;
         _AreaDepthFront = newSettings.AreaDepthFront;
         _AreaDepthRear = newSettings.AreaDepthRear;

         _WeldPriority = newSettings.WeldPriority;
         _GrindPriority = newSettings.GrindPriority;
         _ComponentCollectPriority = newSettings.ComponentCollectPriority;

         _SoundVolume = newSettings.SoundVolume;
         _SearchMode = newSettings.SearchMode;
         _WorkMode = newSettings.WorkMode;

         RecalcAreaBoundigBox();
         weldPriority.SetEntries(WeldPriority);
         grindPriority.SetEntries(GrindPriority);
         componentCollectPriority.SetEntries(ComponentCollectPriority);

         Changed = 2u;
      }

      public SyncBlockSettings GetTransmit()
      {
         _LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
         Changed = Changed & ~1u;
         return this;
      }

      public bool IsTransmitNeeded()
      {
         return (Changed & 1u) != 0 && MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastTransmitted) >= TimeSpan.FromSeconds(2);
      }

      private void RecalcAreaBoundigBox()
      {
         var border = 0.25d;
         _AreaBoundingBox = new BoundingBoxD(new Vector3D(-AreaDepthRear + border, -AreaWidthLeft + border, -AreaHeightBottom + border), new Vector3D(AreaDepthFront - border, AreaWidthRight - border, AreaHeightTop - border));
      }

      public void CheckLimits(NanobotBuildAndRepairSystemBlock system, bool init)
      {
         var scale = (system != null && system.Welder != null ? (system.Welder.BlockDefinition.SubtypeName.Contains("Large") ? 1f : 3f) : 1f);

         MaximumRange = (int)Math.Ceiling(NanobotBuildAndRepairSystemMod.Settings.Range / scale);
         if (AreaWidthLeft > MaximumRange || init) AreaWidthLeft = MaximumRange;
         if (AreaWidthRight > MaximumRange || init) AreaWidthRight = MaximumRange;
         if (AreaHeightTop > MaximumRange || init) AreaHeightTop = MaximumRange;
         if (AreaHeightBottom > MaximumRange || init) AreaHeightBottom = MaximumRange;
         if (AreaDepthFront > MaximumRange || init) AreaDepthFront = MaximumRange;
         if (AreaDepthRear > MaximumRange || init) AreaDepthRear = MaximumRange;
        
         MaximumRequiredElectricPowerTransport = NanobotBuildAndRepairSystemMod.Settings.MaximumRequiredElectricPowerTransport / scale;
         MaximumRequiredElectricPowerWelding = NanobotBuildAndRepairSystemMod.Settings.Welder.MaximumRequiredElectricPowerWelding / scale;
         MaximumRequiredElectricPowerGrinding = NanobotBuildAndRepairSystemMod.Settings.Welder.MaximumRequiredElectricPowerGrinding / scale;

         var maxMultiplier = Math.Max(NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier, NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier);
         TransportSpeed = maxMultiplier * NanobotBuildAndRepairSystemBlock.WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT * Math.Min(NanobotBuildAndRepairSystemMod.Settings.Range / NanobotBuildAndRepairSystemBlock.WELDER_RANGE_DEFAULT_IN_M, 4.0f);

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed || init)
         {
            Flags = (Flags & ~Settings.AllowBuild) | (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildDefault ? Settings.AllowBuild : 0);
         }

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed || init)
         {
            Flags = (Flags & ~Settings.UseIgnoreColor) | (NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorDefault ? Settings.UseIgnoreColor : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault != null && NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault.Length >= 3)
            {
               IgnoreColor = new Vector3D(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[0] / 360f,
                                         ((float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                         ((float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.IgnoreColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
            }
         }

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed || init)
         {
            Flags = (Flags & ~Settings.UseGrindColor) | (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorDefault ? Settings.UseGrindColor : 0);
            if (NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault != null && NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault.Length >= 3)
            {
               GrindColor = new Vector3D(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[0] / 360f,
                                         ((float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[1], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                         ((float)Math.Round(NanobotBuildAndRepairSystemMod.Settings.Welder.GrindColorDefault[2], 1, MidpointRounding.AwayFromZero) / 100f) - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
            }
         }

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed || init)
         {
            UseGrindJanitorOn = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorDefault;
            GrindJanitorOptions = NanobotBuildAndRepairSystemMod.Settings.Welder.GrindJanitorOptionsDefault;
         }

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed || init) Flags = (Flags & ~Settings.ShowArea);
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed || init)
         {
            AreaWidthLeft = MaximumRange;
            AreaWidthRight = MaximumRange;
            AreaHeightTop = MaximumRange;
            AreaHeightBottom = MaximumRange;
            AreaDepthFront = MaximumRange;
            AreaDepthRear = MaximumRange;
         }

         if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed || init) Flags = (Flags & ~Settings.PushIngotOreImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyDefault ? Settings.PushIngotOreImmediately : 0);
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed || init) Flags = (Flags & ~Settings.PushComponentImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyDefault ? Settings.PushComponentImmediately : 0);
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed || init) Flags = (Flags & ~Settings.PushItemsImmediately) | (NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyDefault ? Settings.PushItemsImmediately : 0);
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed || init) Flags = (Flags & ~Settings.ComponentCollectIfIdle) | (NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleDefault ? Settings.ComponentCollectIfIdle : 0);
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed || init) SoundVolume = NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeDefault;
         if (NanobotBuildAndRepairSystemMod.Settings.Welder.ScriptControllFixed || init) Flags = (Flags & ~Settings.ScriptControlled);
         if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchMode) == 0 || init)
         {
            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & NanobotBuildAndRepairSystemMod.Settings.Welder.SearchModeDefault) != 0)
            {
               SearchMode = NanobotBuildAndRepairSystemMod.Settings.Welder.SearchModeDefault;
            }
            else
            {
               if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.Grids) != 0) SearchMode = SearchModes.Grids;
               else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) != 0) SearchMode = SearchModes.BoundingBox;
            }
         }

         if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkMode) == 0 || init)
         {
            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & NanobotBuildAndRepairSystemMod.Settings.Welder.WorkModeDefault) != 0)
            {
               WorkMode = NanobotBuildAndRepairSystemMod.Settings.Welder.WorkModeDefault;
            }
            else
            {
               if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.WeldBeforeGrind) != 0) WorkMode = WorkModes.WeldBeforeGrind;
               else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.GrindBeforeWeld) != 0) WorkMode = WorkModes.GrindBeforeWeld;
               else if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & WorkModes.GrindIfWeldGetStuck) != 0) WorkMode = WorkModes.GrindIfWeldGetStuck;
            }
         }
      }
   }

   /// <summary>
   /// Current State of block
   /// </summary>
   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class SyncBlockState
   {
      public const int MaxSyncItems = 20;
      private bool _Ready;
      private bool _Welding;
      private bool _NeedWelding;
      private bool _Grinding;
      private bool _NeedGrinding;
      private bool _Transporting;
      private bool _InventoryFull;
      private IMySlimBlock _CurrentWeldingBlock;
      private IMySlimBlock _CurrentGrindingBlock;
      private Vector3D? _CurrentTransportTarget;
      private bool _CurrentTransportIsPick;
      private List<SyncComponents> _MissingComponentsSync;
      private List<SyncTargetEntityData> _PossibleWeldTargetsSync;
      private List<SyncTargetEntityData> _PossibleGrindTargetsSync;
      private List<SyncTargetEntityData> _PossibleFloatingTargetsSync;
      private TimeSpan _TransportTime = TimeSpan.Zero;
      private TimeSpan _LastTransportStartTime = TimeSpan.Zero;

      public bool Changed { get; private set; }
      public override string ToString()
      {
         return string.Format("Ready={0}, Welding={1}/{2}, Grinding={3}/{4}, MissingComponentsCount={5}, PossibleWeldTargetsCount={6}, PossibleGrindTargetsCount={7}, PossibleFloatingTargetsCount={8}, CurrentWeldingBlock={9}, CurrentGrindingBlock={10}, CurrentTransportTarget={11}",
            Ready, Welding, NeedWelding, Grinding, NeedGrinding, MissingComponentsSync != null ? MissingComponentsSync.Count : -1, PossibleWeldTargetsSync != null ? PossibleWeldTargetsSync.Count : -1, PossibleGrindTargetsSync != null ? PossibleGrindTargetsSync.Count : -1, PossibleFloatingTargetsSync != null ? PossibleFloatingTargetsSync.Count : -1,
            Logging.BlockName(CurrentWeldingBlock, Logging.BlockNameOptions.None), Logging.BlockName(CurrentGrindingBlock, Logging.BlockNameOptions.None), CurrentTransportTarget);
      }

      [ProtoMember(1)]
      public bool Ready
      {
         get { return _Ready; }
         set
         {
            if (value != _Ready)
            {
               _Ready = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(2)]
      public bool Welding
      {
         get { return _Welding; }
         set
         {
            if (value != _Welding)
            {
               _Welding = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(3)]
      public bool NeedWelding
      {
         get { return _NeedWelding; }
         set
         {
            if (value != _NeedWelding)
            {
               _NeedWelding = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(4)]
      public bool Grinding
      {
         get { return _Grinding; }
         set
         {
            if (value != _Grinding)
            {
               _Grinding = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(5)]
      public bool NeedGrinding
      {
         get { return _NeedGrinding; }
         set
         {
            if (value != _NeedGrinding)
            {
               _NeedGrinding = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(6)]
      public bool Transporting
      {
         get { return _Transporting; }
         set
         {
            if (value != _Transporting)
            {
               _Transporting = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(7)]
      public TimeSpan LastTransmitted { get; set; }

      public IMySlimBlock CurrentWeldingBlock
      {
         get { return _CurrentWeldingBlock; }
         set
         {
            if (value != _CurrentWeldingBlock)
            {
               _CurrentWeldingBlock = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(10)]
      public SyncEntityId CurrentWeldingBlockSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentWeldingBlock);
         }
         set
         {
            CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(value);
         }
      }

      public IMySlimBlock CurrentGrindingBlock
      {
         get { return _CurrentGrindingBlock; }
         set
         {
            if (value != _CurrentGrindingBlock)
            {
               _CurrentGrindingBlock = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(15)]
      public SyncEntityId CurrentGrindingBlockSync
      {
         get
         {
            return SyncEntityId.GetSyncId(_CurrentGrindingBlock);
         }
         set
         {
            CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(value);
         }
      }

      [ProtoMember(16)]
      public Vector3D? CurrentTransportTarget
      {
         get { return _CurrentTransportTarget; }
         set
         {
            if (value != _CurrentTransportTarget)
            {
               _CurrentTransportTarget = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(17)]
      public bool CurrentTransportIsPick
      {
         get { return _CurrentTransportIsPick; }
         set
         {
            if (value != _CurrentTransportIsPick)
            {
               _CurrentTransportIsPick = value;
               Changed = true;
            }
         }
      }


      [ProtoMember(18)]
      public TimeSpan TransportTime
      {
         get { return _TransportTime; }
         set
         {
            if (value != _TransportTime)
            {
               _TransportTime = value;
               Changed = true;
            }
         }
      }

      [ProtoMember(19)]
      public TimeSpan LastTransportStartTime
      {
         get { return _LastTransportStartTime; }
         set
         {
            if (value != _LastTransportStartTime)
            {
               _LastTransportStartTime = value;
               Changed = true;
            }
         }
      }

      public DefinitionIdHashDictionary MissingComponents { get; private set; }

      [ProtoMember(20)]
      public List<SyncComponents> MissingComponentsSync
      {
         get
         {
            if (_MissingComponentsSync == null)
            {
               if (MissingComponents != null) _MissingComponentsSync = MissingComponents.GetSyncList();
               else _MissingComponentsSync = new List<SyncComponents>();
            }
            return _MissingComponentsSync;
         }
      }

      [ProtoMember(21)]
      public bool InventoryFull
      {
         get { return _InventoryFull; }
         set
         {
            if (value != _InventoryFull)
            {
               _InventoryFull = value;
               Changed = true;
            }
         }
      }

      public TargetBlockDataHashList PossibleWeldTargets { get; private set; }

      [ProtoMember(30)]
      public List<SyncTargetEntityData> PossibleWeldTargetsSync
      {
         get
         {
            if (_PossibleWeldTargetsSync == null)
            {
               if (PossibleWeldTargets != null) _PossibleWeldTargetsSync = PossibleWeldTargets.GetSyncList();
               else _PossibleWeldTargetsSync = new List<SyncTargetEntityData>();
            }
            return _PossibleWeldTargetsSync;
         }
      }

      public TargetBlockDataHashList PossibleGrindTargets { get; private set; }

      [ProtoMember(35)]
      public List<SyncTargetEntityData> PossibleGrindTargetsSync
      {
         get
         {
            if (_PossibleGrindTargetsSync == null)
            {
               if (PossibleGrindTargets != null) _PossibleGrindTargetsSync = PossibleGrindTargets.GetSyncList();
               else _PossibleGrindTargetsSync = new List<SyncTargetEntityData>();
            }
            return _PossibleGrindTargetsSync;
         }
      }

      public TargetEntityDataHashList PossibleFloatingTargets { get; private set; }

      [ProtoMember(36)]
      public List<SyncTargetEntityData> PossibleFloatingTargetsSync
      {
         get
         {
            if (_PossibleFloatingTargetsSync == null)
            {
               if (PossibleFloatingTargets != null) _PossibleFloatingTargetsSync = PossibleFloatingTargets.GetSyncList();
               else _PossibleFloatingTargetsSync = new List<SyncTargetEntityData>();
            }
            return _PossibleFloatingTargetsSync;
         }
      }

      public SyncBlockState()
      {
         MissingComponents = new DefinitionIdHashDictionary();
         PossibleWeldTargets = new TargetBlockDataHashList();
         PossibleGrindTargets = new TargetBlockDataHashList();
         PossibleFloatingTargets = new TargetEntityDataHashList();
      }

      internal void HasChanged()
      {
         Changed = true;
      }

      internal bool IsTransmitNeeded()
      {
         return Changed && MyAPIGateway.Session.ElapsedPlayTime.Subtract(LastTransmitted).TotalSeconds >= 2;
      }

      internal SyncBlockState GetTransmit()
      {
         _MissingComponentsSync = null;
         _PossibleWeldTargetsSync = null;
         _PossibleGrindTargetsSync = null;
         _PossibleFloatingTargetsSync = null;
         LastTransmitted = MyAPIGateway.Session.ElapsedPlayTime;
         Changed = false;
         return this;
      }

      internal void AssignReceived(SyncBlockState newState)
      {
         _Ready = newState.Ready;
         _Welding = newState.Welding;
         _NeedWelding = newState.NeedWelding;
         _Grinding = newState.Grinding;
         _NeedGrinding = newState.NeedGrinding;
         _InventoryFull = newState.InventoryFull;
         _LastTransportStartTime = MyAPIGateway.Session.ElapsedPlayTime - (newState.LastTransmitted - newState.LastTransportStartTime);
         _TransportTime = newState.TransportTime;

         _CurrentWeldingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentWeldingBlockSync);
         _CurrentGrindingBlock = SyncEntityId.GetItemAsSlimBlock(newState.CurrentGrindingBlockSync);
         _CurrentTransportTarget = newState.CurrentTransportTarget;
         _CurrentTransportIsPick = newState.CurrentTransportIsPick;

         MissingComponents.Clear();
         var missingComponentsSync = newState.MissingComponentsSync;
         if (missingComponentsSync != null) foreach (var item in missingComponentsSync) MissingComponents.Add(item.Component, item.Amount);

         PossibleWeldTargets.Clear();
         var possibleWeldTargetsSync = newState.PossibleWeldTargetsSync;
         if (possibleWeldTargetsSync != null) foreach (var item in possibleWeldTargetsSync) PossibleWeldTargets.Add(new TargetBlockData(SyncEntityId.GetItemAsSlimBlock(item.Entity), item.Distance, 0));

         PossibleGrindTargets.Clear();
         var possibleGrindTargetsSync = newState.PossibleGrindTargetsSync;
         if (possibleGrindTargetsSync != null) foreach (var item in possibleGrindTargetsSync) PossibleGrindTargets.Add(new TargetBlockData(SyncEntityId.GetItemAsSlimBlock(item.Entity), item.Distance, 0));

         PossibleFloatingTargets.Clear();
         var possibleFloatingTargetsSync = newState.PossibleFloatingTargetsSync;
         if (possibleFloatingTargetsSync != null) foreach (var item in possibleFloatingTargetsSync) PossibleFloatingTargets.Add(new TargetEntityData(SyncEntityId.GetItemAs<Sandbox.Game.Entities.MyFloatingObject>(item.Entity), item.Distance));

         Changed = true;
      }

      internal void ResetChanged()
      {
         Changed = false;
      }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModCommand
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }

      [ProtoMember(2)]
      public string Command { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModDataRequest
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgModSettings
   {
      [ProtoMember(2)]
      public SyncModSettings Settings { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockDataRequest
   {
      [ProtoMember(1)]
      public ulong SteamId { get; set; }
      [ProtoMember(2)]
      public long EntityId { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockSettings
   {
      [ProtoMember(1)]
      public long EntityId { get; set; }
      [ProtoMember(2)]
      public SyncBlockSettings Settings { get; set; }
   }

   [ProtoContract(SkipConstructor = true, UseProtoMembersOnly = true)]
   public class MsgBlockState
   {
      [ProtoMember(1)]
      public long EntityId { get; set; }
      [ProtoMember(2)]
      public SyncBlockState State { get; set; }
   }

   /// <summary>
   /// List including Hash Values to detect changes
   /// </summary>
   /// <typeparam name="T"></typeparam>
   public abstract class HashList<T, ST> : List<T>
   {
      private long _CurrentHash;
      private long _LastHash;
      private int _CurrentCount;
      public long CurrentHash { get { return _CurrentHash; } protected set { _CurrentHash = value; } }
      public long LastHash { get { return _LastHash; } set { _LastHash = value; } }
      public int CurrentCount { get { return _CurrentCount; } protected set { _CurrentCount = value; } }

      public abstract void RebuildHash();
      public abstract List<ST> GetSyncList();

      protected static uint RotateLeft(uint x, int n)
      {
         return (x << n) | (x >> (32 - n));
      }
   }

   /// <summary>
   /// Hash list for TargetBlockData
   /// </summary>
   public class TargetBlockDataHashList : HashList<TargetBlockData, SyncTargetEntityData>
   {
      public override List<SyncTargetEntityData> GetSyncList()
      {
         var result = new List<SyncTargetEntityData>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Block), Distance = item.Distance });
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= RotateLeft((uint)entry.Block.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = this.Count;
            CurrentHash = hash;
         }
      }
   }

   /// <summary>
   /// Hash list for TargetEntityData
   /// </summary>
   public class TargetEntityDataHashList : HashList<TargetEntityData, SyncTargetEntityData>
   {
      public override List<SyncTargetEntityData> GetSyncList()
      {
         var result = new List<SyncTargetEntityData>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(new SyncTargetEntityData() { Entity = SyncEntityId.GetSyncId(item.Entity), Distance = item.Distance });
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= RotateLeft((uint)entry.Entity.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = this.Count;
            CurrentHash = hash;
         }
      }
   }


   /// <summary>
   /// List including Hash Values to detect changes
   /// </summary>
   /// <typeparam name="T"></typeparam>
   public abstract class HashDictionary<T, T1, ST> : Dictionary<T, T1>
   {
      private long _CurrentHash;
      private long _LastHash;
      private int _CurrentCount;
      public long CurrentHash { get { return _CurrentHash; } protected set { _CurrentHash = value; } }
      public long LastHash { get { return _LastHash; } set { _LastHash = value; } }
      public int CurrentCount { get { return _CurrentCount; } protected set { _CurrentCount = value; } }

      public abstract void RebuildHash();
      public abstract List<ST> GetSyncList();

      protected static uint RotateLeft(uint x, int n)
      {
         return (x << n) | (x >> (32 - n));
      }
   }

   public class DefinitionIdHashDictionary : HashDictionary<MyDefinitionId, int, SyncComponents>
   {
      public override List<SyncComponents> GetSyncList()
      {
         var result = new List<SyncComponents>();
         var idx = 0;
         foreach (var item in this)
         {
            result.Add(new SyncComponents() { Component = item.Key, Amount = item.Value });
            idx++;
            if (idx > SyncBlockState.MaxSyncItems) break;
         }
         return result;
      }

      public override void RebuildHash()
      {
         uint hash = 0;
         var idx = 0;
         lock (this)
         {
            foreach (var entry in this)
            {
               hash ^= RotateLeft((uint)entry.GetHashCode(), idx + 1);
               idx++;
               if (idx >= SyncBlockState.MaxSyncItems) break;
            }
            CurrentCount = Count;
            CurrentHash = hash;
         }
      }
   }
}
