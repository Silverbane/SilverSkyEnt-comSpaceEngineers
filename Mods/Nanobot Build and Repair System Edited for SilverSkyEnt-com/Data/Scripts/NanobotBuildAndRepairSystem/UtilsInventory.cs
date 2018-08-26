using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SpaceEquipmentLtd.Utils
{
   public static class UtilsInventory
   {
      /// <summary>
      /// Check if all inventories are empty
      /// </summary>
      /// <param name="entity"></param>
      /// <returns></returns>
      public static bool InventoriesEmpty(this IMyEntity entity)
      {
         if (!entity.HasInventory) return true;
         for (int i1 = 0; i1 < entity.InventoryCount; ++i1)
         {
            var srcInventory = entity.GetInventory(i1) as IMyInventory;
            if (!srcInventory.Empty()) return false;
         }
         return true;
      }

      /// <summary>
      /// Push all components into destinations
      /// </summary>
      public static bool PushComponents(this IMyInventory srcInventory, List<IMyInventory> destinations)
      {
         var moved = false;
         lock (destinations)
         {
            var srcItems = srcInventory.GetItems();
            for (int srcItemIndex = srcItems.Count-1; srcItemIndex >= 0; srcItemIndex--)
            {
               var srcItem = srcItems[srcItemIndex];
               moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, true) || moved;
            }

            if (!moved) {
               srcItems = srcInventory.GetItems();
               for (int srcItemIndex = srcItems.Count - 1; srcItemIndex >= 0; srcItemIndex--)
               {
                  var srcItem = srcItems[srcItemIndex];
                  moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, false) || moved;
               }
            }
         }
         return moved;
      }

      /// <summary>
      /// Push given items into destinations
      /// </summary>
      public static bool PushComponents(this IMyInventory srcInventory, List<IMyInventory> destinations, int srcItemIndex, IMyInventoryItem srcItem)
      {
         var moved = false;
         lock (destinations)
         {
            moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, true);
            if (!moved)
            {
               moved = TryTransferItemTo(srcInventory, destinations, srcItemIndex, srcItem, false);
            }
         }
         return moved;
      }

      /// <summary>
      /// As long as ComputeAmountThatFits is not available for modding we have to try
      /// </summary>
      public static VRage.MyFixedPoint MaxItemsAddable(this IMyInventory destInventory, VRage.MyFixedPoint maxNeeded, VRage.ObjectBuilders.SerializableDefinitionId contentId)
      {
         if (destInventory.CanItemsBeAdded(maxNeeded, contentId))
         {
            return maxNeeded;
         }

         int maxPossible = 0;
         int currentStep = Math.Max((int)maxNeeded / 2, 1);
         int currentTry = 0;
         while (currentStep > 0)
         {
            currentTry = maxPossible + currentStep;
            if (destInventory.CanItemsBeAdded(currentTry, contentId))
            {
               maxPossible = currentTry;
            }
            else
            {
               if (currentStep <= 1) break;
            }
            if (currentStep > 1) currentStep = currentStep / 2;
         }
         return maxPossible;
      }

      /// <summary>
      /// Moves as many as possible from srcInventory to destinations
      /// </summary>
      private static bool TryTransferItemTo(IMyInventory srcInventory, List<IMyInventory> destinations, int srcItemIndex, IMyInventoryItem srcItem, bool all)
      {
         var moved = false;
         if (all)
         {
            foreach (var destInventory in destinations)
            {
               if (destInventory.CanItemsBeAdded(srcItem.Amount, srcItem.Content.GetId()))
               {
                  moved = srcInventory.TransferItemTo(destInventory, srcItemIndex, null, true, srcItem.Amount) || moved;
                  break;
               }
            }
            return moved;
         }

         foreach (var destInventory in destinations)
         {
            var amount = destInventory.MaxItemsAddable(srcItem.Amount, srcItem.Content.GetId());
            if (amount > 0) {
               moved = srcInventory.TransferItemTo(destInventory, srcItemIndex, null, true, amount) || moved;
               if (srcItem.Amount <= 0) break;
            }
         }
         return moved;
      }

      /// <summary>
      /// Retrieve the total amount of componets to build a blueprint
      /// (blueprint loaded inside projector)
      /// </summary>
      /// <param name="projector"></param>
      /// <param name="componentList"></param>
      public static int NeededComponents4Blueprint(Sandbox.ModAPI.Ingame.IMyProjector srcProjector, Dictionary<MyDefinitionId, VRage.MyFixedPoint> componentList)
      {
         var projector = srcProjector as IMyProjector;
         if (componentList == null || projector == null || !projector.IsProjecting) return -1;

         //Add buildable blocks
         var projectedCubeGrid = projector.ProjectedGrid;
         if (projectedCubeGrid != null)
         {
            var projectedBlocks = new List<IMySlimBlock>();
            projectedCubeGrid.GetBlocks(projectedBlocks);
            foreach (IMySlimBlock block in projectedBlocks)
            {
               var blockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
               foreach(var component in blockDefinition.Components)
               {
                  if (componentList.ContainsKey(component.Definition.Id)) componentList[component.Definition.Id] += component.Count;
                  else componentList[component.Definition.Id] = component.Count;
               }
            }
         }
         return componentList.Count();
      }
   }
}
