namespace SpaceEquipmentLtd.NanobotBuildAndRepairSystem
{
   using System;
   using System.Collections.Generic;
   using System.Text;

   using VRage;
   using VRage.Game.Components;
   using VRage.Game;
   using VRage.ObjectBuilders;
   using VRage.ModAPI;
   using VRage.Game.ModAPI;
   using VRage.Utils;
   using VRageMath;

   using Sandbox.ModAPI;
   using Sandbox.Common.ObjectBuilders;
   using Sandbox.Game.Entities;
   using Sandbox.Game.Lights;
   using Sandbox.ModAPI.Ingame;
   using Sandbox.Definitions;

   using SpaceEquipmentLtd.Utils;

   using IMyShipWelder = Sandbox.ModAPI.IMyShipWelder;
   using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
   using System.Threading;
   using Sandbox;
   using System.Diagnostics;


   [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false, "SELtdLargeNanobotBuildAndRepairSystem")]
   public class NanobotBuildAndRepairSystemBlock : MyGameLogicComponent
   {
      private enum WorkingState
      {
         Invalid = 0, NotReady = 1, Idle = 2, Welding = 3, NeedWelding = 4, MissingComponents = 5, Grinding = 6, NeedGrinding = 7, InventoryFull = 8
      }

      public const int WELDER_RANGE_DEFAULT_IN_M = 75;
      public const int WELDER_RANGE_MAX_IN_M = 1000;
      public const int WELDER_RANGE_MIN_IN_M = 1;
      public const float WELDING_GRINDING_MULTIPLIER_MIN = 0.001f;
      public const float WELDING_GRINDING_MULTIPLIER_MAX = 1000f;

      public const float WELDER_REQUIRED_ELECTRIC_POWER_STANDBY = 0.02f / 1000; //20W
      public const float WELDER_REQUIRED_ELECTRIC_POWER_WELDING_DEFAULT = 2.0f / 1000; //2kW
      public const float WELDER_REQUIRED_ELECTRIC_POWER_GRINDING_DEFAULT = 1.5f / 1000; //1.5kW
      public const float WELDER_REQUIRED_ELECTRIC_POWER_TRANSPORT_DEFAULT = 10.0f / 1000; //10kW
      public const float WELDER_TRANSPORTSPEED_METER_PER_SECOND_DEFAULT = 20f;
      public const float WELDER_TRANSPORTVOLUME_DIVISOR = 10f;
      public const float WELDER_AMOUNT_PER_SECOND = 2f;
      public const float WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED = 0.2f;
      public const float GRINDER_AMOUNT_PER_SECOND = 4f;
      public const float WELDER_SOUND_VOLUME = 2f;

      public static readonly int COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY = 50;

      public static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
      private static readonly MyStringId RangeGridResourceId = MyStringId.GetOrCompute("WelderGrid");
      private static readonly Random _RandomDelay = new Random();

      private static MySoundPair[] _Sounds = new[] { null, null, null, new MySoundPair("ToolLrgWeldMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable"), new MySoundPair("ToolLrgGrindMetal"), new MySoundPair("BlockModuleProductivity"), new MySoundPair("BaRUnable") };
      private static float[] _SoundLevels = new[] { 0f, 0f, 0f, 1f, 0.5f, 0.4f, 1f, 0.5f, 0.4f };

      private const string PARTICLE_EFFECT_WELDING1 = MyParticleEffectsNameEnum.WelderContactPoint;
      private const string PARTICLE_EFFECT_GRINDING1 = MyParticleEffectsNameEnum.AngleGrinder;
      private const string PARTICLE_EFFECT_TRANSPORT1 = MyParticleEffectsNameEnum.Smoke_Missile;

      private Stopwatch _DelayWatch = new Stopwatch();
      private int _Delay = 0;

      private bool _AsyncUpdateSourcesAndTargetsRunning = false;
      private List<TargetBlockData> _TempPossibleWeldTargets = new List<TargetBlockData>();
      private List<TargetBlockData> _TempPossibleGrindTargets = new List<TargetBlockData>();
      private List<TargetEntityData> _TempPossibleFloatingTargets = new List<TargetEntityData>();
      private List<IMyInventory> _TempPossibleSources = new List<IMyInventory>();

      private IMyShipWelder _Welder;
      private IMyInventory _TransportInventory;
      private bool _IsInit;
      private List<IMyInventory> _PossibleSources = new List<IMyInventory>();
      private Dictionary<string, int> _TempMissingComponents = new Dictionary<string, int>();
      private TimeSpan _LastFriendlyDamageCleanup;

      private static readonly int MaxTransportEffects = 50;
      private static int _ActiveTransportEffects = 0;
      private static readonly int MaxWorkingEffects = 80;
      private static int _ActiveWorkingEffects = 0;

      private MyEntity3DSoundEmitter _SoundEmitter;
      private MyEntity3DSoundEmitter _SoundEmitterWorking;
      private MyParticleEffect _ParticleEffectWorking1;
      private MyParticleEffect _ParticleEffectTransport1;
      private bool _ParticleEffectTransport1Active;
      private MyLight _LightEffect;
      private MyFlareDefinition _LightEffectFlareWelding;
      private MyFlareDefinition _LightEffectFlareGrinding;
      private Vector3 _EmitterPosition;

      private TimeSpan _LastSourcesAndTargetsUpdate;
      private int _SourcesAndTargetsUpdateRun;

      private int _UpdateEffectsInterval;
      private bool _UpdateCustomInfoNeeded;
      private TimeSpan _UpdateCustomInfoLast;
      private WorkingState _WorkingStateSet = WorkingState.Invalid;
      private float _SoundVolumeSet;
      private bool _TransportStateSet;
      private float _MaxTransportVolume;
      private WorkingState _WorkingState;
      private int _ContinuouslyError;
      private bool _PowerReady;
      private bool _PowerWelding;
      private bool _PowerGrinding;
      private bool _PowerTransporting;
      private TimeSpan _UpdatePowerSinkLast;
      private TimeSpan _TryAutoPushInventoryLast;
      private TimeSpan _TryPushInventoryLast;

      private SyncBlockSettings _Settings;
      internal SyncBlockSettings Settings
      {
         get
         {
            return _Settings != null ? _Settings : _Settings = SyncBlockSettings.Load(Entity, NanobotBuildAndRepairSystemMod.ModGuid, BlockWeldPriority, BlockGrindPriority, ComponentCollectPriority);
         }
      }

      private NanobotBuildAndRepairSystemBlockPriorityHandling _BlockWeldPriority = new NanobotBuildAndRepairSystemBlockPriorityHandling();
      internal NanobotBuildAndRepairSystemBlockPriorityHandling BlockWeldPriority
      {
         get
         {
            return _BlockWeldPriority;
         }
      }

      private NanobotBuildAndRepairSystemBlockPriorityHandling _BlockGrindPriority = new NanobotBuildAndRepairSystemBlockPriorityHandling();
      internal NanobotBuildAndRepairSystemBlockPriorityHandling BlockGrindPriority
      {
         get
         {
            return _BlockGrindPriority;
         }
      }

      private NanobotBuildAndRepairSystemComponentPriorityHandling _ComponentCollectPriority = new NanobotBuildAndRepairSystemComponentPriorityHandling();
      internal NanobotBuildAndRepairSystemComponentPriorityHandling ComponentCollectPriority
      {
         get
         {
            return _ComponentCollectPriority;
         }
      }

      public IMyShipWelder Welder { get { return _Welder; } }

      private SyncBlockState _State = new SyncBlockState();
      public SyncBlockState State { get { return _State; } }

      /// <summary>
      /// Currently friendly damaged blocks
      /// </summary>
      private Dictionary<IMySlimBlock, TimeSpan> _FriendlyDamage;
      public Dictionary<IMySlimBlock, TimeSpan> FriendlyDamage
      {
         get
         {
            return _FriendlyDamage != null ? _FriendlyDamage : _FriendlyDamage = new Dictionary<IMySlimBlock, TimeSpan>();
         }
      }


      /// <summary>
      /// Initialize logical component
      /// </summary>
      /// <param name="objectBuilder"></param>
      public override void Init(MyObjectBuilder_EntityBase objectBuilder)
      {
         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Initializing", Logging.BlockName(Entity, Logging.BlockNameOptions.None));


         base.Init(objectBuilder);
         NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

         if (Entity.GameLogic is MyCompositeGameLogicComponent)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock: Init Entiy.Logic remove other mods from this entity");
            Entity.GameLogic = this;
         }

         _Welder = Entity as IMyShipWelder;
         _Welder.AppendingCustomInfo += AppendingCustomInfo;

         _WorkingState = WorkingState.NotReady;

         if (Settings == null) //Force load of settings (is much faster here than initial load in UpdateBeforeSimulation10_100)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: Initializing Load-Settings failed", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
         };

         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Initialized", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
      }

      /// <summary>
      /// 
      /// </summary>
      public void SettingsChanged()
      {
         if (_Settings != null) _Settings.CheckLimits(this, false);

         var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
         if (resourceSink != null)
         {
            var electricPowerTransport = _Settings.MaximumRequiredElectricPowerTransport;
            if ((NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & SearchModes.BoundingBox) == 0) electricPowerTransport /= 10;
            resourceSink.SetMaxRequiredInputByType(ElectricityId, _Settings.MaximumRequiredElectricPowerWelding + electricPowerTransport + WELDER_REQUIRED_ELECTRIC_POWER_STANDBY);
            resourceSink.SetRequiredInputFuncByType(ElectricityId, ComputeRequiredElectricPower);
         }

         var maxMultiplier = Math.Max(NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier, NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier);
         if (maxMultiplier > 10) NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
         else NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;

         var multiplier = Math.Max(WELDER_TRANSPORTVOLUME_DIVISOR, maxMultiplier);
         multiplier = Math.Min(1, multiplier);
         _MaxTransportVolume = ((float)_TransportInventory.MaxVolume * multiplier) / WELDER_TRANSPORTVOLUME_DIVISOR;

         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init Inventory Volume {1}/{2} MaxTransportVolume={3} Mode={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), (float)_Welder.GetInventory(0).MaxVolume, _TransportInventory.MaxVolume, _MaxTransportVolume, Settings.SearchMode);
      }

      /// <summary>
      /// 
      /// </summary>
      private void Init()
      {
         if (_IsInit) return;
         if (_Welder.SlimBlock.IsProjected())
         {
            if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init Block is only projected -> exit", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
            NeedsUpdate = MyEntityUpdateEnum.NONE;
            return;
         }

         lock (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
         {
            if (!NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.ContainsKey(Entity.EntityId))
            {
               NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Add(Entity.EntityId, this);
            }
         }
         NanobotBuildAndRepairSystemMod.InitControls();

         var welderInventory = _Welder.GetInventory(0);
         if (welderInventory == null) return;
         _TransportInventory = new Sandbox.Game.MyInventory((float)welderInventory.MaxVolume / MyAPIGateway.Session.InventoryMultiplier, welderInventory.Size, MyInventoryFlags.CanSend); //MaxVolume handled in Pick functions

         SettingsChanged();

         var dummies = new Dictionary<string, IMyModelDummy>();
         _Welder.Model.GetDummies(dummies);
         foreach (var dummy in dummies)
         {
            if (dummy.Key.ToLower().Contains("detector_emitter"))
            {
               var matrix = dummy.Value.Matrix;
               _EmitterPosition = matrix.Translation;
               break;
            }
         }

         NanobotBuildAndRepairSystemMod.SyncBlockDataRequestSend(this);
         UpdateCustomInfo(true);
         _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime.Add(TimeSpan.FromSeconds(10));
         _TryAutoPushInventoryLast = _TryPushInventoryLast;
         _WorkingStateSet = WorkingState.Invalid;
         _SoundVolumeSet = -1;
         _IsInit = true;
         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Init -> done", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
      }

      /// <summary>
      /// 
      /// </summary>
      /// <returns></returns>
      private float ComputeRequiredElectricPower()
      {
         if (_Welder == null) return 0f;
         var required = 0f;
         if (_Welder.Enabled)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ComputeRequiredElectricPower Enabled", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
            required += WELDER_REQUIRED_ELECTRIC_POWER_STANDBY;
            required += State.Welding ? Settings.MaximumRequiredElectricPowerWelding : 0f;
            required += State.Grinding ? Settings.MaximumRequiredElectricPowerGrinding : 0f;
            required += State.Transporting ? (Settings.SearchMode == SearchModes.Grids ? Settings.MaximumRequiredElectricPowerTransport / 10 : Settings.MaximumRequiredElectricPowerTransport) : 0f;
         }
         if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ComputeRequiredElectricPower {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), required);
         return required;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="required"></param>
      /// <returns></returns>
      private bool HasRequiredElectricPower(bool weld, bool grind, bool transport)
      {
         if (_Welder == null) return false;
         var enought = true;
         var required = WELDER_REQUIRED_ELECTRIC_POWER_STANDBY;
         required += weld ? Settings.MaximumRequiredElectricPowerWelding : 0f;
         required += grind ? Settings.MaximumRequiredElectricPowerGrinding : 0f;
         required += transport ? (Settings.SearchMode == SearchModes.Grids ? Settings.MaximumRequiredElectricPowerTransport / 10 : Settings.MaximumRequiredElectricPowerTransport) : 0f;
         var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
         if (resourceSink != null)
         {
            enought = resourceSink.IsPowerAvailable(ElectricityId, required);
         }
         if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: HasRequiredElectricPower {1} / {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), enought, required);
         return enought || MyAPIGateway.Session.CreativeMode;
      }

      /// <summary>
      /// 
      /// </summary>
      public override void Close()
      {
         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Close", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
         if (_IsInit)
         {
            Settings.Save(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
            lock (NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
            {
               NanobotBuildAndRepairSystemMod.BuildAndRepairSystems.Remove(Entity.EntityId);
            }

            //Stop effects
            State.CurrentTransportTarget = null;
            State.Ready = false;
            UpdateEffects();
         }
         base.Close();
      }

      /// <summary>
      /// 
      /// </summary>
      public override void UpdateBeforeSimulation()
      {
         try
         {
            base.UpdateBeforeSimulation();

            if (_Welder == null || !_IsInit) return;

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
               if ((Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0)
               {
                  var colorWelder = _Welder.SlimBlock.GetColorMask().HSVtoColor();
                  var color = Color.FromNonPremultiplied(colorWelder.R, colorWelder.G, colorWelder.B, 255);
                  var areaBoundingBox = Settings.AreaBoundingBox;
                  var matrix = _Welder.WorldMatrix;
                  MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref areaBoundingBox, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, RangeGridResourceId, null, false);
               }

               //Debug draw target boxes
               //lock (_PossibleWeldTargets)
               //{
               //   var colorWelder = _Welder.SlimBlock.GetColorMask().HSVtoColor();
               //   var color = Color.FromNonPremultiplied(colorWelder.R, colorWelder.G, colorWelder.B, 255);

               //   foreach (var targetData in _PossibleWeldTargets)
               //   {
               //      BoundingBoxD box;
               //      Vector3 halfExtents;
               //      targetData.Block.ComputeScaledHalfExtents(out halfExtents);
               //      halfExtents *= 1.2f;
               //      var matrix = targetData.Block.CubeGrid.WorldMatrix;
               //      matrix.Translation = targetData.Block.CubeGrid.GridIntegerToWorld(targetData.Block.Position);

               //      box = new BoundingBoxD(-(halfExtents), (halfExtents));
               //      MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref box, ref color, MySimpleObjectRasterizer.Solid, 1, 0.04f, "HoneyComb", null, false);
               //   }
               //}

               _UpdateEffectsInterval = (_UpdateEffectsInterval++) % 2;
               if (_UpdateEffectsInterval == 0) UpdateEffects();
            }
         }
         catch (Exception ex)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public override void UpdateBeforeSimulation10()
      {
         base.UpdateBeforeSimulation10();
         UpdateBeforeSimulation10_100(true);
      }

      /// <summary>
      /// 
      /// </summary>
      public override void UpdateBeforeSimulation100()
      {
         base.UpdateBeforeSimulation100();
         UpdateBeforeSimulation10_100(false);
      }

      /// <summary>
      /// 
      /// </summary>
      public override void UpdatingStopped()
      {
         if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: UpdatingStopped", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
         if (_IsInit)
         {
            Settings.Save(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
         }
         //Stop sound effects
         StopSoundEffects();
         _WorkingStateSet = WorkingState.Invalid;
         base.UpdatingStopped();
      }

      private void UpdateBeforeSimulation10_100(bool fast)
      {
         try
         {
            if (_Welder == null) return;
            if (!_IsInit) Init();
            if (!_IsInit) return;

            if (_Delay > 0)
            {
               _Delay--;
               return;
            }

            _DelayWatch.Restart();
            if (MyAPIGateway.Session.IsServer)
            {
               if (!fast)
               {
                  CleanupFriendlyDamage();
               }
               ServerTryWeldingGrindingCollecting();
               if (!fast)
               {
                  if ((State.Ready != _PowerReady || State.Welding != _PowerWelding || State.Grinding != _PowerGrinding || State.Transporting != _PowerTransporting) &&
                      MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdatePowerSinkLast).TotalSeconds > 5)
                  {
                     _UpdatePowerSinkLast = MyAPIGateway.Session.ElapsedPlayTime;
                     _PowerReady = State.Ready;
                     _PowerWelding = State.Welding;
                     _PowerGrinding = State.Grinding;
                     _PowerTransporting = State.Transporting;

                     var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
                     if (resourceSink != null)
                     {
                        resourceSink.Update();
                     }
                  }

                  Settings.TrySave(Entity, NanobotBuildAndRepairSystemMod.ModGuid);
                  if (State.IsTransmitNeeded())
                  {
                     NanobotBuildAndRepairSystemMod.SyncBlockStateSend(0, this);
                  }
               }
            }
            else
            {
               if (State.Changed)
               {
                  UpdateCustomInfo(true);
                  State.ResetChanged();
               }
            }
            if (Settings.IsTransmitNeeded())
            {
               NanobotBuildAndRepairSystemMod.SyncBlockSettingsSend(0, this);
            }

            _DelayWatch.Stop();
            if (_DelayWatch.ElapsedMilliseconds > 40)
            {
               _Delay = _RandomDelay.Next(1, 20); //Slowdown a little bit
               if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: Delay {1} ({2}ms)", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Delay, _DelayWatch.ElapsedMilliseconds);
            }
         }
         catch (Exception ex)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Error)) Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: UpdateBeforeSimulation10/100 Exception:{1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
         }
      }

      /// <summary>
      /// Try to weld/grind/collect the possible targets
      /// </summary>
      private void ServerTryWeldingGrindingCollecting()
      {
         var inventoryFull = State.InventoryFull;
         var welding = false;
         var needwelding = false;
         var grinding = false;
         var needgrinding = false;
         var collecting = false;
         var needcollecting = false;
         var transporting = false;
         var ready = _Welder.Enabled && _Welder.IsWorking && _Welder.IsFunctional;
         IMySlimBlock currentWeldingBlock = null;
         IMySlimBlock currentGrindingBlock = null;
         if (ready)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWeldingGrindingCollecting Welder ready: Enabled={1}, IsWorking={2}, IsFunctional={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Welder.Enabled, _Welder.IsWorking, _Welder.IsFunctional);

            ServerTryPushInventory();
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            transporting = IsTransportRunnning(playTime);
            if (transporting && State.CurrentTransportIsPick) needgrinding = true;
            if ((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) == 0 && !transporting) ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
            if (!transporting)
            {
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWeldingGrindingCollecting TryWeldGrind: Mode {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Settings.WorkMode);
               State.MissingComponents.Clear();
               switch (Settings.WorkMode)
               {
                  case WorkModes.WeldBeforeGrind:
                     ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                     if (State.PossibleWeldTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                     {
                        ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                     }
                     break;
                  case WorkModes.GrindBeforeWeld:
                     ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                     if (State.PossibleGrindTargets.CurrentCount == 0 || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedWeldingBlock != null))
                     {
                        ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                     }
                     break;
                  case WorkModes.GrindIfWeldGetStuck:
                     ServerTryWelding(out welding, out needwelding, out transporting, out currentWeldingBlock);
                     if (!(welding || transporting) || (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && Settings.CurrentPickedGrindingBlock != null))
                     {
                        ServerTryGrinding(out grinding, out needgrinding, out transporting, out currentGrindingBlock);
                     }
                     break;
               }
               State.MissingComponents.RebuildHash();
            }
            if (((Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) && !transporting && !welding && !grinding) ServerTryCollectingFloatingTargets(out collecting, out needcollecting, out transporting);
         }
         else
         {
            State.MissingComponents.Clear();
            State.MissingComponents.RebuildHash();
            _SourcesAndTargetsUpdateRun = 0;
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: TryWelding Welder not ready: Enabled={1}, IsWorking={2}, IsFunctional={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Welder.Enabled || MyAPIGateway.Session.CreativeMode, _Welder.IsWorking, _Welder.IsFunctional);
         }

         if ((State.Welding && !welding) || (State.Grinding && !(grinding || collecting)))
         {
            StartAsyncUpdateSourcesAndTargets(false); //Scan immediately once for new targets
         }

         var readyChanged = State.Ready != ready;
         State.Ready = ready;
         State.Welding = welding;
         State.NeedWelding = needwelding;
         State.CurrentWeldingBlock = currentWeldingBlock;

         State.Grinding = grinding;
         State.NeedGrinding = needgrinding;
         State.CurrentGrindingBlock = currentGrindingBlock;

         State.Transporting = transporting;

         var inventoryFullChanged = State.InventoryFull != inventoryFull;

         var missingComponentsChanged = State.MissingComponents.LastHash != State.MissingComponents.CurrentHash;
         State.MissingComponents.LastHash = State.MissingComponents.CurrentHash;

         var possibleWeldTargetsChanged = State.PossibleWeldTargets.LastHash != State.PossibleWeldTargets.CurrentHash;
         State.PossibleWeldTargets.LastHash = State.PossibleWeldTargets.CurrentHash;

         var possibleGrindTargetsChanged = State.PossibleGrindTargets.LastHash != State.PossibleGrindTargets.CurrentHash;
         State.PossibleGrindTargets.LastHash = State.PossibleGrindTargets.CurrentHash;

         var possibleFloatingTargetsChanged = State.PossibleFloatingTargets.LastHash != State.PossibleFloatingTargets.CurrentHash;
         State.PossibleFloatingTargets.LastHash = State.PossibleFloatingTargets.CurrentHash;

         if (missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged) State.HasChanged();

         if (missingComponentsChanged && Mod.Log.ShouldLog(Logging.Level.Verbose))
         {
            lock (Mod.Log)
            {
               Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: TryWelding: MissingComponents --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
               Mod.Log.IncreaseIndent(Logging.Level.Verbose);
               foreach (var missing in State.MissingComponents)
               {
                  Mod.Log.Write(Logging.Level.Verbose, "{0}:{1}", missing.Key.SubtypeName, missing.Value);
               }
               Mod.Log.DecreaseIndent(Logging.Level.Verbose);
               Mod.Log.Write(Logging.Level.Verbose, "<--- MissingComponents");
            }
         }

         UpdateCustomInfo(missingComponentsChanged || possibleWeldTargetsChanged || possibleGrindTargetsChanged || possibleFloatingTargetsChanged || readyChanged || inventoryFullChanged);
      }

      /// <summary>
      /// Push ore/ingot out of the welder
      /// </summary>
      private void ServerTryPushInventory()
      {
            if ((Settings.Flags & (SyncBlockSettings.Settings.PushIngotOreImmediately | SyncBlockSettings.Settings.PushComponentImmediately) | SyncBlockSettings.Settings.PushItemsImmediately) == 0) return;
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryAutoPushInventoryLast).TotalSeconds <= 5) return;

            var welderInventory = _Welder.GetInventory(0);
            if (welderInventory != null)
            {
               if (welderInventory.Empty()) return;
               var lastPush = MyAPIGateway.Session.ElapsedPlayTime;

               var srcItems = welderInventory.GetItems();
               for (int srcItemIndex = srcItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
               {
                  var srcItem = srcItems[srcItemIndex];
                  if (srcItem.Content.TypeId == typeof(MyObjectBuilder_Ore) || srcItem.Content.TypeId == typeof(MyObjectBuilder_Ingot))
                  {
                     if ((Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerTryPushInventory TryPush IngotOre: Item={1} Amount={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), srcItem.ToString(), srcItem.Amount);
                        welderInventory.PushComponents(_PossibleSources, srcItemIndex, srcItem);
                        _TryAutoPushInventoryLast = lastPush;
                     }
                  }
                  else if (srcItem.Content.TypeId == typeof(MyObjectBuilder_Component))
                  {
                     if ((Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerTryPushInventory TryPush Component: Item={1} Amount={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), srcItem.ToString(), srcItem.Amount);
                        welderInventory.PushComponents(_PossibleSources, srcItemIndex, srcItem);
                        _TryAutoPushInventoryLast = lastPush;
                     }
                  }
                  else
                  {
                     //Any kind of items (Tools, Weapons, Ammo, Bottles, ..)
                     if ((Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerTryPushInventory TryPush Items: Item={1} Amount={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), srcItem.ToString(), srcItem.Amount);
                        welderInventory.PushComponents(_PossibleSources, srcItemIndex, srcItem);
                        _TryAutoPushInventoryLast = lastPush;
                     }
                  }
               }
            }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="collecting"></param>
      /// <param name="needcollecting"></param>
      /// <param name="transporting"></param>
      private void ServerTryCollectingFloatingTargets(out bool collecting, out bool needcollecting, out bool transporting)
      {
         collecting = false;
         needcollecting = false;
         transporting = false;
         if (!HasRequiredElectricPower(false, false, true)) return; //-> Not enought power
         lock (State.PossibleFloatingTargets)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryCollectingFloatingTargets PossibleFloatingTargets={1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.PossibleFloatingTargets.CurrentCount);
            TargetEntityData collectingFirstTarget = null;
            var collectingCount = 0;
            foreach (var targetData in State.PossibleFloatingTargets)
            {
               if (targetData.Entity != null && !targetData.Ignore)
               {
                  if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryCollectingFloatingTargets: {1} distance={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Entity), targetData.Distance);
                  needcollecting = true;
                  var added = ServerDoCollectFloating(targetData, out transporting, ref collectingFirstTarget);
                  collecting |= added;
                  if (added) collectingCount++;
                  if (transporting || collectingCount >= COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY)
                  {
                     break; //Max Inventorysize reached or max simultaneously floating object reached
                  }
               }
            }
            if (collecting && !transporting) ServerDoCollectFloating(null, out transporting, ref collectingFirstTarget); //Starttransport if pending
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void ServerTryGrinding(out bool grinding, out bool needgrinding, out bool transporting, out IMySlimBlock currentGrindingBlock)
      {

         grinding = false;
         needgrinding = false;
         transporting = false;
         currentGrindingBlock = null;
         if (!HasRequiredElectricPower(false, true, true)) return; //No power -> nothing to do

         lock (State.PossibleGrindTargets)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryGrinding PossibleGrindTargets={1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.PossibleGrindTargets.CurrentCount);
            
            foreach (var targetData in State.PossibleGrindTargets)
            {
               var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
               if (!cubeGrid.IsPowered && cubeGrid.IsStatic) cubeGrid.Physics.ClearSpeed(); 
            }
 
            foreach (var targetData in State.PossibleGrindTargets)
            {
               if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedGrindingBlock) continue;

               if (!targetData.Block.IsDestroyed)
               {
                  if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryGrinding: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block));
                  needgrinding = true;
                  grinding = ServerDoGrind(targetData, out transporting);
                  if (grinding)
                  {
                     currentGrindingBlock = targetData.Block;
                     break; //Only grind one block at once
                  }
               }
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void ServerTryWelding(out bool welding, out bool needwelding, out bool transporting, out IMySlimBlock currentWeldingBlock)
      {
         welding = false;
         needwelding = false;
         transporting = false;
         currentWeldingBlock = null;
         var power4WeldingAndTransporting = HasRequiredElectricPower(true, false, true);
         var power4Welding = power4WeldingAndTransporting ? true : HasRequiredElectricPower(true, false, false);
         var power4Transporting = power4WeldingAndTransporting ? true : HasRequiredElectricPower(false, false, true);

         if (!power4Welding && !power4Transporting) return; //No power -> nothing to do

         lock (State.PossibleWeldTargets)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWelding PossibleWeldTargets={1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.PossibleWeldTargets.CurrentCount);
            foreach (var targetData in State.PossibleWeldTargets)
            {
               if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) && targetData.Block != Settings.CurrentPickedWeldingBlock) continue;
               if (((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) || (!targetData.Ignore && Weldable(targetData)))
               {
                  needwelding = true;
                  if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerTryWelding: {1} HasDeformation={2} (MaxDeformation={3}), IsFullIntegrity={4}, HasFatBlock={5}, IsProjected={6}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), targetData.Block.HasDeformation, targetData.Block.MaxDeformation, targetData.Block.IsFullIntegrity, targetData.Block.FatBlock != null, targetData.Block.IsProjected());

                  if (power4Transporting && !transporting)
                  {
                     transporting = ServerFindMissingComponents(targetData);
                  }
                  if (power4Welding)
                  {
                     welding = ServerDoWeld(targetData);
                     if (welding)
                     {
                        currentWeldingBlock = targetData.Block;
                        break; //Only weld one block at once (do not split over all blocks as the base shipwelder does)
                     }
                  }
                  else
                  {
                     if (transporting) break; //Tranport running and no power for welding nothing more to do
                  }
               }
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="targetData"></param>
      /// <returns></returns>
      private bool Weldable(TargetBlockData targetData)
      {
         var target = targetData.Block;
         if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
         {
            if (target.CanBuild(true)) return true;
            //Is the block already created (maybe by user or an other BaR block) ->
            //After creation we can't welding this projected block, we have to find the 'physical' block instead.
            var cubeGridProjected = target.CubeGrid as MyCubeGrid;
            if (cubeGridProjected != null && cubeGridProjected.Projector != null)
            {
               var cubeGrid = cubeGridProjected.Projector.CubeGrid;
               Vector3I blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
               target = cubeGrid.GetCubeBlock(blockPos);
               if (target != null)
               {
                  targetData.Block = target;
                  targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                  return Weldable(targetData);
               }
            }
            targetData.Ignore = true;
            return false;
         }

         var weld = target.NeedRepair() && !IsFriendlyDamage(target);
         targetData.Ignore = !weld;
         return weld;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="playTime"></param>
      /// <returns></returns>
      private bool IsTransportRunnning(TimeSpan playTime)
      {
         if (State.LastTransportStartTime > TimeSpan.Zero)
         {
            //Transport started
            if (State.CurrentTransportIsPick) {
               if (!ServerEmptyTranportInventory(true))
               {
                  if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: IsTransportRunnning transport still running transport inventory not emtpy",
                     Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                  return true;
               }
            }

            if (playTime.Subtract(State.LastTransportStartTime) < State.TransportTime)
            {
               //Last transport still running -> wait
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: IsTransportRunnning: transport still running remaining transporttime={1}",
                  Logging.BlockName(_Welder, Logging.BlockNameOptions.None), State.TransportTime.Subtract(MyAPIGateway.Session.ElapsedPlayTime.Subtract(State.LastTransportStartTime)));
               return true;
            }
            State.LastTransportStartTime = TimeSpan.Zero;
            State.CurrentTransportTarget = null;
         } else State.CurrentTransportTarget = null;
         return false;
      }

      /// <summary>
      /// 
      /// </summary>
      private void UpdateCustomInfo(bool changed)
      {
         _UpdateCustomInfoNeeded |= changed;
         if (_UpdateCustomInfoNeeded && (!MyAPIGateway.Session.IsServer || MyAPIGateway.Session.ElapsedPlayTime.Subtract(_UpdateCustomInfoLast).TotalSeconds >= 2))
         {
            _UpdateCustomInfoLast = MyAPIGateway.Session.ElapsedPlayTime;
            _Welder.RefreshCustomInfo();
            TriggerTerminalRefresh();
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public void TriggerTerminalRefresh()
      {
         //Workaround as long as RaisePropertiesChanged is not public
         if (_Welder != null)
         {
            var action = _Welder.GetActionWithName("helpOthers");
            if (action != null)
            {
               action.Apply(_Welder);
               action.Apply(_Welder);
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private bool ServerDoWeld(TargetBlockData targetData)
      {
            var welderInventory = _Welder.GetInventory(0);
            var welding = false;
            var created = false;
            var target = targetData.Block;
            var hasIgnoreColor = ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) && IsColorNearlyEquals(Settings.IgnoreColor, target.GetColorMask());

            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {

                  //New Block (Projected)
                  var cubeGridProjected = target.CubeGrid as MyCubeGrid;
                  var blockDefinition = target.BlockDefinition as MyCubeBlockDefinition;
                  var item = _TransportInventory.FindItem(blockDefinition.Components[0].Definition.Id);
                  if ((MyAPIGateway.Session.CreativeMode || (item != null && item.Amount >= 1)) && cubeGridProjected != null && cubeGridProjected.Projector != null)
                  {
                     ((Sandbox.ModAPI.IMyProjector)cubeGridProjected.Projector).Build(target, _Welder.OwnerId, _Welder.EntityId, true);
                     if (!MyAPIGateway.Session.CreativeMode) _TransportInventory.RemoveItems(item.ItemId, 1);
                     if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (new): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));

                     //After creation we can't welding this projected block, we have to find the 'physical' block instead.
                     var cubeGrid = cubeGridProjected.Projector.CubeGrid;
                     Vector3I blockPos = cubeGrid.WorldToGridInteger(cubeGridProjected.GridIntegerToWorld(target.Position));
                     target = cubeGrid.GetCubeBlock(blockPos);
                     if (target != null) targetData.Block = target;
                     targetData.Attributes &= ~TargetBlockData.AttributeFlags.Projected;
                     created = true;
                     return true;
                  }
            }

            if (!hasIgnoreColor && target != null && (targetData.Attributes & TargetBlockData.AttributeFlags.Projected) == 0)
            {
               //No ignore color and allready created
               if (!target.IsFullIntegrity || created)
               {
                  //Incomplete
                  welding = target.CanContinueBuild(_TransportInventory) || MyAPIGateway.Session.CreativeMode;
                  //If we could weld or welder is getting full move collected items to stockpile.
                  //Otherwise keep them in welder, maybe we could use them for a block that could be immediately welded
                  target.MoveItemsToConstructionStockpile(_TransportInventory);
                  if (welding)
                  {
                     if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (incomplete): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
                     //target.MoveUnneededItemsFromConstructionStockpile(welderInventory); not available in modding api
                     target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
                  }
                  ServerEmptyTranportInventory(false);
               }
               else
               {
                  //Deformation
                  if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoWeld (deformed): {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
                  welding = true;
                  target.IncreaseMountLevel(MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * WELDER_AMOUNT_PER_SECOND, _Welder.OwnerId, welderInventory, MyAPIGateway.Session.WelderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.WeldingMultiplier * WELDER_MAX_REPAIR_BONE_MOVEMENT_SPEED, _Welder.HelpOthers);
               }
            }
            return welding || created;
      }

      /// <summary>
      /// 
      /// </summary>
      private bool ServerDoGrind(TargetBlockData targetData, out bool transporting)
      {
         var target = targetData.Block;
         var playTime = MyAPIGateway.Session.ElapsedPlayTime;
         transporting = IsTransportRunnning(playTime);
         if (transporting) return false;

         var welderInventory = _Welder.GetInventory(0);
         var targetGrid = target.CubeGrid;

         if (targetGrid.Physics == null || !targetGrid.Physics.Enabled) return false;

         var criticalIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).CriticalIntegrityRatio;
         var ownershipIntegrityRatio = ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)target.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
         var integrityRatio = target.Integrity / target.MaxIntegrity;

         if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
         {
            if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && target.FatBlock != null && integrityRatio < criticalIntegrityRatio)
            {
               //Block allready out of order -> stop grinding and switch to next
               return false;
            }
            if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && target.FatBlock != null && integrityRatio < ownershipIntegrityRatio)
            {
               //Block allready hacked -> stop grinding and switch to next
               return false;
            }
         }

         var disassembleRatio = target.FatBlock != null ? target.FatBlock.DisassembleRatio : ((MyCubeBlockDefinition)target.BlockDefinition).DisassembleRatio;
         var integrityPointsPerSec = ((MyCubeBlockDefinition)target.BlockDefinition).IntegrityPointsPerSec;

         float damage = MyAPIGateway.Session.GrinderSpeedMultiplier * NanobotBuildAndRepairSystemMod.Settings.Welder.GrindingMultiplier * GRINDER_AMOUNT_PER_SECOND;
         var grinderAmount = damage * integrityPointsPerSec / disassembleRatio;
         integrityRatio = (target.Integrity - grinderAmount) / target.MaxIntegrity;

         if ((targetData.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
         {
            if ((Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 && integrityRatio < criticalIntegrityRatio)
            {
               //Grind only down to critical ratio not further
               grinderAmount = target.Integrity - (0.9f * criticalIntegrityRatio * target.MaxIntegrity);
               damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
               integrityRatio = criticalIntegrityRatio;
            }
            else if ((Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 && integrityRatio < ownershipIntegrityRatio)
            {
               //Grind only down to ownership ratio not further
               grinderAmount = target.Integrity - (0.9f * ownershipIntegrityRatio * target.MaxIntegrity);
               damage = grinderAmount * disassembleRatio / integrityPointsPerSec;
               integrityRatio = ownershipIntegrityRatio;
            }
         }

         var emptying = false;
         if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} integrityRatio={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), integrityRatio);
         if (integrityRatio <= 0.2)
         {
            //Try to emtpy inventory (if any)
            if (target.FatBlock != null && target.FatBlock.HasInventory)
            {
               bool isEmpty;
               emptying = EmptyBlockInventories(target.FatBlock, _TransportInventory, out isEmpty);
               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} Try empty Inventory running={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), emptying);
            }
         }

         if (!emptying)
         {
            MyDamageInformation damageInfo = new MyDamageInformation(false, damage, MyDamageType.Grind, _Welder.EntityId);

            if (target.UseDamageSystem)
            {
               //Not available in modding
               //MyAPIGateway.Session.DamageSystem.RaiseBeforeDamageApplied(target, ref damageInfo);

               foreach (var entry in NanobotBuildAndRepairSystemMod.BuildAndRepairSystems)
               {
                  var relation = entry.Value.Welder.GetUserRelationToOwner(_Welder.OwnerId);
                  if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                  {
                     //A 'friendly' damage from grinder -> do not repair (for a while)
                     //I don't check block relation here, because if it is enemy we won't repair it in any case and it just times out
                     entry.Value.FriendlyDamage[target] = MyAPIGateway.Session.ElapsedPlayTime + NanobotBuildAndRepairSystemMod.Settings.FriendlyDamageTimeout;
                     if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock: Damaged Add FriendlyDamage {0} Timeout {1}", Logging.BlockName(target), entry.Value.FriendlyDamage[target]);
                  }
               }
            }

            target.DecreaseMountLevel(damageInfo.Amount, _TransportInventory);
            target.MoveItemsFromConstructionStockpile(_TransportInventory);

            if (target.UseDamageSystem)
            {
               //Not available in modding
               //MyAPIGateway.Session.DamageSystem.RaiseAfterDamageApplied(target, ref damageInfo);
            }
            if (target.IsFullyDismounted)
            {
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoGrind {1} FullyDismounted", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target));
               if (target.UseDamageSystem)
               {
                  //Not available in modding
                  //MyAPIGateway.Session.DamageSystem.RaiseDestroyed(target, damageInfo);
               }

               target.SpawnConstructionStockpile();
               target.CubeGrid.RazeBlock(target.Position);
            }
         }

         if ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || target.IsFullyDismounted)
         {
            //Transport started
            State.CurrentTransportIsPick = true;
            State.CurrentTransportTarget = ComputePosition(target);
            State.LastTransportStartTime = playTime;
            State.TransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoGrind: Target {1} transport started transporttime={2} CurrentVolume={3}/{4}",
               Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), State.TransportTime, _TransportInventory.CurrentVolume, _MaxTransportVolume);
            ServerEmptyTranportInventory(true);
            transporting = true;
         }

         return true;
      }

      /// <summary>
      /// 
      /// </summary>
      private bool ServerDoCollectFloating(TargetEntityData targetData, out bool transporting, ref TargetEntityData collectingFirstTarget)
      {
         transporting = false;
         var collecting = false;
         var canAdd = false;
         var isEmpty = true;

         var playTime = MyAPIGateway.Session.ElapsedPlayTime;
         transporting = IsTransportRunnning(playTime);
         if (transporting) return false;
         if (targetData != null)
         {
            var target = targetData.Entity;
            var floating = target as MyFloatingObject;
            var floatingFirstTarget = collectingFirstTarget != null ? collectingFirstTarget.Entity as MyFloatingObject : null;

            canAdd = collectingFirstTarget == null || (floatingFirstTarget != null && floating != null);
            if (canAdd)
            {
               var welderInventory = _Welder.GetInventory(0);

               if (floating != null) collecting = EmptyFloatingObject(floating, _TransportInventory, out isEmpty);
               else
               {
                  collecting = EmptyBlockInventories(target, _TransportInventory, out isEmpty);
                  if (isEmpty) {
                     var character = target as IMyCharacter;
                     if (character != null && character.IsBot)
                     {
                        //Wolf, Spider, ...
                        target.Delete();
                     }
                  }
               }

               if (collecting && collectingFirstTarget == null) collectingFirstTarget = targetData;

               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerDoCollectFloating {1} Try pick floating running={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(target), collecting);

               targetData.Ignore = isEmpty;
            }
         }
         if (collectingFirstTarget != null && ((float)_TransportInventory.CurrentVolume >= _MaxTransportVolume || (!canAdd && _TransportInventory.CurrentVolume > 0)))
         {
            //Transport started
            State.CurrentTransportIsPick = true;
            State.CurrentTransportTarget = ComputePosition(collectingFirstTarget.Entity);
            State.LastTransportStartTime = playTime;
            State.TransportTime = TimeSpan.FromSeconds(2d * collectingFirstTarget.Distance / Settings.TransportSpeed);
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerDoCollectFloating: Target {1} transport started transporttime={2} CurrentVolume={3}/{4}",
               Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(collectingFirstTarget.Entity), State.TransportTime, _TransportInventory.CurrentVolume, _MaxTransportVolume);
            ServerEmptyTranportInventory(true);
            transporting = true;
            collectingFirstTarget = null;
         }

         return collecting;
      }

      /// <summary>
      /// Try to find an the missing components and moves them into welder inventory
      /// </summary>
      private bool ServerFindMissingComponents(TargetBlockData targetData)
      {
         try
         {
            var playTime = MyAPIGateway.Session.ElapsedPlayTime;
            if (IsTransportRunnning(playTime)) return true;

            var remainingVolume = _MaxTransportVolume;
            _TempMissingComponents.Clear();
            var cubeGrid = targetData.Block.CubeGrid as MyCubeGrid;
            if ((targetData.Attributes & TargetBlockData.AttributeFlags.Projected) != 0)
            {
               var blockDefinition = targetData.Block.BlockDefinition as MyCubeBlockDefinition;
               if (blockDefinition.Components == null || blockDefinition.Components.Length == 0) return false;
               var component = blockDefinition.Components[0];
               _TempMissingComponents.Add(component.Definition.Id.SubtypeName, 1);

               var picked = ServerFindMissingComponents(targetData, ref remainingVolume);
               if (picked)
               {
                  if (((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) == 0) || !IsColorNearlyEquals(Settings.IgnoreColor, targetData.Block.GetColorMask()))
                  {
                     //Block could be created and should be welded -> so retrieve the remaining material also
                     if (component.Count > 1) _TempMissingComponents[component.Definition.Id.SubtypeName] = component.Count - 1;
                     for (var idx = 1; idx < blockDefinition.Components.Length; idx++)
                     {
                        component = blockDefinition.Components[idx];
                        if (_TempMissingComponents.ContainsKey(component.Definition.Id.SubtypeName)) _TempMissingComponents[component.Definition.Id.SubtypeName] += component.Count;
                        else _TempMissingComponents.Add(component.Definition.Id.SubtypeName, component.Count);
                     }
                  }
               }
            }
            else
            {
               targetData.Block.GetMissingComponents(_TempMissingComponents);
            }

            
            if (_TempMissingComponents.Count > 0)
            {
               ServerFindMissingComponents(targetData, ref remainingVolume);
            }

            if (remainingVolume < _MaxTransportVolume || (MyAPIGateway.Session.CreativeMode && _TempMissingComponents.Count > 0))
            {
               //Transport startet
               State.CurrentTransportIsPick = false;
               State.CurrentTransportTarget = ComputePosition(targetData.Block);
               State.LastTransportStartTime = playTime;
               State.TransportTime = TimeSpan.FromSeconds(2d * targetData.Distance / Settings.TransportSpeed);
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: FindMissingComponents: Target {1} transport started volume={2} (max {3}) transporttime={4}",
                  Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), _MaxTransportVolume - remainingVolume, _MaxTransportVolume, State.TransportTime);
               return true;
            }
            return false;
         }
         finally
         {
            _TempMissingComponents.Clear();
         }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="targetData"></param>
      /// <returns></returns>
      private bool ServerFindMissingComponents(TargetBlockData targetData, ref float remainingVolume)
      {
         var picked = false;
         foreach (var component in _TempMissingComponents)
         {
            var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), component.Key);
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: FindMissingComponents: Target {1} missing {2}={3} remainingVolume={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(targetData.Block), componentId, component.Value, remainingVolume);
            int neededAmount = 0;
            int amount;

            int allreadyMissingAmount;
            if (!State.MissingComponents.TryGetValue(componentId, out allreadyMissingAmount))
            {
               var group = MyDefinitionManager.Static.GetGroupForComponent(componentId, out amount);
               if (group == null)
               {
                  MyComponentSubstitutionDefinition substitutions;
                  if (MyDefinitionManager.Static.TryGetComponentSubstitutionDefinition(componentId, out substitutions))
                  {
                     foreach (var providingComponent in substitutions.ProvidingComponents)
                     {
                        var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(providingComponent.Key);
                        neededAmount = component.Value / providingComponent.Value;
                        picked = ServerPickFromWelder(providingComponent.Key, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                        if (neededAmount > 0 && remainingVolume > 0) picked = PullComponents(providingComponent.Key, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                     }
                  }
                  else
                  {
                     var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                     neededAmount = component.Value;
                     picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                     if (neededAmount > 0 && remainingVolume > 0) picked = PullComponents(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                  }
               }
               else
               {
                  var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(componentId);
                  neededAmount = component.Value;
                  picked = ServerPickFromWelder(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
                  if (neededAmount > 0 && remainingVolume > 0) picked = PullComponents(componentId, definition.Volume, ref neededAmount, ref remainingVolume) || picked;
               }
            } else
            {
               neededAmount = component.Value;
            }

            if (neededAmount > 0 && remainingVolume > 0) AddToMissingComponents(componentId, neededAmount);
            if (remainingVolume <= 0) break;
         }
         return picked;
      }

      /// <summary>
      /// Try to pick needed material from own inventory, if successfull material is moved into transport inventory
      /// </summary>
      private bool ServerPickFromWelder(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
      {
         if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: PickFromWelder Try: {1}={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount);

         var welderInventory = _Welder.GetInventory(0);
         if (welderInventory == null || welderInventory.Empty())
         {
            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: PickFromWelder welder empty: {1}={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount);
            return false;
         }

         var picked = false;
         var srcItems = welderInventory.GetItems();
         for (int i1 = srcItems.Count - 1; i1 >= 0; i1--)
         {
            var srcItem = srcItems[i1];
            if (srcItem != null && srcItem.Content.TypeId == componentId.TypeId && srcItem.Content.SubtypeId == componentId.SubtypeId && srcItem.Amount > 0)
            {
               var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Floor(remainingVolume / volume));
               var pickedAmount = MyFixedPoint.Min(maxpossibleAmount, srcItem.Amount);
               if (pickedAmount > 0)
               {
                  welderInventory.RemoveItems(srcItem.ItemId, pickedAmount);
                  _TransportInventory.AddItems(pickedAmount, (MyObjectBuilder_PhysicalObject)srcItem.Content);

                  neededAmount -= (int)pickedAmount;
                  remainingVolume -= (float)pickedAmount * volume;

                  picked = true;
               }
               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: PickFromWelder: {1}: missingAmount={2} pickedAmount={3} maxpossibleAmount={4} remainingVolume={5} transportVolumeTotal={6}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount, pickedAmount, maxpossibleAmount, remainingVolume, _TransportInventory.CurrentVolume);
            }
            if (neededAmount <= 0 || remainingVolume <= 0) break;
         }
         return picked;
      }

      /// <summary>
      /// Check if the transport inventory is empty after delivering/grinding/collecting, if not move items back to welder inventory
      /// </summary>
      private bool ServerEmptyTranportInventory(bool push)
      {
         var empty = _TransportInventory.Empty();
         if (!empty)
         {
            if (!MyAPIGateway.Session.CreativeMode)
            {
               var welderInventory = _Welder.GetInventory(0);
               if (welderInventory != null)
               {
                  if (push && !welderInventory.Empty()) {
                     if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: ServerEmptyTranportInventory: push={1}: MaxVolume={2} CurrentVolume={3} Timeout={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), push, welderInventory.MaxVolume, welderInventory.CurrentVolume, MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds);
                     if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_TryPushInventoryLast).TotalSeconds > 5 && welderInventory.MaxVolume - welderInventory.CurrentVolume < _TransportInventory.CurrentVolume * 1.5f)
                     {
                        if (!welderInventory.PushComponents(_PossibleSources))
                        {
                           //Failed retry after timeout
                           _TryPushInventoryLast = MyAPIGateway.Session.ElapsedPlayTime;
                        }
                     }
                  }

                  var items = _TransportInventory.GetItems();
                  foreach (var item in items)
                  {
                     var id = item.Content.GetId();
                     //Try to move as much as possible
                     var moveableAmount = welderInventory.MaxItemsAddable(item.Amount, id);
                     if (moveableAmount > 0)
                     {
                        welderInventory.AddItems(moveableAmount, (MyObjectBuilder_PhysicalObject)item.Content);
                        _TransportInventory.RemoveItems(item.ItemId, moveableAmount, spawn: false);
                        item.Amount -= moveableAmount; //Is not updated
                     }
                     if (moveableAmount > 0 && Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerEmptyTranportInventory move to welder Item {1} amount={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), id, moveableAmount);
                     if (item.Amount > 0 && Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: ServerEmptyTranportInventory (no more room in welder) Item {1} amount={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), item.Content.GetId(), item.Amount);
                  }
               }
            } else
            {
               _TransportInventory.Clear();
            }
            empty = _TransportInventory.Empty();
         }
         State.InventoryFull = !empty;
         return empty;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="block"></param>
      /// <returns></returns>
      private bool EmptyBlockInventories(IMyEntity entity, IMyInventory dstInventory, out bool isEmpty)
      {
         var running = false;
         var remainingVolume = _MaxTransportVolume - (float)dstInventory.CurrentVolume;
         if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyBlockInventories remainingVolume={1} Entity={2}, InventoryCount={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), remainingVolume, Logging.BlockName(entity, Logging.BlockNameOptions.None), entity.InventoryCount);

         isEmpty = true;
         for (int i1 = 0; i1 < entity.InventoryCount; i1++)
         {
            var srcInventory = entity.GetInventory(i1) as IMyInventory;
            if (srcInventory.Empty()) continue;
            isEmpty = false;
            if (remainingVolume <= 0) return true; //No more transport volume

            var srcItems = srcInventory.GetItems();

            for (int i2 = 0; i2 < srcItems.Count; i2++)
            {
               var srcItem = srcItems[i2];
               if (srcItem == null) continue;
               var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(srcItem.Content.GetId());
               var startAmount = srcItem.Amount;

               var maxpossibleAmount = (MyFixedPoint)Math.Min((float)srcItem.Amount, Math.Ceiling(remainingVolume / definition.Volume));
               var mass = srcInventory.CurrentMass;
               srcInventory.RemoveItems(srcItem.ItemId, maxpossibleAmount, spawn:false);
               if (mass > srcInventory.CurrentMass)
               {
                  _TransportInventory.AddItems(maxpossibleAmount, (MyObjectBuilder_PhysicalObject)srcItem.Content);
                  remainingVolume -= (float)maxpossibleAmount * definition.Volume;
                  if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyBlockInventories Removed Item {1} amount={2} remainingVolume={3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), srcItem.Content.GetId(), maxpossibleAmount, remainingVolume);
                  running = true;
                  if (remainingVolume <= 0) return true; //No more transport volume
               }
               else return running; //No more space
            }
         }
         return running;
      }

      /// <summary>
      /// 
      /// </summary>
      private bool EmptyFloatingObject(MyFloatingObject floating, IMyInventory dstInventory, out bool isEmpty)
      {
         var running = false;
         isEmpty = floating.WasRemovedFromWorld || floating.MarkedForClose;
         if (!isEmpty)
         {
            var remainingVolume = _MaxTransportVolume - (double)dstInventory.CurrentVolume;

            var definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(floating.Item.Content.GetId());
            var startAmount = floating.Item.Amount;

            var maxremainAmount = (MyFixedPoint)(remainingVolume / definition.Volume);
            var maxpossibleAmount = maxremainAmount > floating.Item.Amount ? floating.Item.Amount : maxremainAmount; //Do not use MyFixedPoint.Min !Wrong Implementation could cause overflow!
            if (definition.HasIntegralAmounts) maxpossibleAmount = MyFixedPoint.Floor(maxpossibleAmount);
            if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyFloatingObject remainingVolume={1}, Item={2}, ItemAmount={3}, MaxPossibleAmount={4}, ItemVolume={5})", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), remainingVolume, floating.Item.Content.GetId(), floating.Item.Amount, maxpossibleAmount, definition.Volume);
            if (maxpossibleAmount > 0)
            {
               if (maxpossibleAmount >= floating.Item.Amount)
               {
                  MyFloatingObjects.RemoveFloatingObject(floating);
                  isEmpty = true;
               }
               else
               {
                  floating.Item.Amount = floating.Item.Amount - maxpossibleAmount;
                  floating.RefreshDisplayName();
               }

               _TransportInventory.AddItems(maxpossibleAmount, floating.Item.Content);
               remainingVolume -= (float)maxpossibleAmount * definition.Volume;
               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: EmptyFloatingObject Removed Item {1} amount={2} remainingVolume={3} remainingItemAmount={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), floating.Item.Content.GetId(), maxpossibleAmount, remainingVolume, floating.Item.Amount);
               running = true;
            }
         }
         return running;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="componentId"></param>
      /// <param name="neededAmount"></param>
      private void AddToMissingComponents(MyDefinitionId componentId, int neededAmount)
      {
         int missingAmount;
         if (State.MissingComponents.TryGetValue(componentId, out missingAmount))
         {
            State.MissingComponents[componentId] = missingAmount + neededAmount;
         }
         else
         {
            State.MissingComponents.Add(componentId, neededAmount);
         }
      }

      /// <summary>
      /// Pull components into welder
      /// </summary>
      private bool PullComponents(MyDefinitionId componentId, float volume, ref int neededAmount, ref float remainingVolume)
      {
         int availAmount = 0;
         var welderInventory = _Welder.GetInventory(0);
         var maxpossibleAmount = Math.Min(neededAmount, (int)Math.Ceiling(remainingVolume / volume));
         if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents start: {1}={2} maxpossibleAmount={3} volume={4}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, neededAmount, maxpossibleAmount, volume);
         if (maxpossibleAmount <= 0) return false;
         var picked = false;
         lock (_PossibleSources)
         {
            foreach (var srcInventory in _PossibleSources)
            {
               if (srcInventory.FindItem(componentId) != null) //Pre Test is 10 timers faster then get the whole list (as copy!) and iterate for nothing
               {
                  var srcItems = srcInventory.GetItems();
                  for (int i1 = 0; i1 < srcItems.Count; i1++)
                  {
                     var srcItem = srcItems[i1];
                     if (srcItem != null && srcItem.Content.TypeId == componentId.TypeId && srcItem.Content.SubtypeId == componentId.SubtypeId && srcItem.Amount > 0)
                     {
                        if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Found: {1}={2} in {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, srcItem.Amount, Logging.BlockName(srcInventory));
                        var amountPossible = Math.Min(maxpossibleAmount, (int)srcItem.Amount);
                        if (amountPossible > 0)
                        {
                           var amountMoveable = (int)welderInventory.MaxItemsAddable(amountPossible, componentId);
                           if (amountMoveable > 0)
                           {
                              if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Try to move: {1}={2} from {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, amountMoveable, Logging.BlockName(srcInventory));
                              var moved = false;
                              moved = srcInventory.TransferItemTo(welderInventory, i1, null, true, amountMoveable);
                              if (moved)
                              {
                                 maxpossibleAmount -= amountMoveable;
                                 availAmount += amountMoveable;
                                 if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents Moved: {1}={2} from {3}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId, amountMoveable, Logging.BlockName(srcInventory));
                                 picked = ServerPickFromWelder(componentId, volume, ref neededAmount, ref remainingVolume) || picked;
                              }
                           }
                           else
                           {
                              //No (more) space in welder
                              if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: PullComponents no more space in welder: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), componentId);
                              neededAmount -= availAmount;
                              remainingVolume -= availAmount * volume;
                              return picked;
                           }
                        }
                     }
                     if (maxpossibleAmount <= 0) return picked;
                  }
               }
               if (maxpossibleAmount <= 0) return picked;
            }
         }

         return picked;
      }

      /// <summary>
      /// 
      /// </summary>
      public void UpdateSourcesAndTargetsTimer()
      {
         if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(_LastSourcesAndTargetsUpdate) > NanobotBuildAndRepairSystemMod.Settings.SourcesAndTargetsUpdateInterval) {
            StartAsyncUpdateSourcesAndTargets((_SourcesAndTargetsUpdateRun % 6) == 0);
            _SourcesAndTargetsUpdateRun++;
         }
      }

      /// <summary>
      /// Parse all the connected blocks and find the possible targets and sources of components
      /// </summary>
      private void StartAsyncUpdateSourcesAndTargets(bool updateSource)
      {
         if (!_Welder.UseConveyorSystem)
         {
            lock (_PossibleSources)
            {
               _PossibleSources.Clear();
            }
         }

         if (!_Welder.Enabled || !_Welder.IsFunctional || State.Ready == false)
         {
            if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Enabled={1} IsFunctional={2} ---> not ready don't search for targets", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), _Welder.Enabled, _Welder.IsFunctional);
            lock (State.PossibleWeldTargets)
            {
               State.PossibleWeldTargets.Clear();
               State.PossibleWeldTargets.RebuildHash();
            }
            lock (State.PossibleGrindTargets)
            {
               State.PossibleGrindTargets.Clear();
               State.PossibleGrindTargets.RebuildHash();
            }
            lock (State.PossibleFloatingTargets)
            {
               State.PossibleFloatingTargets.Clear();
               State.PossibleFloatingTargets.RebuildHash();
            }
            _AsyncUpdateSourcesAndTargetsRunning = false;
            return;
         };

         lock (_Welder)
         {
            if (_AsyncUpdateSourcesAndTargetsRunning) return;
            _AsyncUpdateSourcesAndTargetsRunning = true;
            NanobotBuildAndRepairSystemMod.AddAsyncAction(() => AsyncUpdateSourcesAndTargets(updateSource));
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public void AsyncUpdateSourcesAndTargets(bool updateSource)
      {
         if (!State.Ready) return;
         int pos = 0;
         try
         {
            try
            {
               pos = 1;

               var grids = new List<IMyCubeGrid>();
               _TempPossibleWeldTargets.Clear();
               _TempPossibleGrindTargets.Clear();
               _TempPossibleFloatingTargets.Clear();
               _TempPossibleSources.Clear();

               var ignoreColor = Settings.IgnoreColor;
               var grindColor = Settings.GrindColor;
               var worldMatrix = _Welder.WorldMatrix;
               var areaBox = new MyOrientedBoundingBoxD(Settings.AreaBoundingBox, worldMatrix);

               if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Search: IgnoreColor={1}, GrindColor={2}, UseGrindJanitorOn={3}, Settings.WorkMode={4}, GrindJanitorOptions={5}",
                  Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ignoreColor, grindColor, Settings.UseGrindJanitorOn, Settings.WorkMode, Settings.GrindJanitorOptions);

               AsyncAddBlocksOfGrid(ref areaBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, _Welder.CubeGrid, grids, updateSource ? _TempPossibleSources : null, BlockWeldPriority.AnyEnabled ? _TempPossibleWeldTargets : null, BlockGrindPriority.AnyEnabled ? _TempPossibleGrindTargets : null);
               switch (Settings.SearchMode)
               {
                  case SearchModes.Grids:
                     break;
                  case SearchModes.BoundingBox:
                     AsyncAddBlocksOfBox(ref areaBox, ((Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0), ref ignoreColor, ((Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0), ref grindColor, Settings.UseGrindJanitorOn, Settings.GrindJanitorOptions, grids, BlockWeldPriority.AnyEnabled ? _TempPossibleWeldTargets : null, BlockGrindPriority.AnyEnabled ? _TempPossibleGrindTargets : null, _ComponentCollectPriority.AnyEnabled ? _TempPossibleFloatingTargets : null);
                     break;
               }

               pos = 2;
               if (updateSource)
               {
                  Vector3D posWelder;
                  _Welder.SlimBlock.ComputeWorldCenter(out posWelder);
                  _TempPossibleSources.Sort((a, b) =>
                  {
                     var blockA = a.Owner as IMyCubeBlock;
                     var blockB = b.Owner as IMyCubeBlock;
                     if (blockA != null && blockB != null)
                     {
                        var welderA = blockA as IMyShipWelder;
                        var welderB = blockB as IMyShipWelder;
                        if ((welderA == null) == (welderB == null))
                        {
                           Vector3D posA;
                           Vector3D posB;
                           blockA.SlimBlock.ComputeWorldCenter(out posA);
                           blockB.SlimBlock.ComputeWorldCenter(out posB);
                           var distanceA = (int)Math.Abs((posWelder - posA).Length());
                           var distanceB = (int)Math.Abs((posWelder - posA).Length());
                           return distanceA - distanceB;
                        } else if (welderA == null)
                        {
                           return -1;
                        } else
                        {
                           return 1;
                        }
                     }
                     else if (blockA != null) return -1;
                     else if (blockB != null) return 1;
                     else return 0;
                  });
               }

               pos = 3;
               _TempPossibleWeldTargets.Sort((a, b) =>
               {
                  var priorityA = BlockWeldPriority.GetPriority(a.Block);
                  var priorityB = BlockWeldPriority.GetPriority(b.Block);
                  if (priorityA == priorityB)
                  {
                     return (int)(a.Distance - b.Distance);
                  }
                  else return priorityA - priorityB;
               });

               pos = 4;
               _TempPossibleGrindTargets.Sort((a, b) =>
               {
                  if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) == (b.Attributes & TargetBlockData.AttributeFlags.Autogrind))
                  {
                     if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0)
                     {
                        var priorityA = BlockGrindPriority.GetPriority(a.Block);
                        var priorityB = BlockGrindPriority.GetPriority(b.Block);
                        if (priorityA == priorityB)
                        {
                           if (((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0))
                           {
                              var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                              return res != 0 ? res : (int)(a.Distance - b.Distance);
                           }
                           if (((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0)) return (int)(a.Distance - b.Distance);
                           return (int)(b.Distance - a.Distance);
                        }
                        else return priorityA - priorityB;
                     }

                     if (((Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0))
                     {
                        var res = ((MyCubeGrid)a.Block.CubeGrid).BlocksCount - ((MyCubeGrid)b.Block.CubeGrid).BlocksCount;
                        return res != 0 ? res : (int)(a.Distance - b.Distance);
                     }
                     if (((Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0)) return (int)(a.Distance - b.Distance);
                     return (int)(b.Distance - a.Distance);
                  }
                  else if ((a.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return -1;
                  else if ((b.Attributes & TargetBlockData.AttributeFlags.Autogrind) != 0) return 1;
                  return 0;
               });

               _TempPossibleFloatingTargets.Sort((a, b) =>
               {
                  var itemA = a.Entity;
                  var itemB = b.Entity;
                  var itemAFloating = itemA as MyFloatingObject;
                  var itemBFloating = itemB as MyFloatingObject;
                  if (itemAFloating != null && itemBFloating != null)
                  {
                     var priorityA = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                     var priorityB = ComponentCollectPriority.GetPriority(itemAFloating.Item.Content.GetObjectId());
                     if (priorityA == priorityB)
                     {
                        return (int)(a.Distance - b.Distance);
                     }
                     else return priorityA - priorityB;
                  }
                  else if (itemAFloating == null) return -1;
                  else if (itemBFloating == null) return  1;
                  return (int)(a.Distance - b.Distance);
               });

               pos = 5;
               if (Mod.Log.ShouldLog(Logging.Level.Verbose))
               {
                  lock (Mod.Log)
                  {
                     Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Build Target Blocks --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                     Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                     foreach (var blockData in _TempPossibleWeldTargets)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "Block: {0} ({1})", Logging.BlockName(blockData.Block), blockData.Distance);
                     }
                     Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                     Mod.Log.Write(Logging.Level.Verbose, "<---");

                     Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Grind Target Blocks --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                     Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                     foreach (var blockData in _TempPossibleGrindTargets)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "Block: {0} ({1})", Logging.BlockName(blockData.Block), blockData.Distance);
                     }
                     Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                     Mod.Log.Write(Logging.Level.Verbose, "<---");

                     Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Floating Targets --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                     Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                     foreach (var floatingData in _TempPossibleFloatingTargets)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "Floating: {0} ({1})", Logging.BlockName(floatingData.Entity), floatingData.Distance);
                     }
                     Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                     Mod.Log.Write(Logging.Level.Verbose, "<---");

                     if (updateSource)
                     {
                        Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets Possible Source Blocks --->", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
                        Mod.Log.IncreaseIndent(Logging.Level.Verbose);
                        foreach (var inventory in _TempPossibleSources)
                        {
                           Mod.Log.Write(Logging.Level.Verbose, "Inventory: {0}", Logging.BlockName(inventory));
                        }
                        Mod.Log.DecreaseIndent(Logging.Level.Verbose);
                        Mod.Log.Write(Logging.Level.Verbose, "<---");
                     }
                  }
               }

               pos = 6;
               lock (State.PossibleWeldTargets)
               {
                  State.PossibleWeldTargets.Clear();
                  State.PossibleWeldTargets.AddRange(_TempPossibleWeldTargets);
                  State.PossibleWeldTargets.RebuildHash();
               }
               _TempPossibleWeldTargets.Clear();
               pos = 7;
               lock (State.PossibleGrindTargets)
               {
                  State.PossibleGrindTargets.Clear();
                  State.PossibleGrindTargets.AddRange(_TempPossibleGrindTargets);
                  State.PossibleGrindTargets.RebuildHash();
               }
               _TempPossibleGrindTargets.Clear();
               pos = 8;
               lock (State.PossibleFloatingTargets)
               {
                  State.PossibleFloatingTargets.Clear();
                  State.PossibleFloatingTargets.AddRange(_TempPossibleFloatingTargets);
                  State.PossibleFloatingTargets.RebuildHash();
               }
               _TempPossibleFloatingTargets.Clear();

               pos = 9;
               if (updateSource)
               {
                  lock (_PossibleSources)
                  {
                     _PossibleSources.Clear();
                     _PossibleSources.AddRange(_TempPossibleSources);
                  }
                  _TempPossibleSources.Clear();
               }

               _ContinuouslyError = 0;
            }
            catch (Exception ex)
            {
               _ContinuouslyError++;
               if (_ContinuouslyError > 10 || Mod.Log.ShouldLog(Logging.Level.Info) || Mod.Log.ShouldLog(Logging.Level.Verbose))
               {
                  Mod.Log.Error("BuildAndRepairSystemBlock {0}: AsyncUpdateSourcesAndTargets exception at {1}: {2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), pos, ex);
                  _ContinuouslyError = 0;
               }
            }
         }
         finally
         {
            _LastSourcesAndTargetsUpdate = MyAPIGateway.Session.ElapsedPlayTime; 
            _AsyncUpdateSourcesAndTargetsRunning = false;
         }
      }

      /// <summary>
      /// Search for grids inside bounding box and add their damaged block also
      /// </summary>
      private void AsyncAddBlocksOfBox(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref Vector3 ignoreColor, bool useGrindColor, ref Vector3 grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, List<IMyCubeGrid> grids, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets, List<TargetEntityData> possibleFloatingTargets)
      {
         if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncAddBlockOfBox", Logging.BlockName(_Welder, Logging.BlockNameOptions.None));
         var areaBoundingBox = Settings.AreaBoundingBox.TransformSlow(_Welder.WorldMatrix);
         List<IMyEntity> entityInRange = null;
         lock (MyAPIGateway.Entities)
         {
            //API not thread save !!!
            entityInRange = MyAPIGateway.Entities.GetElementsInBox(ref areaBoundingBox);
            //The list contains grid, Fatblocks and Damaged blocks in range. But as I would like to use the searchfunction also for grinding,
            //I only could use the grids and have to traverse through the grids to get all slimblocks.
         }
         if (entityInRange != null)
         {
            foreach (var entity in entityInRange)
            {
               var grid = entity as IMyCubeGrid;
               if (grid != null)
               {
                  AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, grid, grids, null, possibleWeldTargets, possibleGrindTargets);
                  continue;
               }

               if (possibleFloatingTargets != null)
               {
                  var floating = entity as MyFloatingObject;
                  if (floating != null)
                  {
                     if (!floating.MarkedForClose && ComponentCollectPriority.GetEnabled(floating.Item.Content.GetObjectId()))
                     {
                        var distance = (areaBox.Center - floating.WorldMatrix.Translation).Length();
                        possibleFloatingTargets.Add(new TargetEntityData(floating, distance));
                     }
                     continue;
                  }

                  var character = entity as IMyCharacter;
                  if (character != null)
                  {
                     if (character.IsDead && !character.InventoriesEmpty())
                     {
                        var distance = (areaBox.Center - character.WorldMatrix.Translation).Length();
                        possibleFloatingTargets.Add(new TargetEntityData(character, distance));
                     }
                     continue;
                  }

                  var inventoryBag = entity as IMyInventoryBag;
                  if (inventoryBag != null)
                  {
                     if (!inventoryBag.InventoriesEmpty())
                     {
                        var distance = (areaBox.Center - inventoryBag.WorldMatrix.Translation).Length();
                        possibleFloatingTargets.Add(new TargetEntityData(inventoryBag, distance));
                     }
                     continue;
                  }
               }
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void AsyncAddBlocksOfGrid(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref Vector3 ignoreColor, bool useGrindColor, ref Vector3 grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMyCubeGrid cubeGrid, List<IMyCubeGrid> grids, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
      {
         if (!State.Ready) return; //Block not ready
         if (grids.Contains(cubeGrid)) return; //Allready parsed

         if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: AsyncAddBlocksOfGrid AddGrid {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), cubeGrid.DisplayName);
         grids.Add(cubeGrid);

         var newBlocks = new List<IMySlimBlock>();
         cubeGrid.GetBlocks(newBlocks);

         foreach (var slimBlock in newBlocks)
         {
            AsyncAddBlockIfTargetOrSource(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, slimBlock, possibleSources, possibleWeldTargets, possibleGrindTargets);

            var fatBlock = slimBlock.FatBlock;
            if (fatBlock == null) continue;

            var mechanicalConnectionBlock = fatBlock as Sandbox.ModAPI.IMyMechanicalConnectionBlock;
            if (mechanicalConnectionBlock != null)
            {
               if (mechanicalConnectionBlock.TopGrid != null)
                  AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, mechanicalConnectionBlock.TopGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
               continue;
            }

            var attachableTopBlock = fatBlock as Sandbox.ModAPI.IMyAttachableTopBlock;
            if (attachableTopBlock != null)
            {
               if (attachableTopBlock.Base != null && attachableTopBlock.Base.CubeGrid != null)
                  AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, attachableTopBlock.Base.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
               continue;
            }

            var connector = fatBlock as Sandbox.ModAPI.IMyShipConnector;
            if (connector != null)
            {
               if (connector.Status == MyShipConnectorStatus.Connected && connector.OtherConnector != null)
               {
                  AsyncAddBlocksOfGrid(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, connector.OtherConnector.CubeGrid, grids, possibleSources, possibleWeldTargets, possibleGrindTargets);
               }
               continue;
            }

            if (possibleWeldTargets != null && ((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0)) //If projected blocks should be build
            {
               var projector = fatBlock as Sandbox.ModAPI.IMyProjector;
               if (projector != null)
               {
                  if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Projector={1} IsProjecting={2} BuildableBlockCount={3} IsRelationAllowed={4} Relation={5}/{6}/{7}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), projector.IsProjecting, projector.BuildableBlocksCount, IsRelationAllowed4Welding(slimBlock), slimBlock.GetUserRelationToOwner(_Welder.OwnerId), projector.GetUserRelationToOwner(_Welder.OwnerId), slimBlock.CubeGrid.GetUserRelationToOwner(_Welder.OwnerId));
                  if (projector.IsProjecting && projector.BuildableBlocksCount > 0 && IsRelationAllowed4Welding(slimBlock))
                  {
                     //Add buildable blocks
                     var projectedCubeGrid = projector.ProjectedGrid;
                     if (projectedCubeGrid != null && !grids.Contains(projectedCubeGrid))
                     {
                        grids.Add(projectedCubeGrid);
                        var projectedBlocks = new List<IMySlimBlock>();
                        projectedCubeGrid.GetBlocks(projectedBlocks);

                        foreach (IMySlimBlock block in projectedBlocks)
                        {
                           double distance;
                           if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Projector={1} Block={2} BlockKindEnabled={3}, InRange={4}, CanBuild={5}/{6}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), Logging.BlockName(block), BlockWeldPriority.GetEnabled(block), block.IsInRange(ref areaBox, out distance), block.CanBuild(false), block.Dithering);
                           if (BlockWeldPriority.GetEnabled(block) && block.IsInRange(ref areaBox, out distance) && block.CanBuild(false) )
                           {
                              if (Mod.Log.ShouldLog(Logging.Level.Verbose)) Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Add projected Block {1}:{2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(projector), Logging.BlockName(block));
                              possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
                           }
                        }
                     }
                  }
                  continue;
               }
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void AsyncAddBlockIfTargetOrSource(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref Vector3 ignoreColor, bool useGrindColor, ref Vector3 grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<IMyInventory> possibleSources, List<TargetBlockData> possibleWeldTargets, List<TargetBlockData> possibleGrindTargets)
      {
         try
         {
            if (_Welder.UseConveyorSystem && possibleSources != null)
            {
               //Search for sources of components (Container, Assembler, Welder, Grinder, ?)
               var terminalBlock = block.FatBlock as IMyTerminalBlock;
               if (terminalBlock != null && terminalBlock.EntityId != _Welder.EntityId && terminalBlock.IsFunctional) //Own inventor is no external source (handled internaly)
               {
                  var relation = terminalBlock.GetUserRelationToOwner(_Welder.OwnerId);
                  if (MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(relation))
                  {
                     try
                     {
                        var welderInventory = _Welder.GetInventory(0);
                        var maxInv = terminalBlock.InventoryCount;
                        for (var idx = 0; idx < maxInv; idx++)
                        {
                           var inventory = terminalBlock.GetInventory(idx);
                           if (!possibleSources.Contains(inventory) && inventory.IsConnectedTo(welderInventory))
                           {
                              possibleSources.Add(inventory);
                           }
                        }
                     }
                     catch (Exception ex)
                     {
                        Mod.Log.Write(Logging.Level.Event, "BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource1 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
                     }
                  }
               };
            }

            var added = false;
            if (possibleGrindTargets != null && (useGrindColor || autoGrindRelation != 0))
            {
               added = AsyncAddBlockIfGrindTarget(ref areaBox, useGrindColor, ref grindColor, autoGrindRelation, autoGrindOptions, block, possibleGrindTargets);
            }

            if (possibleWeldTargets != null && !added) //Do not weld if in grind list (could happen if auto grind neutrals is enabled and "HelpOthers" is active)
            {
               AsyncAddBlockIfWeldTarget(ref areaBox, useIgnoreColor, ref ignoreColor, useGrindColor, ref grindColor, block, possibleWeldTargets);
            }
         }
         catch (Exception ex)
         {
            Mod.Log.Error("BuildAndRepairSystemBlock {0}: AsyncAddBlockIfTargetOrSource2 exception: {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), ex);
            throw;
         }
      }

      /// <summary>
      /// Check if the given slim block is a weld target (in range, owned, damaged, new, ..)
      /// </summary>
      private bool AsyncAddBlockIfWeldTarget(ref MyOrientedBoundingBoxD areaBox, bool useIgnoreColor, ref Vector3 ignoreColor, bool useGrindColor, ref Vector3 grindColor, IMySlimBlock block, List<TargetBlockData> possibleWeldTargets)
      {
         if (Mod.Log.ShouldLog(Logging.Level.Verbose))
            Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Weld Check Block {1} IsProjected={2} IsDestroyed={3}, IsFullyDismounted={4}, HasFatBlock={5}, FatBlockClosed={6}, MaxDeformation={7}, (HasDeformation={8}), IsFullIntegrity={9}, NeedRepair={10}, Relation={11}",
            Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block),
            block.IsProjected(),
            block.IsDestroyed, block.IsFullyDismounted, block.FatBlock != null, block.FatBlock != null ? block.FatBlock.Closed.ToString() : "-",
            block.MaxDeformation, block.HasDeformation, block.IsFullIntegrity, block.NeedRepair(), block.GetUserRelationToOwner(_Welder.OwnerId));

         double distance;
         var colorMask = block.GetColorMask();
         Sandbox.ModAPI.IMyProjector projector;
         if (block.IsProjected(out projector))
         {
            if (((Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) &&
               (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
               BlockWeldPriority.GetEnabled(block) &&
               block.IsInRange(ref areaBox, out distance) &&
               IsRelationAllowed4Welding(projector.SlimBlock) &&
               block.CanBuild(false))
            {
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add projected Block {1}, HasFatBlock={2}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.FatBlock != null);
               possibleWeldTargets.Add(new TargetBlockData(block, distance, TargetBlockData.AttributeFlags.Projected));
               return true;
            }
         }
         else
         {
            if ((!useIgnoreColor || !IsColorNearlyEquals(ignoreColor, colorMask)) && (!useGrindColor || !IsColorNearlyEquals(grindColor, colorMask)) &&
               BlockWeldPriority.GetEnabled(block) &&
               block.IsInRange(ref areaBox, out distance) &&
               IsRelationAllowed4Welding(block) &&
               block.NeedRepair())
            {
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add damaged Block {1} MaxDeformation={2}, (HasDeformation={3}), IsFullIntegrity={4}, HasFatBlock={5}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.MaxDeformation, block.HasDeformation, block.IsFullIntegrity, block.FatBlock != null);
               possibleWeldTargets.Add(new TargetBlockData(block, distance, 0));
               return true;
            }
         }
         
         return false;
      }

      /// <summary>
      /// Check if the given slim block is a grind target (in range, color )
      /// </summary>
      private bool AsyncAddBlockIfGrindTarget(ref MyOrientedBoundingBoxD areaBox, bool useGrindColor, ref Vector3 grindColor, AutoGrindRelation autoGrindRelation, AutoGrindOptions autoGrindOptions, IMySlimBlock block, List<TargetBlockData> possibleGrindTargets)
      {
         //block.CubeGrid.BlocksDestructionEnabled is not available for modding, so at least check if general destruction is enabled
         if ((MyAPIGateway.Session.SessionSettings.Scenario || MyAPIGateway.Session.SessionSettings.ScenarioEditMode) && !MyAPIGateway.Session.SessionSettings.DestructibleBlocks) return false;

         //block.CubeGrid.Editable is not available for modding -> wait until it might be availabel
         //if (!block.CubeGrid.Editable) return;
         if (Mod.Log.ShouldLog(Logging.Level.Verbose))
         {
            Mod.Log.Write(Logging.Level.Verbose, "BuildAndRepairSystemBlock {0}: Grind Check Block {1} Color={2} Projected={3} AutoGrindRelation={4} Relation={5} UseGrindColor={6} HasGrindColor={7} ({8},{9})",
            Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block), block.GetColorMask(), block.IsProjected(), autoGrindRelation, block.GetUserRelationToOwner(_Welder.OwnerId), useGrindColor, IsColorNearlyEquals(grindColor, block.GetColorMask()), grindColor, block.GetColorMask());
         }

         if (block.IsProjected()) return false;

         var autoGrind = autoGrindRelation != 0 && BlockGrindPriority.GetEnabled(block);
         if (autoGrind)
         {
            var relation = block.GetUserRelationToOwner(_Welder.OwnerId);
            autoGrind =
               (relation == MyRelationsBetweenPlayerAndBlock.NoOwnership && ((autoGrindRelation & AutoGrindRelation.NoOwnership) != 0)) ||
               (relation == MyRelationsBetweenPlayerAndBlock.Enemies && ((autoGrindRelation & AutoGrindRelation.Enemies) != 0)) ||
               (relation == MyRelationsBetweenPlayerAndBlock.Neutral && ((autoGrindRelation & AutoGrindRelation.Neutral) != 0));
         }

         if (autoGrind && ((autoGrindOptions & (AutoGrindOptions.DisableOnly | AutoGrindOptions.HackOnly)) != 0)) {
            var criticalIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).CriticalIntegrityRatio;
            var ownershipIntegrityRatio = ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio > 0 ? ((MyCubeBlockDefinition)block.BlockDefinition).OwnershipIntegrityRatio : criticalIntegrityRatio;
            var integrityRation = block.Integrity / block.MaxIntegrity;
            if (autoGrind && ((autoGrindOptions & AutoGrindOptions.DisableOnly) != 0))
            {
               autoGrind = block.FatBlock != null && integrityRation > criticalIntegrityRatio;
            }
            if (autoGrind && ((autoGrindOptions & AutoGrindOptions.HackOnly) != 0))
            {
               autoGrind = block.FatBlock != null && integrityRation > ownershipIntegrityRatio;
            }
         }

         if (autoGrind || (useGrindColor && IsColorNearlyEquals(grindColor, block.GetColorMask())))
         {
            double distance;
            if (block.IsInRange(ref areaBox, out distance))
            {
               if (Mod.Log.ShouldLog(Logging.Level.Info)) Mod.Log.Write(Logging.Level.Info, "BuildAndRepairSystemBlock {0}: Add grind Block {1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), Logging.BlockName(block));
               possibleGrindTargets.Add(new TargetBlockData(block, distance, autoGrind ? TargetBlockData.AttributeFlags.Autogrind : 0));
               return true;
            }
         }
         return false;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="block"></param>
      /// <returns></returns>
      private bool IsRelationAllowed4Welding(IMySlimBlock block) {
         var relation = _Welder.OwnerId == 0 ? MyRelationsBetweenPlayerAndBlock.NoOwnership : block.GetUserRelationToOwner(_Welder.OwnerId);
         if (relation == MyRelationsBetweenPlayerAndBlock.Enemies) return false;
         if (!_Welder.HelpOthers && (relation == MyRelationsBetweenPlayerAndBlock.Neutral || relation == MyRelationsBetweenPlayerAndBlock.NoOwnership)) return false;
         return true;
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="block"></param>
      /// <returns></returns>
      private static bool IsColorNearlyEquals(Vector3 colorA, Vector3 colorB)
      {
         return Math.Round(colorA.X, 3, MidpointRounding.AwayFromZero) == Math.Round(colorB.X, 3, MidpointRounding.AwayFromZero) &&
                Math.Round(colorA.Y, 3, MidpointRounding.AwayFromZero) == Math.Round(colorB.Y, 3, MidpointRounding.AwayFromZero) &&
                Math.Round(colorA.Z, 3, MidpointRounding.AwayFromZero) == Math.Round(colorB.Z, 3, MidpointRounding.AwayFromZero);
      }

      /// <summary>
      /// Update custom info of the block
      /// </summary>
      /// <param name="block"></param>
      /// <param name="details"></param>
      private void AppendingCustomInfo(IMyTerminalBlock terminalBlock, StringBuilder details)
      {
         details.Clear();

         details.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_Type")));
         details.Append("BuildAndRepairSystem");
         details.Append(Environment.NewLine);

         var resourceSink = _Welder.Components.Get<Sandbox.Game.EntityComponents.MyResourceSinkComponent>();
         if (resourceSink != null)
         {
            details.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_MaxRequiredInput")));
            MyValueFormatter.AppendWorkInBestUnit(resourceSink.MaxRequiredInputByType(ElectricityId), details);
            details.Append(Environment.NewLine);
            details.Append(MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_RequiredInput")));
            MyValueFormatter.AppendWorkInBestUnit(resourceSink.RequiredInputByType(ElectricityId), details);
            details.Append(Environment.NewLine);
         }
         details.Append(Environment.NewLine);

         if ((_Welder.Enabled || MyAPIGateway.Session.CreativeMode) && _Welder.IsWorking && _Welder.IsFunctional)
         {
            if ((Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0)
            {
               details.Append("Picked Welding Block:" + Environment.NewLine);
               details.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedWeldingBlock.BlockName()));
               details.Append("Picked Grinding Block:" + Environment.NewLine);
               details.Append(string.Format(" -{0}" + Environment.NewLine, Settings.CurrentPickedGrindingBlock.BlockName()));
            }

            if (State.InventoryFull) details.Append("Block inventory is full!" + Environment.NewLine);

            var cnt = 0;
            details.Append("Missing Items:" + Environment.NewLine);
            lock (State.MissingComponents)
            { 
               foreach (var component in State.MissingComponents)
               {
                  details.Append(string.Format(" -{0}: {1}" + Environment.NewLine, component.Key.SubtypeName, component.Value));
                  cnt++;
                  if (cnt >= SyncBlockState.MaxSyncItems)
                  {
                     details.Append(" -.." + Environment.NewLine);
                     break;
                  }
               }
            }
            details.Append(Environment.NewLine);

            cnt = 0;
            details.Append("Blocks to build:" + Environment.NewLine);
            lock (State.PossibleWeldTargets)
            {
               foreach (var blockData in State.PossibleWeldTargets)
               {
                  details.Append(string.Format(" -{0}" + Environment.NewLine,  blockData.Block.BlockName()));
                  cnt++;
                  if (cnt >= SyncBlockState.MaxSyncItems)
                  {
                     details.Append(" -.." + Environment.NewLine);
                     break;
                  }
               }
            }
            details.Append(Environment.NewLine);

            cnt = 0;
            details.Append("Blocks to dismantle:" + Environment.NewLine);
            lock (State.PossibleGrindTargets)
            {
               foreach (var blockData in State.PossibleGrindTargets)
               {
                  details.Append(string.Format(" -{0}" + Environment.NewLine, blockData.Block.BlockName()));
                  cnt++;
                  if (cnt >= SyncBlockState.MaxSyncItems)
                  {
                     details.Append(" -.." + Environment.NewLine);
                     break;
                  }
               }
            }
            details.Append(Environment.NewLine);

            cnt = 0;
            details.Append("Floatings to collect:" + Environment.NewLine);
            lock (State.PossibleFloatingTargets)
            {
               foreach (var entityData in State.PossibleFloatingTargets)
               {
                  details.Append(string.Format(" -{0}" + Environment.NewLine, Logging.BlockName(entityData.Entity)));
                  cnt++;
                  if (cnt >= SyncBlockState.MaxSyncItems)
                  {
                     details.Append(" -.." + Environment.NewLine);
                     break;
                  }
               }
            }
         }
         else
         {
            details.Append("Block is not ready" + Environment.NewLine);
         }
      }

      /// <summary>
      /// Check if block currently has been damaged by friendly(grinder)
      /// </summary>
      public bool IsFriendlyDamage(IMySlimBlock slimBlock)
      {
         return FriendlyDamage.ContainsKey(slimBlock);
      }

      /// <summary>
      /// Clear timedout friendly damaged blocks
      /// </summary>
      private void CleanupFriendlyDamage()
      {
         var playTime = MyAPIGateway.Session.ElapsedPlayTime;
         if (playTime.Subtract(_LastFriendlyDamageCleanup) > NanobotBuildAndRepairSystemMod.Settings.FriendlyDamageCleanup)
         {
            //Cleanup
            var timedout = new List<IMySlimBlock>();
            foreach (var entry in FriendlyDamage)
            {
               if (entry.Value < playTime) timedout.Add(entry.Key);
            }
            for (var idx = timedout.Count - 1; idx >= 0; idx--)
            {
               FriendlyDamage.Remove(timedout[idx]);
            }
            _LastFriendlyDamageCleanup = playTime;
         }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <returns></returns>
      private WorkingState GetWorkingState()
      {
         if (!State.Ready) return WorkingState.NotReady;
         else if (State.Welding) return WorkingState.Welding;
         else if (State.NeedWelding)
         {
            if (State.MissingComponents.Count > 0) return WorkingState.MissingComponents;
            return WorkingState.NeedWelding;
         }
         else if (State.Grinding) return WorkingState.Grinding;
         else if (State.NeedGrinding)
         {
            if (State.InventoryFull) return WorkingState.InventoryFull;
            return WorkingState.NeedGrinding;
         }
         return WorkingState.Idle;
      }

      /// <summary>
      /// Set actual state and position of visual effects
      /// </summary>
      private void UpdateEffects()
      {
         var transportState = State.Ready && Settings.SearchMode == SearchModes.BoundingBox && State.CurrentTransportTarget != null && !State.InventoryFull && State.CurrentTransportTarget.HasValue;
         if (transportState != _TransportStateSet)
         {
            SetTransportEffects(transportState);
            _TransportStateSet = transportState;
         }
         else
         {
            UpdateTransportEffectPosition();
         }

         //Welding/Grinding state
         var workingState = GetWorkingState();
         if (workingState != _WorkingStateSet || Settings.SoundVolume != _SoundVolumeSet)
         {
            SetWorkingEffects(workingState);
            _WorkingStateSet = workingState;
            _SoundVolumeSet = Settings.SoundVolume;
         }
         else
         {
            UpdateWorkingEffectPosition(workingState);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private void StopSoundEffects()
      {
         if (_SoundEmitter != null)
         {
            _SoundEmitter.StopSound(false);
         }

         if (_SoundEmitterWorking != null)
         {
            _SoundEmitterWorking.StopSound(false);
            _SoundEmitterWorking.SetPosition(null); //Reset
         }
      }

      /// <summary>
      /// Start visual effects for welding/grinding
      /// </summary>
      private void SetWorkingEffects(WorkingState workingState)
      {
         if (_ParticleEffectWorking1 != null)
         {
            Interlocked.Decrement(ref _ActiveWorkingEffects);
            _ParticleEffectWorking1.Stop();
            _ParticleEffectWorking1 = null;
         }

         if (_LightEffect != null)
         {
            MyLights.RemoveLight(_LightEffect);
            _LightEffect = null;
         }

         switch (workingState) {
            case WorkingState.Welding:
            case WorkingState.Grinding:
               if (_ActiveWorkingEffects < MaxWorkingEffects)
               {
                  Interlocked.Increment(ref _ActiveWorkingEffects);

                  MyParticlesManager.TryCreateParticleEffect(workingState == WorkingState.Welding ? PARTICLE_EFFECT_WELDING1 : PARTICLE_EFFECT_GRINDING1, ref MatrixD.Identity, ref Vector3D.Zero, 0xFFFFFFFF, out _ParticleEffectWorking1);
                  if (_ParticleEffectWorking1 != null) _ParticleEffectWorking1.UserRadiusMultiplier = workingState == WorkingState.Welding ? 4f : 0.5f;

                  if (workingState == WorkingState.Welding && _LightEffectFlareWelding == null)
                  {
                     MyDefinitionId myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipWelder");
                     _LightEffectFlareWelding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                  }
                  else if (workingState == WorkingState.Grinding && _LightEffectFlareGrinding == null)
                  {
                     MyDefinitionId myDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_FlareDefinition), "ShipGrinder");
                     _LightEffectFlareGrinding = MyDefinitionManager.Static.GetDefinition(myDefinitionId) as MyFlareDefinition;
                  }

                  var flare = workingState == WorkingState.Welding ? _LightEffectFlareWelding : _LightEffectFlareGrinding;

                  if (flare != null)
                  {
                     _LightEffect = MyLights.AddLight();
                     _LightEffect.Start(Vector3.Zero, new Vector4(0.7f, 0.85f, 1f, 1f), 5f, string.Concat(_Welder.DisplayNameText, " EffectLight"));
                     _LightEffect.Falloff = 2f;
                     _LightEffect.LightOn = true;
                     _LightEffect.GlareOn = true;
                     _LightEffect.GlareQuerySize = 0.8f;
                     _LightEffect.PointLightOffset = 0.1f;
                     _LightEffect.GlareType = VRageRender.Lights.MyGlareTypeEnum.Normal;
                     _LightEffect.SubGlares = flare.SubGlares;
                     _LightEffect.Intensity = flare.Intensity;
                     _LightEffect.GlareSize = flare.Size;
                  }
               }
               _Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", workingState == WorkingState.Welding ? Color.Yellow : Color.Blue, 1.0f);
               break;
            case WorkingState.MissingComponents:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
               break;
            case WorkingState.InventoryFull:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Red, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
               break;
            case WorkingState.NeedWelding:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Yellow, 1.0f);
               break;
            case WorkingState.NeedGrinding:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Blue, 1.0f);
               break;
            case WorkingState.Idle:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Green, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
               break;
            case WorkingState.NotReady:
               _Welder.SetEmissiveParts("EmissiveReady", Color.Black, 1.0f);
               _Welder.SetEmissiveParts("EmissiveWorking", Color.Black, 1.0f);
               break;
         }

         var sound = _Sounds[(int)workingState];
         if (sound != null)
         {
            if (_SoundEmitter == null)
            {
               _SoundEmitter = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)_Welder);
               _SoundEmitter.CustomMaxDistance = 30f;
               _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
            }
            if (_SoundEmitterWorking == null)
            {
               _SoundEmitterWorking = new MyEntity3DSoundEmitter((VRage.Game.Entity.MyEntity)_Welder);
               _SoundEmitterWorking.CustomMaxDistance = 30f;
               _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
            }

            if (_SoundEmitter != null)
            {
               _SoundEmitter.StopSound(true);
               _SoundEmitter.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
               _SoundEmitter.PlaySingleSound(sound, true);
            }

            if (_SoundEmitterWorking != null)
            {
               _SoundEmitterWorking.StopSound(true);
               _SoundEmitterWorking.CustomVolume = _SoundLevels[(int)workingState] * Settings.SoundVolume;
               _SoundEmitterWorking.SetPosition(null); //Reset
               //_SoundEmitterWorking.PlaySingleSound(sound, true); done after position is set
            }
         }
         else
         {
            if (_SoundEmitter != null)
            {
               _SoundEmitter.StopSound(false);
            }

            if (_SoundEmitterWorking != null)
            {
               _SoundEmitterWorking.StopSound(false);
               _SoundEmitterWorking.SetPosition(null); //Reset
            }
         }
         UpdateWorkingEffectPosition(workingState);
      }

      /// <summary>
      /// Set the position of the visual effects
      /// </summary>
      private void UpdateWorkingEffectPosition(WorkingState workingState)
      {
         if (_ParticleEffectWorking1 == null) return;

         Vector3D position;
         MatrixD matrix;
         if (State.CurrentWeldingBlock != null)
         {
            BoundingBoxD box;
            State.CurrentWeldingBlock.GetWorldBoundingBox(out box, false);
            matrix = box.Matrix;
            position = matrix.Translation;
         }
         else if (State.CurrentGrindingBlock != null)
         {
            BoundingBoxD box;
            State.CurrentGrindingBlock.GetWorldBoundingBox(out box, false);
            matrix = box.Matrix;
            position = matrix.Translation;
         }
         else
         {
            matrix = _Welder.WorldMatrix;
            position = matrix.Translation;
         }

         if (_LightEffect != null)
         {
            _LightEffect.Position = position;
            _LightEffect.Intensity = MyUtils.GetRandomFloat(0.1f, 0.6f);
            _LightEffect.UpdateLight();
         }

         if (_ParticleEffectWorking1 != null)
         {
            _ParticleEffectWorking1.WorldMatrix = matrix;
         }

         if (_SoundEmitterWorking != null)
         {
            var sound = _Sounds[(int)workingState];
            if (sound == null)
            {
               _SoundEmitterWorking.StopSound(false);
               _SoundEmitterWorking.SetPosition(null); //Reset
            }
            else if (_SoundEmitterWorking.SourcePosition != position || !_SoundEmitterWorking.IsPlaying || _SoundEmitterWorking.SoundPair != sound)
            {
               _SoundEmitterWorking.SetPosition(position);
               _SoundEmitterWorking.PlaySingleSound(sound, true);
            }
         }
      }

      /// <summary>
      /// Start visual effects for transport
      /// </summary>
      private void SetTransportEffects(bool active)
      {
         if (active)
         {
//Remove
Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: transportState={1}", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), active);
            if (_ParticleEffectTransport1 != null)
            {
               Interlocked.Decrement(ref _ActiveTransportEffects);
               _ParticleEffectTransport1.Stop();
               _ParticleEffectTransport1.Close(true, true);
               _ParticleEffectTransport1 = null;
            }

            if (_ActiveTransportEffects < MaxTransportEffects) {
               MyParticlesManager.TryCreateParticleEffect(PARTICLE_EFFECT_TRANSPORT1, out _ParticleEffectTransport1);
               if (_ParticleEffectTransport1 != null)
               {
                  Interlocked.Increment(ref _ActiveTransportEffects);
                  _ParticleEffectTransport1.UserScale = 0.1f;
                  UpdateTransportEffectPosition();
               }
            }
         } else
         {
//Remove
Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: transportState=Stop", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), active);

            if (_ParticleEffectTransport1 != null)
            {
//Remove
Mod.Log.Write(Logging.Level.Error, "BuildAndRepairSystemBlock {0}: transportState=Stop", Logging.BlockName(_Welder, Logging.BlockNameOptions.None), active);
               Interlocked.Decrement(ref _ActiveTransportEffects);
               _ParticleEffectTransport1.Stop();
               _ParticleEffectTransport1.Close(true, true);
               _ParticleEffectTransport1 = null;
            }
         }
      }

      /// <summary>
      /// Set the position of the visual effects for transport
      /// </summary>
      private void UpdateTransportEffectPosition()
      {
         if (_ParticleEffectTransport1 == null) return;

         var playTime = MyAPIGateway.Session.ElapsedPlayTime;
         var elapsed = State.TransportTime.Ticks != 0 ? (double)playTime.Subtract(State.LastTransportStartTime).Ticks / State.TransportTime.Ticks : 0d;
         elapsed = elapsed < 1 ? elapsed : 1;

         MatrixD startMatrix;
         var target = State.CurrentTransportTarget;
         startMatrix = _Welder.WorldMatrix;
         startMatrix.Translation = Vector3D.Transform(_EmitterPosition, _Welder.WorldMatrix);

         var direction = target.Value - startMatrix.Translation;
         var distance = direction.Normalize();
         elapsed = (elapsed > 0.5 ? 1 - elapsed : elapsed) * 2;
         startMatrix.Translation += direction * (distance * elapsed);

         _ParticleEffectTransport1.WorldMatrix = startMatrix;

      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="target"></param>
      /// <returns></returns>
      internal Vector3D? ComputePosition(object target)
      {
         if (target is IMySlimBlock)
         {
            Vector3D endPosition;
            ((IMySlimBlock)target).ComputeWorldCenter(out endPosition);
            return endPosition;
         }
         else if (target is IMyEntity) return ((IMyEntity)target).WorldMatrix.Translation;
         else if (target is Vector3D) return (Vector3D)target;
         return null;
      }

      /// <summary>
      /// Get a list of currently missing components (Scripting)
      /// </summary>
      /// <returns></returns>
      internal Dictionary<VRage.Game.MyDefinitionId, int> GetMissingComponentsDict()
      {
         var dict = new Dictionary<VRage.Game.MyDefinitionId, int>();
         lock (State.MissingComponents)
         {
            foreach (var item in State.MissingComponents)
            {
               dict.Add(item.Key, item.Value);
            }
         }
         return dict;
      }

      /// <summary>
      /// Get a list of currently build/repairable blocks (Scripting)
      /// </summary>
      /// <returns></returns>
      internal List<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleWeldTargetsList()
      {
         var list = new List<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
         lock (State.PossibleWeldTargets)
         {
            foreach (var blockData in State.PossibleWeldTargets)
            {
               list.Add(blockData.Block);
            }
         }
         return list;
      }

      /// <summary>
      /// Get a list of currently grind blocks (Scripting)
      /// </summary>
      /// <returns></returns>
      internal List<VRage.Game.ModAPI.Ingame.IMySlimBlock> GetPossibleGrindTargetsList()
      {
         var list = new List<VRage.Game.ModAPI.Ingame.IMySlimBlock>();
         lock (State.PossibleGrindTargets)
         {
            foreach (var blockData in State.PossibleGrindTargets)
            {
               list.Add(blockData.Block);
            }
         }
         return list;
      }

      /// <summary>
      /// Get a list of currently collectable floating objects (Scripting)
      /// </summary>
      /// <returns></returns>
      internal List<VRage.Game.ModAPI.Ingame.IMyEntity> GetPossibleCollectingTargetsList()
      {
         var list = new List<VRage.Game.ModAPI.Ingame.IMyEntity>();
         lock (State.PossibleFloatingTargets)
         {
            foreach (var floatingData in State.PossibleFloatingTargets)
            {
               list.Add(floatingData.Entity);
            }
         }
         return list;
      }
   }

   public class TargetBlockData
   {
      [Flags]
      public enum AttributeFlags
      {
         Projected = 0x0001,
         Autogrind = 0x0100
      }
      public IMySlimBlock Block { get; internal set; }
      public double Distance { get; internal set; }
      public AttributeFlags Attributes { get; internal set; }
      public bool Ignore { get; set; }
      public TargetBlockData(IMySlimBlock block, double distance, AttributeFlags attributes)
      {
         Block = block;
         Distance = distance;
         Attributes = attributes;
         Ignore = false;
      }
   }

   public class TargetEntityData
   {
      public IMyEntity Entity { get; internal set; }
      public double Distance { get; internal set; }
      public bool Ignore { get; set; }
      public TargetEntityData(IMyEntity entity, double distance)
      {
         Entity = entity;
         Distance = distance;
         Ignore = false;
      }
   }
}
