namespace SpaceEquipmentLtd.NanobotBuildAndRepairSystem
{
   using System;
   using System.Collections.Generic;
   using System.Text;
   using VRage.ModAPI;
   using VRage.Utils;
   using VRageMath;
   using Sandbox.ModAPI;
   using Sandbox.ModAPI.Interfaces.Terminal;
   using SpaceEquipmentLtd.Utils;
   using System.Runtime.CompilerServices;
   using System.Diagnostics;

   [Flags]
   public enum SearchModes
   {
      /// <summary>
      /// Search Target blocks only inside connected blocks
      /// </summary>
      Grids = 0x0001,

      /// <summary>
      /// Search Target blocks in bounding boy independend of connection
      /// </summary>
      BoundingBox = 0x0002
   }

   [Flags]
   public enum WorkModes
   {
      /// <summary>
      /// Grind only if nothing to weld
      /// </summary>
      WeldBeforeGrind = 0x0001,

      /// <summary>
      /// Weld onyl if nothing to grind
      /// </summary>
      GrindBeforeWeld = 0x0002,

      /// <summary>
      /// Grind only if nothing to weld or
      /// build waiting for missing items
      /// </summary>
      GrindIfWeldGetStuck = 0x0004
   }

   [Flags]
   public enum AutoGrindRelation
   {
      NoOwnership = 0x0001,
      Owner = 0x0002,
      FactionShare = 0x0004,
      Neutral = 0x0008,
      Enemies = 0x0010
   }

   [Flags]
   public enum AutoGrindOptions
   {
      DisableOnly = 0x0001,
      HackOnly = 0x0002
   }

   public static class NanobotBuildAndRepairSystemTerminal
   {
      public const float SATURATION_DELTA = 0.8f;
      public const float VALUE_DELTA = 0.55f;
      public const float VALUE_COLORIZE_DELTA = 0.1f;

      public static bool CustomControlsInit = false;
      private static List<IMyTerminalControl> CustomControls = new List<IMyTerminalControl>();

      private static IMyTerminalControlSlider _IgnoreColorHueSlider;
      private static IMyTerminalControlSlider _IgnoreColorSaturationSlider;
      private static IMyTerminalControlSlider _IgnoreColorValueSlider;

      private static IMyTerminalControlSlider _GrindColorHueSlider;
      private static IMyTerminalControlSlider _GrindColorSaturationSlider;
      private static IMyTerminalControlSlider _GrindColorValueSlider;

      private static IMyTerminalControlButton _WeldEnableDisableButton;
      private static IMyTerminalControlButton _WeldPriorityButtonUp;
      private static IMyTerminalControlButton _WeldPriorityButtonDown;
      private static IMyTerminalControlListbox _WeldPriorityListBox;
      private static IMyTerminalControlButton _GrindEnableDisableButton;
      private static IMyTerminalControlButton _GrindPriorityButtonUp;
      private static IMyTerminalControlButton _GrindPriorityButtonDown;
      private static IMyTerminalControlListbox _GrindPriorityListBox;

      private static IMyTerminalControlButton _ComponentCollectEnableDisableButton;
      private static IMyTerminalControlButton _ComponentCollectPriorityButtonUp;
      private static IMyTerminalControlButton _ComponentCollectPriorityButtonDown;
      private static IMyTerminalControlListbox _ComponentCollectPriorityListBox;

      /// <summary>
      /// Check an return the GameLogic object
      /// </summary>
      /// <param name="block"></param>
      /// <returns></returns>
      private static NanobotBuildAndRepairSystemBlock GetSystem(IMyTerminalBlock block)
      {
         if (block != null && block.GameLogic != null) return block.GameLogic.GetAs<NanobotBuildAndRepairSystemBlock>();
         return null;
      }

      /// <summary>
      /// Initialize custom control definition
      /// </summary>
      public static void InitializeControls()
      {
         lock (CustomControls)
         {
            if (CustomControlsInit) return;
            CustomControlsInit = true;
            try
            {
               // As CustomControlGetter is only called if the Terminal is opened, 
               // I add also some properties immediately and permanent to support scripting.
               // !! As we can't subtype here they will be also available in every Shipwelder but without function !!

               if (Mod.Log.ShouldLog(Logging.Level.Event)) Mod.Log.Write(Logging.Level.Event, "InitializeControls");

               MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;

               IMyTerminalControlCheckbox checkbox;
               IMyTerminalControlCombobox comboBox;
               IMyTerminalControlSeparator separateArea;
               IMyTerminalControlSlider slider;
               IMyTerminalControlOnOffSwitch onoffSwitch;
               IMyTerminalControlButton button;

               Func<IMyTerminalBlock, bool> isBaRSystem = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null;
               };

               Func<IMyTerminalBlock, bool> isReadonly = (block) =>
               {
                  return false;
               };

               // --- AllowBuild CheckBox
               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("AllowBuild");
               checkbox.Title = MyStringId.GetOrCompute("Build new");
               checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the BuildAndRepairSystem will also construct projected blocks.");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.AllowBuild) != 0) : false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.AllowBuild) | (value ? SyncBlockSettings.Settings.AllowBuild : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("AllowBuild", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.AllowBuildFixed);

               // --- Select search mode
               var onlyOneAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes & (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes - 1)) == 0;
               comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>("Mode");
               comboBox.Title = MyStringId.GetOrCompute("Mode");
               comboBox.Tooltip = MyStringId.GetOrCompute("Select how the nanobots search and reach their targets");
               comboBox.Enabled = onlyOneAllowed ? isReadonly : isBaRSystem;

               comboBox.ComboBoxContent = (list) =>
               {
                  if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.Grids))
                     list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.Grids, Value = MyStringId.GetOrCompute("Walk mode") });
                  if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag(SearchModes.BoundingBox))
                     list.Add(new MyTerminalControlComboBoxItem() { Key = (long)SearchModes.BoundingBox, Value = MyStringId.GetOrCompute("Fly mode") });
               };
               comboBox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  if (system == null) return 0;
                  else return (long)system.Settings.SearchMode;
               };
               comboBox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedSearchModes.HasFlag((SearchModes)value))
                     {
                        system.Settings.SearchMode = (SearchModes)value;
                     }
                  }
               };
               comboBox.SupportsMultipleBlocks = true;
               CustomControls.Add(comboBox);
               CreateProperty(comboBox, onlyOneAllowed);

               //Allow switch mode by Buttonpanel
               var list1 = new List<MyTerminalControlComboBoxItem>();
               comboBox.ComboBoxContent(list1);
               foreach (var entry in list1)
               {
                  var mode = entry.Key;
                  var comboBox1 = comboBox;
                  var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", ((SearchModes)mode).ToString()));
                  action.Name = new StringBuilder(string.Format("{0} On", entry.Value));
                  action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
                  action.Enabled = isBaRSystem;
                  action.Action = (block) =>
                  {
                     comboBox1.Setter(block, mode);
                  };
                  action.ValidForGroups = true;
                  MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
               }


               // --- Select work mode
               onlyOneAllowed = (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes & (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes - 1)) == 0;
               comboBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCombobox, IMyShipWelder>("WorkMode");
               comboBox.Title = MyStringId.GetOrCompute("WorkMode");
               comboBox.Tooltip = MyStringId.GetOrCompute("Select how the nanobots decide what to do (weld or grind)");
               comboBox.Enabled = onlyOneAllowed ? isReadonly : isBaRSystem;
               comboBox.ComboBoxContent = (list) =>
               {
                  if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.WeldBeforeGrind))
                     list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.WeldBeforeGrind, Value = MyStringId.GetOrCompute("Weld before grind") });
                  if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindBeforeWeld))
                     list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindBeforeWeld, Value = MyStringId.GetOrCompute("Grind before weld") });
                  if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag(WorkModes.GrindIfWeldGetStuck))
                     list.Add(new MyTerminalControlComboBoxItem() { Key = (long)WorkModes.GrindIfWeldGetStuck, Value = MyStringId.GetOrCompute("Grind if weld get stuck") });
               };
               comboBox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  if (system == null) return 0;
                  else return (long)system.Settings.WorkMode;
               };
               comboBox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     if (NanobotBuildAndRepairSystemMod.Settings.Welder.AllowedWorkModes.HasFlag((WorkModes)value))
                     {
                        system.Settings.WorkMode = (WorkModes)value;
                     }
                  }
               };
               comboBox.SupportsMultipleBlocks = true;
               CustomControls.Add(comboBox);
               CreateProperty(comboBox, onlyOneAllowed);


               // --- Set Color that marks blocks as 'ignore'
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateIgnoreColor");
               CustomControls.Add(separateArea);
               {
                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("UseIgnoreColor");
                  checkbox.Title = MyStringId.GetOrCompute("Use Ignore Color");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will ignore blocks with the color defined further down.");
                  checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed ? isReadonly : isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseIgnoreColor) | (value ? SyncBlockSettings.Settings.UseIgnoreColor : 0);
                        foreach (var ctrl in CustomControls)
                        {
                           if (ctrl.Id.Contains("IgnoreColor")) ctrl.UpdateVisual();
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("UseIgnoreColor", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed);

                  Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseIgnoreColor) != 0 && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed) : false;
                  };

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("IgnoreColorPickCurrent");
                  button.Title = MyStringId.GetOrCompute("Pick current build color");
                  button.Enabled = colorPickerEnabled;
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                     {
                        system.Settings.IgnoreColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                        _IgnoreColorHueSlider.UpdateVisual();
                        _IgnoreColorSaturationSlider.UpdateVisual();
                        _IgnoreColorValueSlider.UpdateVisual();
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  CustomControls.Add(button);

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("IgnoreColorSetAsCurrent");
                  button.Title = MyStringId.GetOrCompute("Set current build color");
                  button.Enabled = colorPickerEnabled;
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                     {
                        MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.IgnoreColor;
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  CustomControls.Add(button);


                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorHue");
                  _IgnoreColorHueSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Hue");
                  slider.SetLimits(0, 360);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.Settings.IgnoreColor.X * 360f : 0;
                  };
                  slider.Setter = (block, x) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        x = x < 0 ? 0 : x > 360 ? 360 : x;
                        hsv.X = (float)Math.Round(x, 1, MidpointRounding.AwayFromZero) / 360;
                        system.Settings.IgnoreColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        y.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  slider.SupportsMultipleBlocks = true;
                  CustomControls.Add(slider);
                  CreateSliderActions("IgnoreColorHue", slider, 0, 360);

                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorSaturation");
                  _IgnoreColorSaturationSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Saturation");
                  slider.SetLimits(0, 100);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.IgnoreColor.Y + SATURATION_DELTA) * 100f : 0;
                  };
                  slider.Setter = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        y = y < 0 ? 0 : y > 100 ? 100 : y;
                        hsv.Y = ((float)Math.Round(y, 1, MidpointRounding.AwayFromZero) / 100f) - SATURATION_DELTA;
                        system.Settings.IgnoreColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        y.Append(Math.Round((hsv.Y + SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  slider.SupportsMultipleBlocks = true;
                  CustomControls.Add(slider);
                  CreateSliderActions("IgnoreColorSaturation", slider, 0, 100);

                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("IgnoreColorValue");
                  _IgnoreColorValueSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Value");
                  slider.SetLimits(0, 100);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.IgnoreColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f : 0;
                  };
                  slider.Setter = (block, z) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        z = z < 0 ? 0 : z > 100 ? 100 : z;
                        hsv.Z = ((float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f) - VALUE_DELTA + VALUE_COLORIZE_DELTA;
                        system.Settings.IgnoreColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.IgnoreColor;
                        y.Append(Math.Round((hsv.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  CustomControls.Add(slider);
                  CreateSliderActions("IgnoreColorValue", slider, 0, 100);

                  var propertyIC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.IgnoreColor");
                  propertyIC.SupportsMultipleBlocks = false;
                  propertyIC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? 
                        new Vector3(
                           system.Settings.IgnoreColor.X * 360f,
                           (system.Settings.IgnoreColor.Y + SATURATION_DELTA) * 100f,
                           (system.Settings.IgnoreColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f
                           )
                        : Vector3.Zero;
                  };
                  propertyIC.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseIgnoreColorFixed)
                     {
                        if (value.X < 0f) value.X = 0f;
                        if (value.X > 360f) value.X = 360f;
                        if (value.Y < 0f) value.Y = 0f;
                        if (value.Y > 100f) value.Y = 100f;
                        if (value.Z < 0f) value.Z = 0f;
                        if (value.Z > 100f) value.Z = 100f;

                        system.Settings.IgnoreColor = new Vector3(value.X / 360f,
                                                     (value.Y / 100f) - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                                     (value.Z / 100f) - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
                     }
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyIC);
               }

               // --- Set Color that marks blocks as 'grind'
               {
                  separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindColor");
                  CustomControls.Add(separateArea);

                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("UseGrindColor");
                  checkbox.Title = MyStringId.GetOrCompute("Use Grind Color");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will grind blocks with the color defined further down.");
                  checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed ? isReadonly : isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.UseGrindColor) | (value ? SyncBlockSettings.Settings.UseGrindColor : 0);
                        foreach (var ctrl in CustomControls)
                        {
                           if (ctrl.Id.Contains("GrindColor")) ctrl.UpdateVisual();
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("UseGrindColor", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed);

                  Func<IMyTerminalBlock, bool> colorPickerEnabled = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.UseGrindColor) != 0) : false;
                  };

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindColorPickCurrent");
                  button.Title = MyStringId.GetOrCompute("Pick current build color");
                  button.Enabled = colorPickerEnabled;
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                     {
                        system.Settings.GrindColor = MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor;
                        _GrindColorHueSlider.UpdateVisual();
                        _GrindColorSaturationSlider.UpdateVisual();
                        _GrindColorValueSlider.UpdateVisual();
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  CustomControls.Add(button);

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindColorSetAsCurrent");
                  button.Title = MyStringId.GetOrCompute("Set current build color");
                  button.Enabled = colorPickerEnabled;
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && MyAPIGateway.Session.LocalHumanPlayer != null)
                     {
                        MyAPIGateway.Session.LocalHumanPlayer.SelectedBuildColor = system.Settings.GrindColor;
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  CustomControls.Add(button);

                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorHue");
                  _GrindColorHueSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Hue");
                  slider.SetLimits(0, 360);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.Settings.GrindColor.X * 360f : 0;
                  };
                  slider.Setter = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        y = y < 0 ? 0 : y > 360 ? 360 : y;
                        hsv.X = (float)Math.Round(y, 1, MidpointRounding.AwayFromZero) / 360;
                        system.Settings.GrindColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        y.Append(Math.Round(hsv.X * 360f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  slider.SupportsMultipleBlocks = true;
                  CustomControls.Add(slider);
                  CreateSliderActions("GrindColorHue", slider, 0, 360);

                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorSaturation");
                  _GrindColorSaturationSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Saturation");
                  slider.SetLimits(0, 100);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.GrindColor.Y + SATURATION_DELTA) * 100f : 0;
                  };
                  slider.Setter = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        y = y < 0 ? 0 : y > 100 ? 100 : y;
                        hsv.Y = ((float)Math.Round(y, 1, MidpointRounding.AwayFromZero) / 100f) - SATURATION_DELTA;
                        system.Settings.GrindColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        y.Append(Math.Round((hsv.Y + SATURATION_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  slider.SupportsMultipleBlocks = true;
                  CustomControls.Add(slider);
                  CreateSliderActions("GrindColorSaturation", slider, 0, 100);

                  slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("GrindColorValue");
                  _GrindColorValueSlider = slider;
                  slider.Title = MyStringId.GetOrCompute("Value");
                  slider.SetLimits(0, 100);
                  slider.Enabled = colorPickerEnabled;
                  slider.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.GrindColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f : 0;
                  };
                  slider.Setter = (block, z) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        z = z < 0 ? 0 : z > 100 ? 100 : z;
                        hsv.Z = ((float)Math.Round(z, 1, MidpointRounding.AwayFromZero) / 100f) - VALUE_DELTA + VALUE_COLORIZE_DELTA;
                        system.Settings.GrindColor = hsv;
                     }
                  };
                  slider.Writer = (block, y) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        var hsv = system.Settings.GrindColor;
                        y.Append(Math.Round((hsv.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f, 1, MidpointRounding.AwayFromZero));
                     }
                  };
                  slider.SupportsMultipleBlocks = true;
                  CustomControls.Add(slider);
                  CreateSliderActions("GrindColorValue", slider, 0, 100);

                  var propertyGC = MyAPIGateway.TerminalControls.CreateProperty<Vector3, IMyShipWelder>("BuildAndRepair.GrindColor");
                  propertyGC.SupportsMultipleBlocks = false;
                  propertyGC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ?
                        new Vector3(
                           system.Settings.GrindColor.X * 360f,
                           (system.Settings.GrindColor.Y + SATURATION_DELTA) * 100f,
                           (system.Settings.GrindColor.Z + VALUE_DELTA - VALUE_COLORIZE_DELTA) * 100f
                           )
                        : Vector3.Zero;
                  };
                  propertyGC.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && !NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindColorFixed)
                     {
                        if (value.X < 0f) value.X = 0f;
                        if (value.X > 360f) value.X = 360f;
                        if (value.Y < 0f) value.Y = 0f;
                        if (value.Y > 100f) value.Y = 100f;
                        if (value.Z < 0f) value.Z = 0f;
                        if (value.Z > 100f) value.Z = 100f;

                        system.Settings.GrindColor = new Vector3( value.X / 360f,
                                                     (value.Y / 100f) - NanobotBuildAndRepairSystemTerminal.SATURATION_DELTA,
                                                     (value.Z / 100f) - NanobotBuildAndRepairSystemTerminal.VALUE_DELTA + NanobotBuildAndRepairSystemTerminal.VALUE_COLORIZE_DELTA);
                     }
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGC);
               }

               // --- Enable Janitor grinding
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindJanitor");
               CustomControls.Add(separateArea);

               //--Grind enemy
               onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorEnemies");
               onoffSwitch.Title = MyStringId.GetOrCompute("Janitor grinds enemy blocks");
               onoffSwitch.Tooltip = MyStringId.GetOrCompute("When checked, enemy blocks in range will be grinded.");
               onoffSwitch.OnText = MyStringId.GetOrCompute("On");
               onoffSwitch.OffText = MyStringId.GetOrCompute("Off");
               onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed ? isReadonly : isBaRSystem;
               onoffSwitch.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Enemies) != 0 : false;
               };
               onoffSwitch.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Enemies) | (value ? AutoGrindRelation.Enemies : 0);
                  }
               };
               onoffSwitch.SupportsMultipleBlocks = true;
               CreateOnOffSwitchAction("GrindJanitorEnemies", onoffSwitch);
               CustomControls.Add(onoffSwitch);
               CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed);

               //--Grind not owned
               onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorNotOwned");
               onoffSwitch.Title = MyStringId.GetOrCompute("Janitor grinds not owned blocks");
               onoffSwitch.Tooltip = MyStringId.GetOrCompute("When checked, blocks without owner in range will be grinded.");
               onoffSwitch.OnText = MyStringId.GetOrCompute("On");
               onoffSwitch.OffText = MyStringId.GetOrCompute("Off");
               onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed ? isReadonly : isBaRSystem;
               onoffSwitch.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.NoOwnership) != 0 : false;
               };
               onoffSwitch.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.NoOwnership) | (value ? AutoGrindRelation.NoOwnership : 0);
                  }
               };
               onoffSwitch.SupportsMultipleBlocks = true;
               CreateOnOffSwitchAction("GrindJanitorNotOwned", onoffSwitch);
               CustomControls.Add(onoffSwitch);
               CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed);

               //--Grind Neutrals
               onoffSwitch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyShipWelder>("GrindJanitorNeutrals");
               onoffSwitch.Title = MyStringId.GetOrCompute("Janitor grinds neutral blocks");
               onoffSwitch.Tooltip = MyStringId.GetOrCompute("When checked, the system will grind also blocks owned by neutrals (fractions not at war).");
               onoffSwitch.OnText = MyStringId.GetOrCompute("On");
               onoffSwitch.OffText = MyStringId.GetOrCompute("Off");
               onoffSwitch.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed ? isReadonly : isBaRSystem;
               onoffSwitch.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? (system.Settings.UseGrindJanitorOn & AutoGrindRelation.Neutral) != 0 : false;
               };
               onoffSwitch.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.UseGrindJanitorOn = (system.Settings.UseGrindJanitorOn & ~AutoGrindRelation.Neutral) | (value ? AutoGrindRelation.Neutral : 0);
                  }
               };
               onoffSwitch.SupportsMultipleBlocks = true;
               CreateOnOffSwitchAction("GrindJanitorNeutrals", onoffSwitch);
               CustomControls.Add(onoffSwitch);
               CreateProperty(onoffSwitch, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed);


               //Grind Options
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindOptions");
               CustomControls.Add(separateArea);
               {
                  //--Grind Disable only
                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindJanitorOptionDisableOnly");
                  checkbox.Title = MyStringId.GetOrCompute("Janitor grind to disable only");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, only functional blocks are grinded and these only until they stop working.");
                  checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed ? isReadonly : isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.GrindJanitorOptions & AutoGrindOptions.DisableOnly) != 0 : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        //Only one option (HackOnly or DisableOnly) at a time is allowed 
                        if (value)
                        {
                           system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly) | AutoGrindOptions.DisableOnly;
                           foreach (var ctrl in CustomControls)
                           {
                              if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                           }
                        }
                        else
                        {
                           system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly);
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("GrindJanitorOptionDisableOnly", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed);

                  //--Grind Hack only
                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindJanitorOptionHackOnly");
                  checkbox.Title = MyStringId.GetOrCompute("Janitor grind to hack only");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, only functional blocks are grinded and these only until they could be hacked.");
                  checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed ? isReadonly : isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? (system.Settings.GrindJanitorOptions & AutoGrindOptions.HackOnly) != 0 : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        //Only one option (HackOnly or DisableOnly) at a time is allowed 
                        if (value)
                        {
                           system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.DisableOnly) | AutoGrindOptions.HackOnly;
                           foreach (var ctrl in CustomControls)
                           {
                              if (ctrl.Id.Contains("GrindJanitorOption")) ctrl.UpdateVisual();
                           }
                        }
                        else
                        {
                           system.Settings.GrindJanitorOptions = (system.Settings.GrindJanitorOptions & ~AutoGrindOptions.HackOnly);
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("GrindJanitorOptionHackOnly", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.UseGrindJanitorFixed);
               }

               // -- Priority Welding
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateWeldPrio");
               CustomControls.Add(separateArea);

               var textbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("WeldPriority");
               textbox.Label = MyStringId.GetOrCompute("Build-Repair Priority");
               CustomControls.Add(textbox);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("WeldEnableDisable");
               _WeldEnableDisableButton = button;
               button.Title = MyStringId.GetOrCompute("Enable/Disable");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.BlockWeldPriority.ToggleEnabled();
                     system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                     _WeldPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("WeldPriorityUp");
               _WeldPriorityButtonUp = button;
               button.Title = MyStringId.GetOrCompute("Priority Up");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.BlockWeldPriority.MoveSelectedUp();
                     system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                     _WeldPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("WeldPriorityDown");
               _WeldPriorityButtonDown = button;
               button.Title = MyStringId.GetOrCompute("Priority Down");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.BlockWeldPriority != null && system.BlockWeldPriority.Selected != null;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.BlockWeldPriority.MoveSelectedDown();
                     system.Settings.WeldPriority = system.BlockWeldPriority.GetEntries();
                     _WeldPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

               var listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("WeldPriority");
               _WeldPriorityListBox = listbox;

               listbox.Multiselect = false;
               listbox.VisibleRowsCount = 15;
               listbox.ItemSelected = (block, selected) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     if (selected.Count > 0 && system.BlockWeldPriority != null)
                     {
                        system.BlockWeldPriority.Selected = (BlockClass)selected[0].UserData;
                     }
                     else system.BlockWeldPriority.Selected = null;
                     _WeldEnableDisableButton.UpdateVisual();
                     _WeldPriorityButtonUp.UpdateVisual();
                     _WeldPriorityButtonDown.UpdateVisual();
                  }
               };
               listbox.ListContent = (block, items, selected) =>
               {
                  var system = GetSystem(block);
                  if (system != null && system.BlockWeldPriority != null)
                  {
                     system.BlockWeldPriority.FillTerminalList(items, selected);
                  }
               };
               listbox.SupportsMultipleBlocks = true;
               CustomControls.Add(listbox);

               //Grind Priority
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateGrindPrio");
               CustomControls.Add(separateArea);
               {

                  textbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("GrindPriority");
                  textbox.Label = MyStringId.GetOrCompute("Grind Priority");
                  CustomControls.Add(textbox);

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindEnableDisable");
                  _GrindEnableDisableButton = button;
                  button.Title = MyStringId.GetOrCompute("Enable/Disable");
                  button.Enabled = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null;
                  };
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.BlockGrindPriority.ToggleEnabled();
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        _GrindPriorityListBox.UpdateVisual();
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindPriorityUp");
                  _GrindPriorityButtonUp = button;
                  button.Title = MyStringId.GetOrCompute("Priority Up");
                  button.Enabled = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null;
                  };
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.BlockGrindPriority.MoveSelectedUp();
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        _GrindPriorityListBox.UpdateVisual();
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

                  button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("GrindPriorityDown");
                  _GrindPriorityButtonDown = button;
                  button.Title = MyStringId.GetOrCompute("Priority Down");
                  button.Enabled = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null && system.BlockGrindPriority != null && system.BlockGrindPriority.Selected != null;
                  };
                  button.Action = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.BlockGrindPriority.MoveSelectedDown();
                        system.Settings.GrindPriority = system.BlockGrindPriority.GetEntries();
                        _GrindPriorityListBox.UpdateVisual();
                     }
                  };
                  button.SupportsMultipleBlocks = true;
                  if (!NanobotBuildAndRepairSystemMod.Settings.Welder.PriorityFixed) CustomControls.Add(button);

                  listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("GrindPriority");
                  _GrindPriorityListBox = listbox;

                  listbox.Multiselect = false;
                  listbox.VisibleRowsCount = 15;
                  listbox.ItemSelected = (block, selected) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && system.BlockGrindPriority != null)
                     {
                        if (selected.Count > 0)
                        {
                           system.BlockGrindPriority.Selected = (BlockClass)selected[0].UserData;
                        }
                        else system.BlockGrindPriority.Selected = null;
                        _GrindEnableDisableButton.UpdateVisual();
                        _GrindPriorityButtonUp.UpdateVisual();
                        _GrindPriorityButtonDown.UpdateVisual();
                     }
                  };
                  listbox.ListContent = (block, items, selected) =>
                  {
                     var system = GetSystem(block);
                     if (system != null && system.BlockGrindPriority != null)
                     {
                        system.BlockGrindPriority.FillTerminalList(items, selected);
                     }
                  };
                  listbox.SupportsMultipleBlocks = true;
                  CustomControls.Add(listbox);

                  //--Grind order near/far/smallest grid
                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindNearFirst");
                  checkbox.Title = MyStringId.GetOrCompute("Nearest First");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, if blocks have the same priority, the nearest is grinded first.");
                  checkbox.Enabled = isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.GrindNearFirst) != 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed 
                        if (value)
                        {
                           system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindSmallestGridFirst) | SyncBlockSettings.Settings.GrindNearFirst;
                        }
                        else
                        {
                           system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst);
                        }
                        foreach (var ctrl in CustomControls)
                        {
                           if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                           if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("GrindNearFirst", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox);

                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindFarFirst");
                  checkbox.Title = MyStringId.GetOrCompute("Furthest first");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, if blocks have the same priority, the furthest is grinded first.");
                  checkbox.Enabled = isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & (SyncBlockSettings.Settings.GrindNearFirst | SyncBlockSettings.Settings.GrindSmallestGridFirst)) == 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed 
                        if (value)
                        {
                           system.Settings.Flags = (system.Settings.Flags & ~(SyncBlockSettings.Settings.GrindSmallestGridFirst | SyncBlockSettings.Settings.GrindNearFirst));
                        }
                        foreach (var ctrl in CustomControls)
                        {
                           if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                           if (ctrl.Id.Contains("GrindSmallestGridFirst")) ctrl.UpdateVisual();
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("GrindFarFirst", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox);

                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("GrindSmallestGridFirst");
                  checkbox.Title = MyStringId.GetOrCompute("Smallest grid first");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, if blocks have the same priority, the smallest grid is grinded first.");
                  checkbox.Enabled = isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.GrindSmallestGridFirst) != 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        //Only one option (GrindNearFirst or GrindSmallestGridFirst) at a time is allowed 
                        if (value)
                        {
                           system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.GrindNearFirst) | SyncBlockSettings.Settings.GrindSmallestGridFirst;
                        }
                        foreach (var ctrl in CustomControls)
                        {
                           if (ctrl.Id.Contains("GrindNearFirst")) ctrl.UpdateVisual();
                           if (ctrl.Id.Contains("GrindFarFirst")) ctrl.UpdateVisual();
                        }
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("GrindSmallestGridFirst", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox);
               }

               // --- Collect floating objects
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateCollectPrio");
               CustomControls.Add(separateArea);

               textbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, IMyShipWelder>("CollectPriority");
               textbox.Label = MyStringId.GetOrCompute("Collect Priority");
               CustomControls.Add(textbox);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("EnableDisableCollect");
               _ComponentCollectEnableDisableButton = button;
               button.Title = MyStringId.GetOrCompute("Enable/Disable");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && system.Settings.SearchMode == SearchModes.BoundingBox;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.ComponentCollectPriority.ToggleEnabled();
                     system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                     _ComponentCollectPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed) CustomControls.Add(button);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("CollectPriorityUp");
               _ComponentCollectPriorityButtonUp = button;
               button.Title = MyStringId.GetOrCompute("Priority Up");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && system.Settings.SearchMode == SearchModes.BoundingBox;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.ComponentCollectPriority.MoveSelectedUp();
                     system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                     _ComponentCollectPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed) CustomControls.Add(button);

               button = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipWelder>("CollectPriorityDown");
               _ComponentCollectPriorityButtonDown = button;
               button.Title = MyStringId.GetOrCompute("Priority Down");
               button.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.ComponentCollectPriority != null && system.ComponentCollectPriority.Selected != null && system.Settings.SearchMode == SearchModes.BoundingBox;
               };
               button.Action = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.ComponentCollectPriority.MoveSelectedDown();
                     system.Settings.ComponentCollectPriority = system.ComponentCollectPriority.GetEntries();
                     _ComponentCollectPriorityListBox.UpdateVisual();
                  }
               };
               button.SupportsMultipleBlocks = true;
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.CollectPriorityFixed) CustomControls.Add(button);

               listbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyShipWelder>("CollectPriority");
               _ComponentCollectPriorityListBox = listbox;

               listbox.Multiselect = false;
               listbox.VisibleRowsCount = 5;
               listbox.Enabled = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.Settings.SearchMode == SearchModes.BoundingBox;
               };
               listbox.ItemSelected = (block, selected) =>
               {
                  var system = GetSystem(block);
                  if (system != null && system.ComponentCollectPriority != null)
                  {
                     if (selected.Count > 0)
                     {
                        system.ComponentCollectPriority.Selected = (ComponentClass)selected[0].UserData;
                     }
                     else system.ComponentCollectPriority.Selected = null;
                     _ComponentCollectEnableDisableButton.UpdateVisual();
                     _ComponentCollectPriorityButtonUp.UpdateVisual();
                     _ComponentCollectPriorityButtonDown.UpdateVisual();
                  }
               };
               listbox.ListContent = (block, items, selected) =>
               {
                  var system = GetSystem(block);
                  if (system != null && system.ComponentCollectPriority != null)
                  {
                     system.ComponentCollectPriority.FillTerminalList(items, selected);
                  }
               };
               listbox.SupportsMultipleBlocks = true;
               CustomControls.Add(listbox);

               // Collect if idle
               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("CollectIfIdle");
               checkbox.Title = MyStringId.GetOrCompute("Collect only if idle");
               checkbox.Tooltip = MyStringId.GetOrCompute("if set collecting floating objects is done only if no welding/grinding is needed.");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ComponentCollectIfIdle) != 0) : false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ComponentCollectIfIdle) | (value ? SyncBlockSettings.Settings.ComponentCollectIfIdle : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("CollectIfIdle", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.CollectIfIdleFixed);

               //Push Ingot/ore immediately
               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushIngotOreImmediately");
               checkbox.Title = MyStringId.GetOrCompute("Push ingot/ore immediately");
               checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will push ingot/ore immediately into connected conveyors.");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushIngotOreImmediately) != 0) : false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushIngotOreImmediately) | (value ? SyncBlockSettings.Settings.PushIngotOreImmediately : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("PushIngotOreImmediately", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushIngotOreImmediatelyFixed);

               //Push Items immediately
               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushItemsImmediately");
               checkbox.Title = MyStringId.GetOrCompute("Push items immediately");
               checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will push items (tools,weapons,ammo,bottles, ..) immediately into connected conveyors.");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushItemsImmediately) != 0) : false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushItemsImmediately) | (value ? SyncBlockSettings.Settings.PushItemsImmediately : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("PushItemsImmediately", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushItemsImmediatelyFixed);

               //Push Component immediately
               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("PushComponentImmediately");
               checkbox.Title = MyStringId.GetOrCompute("Push components immediately");
               checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will push components immediately into connected conveyors.");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.PushComponentImmediately) != 0) : false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.PushComponentImmediately) | (value ? SyncBlockSettings.Settings.PushComponentImmediately : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("PushComponentImmediately", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.PushComponentImmediatelyFixed);

               // -- Highlight Area
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateArea");
               CustomControls.Add(separateArea);

               Func<IMyTerminalBlock, float> getLimitMin = (block) => NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M;
               Func<IMyTerminalBlock, float> getLimitMax = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null && system.Settings != null ? system.Settings.MaximumRange : NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M;
               };

               checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("ShowArea");
               checkbox.Title = MyStringId.GetOrCompute("Show Area");
               checkbox.Tooltip = MyStringId.GetOrCompute("When checked, it will show you the area this system covers");
               checkbox.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed ? isReadonly : isBaRSystem;
               checkbox.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ShowArea) != 0) : false;
                  }

                  return false;
               };
               checkbox.Setter = (block, value) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ShowArea) | (value ? SyncBlockSettings.Settings.ShowArea : 0);
                  }
               };
               checkbox.SupportsMultipleBlocks = true;
               CreateCheckBoxAction("ShowArea", checkbox);
               CustomControls.Add(checkbox);
               CreateProperty(checkbox, NanobotBuildAndRepairSystemMod.Settings.Welder.ShowAreaFixed);

               //Slider Area
               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaWidthLeft");
               slider.Title = MyStringId.GetOrCompute("Area Width Left");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaWidthLeft : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaWidthLeft = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaWidthLeft + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaWidthLeft", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaWidthRight");
               slider.Title = MyStringId.GetOrCompute("Area Width Right");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaWidthRight : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaWidthRight = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaWidthRight + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaWidthRight", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaHeightBottom");
               slider.Title = MyStringId.GetOrCompute("Area Height Bottom");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaHeightBottom : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaHeightBottom = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaHeightBottom + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaHeightBottom", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaHeightTop");
               slider.Title = MyStringId.GetOrCompute("Area Height Top");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaHeightTop : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaHeightTop = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaHeightTop + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaHeightTop", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaDepthRear");
               slider.Title = MyStringId.GetOrCompute("Area Depth Rear");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaDepthRear : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaDepthRear = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaDepthRear + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaDepthRear", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("AreaDepthFront");
               slider.Title = MyStringId.GetOrCompute("Area Depth Front");
               slider.SetLimits(getLimitMin, getLimitMax);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? system.Settings.AreaDepthFront : 0;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = getLimitMin(block);
                     var max = getLimitMax(block);
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.AreaDepthFront = (int)y;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(system.Settings.AreaDepthFront + " m");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("AreaDepthFront", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.AreaSizeFixed);

               // -- Sound enabled
               separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateOther");
               CustomControls.Add(separateArea);

               slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipWelder>("SoundVolume");
               slider.Title = MyStringId.GetOrCompute("Sound Volume");
               slider.SetLimits(0f, 100f);
               slider.Enabled = NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed ? isReadonly : isBaRSystem;
               slider.Getter = (block) =>
               {
                  var system = GetSystem(block);
                  return system != null ? 100f * system.Settings.SoundVolume / NanobotBuildAndRepairSystemBlock.WELDER_SOUND_VOLUME : 0f;
               };
               slider.Setter = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     var min = 0;
                     var max = 100;
                     y = y < min ? min : y > max ? max : y;
                     system.Settings.SoundVolume = (float)Math.Round(y * NanobotBuildAndRepairSystemBlock.WELDER_SOUND_VOLUME) / 100f;
                  }
               };
               slider.Writer = (block, y) =>
               {
                  var system = GetSystem(block);
                  if (system != null)
                  {
                     y.Append(Math.Round(100f * system.Settings.SoundVolume / NanobotBuildAndRepairSystemBlock.WELDER_SOUND_VOLUME) + " %");
                  }
               };
               slider.SupportsMultipleBlocks = true;
               CustomControls.Add(slider);
               CreateSliderActionsArea("SoundVolume", slider);
               CreateProperty(slider, NanobotBuildAndRepairSystemMod.Settings.Welder.SoundVolumeFixed);

               // -- Script Control
               if (!NanobotBuildAndRepairSystemMod.Settings.Welder.ScriptControllFixed)
               {
                  separateArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipWelder>("SeparateScriptControl");
                  CustomControls.Add(separateArea);

                  checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipWelder>("ScriptControlled");
                  checkbox.Title = MyStringId.GetOrCompute("Controlled by Script");
                  checkbox.Tooltip = MyStringId.GetOrCompute("When checked, the system will not build/repair blocks automatically. Each block has to be picked by calling scripting functions.");
                  checkbox.Enabled = isBaRSystem;
                  checkbox.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? ((system.Settings.Flags & SyncBlockSettings.Settings.ScriptControlled) != 0) : false;
                  };
                  checkbox.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.Settings.Flags = (system.Settings.Flags & ~SyncBlockSettings.Settings.ScriptControlled) | (value ? SyncBlockSettings.Settings.ScriptControlled : 0);
                     }
                  };
                  checkbox.SupportsMultipleBlocks = true;
                  CreateCheckBoxAction("ScriptControlled", checkbox);
                  CustomControls.Add(checkbox);
                  CreateProperty(checkbox);

                  //Scripting support for Priority and enabling Weld BlockClasses
                  var propertyWeldPriorityList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.WeldPriorityList");
                  propertyWeldPriorityList.SupportsMultipleBlocks = false;
                  propertyWeldPriorityList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.BlockWeldPriority.GetList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyWeldPriorityList);

                  var propertySWP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetWeldPriority");
                  propertySWP.SupportsMultipleBlocks = false;
                  propertySWP.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockWeldPriority.SetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWP);

                  var propertyGWP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetWeldPriority");
                  propertyGWP.SupportsMultipleBlocks = false;
                  propertyGWP.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockWeldPriority.GetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWP);

                  var propertySWE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetWeldEnabled");
                  propertySWE.SupportsMultipleBlocks = false;
                  propertySWE.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockWeldPriority.SetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySWE);

                  var propertyGWE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetWeldEnabled");
                  propertyGWE.SupportsMultipleBlocks = false;
                  propertyGWE.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockWeldPriority.GetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGWE);

                  //Scripting support for Priority and enabling GrindWeld BlockClasses
                  var propertyGrindPriorityList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.GrindPriorityList");
                  propertyGrindPriorityList.SupportsMultipleBlocks = false;
                  propertyGrindPriorityList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.BlockGrindPriority.GetList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGrindPriorityList);

                  var propertySGP = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetGrindPriority");
                  propertySGP.SupportsMultipleBlocks = false;
                  propertySGP.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockGrindPriority.SetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGP);

                  var propertyGGP = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetGrindPriority");
                  propertyGGP.SupportsMultipleBlocks = false;
                  propertyGGP.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockGrindPriority.GetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGP);

                  var propertySGE = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetGrindEnabled");
                  propertySGE.SupportsMultipleBlocks = false;
                  propertySGE.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockGrindPriority.SetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySGE);

                  var propertyGGE = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetGrindEnabled");
                  propertyGGE.SupportsMultipleBlocks = false;
                  propertyGGE.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.BlockGrindPriority.GetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGGE);

                  //Scripting support for Priority and enabling ComponentClasses
                  var propertyComponentClassList = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyShipWelder>("BuildAndRepair.ComponentClassList");
                  propertyComponentClassList.SupportsMultipleBlocks = false;
                  propertyComponentClassList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.ComponentCollectPriority.GetList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyComponentClassList);

                  var propertySPC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, int>, IMyShipWelder>("BuildAndRepair.SetCollectPriority");
                  propertySPC.SupportsMultipleBlocks = false;
                  propertySPC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.ComponentCollectPriority.SetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySPC);

                  var propertyGPC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, int>, IMyShipWelder>("BuildAndRepair.GetCollectPriority");
                  propertyGPC.SupportsMultipleBlocks = false;
                  propertyGPC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.ComponentCollectPriority.GetPriority;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGPC);

                  var propertySEC = MyAPIGateway.TerminalControls.CreateProperty<Action<int, bool>, IMyShipWelder>("BuildAndRepair.SetCollectEnabled");
                  propertySEC.SupportsMultipleBlocks = false;
                  propertySEC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.ComponentCollectPriority.SetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertySEC);

                  var propertyGEC = MyAPIGateway.TerminalControls.CreateProperty<Func<int, bool>, IMyShipWelder>("BuildAndRepair.GetCollectEnabled");
                  propertyGEC.SupportsMultipleBlocks = false;
                  propertyGEC.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        return system.ComponentCollectPriority.GetEnabled;
                     }
                     return null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyGEC);

                  //Working Lists
                  var propertyMissingComponentsDict = MyAPIGateway.TerminalControls.CreateProperty<Dictionary<VRage.Game.MyDefinitionId, int>, IMyShipWelder>("BuildAndRepair.MissingComponents");
                  propertyMissingComponentsDict.SupportsMultipleBlocks = false;
                  propertyMissingComponentsDict.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.GetMissingComponentsDict() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyMissingComponentsDict);

                  var propertyPossibleWeldTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleTargets");
                  propertyPossibleWeldTargetsList.SupportsMultipleBlocks = false;
                  propertyPossibleWeldTargetsList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.GetPossibleWeldTargetsList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleWeldTargetsList);

                  var propertyPossibleGridTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMySlimBlock>, IMyShipWelder>("BuildAndRepair.PossibleGrindTargets");
                  propertyPossibleGridTargetsList.SupportsMultipleBlocks = false;
                  propertyPossibleGridTargetsList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.GetPossibleGrindTargetsList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleGridTargetsList);

                  var propertyPossibleCollectTargetsList = MyAPIGateway.TerminalControls.CreateProperty<List<VRage.Game.ModAPI.Ingame.IMyEntity>, IMyShipWelder>("BuildAndRepair.PossibleCollectTargets");
                  propertyPossibleCollectTargetsList.SupportsMultipleBlocks = false;
                  propertyPossibleCollectTargetsList.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.GetPossibleCollectingTargetsList() : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPossibleCollectTargetsList);

                  //Control welding
                  var propertyCPT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedTarget");
                  propertyCPT.SupportsMultipleBlocks = false;
                  propertyCPT.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.Settings.CurrentPickedWeldingBlock : null;
                  };
                  propertyCPT.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.Settings.CurrentPickedWeldingBlock = value;
                     }
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPT);

                  var propertyCT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentTarget");
                  propertyCT.SupportsMultipleBlocks = false;
                  propertyCT.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.State.CurrentWeldingBlock : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCT);

                  //Control grinding
                  var propertyCPGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentPickedGrindTarget");
                  propertyCPGT.SupportsMultipleBlocks = false;
                  propertyCPGT.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.Settings.CurrentPickedGrindingBlock : null;
                  };
                  propertyCPGT.Setter = (block, value) =>
                  {
                     var system = GetSystem(block);
                     if (system != null)
                     {
                        system.Settings.CurrentPickedGrindingBlock = value;
                     }
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCPGT);

                  var propertyCGT = MyAPIGateway.TerminalControls.CreateProperty<VRage.Game.ModAPI.Ingame.IMySlimBlock, IMyShipWelder>("BuildAndRepair.CurrentGrindTarget");
                  propertyCGT.SupportsMultipleBlocks = false;
                  propertyCGT.Getter = (block) =>
                  {
                     var system = GetSystem(block);
                     return system != null ? system.State.CurrentGrindingBlock : null;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyCGT);

                  //Publish functions to scripting
                  var propertyPEQ = MyAPIGateway.TerminalControls.CreateProperty<Func<IEnumerable<long>, VRage.Game.MyDefinitionId, int, int>, IMyShipWelder>("BuildAndRepair.ProductionBlock.EnsureQueued");
                  propertyPEQ.SupportsMultipleBlocks = false;
                  propertyPEQ.Getter = (block) =>
                  {
                     return UtilsProductionBlock.EnsureQueued;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyPEQ);

                  var propertyNC4B = MyAPIGateway.TerminalControls.CreateProperty<Func<Sandbox.ModAPI.Ingame.IMyProjector, Dictionary<VRage.Game.MyDefinitionId, VRage.MyFixedPoint>, int>, IMyShipWelder>("BuildAndRepair.Inventory.NeededComponents4Blueprint");
                  propertyNC4B.SupportsMultipleBlocks = false;
                  propertyNC4B.Getter = (block) =>
                  {
                     return UtilsInventory.NeededComponents4Blueprint;
                  };
                  MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(propertyNC4B);
               }
            }
            catch (Exception ex)
            {
               Mod.Log.Write(Logging.Level.Error, "NanobotBuildAndRepairSystemTerminal: InitializeControls exception: {0}", ex);
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      private static void CreateCheckBoxAction(string name, IMyTerminalControlCheckbox checkbox)
      {
         var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_OnOff", name));
         action.Name = new StringBuilder(string.Format("{0} On/Off", name));
         action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
         action.Enabled = checkbox.Enabled;
         action.Action = (block) =>
         {
            checkbox.Setter(block, !checkbox.Getter(block));
         };
         action.ValidForGroups = checkbox.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

        action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", name));
         action.Name = new StringBuilder(string.Format("{0} On", name));
         action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
         action.Enabled = checkbox.Enabled;
         action.Action = (block) =>
         {
            checkbox.Setter(block, true);
         };
         action.ValidForGroups = checkbox.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

         action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Off", name));
         action.Name = new StringBuilder(string.Format("{0} Off", name));
         action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
         action.Enabled = checkbox.Enabled;
         action.Action = (block) =>
         {
            checkbox.Setter(block, false);
         };
         action.ValidForGroups = checkbox.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void CreateOnOffSwitchAction(string name, IMyTerminalControlOnOffSwitch onoffSwitch)
      {
         var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_OnOff", name));
         action.Name = new StringBuilder(string.Format("{0} {1}/{2}", name, onoffSwitch.OnText, onoffSwitch.OffText));
         action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
         action.Enabled = onoffSwitch.Enabled;
         action.Action = (block) =>
         {
            onoffSwitch.Setter(block, !onoffSwitch.Getter(block));
         };
         action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

         action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_On", name));
         action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OnText));
         action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
         action.Enabled = onoffSwitch.Enabled;
         action.Action = (block) =>
         {
            onoffSwitch.Setter(block, true);
         };
         action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

         action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Off", name));
         action.Name = new StringBuilder(string.Format("{0} {1}", name, onoffSwitch.OffText));
         action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
         action.Enabled = onoffSwitch.Enabled;
         action.Action = (block) =>
         {
            onoffSwitch.Setter(block, false);
         };
         action.ValidForGroups = onoffSwitch.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void CreateSliderActions(string sliderName, IMyTerminalControlSlider slider, int minValue, int maxValue)
      {
         var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Increase", sliderName));
         action.Name = new StringBuilder(string.Format("{0} Increase", sliderName));
         action.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
         action.Enabled = slider.Enabled;
         action.Action = (block) =>
         {
            var val = slider.Getter(block);
            if (val < maxValue)
               slider.Setter(block, val + 1);
         };
         action.ValidForGroups = slider.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

         action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Decrease", sliderName));
         action.Name = new StringBuilder(string.Format("{0} Decrease", sliderName));
         action.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
         action.Enabled = slider.Enabled;
         action.Action = (block) =>
         {
            var val = slider.Getter(block);
            if (val > minValue)
               slider.Setter(block, val - 1);
         };
         action.ValidForGroups = slider.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
      }

      /// <summary>
      /// 
      /// </summary>
      /// <typeparam name="T"></typeparam>
      /// <param name="control"></param>
      private static void CreateProperty<T>(IMyTerminalValueControl<T> control, bool readOnly = false)
      {
         var property = MyAPIGateway.TerminalControls.CreateProperty<T, IMyShipWelder>("BuildAndRepair." + control.Id);
         property.SupportsMultipleBlocks = false;
         property.Getter = control.Getter;
         if (!readOnly) property.Setter = control.Setter;
         MyAPIGateway.TerminalControls.AddControl<IMyShipWelder>(property);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void CreateSliderActionsArea(string sliderName, IMyTerminalControlSlider slider)
      {
         var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Increase", sliderName));
         action.Name = new StringBuilder(string.Format("{0} Increase", sliderName));
         action.Icon = @"Textures\GUI\Icons\Actions\Increase.dds";
         action.Enabled = slider.Enabled;
         action.Action = (block) =>
         {
            var system = GetSystem(block);
            var max = system != null && system.Settings != null ? system.Settings.MaximumRange : NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M;
            var val = slider.Getter(block);
            if (val < max)
               slider.Setter(block, val + 1);
         };
         action.ValidForGroups = slider.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);

         action = MyAPIGateway.TerminalControls.CreateAction<IMyShipWelder>(string.Format("{0}_Decrease", sliderName));
         action.Name = new StringBuilder(string.Format("{0} Decrease", sliderName));
         action.Icon = @"Textures\GUI\Icons\Actions\Decrease.dds";
         action.Enabled = slider.Enabled;
         action.Action = (block) =>
         {
            var min = NanobotBuildAndRepairSystemBlock.WELDER_RANGE_MIN_IN_M;
            var val = slider.Getter(block);
            if (val > min)
               slider.Setter(block, val - 1);
         };
         action.ValidForGroups = slider.SupportsMultipleBlocks;
         MyAPIGateway.TerminalControls.AddAction<IMyShipWelder>(action);
      }

      /// <summary>
      /// Callback to add custom controls
      /// </summary>
      private static void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
      {
         if (block.BlockDefinition.SubtypeName.StartsWith("SELtd") && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem"))
         {
            foreach (var item in CustomControls)
               controls.Add(item);
         }
      }
   }
}
