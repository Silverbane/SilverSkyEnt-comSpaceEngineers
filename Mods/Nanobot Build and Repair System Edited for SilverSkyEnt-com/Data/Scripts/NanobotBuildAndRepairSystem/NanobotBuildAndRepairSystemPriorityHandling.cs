namespace SpaceEquipmentLtd.NanobotBuildAndRepairSystem
{
   using System;
   using System.Collections.Generic;
   using System.Linq;
   using Utils;
   using VRage.Game;
   using VRage.Game.ModAPI;
   using VRage.ModAPI;
   using VRage.Utils;

   public enum BlockClass
   {
      AutoRepairSystem = 1,
      ShipController,
      Thruster,
      Gyroscope,
      CargoContainer,
      Conveyor,
      ControllableGun,
      PowerBlock,
      ProgrammableBlock,
      Projector,
      FunctionalBlock,
      ProductionBlock,
      Door,
      ArmorBlock
   }

   public enum ComponentClass
   {
      Material = 1,
      Ingot,
      Ore,
      Stone,
      Gravel
   }

   public class ClassState<T> where T : struct
   {
      public T ItemClass { get; }
      public bool Enabled { get; set; }
      public ClassState(T itemClass, bool enabled)
      {
         ItemClass = itemClass;
         Enabled = enabled;
      }
   }

   public abstract class NanobotBuildAndRepairSystemPriorityHandling<C, I> : List<ClassState<C>>  where C : struct
   {
      private bool _HashDirty = true;
      private List<string> _ClassList = new List<string>();
      private Dictionary<C, int> _PrioHash = new Dictionary<C, int>();

      internal C? Selected { get; set; } //Visual

      internal NanobotBuildAndRepairSystemPriorityHandling()
      {
         foreach (C itemClass in Enum.GetValues(typeof(C)))
         {
            Add(new ClassState<C>(itemClass, true));
         }
      }

      /// <summary>
      /// Retrieve the build/repair priority of the item.
      /// </summary>
      internal int GetPriority(I a)
      {
         var itemClass = GetItemClass(a, false);
         if (_HashDirty) GenerateHash();
         return _PrioHash[itemClass];
      }

      /// <summary>
      /// Retrieve if the build/repair of this item kind is enabled.
      /// </summary>
      internal bool GetEnabled(I a)
      {
         var itemClass = GetItemClass(a, true);
         if (_HashDirty) GenerateHash();
         return _PrioHash[itemClass] < int.MaxValue;
      }

      /// <summary>
      /// Get the item class
      /// </summary>
      /// <param name="a"></param>
      /// <returns></returns>
      protected abstract C GetItemClass(I a, bool real);

      /// <summary>
      /// 
      /// </summary>
      /// <param name="items"></param>
      internal void FillTerminalList(List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
      {
         foreach (var entry in this)
         {
            var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(string.Format("({0}) {1}", entry.Enabled ? "X" : "-", entry.ItemClass.ToString())), MyStringId.NullOrEmpty, entry.ItemClass);
            items.Add(item);

            if (entry.ItemClass.Equals(Selected))
            {
               selected.Add(item);
            }
         }
      }

      internal void MoveSelectedUp()
      {
         if (Selected != null)
         {
            var currentPrio = FindIndex((kv) => kv.ItemClass.Equals(Selected));
            if (currentPrio > 0)
            {
               this.Move(currentPrio, currentPrio - 1);
               _HashDirty = true;
            }
         }
      }

      internal void MoveSelectedDown()
      {
         if (Selected != null)
         {
            var currentPrio = FindIndex((kv) => kv.ItemClass.Equals(Selected));
            if (currentPrio >= 0 && currentPrio < Count - 1)
            {
               this.Move(currentPrio, currentPrio + 1);
               _HashDirty = true;
            }
         }
      }

      internal void ToggleEnabled()
      {
         if (Selected != null)
         {
            var keyValue = this.FirstOrDefault((kv) => kv.ItemClass.Equals(Selected));
            if (keyValue != null)
            {
               keyValue.Enabled = !keyValue.Enabled;
               _HashDirty = true;
            }
         }
      }

      internal int GetPriority(int itemClass)
      {
         return FindIndex((kv) => Convert.ToInt32(kv.ItemClass) == itemClass);
      }

      internal void SetPriority(int itemClass, int prio)
      {
         if (prio >= 0 && prio < Count)
         {
            var currentPrio = FindIndex((kv) => Convert.ToInt32(kv.ItemClass) == itemClass);
            if (currentPrio >= 0)
            {
               this.Move(currentPrio, prio);
               _HashDirty = true;
            }
         }
      }

      internal bool GetEnabled(int itemClass)
      {
         var keyValue = this.FirstOrDefault((kv) => Convert.ToInt32(kv.ItemClass) == itemClass);
         return keyValue != null ? keyValue.Enabled : false;
      }

      internal void SetEnabled(int itemClass, bool enabled)
      {
         var keyValue = this.FirstOrDefault((kv) => Convert.ToInt32(kv.ItemClass) == itemClass);
         if (keyValue != null)
         {
            if (keyValue.Enabled = enabled)
            {
               keyValue.Enabled = enabled;
               _HashDirty = true;
            }
         }
      }

      public bool AnyEnabled
      {
         get
         {
            return this.Any(i => i.Enabled);
         }
      }

      internal string GetEntries()
      {
         var value = string.Empty;
         foreach (var entry in this)
         {
            value += string.Format("{0};{1}|", Convert.ToInt32(entry.ItemClass), entry.Enabled);
         }
         return value.Remove(value.Length - 1);
      }

      internal void SetEntries(string value)
      {
         if (value == null) return;
         var entries = value.Split('|');
         var prio = 0;
         foreach (var val in entries)
         {
            var itemClassValue = 0;
            var enabled = true;
            var values = val.Split(';');
            if (values.Length >= 2 &&
                int.TryParse(values[0], out itemClassValue) &&
                bool.TryParse(values[1], out enabled))
            {
               var keyValue = this.FirstOrDefault((kv) => Convert.ToInt32(kv.ItemClass) == itemClassValue);
               if (keyValue != null)
               {
                  keyValue.Enabled = enabled;
                  var currentPrio = IndexOf(keyValue);
                  this.Move(currentPrio, prio);
                  prio++;
               }
            }
         }
         _HashDirty = true;
      }

      internal List<string> GetList()
      {
         if (_HashDirty) GenerateHash();
         return _ClassList;
      }

      private void GenerateHash()
      {
         lock (_ClassList)
         {
            if (_HashDirty) //Second check now thread safe
            {
               _ClassList.Clear();
               foreach (var item in this)
               {
                  _ClassList.Add(string.Format("{0};{1}", item.ItemClass, item.Enabled));
               }
               _PrioHash.Clear();
               var prio = 1;
               foreach (var item in this)
               {
                  _PrioHash.Add(item.ItemClass, item.Enabled ? prio : int.MaxValue);
                  prio++;
               }
            }
            _HashDirty = false;
         }
      }
   }

   public class NanobotBuildAndRepairSystemBlockPriorityHandling : NanobotBuildAndRepairSystemPriorityHandling<BlockClass, IMySlimBlock>
   {
      /// <summary>
      /// Get the Block class
      /// </summary>
      /// <param name="a"></param>
      /// <returns></returns>
      protected override BlockClass GetItemClass(IMySlimBlock a, bool real)
      {
         var block = a.FatBlock;
         if (block == null) return BlockClass.ArmorBlock;
         var functionalBlock = block as Sandbox.ModAPI.IMyFunctionalBlock;
         if (!real && functionalBlock != null && !functionalBlock.Enabled) return BlockClass.ArmorBlock; //Switched off -> handle as structural block (if logical class is asked)

         if (block is Sandbox.ModAPI.IMyShipWelder && block.BlockDefinition.SubtypeName.Contains("NanobotBuildAndRepairSystem")) return BlockClass.AutoRepairSystem;
         if (block is Sandbox.ModAPI.IMyShipController) return BlockClass.ShipController;
         if (block is Sandbox.ModAPI.IMyThrust) return BlockClass.Thruster;
         if (block is Sandbox.ModAPI.IMyGyro) return BlockClass.Gyroscope;
         if (block is Sandbox.ModAPI.IMyCargoContainer) return BlockClass.CargoContainer;
         if (block is Sandbox.ModAPI.IMyConveyor || a.FatBlock is Sandbox.ModAPI.IMyConveyorSorter || a.FatBlock is Sandbox.ModAPI.IMyConveyorTube) return BlockClass.Conveyor;
         if (block is Sandbox.ModAPI.IMyUserControllableGun) return BlockClass.ControllableGun;
         if (block is Sandbox.ModAPI.IMyReactor || block is Sandbox.ModAPI.IMyBatteryBlock || block is SpaceEngineers.Game.ModAPI.IMySolarPanel) return BlockClass.PowerBlock;
         if (block is Sandbox.ModAPI.IMyProgrammableBlock) return BlockClass.ProgrammableBlock;
         if (block is SpaceEngineers.Game.ModAPI.IMyTimerBlock) return BlockClass.ProgrammableBlock;
         if (block is Sandbox.ModAPI.IMyProjector) return BlockClass.Projector;
         if (block is Sandbox.ModAPI.IMyDoor) return BlockClass.Door;
         if (block is Sandbox.ModAPI.IMyProductionBlock) return BlockClass.ProductionBlock;
         if (functionalBlock != null) return BlockClass.FunctionalBlock;

         return BlockClass.ArmorBlock;
      }
   }

   public class NanobotBuildAndRepairSystemComponentPriorityHandling : NanobotBuildAndRepairSystemPriorityHandling<ComponentClass, MyDefinitionId>
   {
      /// <summary>
      /// Get the Block class
      /// </summary>
      /// <param name="a"></param>
      /// <returns></returns>
      protected override ComponentClass GetItemClass(MyDefinitionId a, bool real)
      {
         if (a.TypeId == typeof(MyObjectBuilder_Ingot))
         {
            if (a.SubtypeName == "Stone") return ComponentClass.Gravel;
            return ComponentClass.Ingot;
         }
         if (a.TypeId == typeof(MyObjectBuilder_Ore))
         {
            if (a.SubtypeName == "Stone") return ComponentClass.Stone;
            return ComponentClass.Ore;
         }
         return ComponentClass.Material;
      }
   }
}
