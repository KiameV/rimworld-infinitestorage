using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
    [StaticConstructorOnStartup]
    class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = HarmonyInstance.Create("com.InfiniteStorage.rimworld.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message("InfiniteStorage: Adding Harmony Postfix to Pawn_TraderTracker.DrawMedOperationsTab");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to Pawn_TraderTracker.ThingsInGroup");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to Pawn_TraderTracker.ColonyThingsWillingToBuy");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to TradeShip.ColonyThingsWillingToBuy");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to Window.PreClose");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to WorkGiver_DoBill.TryFindBestBillIngredients");
            Log.Message("InfiniteStorage: Adding Harmony Postfix to ReservationManager.CanReserve");
            Log.Message("InfiniteStorage: Adding Harmony Prefix to Designator_Build.ProcessInput - will block if looking for things.");
        }
    }

    [HarmonyPatch(typeof(HealthCardUtility), "DrawMedOperationsTab")]
    static class Patch_HealthCardUtility_DrawMedOperationsTab
    {
        static void Prefix(Pawn pawn)
        {
            Patch_ListerThings_ThingsInGroup.AvailableBodyParts = new List<Thing>();
            foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
            {
                if (storage.IsOperational && storage.Map == pawn.Map)
                {
                    Patch_ListerThings_ThingsInGroup.AvailableBodyParts.AddRange(storage.StoredThings);
                }
            }
        }

        static void Postfix()
        {
            if (Patch_ListerThings_ThingsInGroup.AvailableBodyParts != null)
            {
                Patch_ListerThings_ThingsInGroup.AvailableBodyParts.Clear();
                Patch_ListerThings_ThingsInGroup.AvailableBodyParts = null;
            }
        }
    }

    [HarmonyPatch(typeof(ListerThings), "ThingsInGroup")]
    static class Patch_ListerThings_ThingsInGroup
    {
        public static List<Thing> AvailableBodyParts = null;
        static void Postfix(ref List<Thing> __result, ThingRequestGroup group)
        {
            if (AvailableBodyParts != null)
            {
                __result.AddRange(AvailableBodyParts);
            }
        }
    }

    #region Used by the left hand tally
    [HarmonyPatch(typeof(ResourceCounter), "UpdateResourceCounts")]
    static class Patch_ResourceCounter_UpdateResourceCounts
    {
        static FieldInfo countedAmountsFI = null;
        static void Postfix(ResourceCounter __instance)
        {
            if (countedAmountsFI == null)
            {
                countedAmountsFI = typeof(ResourceCounter).GetField("countedAmounts", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            Dictionary<ThingDef, int> countedAmounts = (Dictionary<ThingDef, int>)countedAmountsFI.GetValue(__instance);

            foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
            {
                foreach (Thing thing in ts.StoredThings)
                {
                    countedAmounts[thing.def] = thing.stackCount;
                }
            }
        }
    }
    #endregion

    #region For right click hauling
    [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStoreCellFor")]
    static class Patch_StoreUtility_TryFindBestBetterStoreCellFor
    {
        static void Postfix(ref bool __result, Thing t, Pawn carrier, Map map, StoragePriority currentPriority, Faction faction, ref IntVec3 foundCell, bool needAccurateResult)
        {
            foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
            {
                if (storage.IsOperational && 
                    storage.settings.Priority > currentPriority && 
                    storage.settings.AllowedToAccept(t))
                {
                    currentPriority = storage.settings.Priority;
                    foundCell = storage.Position;
                    __result = true;
                }
            }
        }
    }
    #endregion

    #region Used for creating other buildings
    [HarmonyPatch(typeof(Designator_Build), "ProcessInput")]
    static class Patch_Designator_Build_ProcessInput
    {
        private static FieldInfo entDefFI = null;
        private static FieldInfo stuffDefFI = null;
        private static FieldInfo writeStuffFI = null;
        static bool Prefix(Designator_Build __instance, Event ev)
        {
            if (entDefFI == null)
            {
                entDefFI = typeof(Designator_Build).GetField("entDef", BindingFlags.NonPublic | BindingFlags.Instance);
                stuffDefFI = typeof(Designator_Build).GetField("stuffDef", BindingFlags.NonPublic | BindingFlags.Instance);
                writeStuffFI = typeof(Designator_Build).GetField("writeStuff", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            ThingDef thingDef = entDefFI.GetValue(__instance) as ThingDef;
            if (thingDef == null || !thingDef.MadeFromStuff || !WorldComp.HasInfiniteStorages)
            {
                return true;
            }

            Map map = Find.VisibleMap;
            List<FloatMenuOption> list = new List<FloatMenuOption>();

            foreach (ThingDef current in map.resourceCounter.AllCountedAmounts.Keys)
            {
                if (current.IsStuff && current.stuffProps.CanMake(thingDef) && (DebugSettings.godMode || map.listerThings.ThingsOfDef(current).Count > 0))
                {
                    ThingDef localStuffDef = current;
                    string labelCap = localStuffDef.LabelCap;
                    list.Add(new FloatMenuOption(labelCap, delegate
                    {
                        __instance.ProcessInput(ev);
                        Find.DesignatorManager.Select(__instance);
                        stuffDefFI.SetValue(__instance, current);
                        writeStuffFI.SetValue(__instance, true);
                    }, MenuOptionPriority.Default, null, null, 0f, null, null));
                }
            }

            foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
            {
                if (storage.Map != map || !storage.Spawned)
                    continue;

                foreach (Thing t in storage.StoredThings)
                {
                    ThingDef current = t.def;
                    if (current.IsStuff &&
                        current.stuffProps.CanMake(thingDef) &&
                        (DebugSettings.godMode || t.stackCount > 0))
                    {
                        string labelCap = current.LabelCap;
                        list.Add(new FloatMenuOption(labelCap, delegate
                        {
                            __instance.ProcessInput(ev);
                            Find.DesignatorManager.Select(__instance);
                            stuffDefFI.SetValue(__instance, current);
                            writeStuffFI.SetValue(__instance, true);
                        }, MenuOptionPriority.Default, null, null, 0f, null, null));
                    }
                }
            }

            if (list.Count == 0)
            {
                Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput);
            }
            else
            {
                FloatMenu floatMenu = new FloatMenu(list);
                floatMenu.vanishIfMouseDistant = true;
                Find.WindowStack.Add(floatMenu);
                Find.DesignatorManager.Select(__instance);
            }
            return false;
        }
    }
    /*    [HarmonyPatch(typeof(Messages), "Message", new Type[] { typeof(string), typeof(MessageTypeDef) })]
        static class Patch_Messages_Message
        {
            internal static bool PreventMessages = false;
            static bool Prefix()
            {
                return !PreventMessages;
            }
        }

        [HarmonyPatch(typeof(WindowStack), "Add")]
        static class Patch_WindowStack_Add
        {
            internal static bool PreventAdd = false;
            internal static List<FloatMenuOption> FloatMenuOptions = null;
            static bool Prefix(Window window)
            {
                if (PreventAdd && window != null && window is FloatMenu)
                {
                    FloatMenuOptions = typeof(FloatMenu).GetField("options", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window) as List<FloatMenuOption>;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Designator_Build), "ProcessInput")]
        static class Patch_Designator_Build_ProcessInput
        {
            private static FieldInfo entDefFI = null;
            private static FieldInfo stuffDefFI = null;
            private static FieldInfo writeStuffFI = null;

            static void Prefix()
            {
                if (WorldComp.HasInfiniteStorages)
                {
                    Patch_Messages_Message.PreventMessages = true;
                    Patch_WindowStack_Add.PreventAdd = true;
                }
            }

            static void Postfix(Designator_Build __instance, Event ev)
            {
                if (!WorldComp.HasInfiniteStorages)
                    return;

                Patch_Messages_Message.PreventMessages = false;
                Patch_WindowStack_Add.PreventAdd = false;

                if (entDefFI == null)
                {
                    entDefFI = typeof(Designator_Build).GetField("entDef", BindingFlags.NonPublic | BindingFlags.Instance);
                    stuffDefFI = typeof(Designator_Build).GetField("stuffDef", BindingFlags.NonPublic | BindingFlags.Instance);
                    writeStuffFI = typeof(Designator_Build).GetField("writeStuff", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                Map map = Find.VisibleMap;
                ThingDef thingDef = entDefFI.GetValue(__instance) as ThingDef;
                List<FloatMenuOption> list = new List<FloatMenuOption>();
                if (Patch_WindowStack_Add.FloatMenuOptions != null)
                {
                    list.AddRange(Patch_WindowStack_Add.FloatMenuOptions);
                    Patch_WindowStack_Add.FloatMenuOptions.Clear();
                }

                foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
                {
                    if (storage.Map != map || !storage.Spawned || !storage.IsOperational)
                        continue;

                    foreach (Thing t in storage.StoredThings)
                    {
                        try
                        {
                            ThingDef tDef = t.def;
                            if (tDef.IsStuff &&
                                tDef.stuffProps.CanMake(thingDef) &&
                                (DebugSettings.godMode || t.stackCount > 0))
                            {
                                string labelCap = tDef.LabelCap;
                                list.Add(new FloatMenuOption(labelCap, delegate
                                {
                                    __instance.ProcessInput(ev);
                                    Find.DesignatorManager.Select(__instance);
                                    stuffDefFI.SetValue(__instance, tDef);
                                    writeStuffFI.SetValue(__instance, true);
                                }, MenuOptionPriority.Default, null, null, 0f, null, null));
                            }
                        }
                        catch
                        {
    #if DEBUG
                            Log.Error("t: " + t.Label + " To Build: " + thingDef.defName);
    #endif
                        }
                    }
                }

                if (list.Count == 0)
                {
                    Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput);
                }
                else
                {
                    FloatMenu floatMenu = new FloatMenu(list);
                    floatMenu.vanishIfMouseDistant = true;
                    Find.WindowStack.Add(floatMenu);
                    Find.DesignatorManager.Select(__instance);
                }
            }

            private static bool CanMake(ThingDef mat, ThingDef toMake)
            {
                Log.Error("CanMake: mat is null [" + (mat == null).ToString() + "] toMake is null [" + (toMake == null).ToString() + "]");
                Log.Warning("    mat: " + mat.label + " has stuff cats: " + (mat.stuffCategories != null).ToString() + " count: " + ((mat.stuffCategories != null) ? mat.stuffCategories.Count.ToString() : "null").ToString());
                Log.Warning("    toMake: " + toMake.label + " has stuff cats: " + (toMake.stuffCategories != null).ToString() + " count: " + ((toMake.stuffCategories != null) ? mat.stuffCategories.Count.ToString() : "null").ToString());
                foreach (StuffCategoryDef matStuffDef in mat.stuffCategories)
                {
                    Log.Error("        matStuffDef: " + matStuffDef.defName);
                    foreach (StuffCategoryDef toMakeStuffDef in toMake.stuffCategories)
                    {
                        Log.Error("            toMakeStuffDef: " + toMakeStuffDef.defName);
                        if (matStuffDef == toMakeStuffDef)
                        {
                            Log.Error("Match");
                            return true;
                        }
                    }
                }
                Log.Error("No Match");
                return false;
            }
        }*/
    /*private static FieldInfo entDefFI = null;
    private static FieldInfo stuffDefFI = null;
    private static FieldInfo writeStuffFI = null;
    static bool Prefix(Designator_Build __instance, Event ev)
    {
        if (entDefFI == null)
        {
            entDefFI = typeof(Designator_Build).GetField("entDef", BindingFlags.NonPublic | BindingFlags.Instance);
            stuffDefFI = typeof(Designator_Build).GetField("stuffDef", BindingFlags.NonPublic | BindingFlags.Instance);
            writeStuffFI = typeof(Designator_Build).GetField("writeStuff", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        ThingDef thingDef = entDefFI.GetValue(__instance) as ThingDef;
        if (thingDef == null || !thingDef.MadeFromStuff || !WorldComp.HasInfiniteStorages)
        {
            return true;
        }

        Map map = Find.VisibleMap;
        List<FloatMenuOption> list = new List<FloatMenuOption>();

        foreach (ThingDef current in map.resourceCounter.AllCountedAmounts.Keys)
        {
            if (current.IsStuff && current.stuffProps.CanMake(thingDef) && (DebugSettings.godMode || map.listerThings.ThingsOfDef(current).Count > 0))
            {
                ThingDef localStuffDef = current;
                string labelCap = localStuffDef.LabelCap;
                list.Add(new FloatMenuOption(labelCap, delegate
                {
                    __instance.ProcessInput(ev);
                    Find.DesignatorManager.Select(__instance);
                    stuffDefFI.SetValue(__instance, current);
                    writeStuffFI.SetValue(__instance, true);
                }, MenuOptionPriority.Default, null, null, 0f, null, null));
            }
        }

        foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
        {
            if (ts.Map != map || !ts.Spawned || !ts.IsOperational)
                continue;

            foreach (Thing t in ts.StoredThings)
            {
                ThingDef current = t.def;
                if (current.IsStuff && 
                    current.stuffProps.CanMake(thingDef) && 
                    (DebugSettings.godMode || t.stackCount > 0))
                {
                    string labelCap = current.LabelCap;
                    list.Add(new FloatMenuOption(labelCap, delegate
                    {
                        __instance.ProcessInput(ev);
                        Find.DesignatorManager.Select(__instance);
                        stuffDefFI.SetValue(__instance, current);
                        writeStuffFI.SetValue(__instance, true);
                    }, MenuOptionPriority.Default, null, null, 0f, null, null));
                }
            }
        }

        if (list.Count == 0)
        {
            Messages.Message("NoStuffsToBuildWith".Translate(), MessageTypeDefOf.RejectInput);
        }
        else
        {
            FloatMenu floatMenu = new FloatMenu(list);
            floatMenu.vanishIfMouseDistant = true;
            Find.WindowStack.Add(floatMenu);
            Find.DesignatorManager.Select(__instance);
        }
        return false;
    }*/
    #endregion

    struct ThingsToUse
    {
        public readonly Building_InfiniteStorage Storage;
        public readonly int Count;
        public readonly Thing Thing;
        public ThingsToUse(Building_InfiniteStorage storage, Thing thing, int count)
        {
#if DEBUG || DROP_DEBUG
            Log.Warning(" new ThingsToUse: " + thing.def.label + " " + count);
#endif
            this.Storage = storage;
            this.Thing = thing;
            this.Count = count;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
    static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
    {
        static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
        {
            if (__result == false && WorldComp.HasInfiniteStorages)
            {
#if DEBUG || DROP_DEBUG
                Log.Warning("TryFindBestBillIngredients.Postfix");
#endif
                Stack<ThingsToUse> usedTexiltes = new Stack<ThingsToUse>();
                foreach (IngredientCount ingredientCount in bill.recipe.ingredients)
                {
#if DEBUG || DROP_DEBUG
                    Log.Warning("   ingredientCount GetBaseCount: " + ingredientCount.GetBaseCount());
#endif
                    int ingCount = (int)ingredientCount.GetBaseCount();
                    foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
                    {
                        if (ts.Spawned && ts.Map == pawn.Map)
                        {
#if DEBUG || DROP_DEBUG
                            System.Text.StringBuilder sb = new System.Text.StringBuilder("        Ings: ");
                            foreach (ThingDef d in ingredientCount.filter.AllowedThingDefs) sb.Append(d.label + ", ");
                            Log.Warning(sb.ToString());
                            sb = new System.Text.StringBuilder("        Bill.Ings: ");
                            foreach (ThingDef d in bill.ingredientFilter.AllowedThingDefs) sb.Append(d.label + ", ");
                            Log.Warning(sb.ToString());
#endif
                            foreach (Thing t in ts.StoredThings)
                            {
#if DEBUG || DROP_DEBUG
                                sb = new System.Text.StringBuilder("            Found: ");
                                sb.Append(t.Label);
                                sb.Append("-ingFilter:");
                                sb.Append(ingredientCount.filter.Allows(t).ToString());
                                sb.Append("-bill.IngFilter:");
                                sb.Append(bill.ingredientFilter.Allows(t).ToString());
#endif
                                if (t != null &&
                                    bill.ingredientFilter.Allows(t))
                                {
                                    if (t.stackCount >= ingCount)
                                    {
                                        usedTexiltes.Push(new ThingsToUse(ts, t, ingCount));
                                        ingCount = 0;
                                        break;
                                    }

                                    if (bill.recipe.allowMixingIngredients)
                                    {
                                        // Subtract from the needed ingrediants since it's known the stackCount is less
                                        ingCount -= t.stackCount;
                                        usedTexiltes.Push(new ThingsToUse(ts, t, t.stackCount));
                                    }
                                }
#if DEBUG || DROP_DEBUG
                                Log.Warning(sb.ToString());
#endif
                            }
                        }
                    }

                    if (ingCount == 0)
                    {
                        __result = true;
                        while (usedTexiltes.Count > 0)
                        {
                            ThingsToUse t = usedTexiltes.Pop();
                            t.Storage.Remove(t.Thing, t.Count);
                            BuildingUtil.DropThing(t.Thing, t.Count, t.Storage, t.Storage.Map, false);
                        }
                    }
                    usedTexiltes.Clear();
                }
            }
        }
    }

    /*   [HarmonyPatch(typeof(WorkGiver_DoBill), "TryStartNewDoBillJob")]
       static class Patch_WorkGiver_DoBill_TryStartNewDoBillJob
       {
           static List<ThingsToUse> ThingsToUse = new List<ThingsToUse>();
           internal static void ClearThingsToUse()
           {
               ThingsToUse.Clear();
           }
           internal static void AddThingTouse(ThingsToUse thingToUse)
           {
               ThingsToUse.Add(thingToUse);
           }

           static void Postfix(Job __result)
           {
               if (ThingsToUse != null)
               {
                   try
                   {
                       if (__result != null && 
                           __result.def == JobDefOf.DoBill)
                       {
                           foreach(ThingsToUse ttu in ThingsToUse)
                           {
                               BuildingUtil.DropThing(ttu.Thing, ttu.Count, ttu.Storage, ttu.Storage.Map, false, null);
                           }
                       }
                   }
                   finally
                   {
                       ClearThingsToUse();
                   }
               }
           }
       }

       [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
       static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
       {
           static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
           {
               Patch_WorkGiver_DoBill_TryStartNewDoBillJob.ClearThingsToUse();
               if (__result == false && WorldComp.HasInfiniteStorages)
               {
#if DEBUG || DROP_DEBUG
                   Log.Warning("TryFindBestBillIngredients.Postfix");
#endif
                   foreach (IngredientCount ingredientCount in bill.recipe.ingredients)
                   {
#if DEBUG || DROP_DEBUG
                       Log.Warning("   ingredientCount GetBaseCount: " + ingredientCount.GetBaseCount());
#endif
                       int ingCount = (int)ingredientCount.GetBaseCount();
                       foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
                       {
                           bool found = false;
                           if (storage.Spawned && storage.Map == pawn.Map && storage.IsOperational)
                           {
#if DEBUG || DROP_DEBUG
                               // System.Text.StringBuilder sb = new System.Text.StringBuilder("        Ings: ");
                               //foreach (ThingDef d in ingredientCount.filter.AllowedThingDefs) sb.Append(d.label + ", ");
                               //Log.Warning(sb.ToString());
                               System.Text.StringBuilder sb = new System.Text.StringBuilder("        Bill.Ings: ");
                               foreach (ThingDef d in bill.ingredientFilter.AllowedThingDefs) sb.Append(d.label + ", ");
                               Log.Warning(sb.ToString());
#endif
                               foreach (Thing thing in storage.StoredThings)
                               {
#if DEBUG || DROP_DEBUG
                                   sb = new System.Text.StringBuilder("            Found: ");
                                   sb.Append(thing.Label);
                                  // sb.Append("-ingFilter:");
                                  // sb.Append(ingredientCount.filter.Allows(thing).ToString());
                                   sb.Append("-bill.IngFilter:");
                                   sb.Append(bill.ingredientFilter.Allows(thing).ToString());
#endif
                                   if (thing != null &&
                                       bill.ingredientFilter.Allows(thing) &&
                                       thing.stackCount >= ingCount)
                                   {
                                       //Log.Warning("Drop Thing: " + thing.Label + "\n" + System.Environment.StackTrace.ToString());
                                       //BuildingUtil.DropThing(thing, ingCount, storage, storage.Map, chosen);
                                       chosen.Add(new ThingAmount(thing, ingCount));
                                       Patch_WorkGiver_DoBill_TryStartNewDoBillJob.AddThingTouse(
                                           new ThingsToUse(storage, thing, ingCount));
                                       break;
                                   }
#if DEBUG || DROP_DEBUG
                                   Log.Warning(sb.ToString());
#endif
                               }
                               if (found)
                                   break;
                           }
                       }
                   }
               }
           }
       }*/

    [HarmonyPatch(typeof(ItemAvailability), "ThingsAvailableAnywhere")]
    static class Patch_ItemAvailability_ThingsAvailableAnywhere
    {
        private static FieldInfo cachedResultsFI = null;
        private static FieldInfo CachedResultsFI
        {
            get
            {
                if (cachedResultsFI == null)
                {
                    cachedResultsFI = typeof(ItemAvailability).GetField("cachedResults", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                return cachedResultsFI;
            }
        }

        static void Postfix(ref bool __result, ItemAvailability __instance, ThingCountClass need, Pawn pawn)
        {
            if (!__result && pawn != null && pawn.Faction == Faction.OfPlayer)
            {
                foreach (Building_InfiniteStorage storage in WorldComp.InfiniteStorages)
                {
                    Thing thing;
                    if (storage.IsOperational && storage.Spawned && storage.TryGetValue(need.thingDef, out thing))
                    {
                        if (thing.stackCount >= need.count)
                        {
                            storage.Remove(thing, need.count);
                            BuildingUtil.DropThing(thing, need.count, storage, storage.Map, false);

                            __result = true;
                            ((Dictionary<int, bool>)CachedResultsFI.GetValue(__instance))[Gen.HashCombine<Faction>(need.GetHashCode(), pawn.Faction)] = __result;
                            return;
                        }
                    }
                }
            }
        }
    }

    static class ReservationManagerUtil
    {
        private static FieldInfo mapFI = null;
        public static Map GetMap(ReservationManager mgr)
        {
            if (mapFI == null)
            {
                mapFI = typeof(ReservationManager).GetField("map", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return (Map)mapFI.GetValue(mgr);
        }

        public static bool IsInfiniteStorageAt(Map map, IntVec3 position)
        {
            IEnumerable<Thing> things = map.thingGrid.ThingsAt(position);
            if (things != null)
            {
                foreach (Thing t in things)
                {
                    if (t.GetType() == typeof(Building_InfiniteStorage))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "Reserve")]
    static class Patch_ReservationManager_Reserve
    {
        static bool Prefix(ref bool __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target)
        {
            Map map = ReservationManagerUtil.GetMap(__instance);
            if (!__result && target != null && target.IsValid && !target.ThingDestroyed &&
                ReservationManagerUtil.IsInfiniteStorageAt(map, target.Cell))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "Release")]
    static class Patch_ReservationManager_Release
    {
        static bool Prefix(ReservationManager __instance, Pawn claimant, LocalTargetInfo target)
        {
            Map map = ReservationManagerUtil.GetMap(__instance);
            if (target != null && target.IsValid && !target.ThingDestroyed &&
                ReservationManagerUtil.IsInfiniteStorageAt(map, target.Cell))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "CanReserve")]
    static class Patch_ReservationManager_CanReserve
    {
        static bool Prefix(ref bool __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target)
        {
            Map map = ReservationManagerUtil.GetMap(__instance);
            if (!__result && target != null && target.IsValid && !target.ThingDestroyed &&
                ReservationManagerUtil.IsInfiniteStorageAt(map, target.Cell))
            {
                __result = true;
                return false;
            }
            return true;
        }
        /*
        private static FieldInfo mapFI = null;
        static void Postfix(ref bool __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target, int maxPawns, int stackCount, ReservationLayerDef layer, bool ignoreOtherReservations)
        {
            if (mapFI == null)
            {
                mapFI = typeof(ReservationManager).GetField("map", BindingFlags.NonPublic | BindingFlags.Instance);
            }

#if DEBUG
            Log.Warning("\nCanReserve original result: " + __result);
#endif
            if (!__result && mapFI != null && (target.Thing == null || target.Thing.def.thingClass.Equals("InfiniteStorage.Building_InfiniteStorage")))
            {
                Map m = (Map)mapFI.GetValue(__instance);
                if (m != null)
                {
                    IEnumerable<Thing> things = m.thingGrid.ThingsAt(target.Cell);
                    if (things != null)
                    {
#if DEBUG
                        Log.Warning("CanReserve - Found things");
#endif
                        foreach (Thing t in things)
                        {
#if DEBUG
                            Log.Warning("CanReserve - def " + t.def.defName);
#endif
                            if (t.def.thingClass.Equals("InfiniteStorage.Building_InfiniteStorage"))
                            {
#if DEBUG
                                Log.Warning("CanReserve is now true\n");
#endif
                                __result = true;
                            }
                        }
                    }
                }
            }
        }*/
    }

    #region Trades
    static class TradeUtil
    {
        public static IEnumerable<Thing> EmptyStorages(Map map)
        {
            List<Thing> l = new List<Thing>();
            foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
            {
                if (ts.Map == map && ts.Spawned && ts.IncludeInTradeDeals)
                {
                    ts.Empty(l);
                }
            }
            return l;
        }

        public static void ReclaimThings()
        {
            foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
            {
                if (ts.Map != null && ts.Spawned)
                {
                    ts.Reclaim();
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_TraderTracker), "ColonyThingsWillingToBuy")]
    static class Patch_TradeShip_ColonyThingsWillingToBuy
    {
        // Before a caravan trade
        static void Postfix(ref IEnumerable<Thing> __result, Pawn playerNegotiator)
        {
            if (playerNegotiator != null && playerNegotiator.Map != null)
            {
                List<Thing> result = new List<Thing>(__result);
                result.AddRange(TradeUtil.EmptyStorages(playerNegotiator.Map));
                __result = result;
            }
        }
    }

    [HarmonyPatch(typeof(TradeShip), "ColonyThingsWillingToBuy")]
    static class Patch_PassingShip_TryOpenComms
    {
        // Before an orbital trade
        static void Postfix(ref IEnumerable<Thing> __result, Pawn playerNegotiator)
        {
            if (playerNegotiator != null && playerNegotiator.Map != null)
            {
                List<Thing> result = new List<Thing>(__result);
                result.AddRange(TradeUtil.EmptyStorages(playerNegotiator.Map));
                __result = result;
            }
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), "Close")]
    static class Patch_Window_PreClose
    {
        static void Postfix(bool doCloseSound)
        {
            TradeUtil.ReclaimThings();
        }
    }
    #endregion

    /*#region Caravan Forming

    [HarmonyPatch(typeof(Window), "PreOpen")]
    static class Patch_Window_PreOpen
    {
        static void Prefix(Window __instance)
        {
            Type type = __instance.GetType();
            if (type == typeof(Dialog_FormCaravan))
            {
                Map map = __instance.GetType().GetField("map", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as Map;
                TradeUtil.EmptyStorages(map);
            }
        }
    }
#endregion*/

    #region Handle "Do until X" for stored weapons
    [HarmonyPatch(typeof(RecipeWorkerCounter), "CountProducts")]
    static class Patch_RecipeWorkerCounter_CountProducts
    {
        static void Postfix(ref int __result, RecipeWorkerCounter __instance, Bill_Production bill)
        {
            List<ThingCountClass> products = __instance.recipe.products;
            if (WorldComp.HasInfiniteStorages && products != null)
            {
                foreach (ThingCountClass product in products)
                {
                    ThingDef def = product.thingDef;
                    foreach (Building_InfiniteStorage s in WorldComp.InfiniteStorages)
                    {
                        if (bill.Map == s.Map)
                        {
                            __result += s.StoredThingCount(def);
                        }
                    }
                }
            }
        }
    }
    #endregion
}