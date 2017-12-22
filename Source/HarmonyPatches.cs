using Harmony;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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
        [HarmonyPriority(Priority.First)]
        static void Prefix(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing != null)
            {
                Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
            }

            Patch_ListerThings_ThingsInGroup.AvailableMedicalThing = new List<Thing>();
            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(pawn.Map))
            {
                if (storage.IsOperational && storage.Map == pawn.Map)
                {
                    foreach (Thing t in storage.StoredThings)
                    {
                        if (t.def.IsDrug || t.def.isBodyPartOrImplant)
                        {
                            Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.AddRange(storage.StoredThings);
                        }
                    }
                }
            }
        }

        static void Postfix()
        {
            if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing != null)
            {
                Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
                Patch_ListerThings_ThingsInGroup.AvailableMedicalThing = null;
            }
        }
    }

    [HarmonyPatch(typeof(ListerThings), "ThingsInGroup")]
    static class Patch_ListerThings_ThingsInGroup
    {
        public static List<Thing> AvailableMedicalThing = null;
        static void Postfix(ref List<Thing> __result, ThingRequestGroup group)
        {
            if (AvailableMedicalThing != null)
            {
                __result.AddRange(AvailableMedicalThing);
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

            foreach (Building_InfiniteStorage ts in WorldComp.GetInfiniteStorages(Find.VisibleMap))
            {
                foreach (Thing thing in ts.StoredThings)
                {
                    if (thing.def.EverStoreable && thing.def.CountAsResource && !thing.IsNotFresh())
                    {
                        int count;
                        if (countedAmounts.TryGetValue(thing.def, out count))
                        {
                            count += thing.stackCount;
                        }
                        else
                        {
                            count = thing.stackCount;
                        }
                        countedAmounts[thing.def] = count;
                    }
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

            Map map = Find.VisibleMap;

            ThingDef thingDef = entDefFI.GetValue(__instance) as ThingDef;
            if (thingDef == null || !thingDef.MadeFromStuff || !WorldComp.HasInfiniteStorages(map))
            {
                return true;
            }

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

            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
            {
                if (storage.Spawned)
                {
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
    #endregion

    #region For bills
    struct ThingsToUse
    {
        public readonly Building_InfiniteStorage Storage;
        public readonly int Count;
        public readonly Thing Thing;
        public ThingsToUse(Building_InfiniteStorage storage, Thing thing, int count)
        {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("    new ThingsToUse: " + thing.def.label + " " + count + " " + storage.Label);
#endif
            this.Storage = storage;
            this.Thing = thing;
            this.Count = count;
            if (this.Count == 0)
            {
                this.Count = this.Thing.stackCount;
                if (this.Count == 0)
                {
                    this.Count = 1;
                }
            }
        }
    }

    struct ThingLookup
    {
        public readonly ThingDef Def;
        public ThingLookup(ThingDef def) { this.Def = def; }
        public override int GetHashCode()
        {
            return this.Def.GetHashCode();
        }
    }

    struct NeededIngrediants
    {
        public readonly ThingFilter Filter;
        public int Count;
        public NeededIngrediants(ThingFilter filter, int count)
        {
            this.Filter = filter;
            this.Count = count;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
    static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
    {
        static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
        {
            if (bill.Map == null)
            {
                Log.Error("Bill's map is null");
            }

            if (__result == true || !WorldComp.HasInfiniteStorages(bill.Map) || bill.Map != pawn.Map)
                return;

#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("TryFindBestBillIngredients.Postfix __result: " + __result);
#endif
            Dictionary<ThingLookup, int> chosenAmounts = new Dictionary<ThingLookup, int>();
            foreach (ThingAmount c in chosen)
            {
                ThingLookup tl = new ThingLookup(c.thing.def);
                int count;
                if (chosenAmounts.TryGetValue(tl, out count))
                {
                    count += c.count;
                }
                else
                {
                    count = c.count;
                }
                chosenAmounts[tl] = count;
            }

#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("    ChosenAmounts:");
            foreach (KeyValuePair<ThingLookup, int> kv in chosenAmounts)
            {
                Log.Warning("        " + kv.Key.Def.label + " - " + kv.Value);
            }
#endif

            LinkedList<NeededIngrediants> neededIngs = new LinkedList<NeededIngrediants>();
            foreach (IngredientCount ing in bill.recipe.ingredients)
            {
                bool found = false;
                foreach (KeyValuePair<ThingLookup, int> kv in chosenAmounts)
                {
                    if ((int)ing.GetBaseCount() == kv.Value)
                    {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                        Log.Warning("    Needed Ing population count is the same");
#endif
                        if (ing.filter.Allows(kv.Key.Def))
                        {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                            Log.Warning("    Needed Ing population found: " + kv.Key.Def.label + " count: " + kv.Value);
#endif
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                    Log.Warning("    Needed Ing population not found");
#endif
                    neededIngs.AddLast(new NeededIngrediants(ing.filter, (int)ing.GetBaseCount()));
                }
            }

#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("    Needed Ings:");
            foreach (NeededIngrediants ings in neededIngs)
            {
                Log.Warning("        " + ings.Count);
            }
#endif

            List<ThingsToUse> thingsToUse = new List<ThingsToUse>();
            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
            {
                LinkedListNode<NeededIngrediants> n = neededIngs.First;
                while (n != null)
                {
                    var next = n.Next;
                    NeededIngrediants neededIng = n.Value;

                    List<Thing> gotten;
                    if (storage.TryGetFilteredThings(neededIng.Filter, out gotten))
                    {
                        foreach (Thing got in gotten)
                        {
                            int count = (got.stackCount > neededIng.Count) ? neededIng.Count : got.stackCount;
                            thingsToUse.Add(new ThingsToUse(storage, got, count));
                            neededIng.Count -= count;
                        }
                        if (neededIng.Count == 0)
                        {
                            neededIngs.Remove(n);
                        }
                    }
                    n = next;
                }
            }

#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("    neededIngs.count: " + neededIngs.Count);
#endif

            if (neededIngs.Count == 0)
            {
                __result = true;
                foreach (ThingsToUse ttu in thingsToUse)
                {
                    Thing removed;
                    if (ttu.Storage.TryRemove(ttu.Thing, ttu.Count, out removed))
                    {
                        List<Thing> dropped = new List<Thing>();
                        BuildingUtil.DropThing(removed, removed.stackCount, ttu.Storage, ttu.Storage.Map, false, dropped);
                        foreach (Thing t in dropped)
                        {
                            chosen.Add(new ThingAmount(t, t.stackCount));
                        }
                    }
                }
            }

            thingsToUse.Clear();
            neededIngs.Clear();
            chosenAmounts.Clear();
        }
    }
    /*{
        static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
        {
            if (__result == false && WorldComp.HasInfiniteStorages)
            {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                Log.Warning("TryFindBestBillIngredients.Postfix");
#endif
                Stack<ThingsToUse> usedTexiltes = new Stack<ThingsToUse>();
                foreach (IngredientCount ing in bill.recipe.ingredients)
                {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                    Log.Warning("   ingredientCount GetBaseCount: " + ing.GetBaseCount());
#endif
                    int ingCountNeeded = (int)ing.GetBaseCount();
                    foreach (ThingAmount c in chosen)
                    {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                        Log.Warning("   ThingAmount: " + c.thing.Label + c.count);
#endif
                        if (ing.filter.Allows(c.thing))
                        {
                            if (ingCountNeeded < c.count)
                            {

                            }
                            else
                            {
                                ingCountNeeded -= c.count;
                            }
                            break;
                        }
                    }
                    if (ingCountNeeded > 0)


                        foreach (Building_InfiniteStorage ts in WorldComp.InfiniteStorages)
                        {
                            if (ts.Spawned && ts.Map == pawn.Map)
                            {
#if DEBUG || DROP_DEBUG
                                System.Text.StringBuilder sb = new System.Text.StringBuilder("        Ings: ");
                                foreach (ThingDef d in ing.filter.AllowedThingDefs) sb.Append(d.label + ", ");
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
                                    sb.Append(ing.filter.Allows(t).ToString());
                                    sb.Append("-bill.IngFilter:");
                                    sb.Append(bill.ingredientFilter.Allows(t).ToString());
#endif
                                    if (t != null &&
                                        bill.ingredientFilter.Allows(t))
                                    {
                                        if (t.stackCount >= ingCountNeeded)
                                        {
                                            usedTexiltes.Push(new ThingsToUse(ts, t, ingCountNeeded));
                                            ingCountNeeded = 0;
                                            break;
                                        }

                                        if (bill.recipe.allowMixingIngredients)
                                        {
                                            // Subtract from the needed ingrediants since it's known the stackCount is less
                                            ingCountNeeded -= t.stackCount;
                                            usedTexiltes.Push(new ThingsToUse(ts, t, t.stackCount));
                                        }
                                    }
#if DEBUG || DROP_DEBUG
                                    Log.Warning(sb.ToString());
#endif
                                }
                            }
                        }

                    if (ingCountNeeded == 0)
                    {
                        __result = true;
                        while (usedTexiltes.Count > 0)
                        {
                            ThingsToUse t = usedTexiltes.Pop();
                            Thing removed;
                            if (t.Storage.TryRemove(t.Thing, t.Count, out removed))
                            {
                                BuildingUtil.DropThing(removed, removed.stackCount, t.Storage, t.Storage.Map, false);
                            }
                        }
                    }
                    usedTexiltes.Clear();
                }
            }
        }
    }*/
    #endregion

    [HarmonyPatch(typeof(WorkGiver_Refuel), "FindBestFuel")]
    static class Patch_WorkGiver_Refuel_FindBestFuel
    {
        private static Dictionary<Thing, Building_InfiniteStorage> droppedAndStorage = null;
        static void Prefix(Pawn pawn, Thing refuelable)
        {
            if (WorldComp.HasInfiniteStorages(refuelable.Map))
            {
                droppedAndStorage = new Dictionary<Thing, Building_InfiniteStorage>();

                ThingFilter filter = refuelable.TryGetComp<CompRefuelable>().Props.fuelFilter;

                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(refuelable.Map))
                {
                    if (storage.Spawned && storage.Map == pawn.Map && storage.IsOperational)
                    {
                        Thing t;
                        if (storage.TryRemove(filter, out t))
                        {
                            List<Thing> removedThings = new List<Thing>();
                            BuildingUtil.DropThing(t, t.def.stackLimit, storage, storage.Map, false, removedThings);
                            if (removedThings.Count > 0)
                                droppedAndStorage.Add(removedThings[0], storage);
                        }
                    }
                }
            }
        }

        static void Postfix(Thing __result)
        {
            if (droppedAndStorage != null)
            {
                foreach (KeyValuePair<Thing, Building_InfiniteStorage> kv in droppedAndStorage)
                {
                    if (kv.Key != __result)
                    {
                        kv.Value.Add(kv.Key);
                    }
                }
                droppedAndStorage.Clear();
            }
        }
    }

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
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(pawn.Map))
                {
                    Thing thing;
                    if (storage.IsOperational &&
                        storage.Spawned &&
                        storage.TryGetValue(need.thingDef, out thing))
                    {
                        if (thing.stackCount >= need.count)
                        {
                            Thing removed;
                            int toDrop = (need.count < thing.def.stackLimit) ? thing.def.stackLimit : need.count;
                            if (storage.TryRemove(thing, toDrop, out removed))
                            {
                                BuildingUtil.DropThing(removed, removed.stackCount, storage, storage.Map, false);

                                __result = true;
                                ((Dictionary<int, bool>)CachedResultsFI.GetValue(__instance))[Gen.HashCombine<Faction>(need.GetHashCode(), pawn.Faction)] = __result;
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    #region Reserve
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
    }
    #endregion

    #region Trades
    static class TradeUtil
    {
        public static IEnumerable<Thing> EmptyStorages(Map map)
        {
            List<Thing> l = new List<Thing>();
            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
            {
                if (storage.Map == map && storage.Spawned && storage.IncludeInTradeDeals)
                {
                    storage.Empty(l);
                }
            }
            return l;
        }

        public static void ReclaimThings()
        {
            foreach (Building_InfiniteStorage storage in WorldComp.GetAllInfiniteStorages())
            {
                if (storage.Map != null && storage.Spawned)
                {
                    storage.Reclaim();
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
        [HarmonyPriority(Priority.First)]
        static void Postfix(bool doCloseSound)
        {
            TradeUtil.ReclaimThings();
        }
    }
    #endregion

    #region Caravan Forming
    [HarmonyPatch(typeof(Dialog_FormCaravan), "PostOpen")]
    static class Patch_Dialog_FormCaravan_PostOpen
    {
        static void Prefix(Window __instance)
        {
            Type type = __instance.GetType();
            if (type == typeof(Dialog_FormCaravan))
            {
                Map map = __instance.GetType().GetField("map", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as Map;
                TradeUtil.EmptyStorages(map);

                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                {
                    storage.CanAutoCollect = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CaravanFormingUtility), "StopFormingCaravan")]
    static class Patch_CaravanFormingUtility_StopFormingCaravan
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(Lord lord)
        {
            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(lord.Map))
            {
                storage.CanAutoCollect = true;
                storage.Reclaim();
            }
        }
    }

    [HarmonyPatch(
        typeof(CaravanExitMapUtility), "ExitMapAndCreateCaravan", 
        new Type[] { typeof (IEnumerable<Pawn>), typeof (Faction), typeof(int), typeof(int) })]
    static class Patch_CaravanExitMapUtility_ExitMapAndCreateCaravan_1
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(IEnumerable<Pawn> pawns, Faction faction, int exitFromTile, int directionTile)
        {
            if (faction == Faction.OfPlayer)
            {
                List<Pawn> p = new List<Pawn>(pawns);
                if (p.Count > 0)
                {
                    foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(p[0].Map))
                    {
                        storage.CanAutoCollect = true;
                        storage.Reclaim();
                    }
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(CaravanExitMapUtility), "ExitMapAndCreateCaravan",
        new Type[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(int) })]
    static class Patch_CaravanExitMapUtility_ExitMapAndCreateCaravan_2
    {
        static void Prefix(IEnumerable<Pawn> pawns, Faction faction, int startingTile)
        {
            if (faction == Faction.OfPlayer)
            {
                List<Pawn> p = new List<Pawn>(pawns);
                if (p.Count > 0)
                {
                    foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(p[0].Map))
                    {
                        storage.CanAutoCollect = true;
                        storage.Reclaim();
                    }
                }
            }
        }
    }
    #endregion

    #region Handle "Do until X" for stored weapons
    [HarmonyPatch(typeof(RecipeWorkerCounter), "CountProducts")]
    static class Patch_RecipeWorkerCounter_CountProducts
    {
        static void Postfix(ref int __result, RecipeWorkerCounter __instance, Bill_Production bill)
        {
            if (bill.Map == null)
            {
                Log.Error("Bill has null map");
            }

            List<ThingCountClass> products = __instance.recipe.products;
            if (WorldComp.HasInfiniteStorages(bill.Map) && products != null)
            {
                foreach (ThingCountClass product in products)
                {
                    ThingDef def = product.thingDef;
                    foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStorages(bill.Map))
                    {
                        __result += s.StoredThingCount(def);
                    }
                }
            }
        }
    }
    #endregion
}