using HarmonyLib;
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
    partial class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("com.InfiniteStorage.rimworld.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            Log.Message(
               "InfiniteStorage Harmony Patches:" + Environment.NewLine +
               "  Prefix:" + Environment.NewLine +
               "    Designator_Build.ProcessInput - will block if looking for things." + Environment.NewLine +
               "    ScribeSaver.InitSaving" + Environment.NewLine +
               "    SettlementAbandonUtility.Abandon" + Environment.NewLine +
               "  Postfix:" + Environment.NewLine +
               "    Pawn_TraderTracker.DrawMedOperationsTab" + Environment.NewLine +
               "    Pawn_TraderTracker.ThingsInGroup" + Environment.NewLine +
               "    Pawn_TraderTracker.ColonyThingsWillingToBuy" + Environment.NewLine +
               "    TradeShip.ColonyThingsWillingToBuy" + Environment.NewLine +
               "    Window.PreClose" + Environment.NewLine +
               "    WorkGiver_DoBill.TryFindBestBillIngredients");
        }
    }

    [HarmonyPatch(typeof(SettlementAbandonUtility), "Abandon")]
    static class Patch_SettlementAbandonUtility_Abandon
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(MapParent settlement)
        {
            WorldComp.Remove(settlement.Map);
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

            foreach (Building_InfiniteStorage ts in WorldComp.GetInfiniteStorages(Find.CurrentMap))
            {
                foreach (Thing thing in ts.StoredThings)
                {
                    if (thing.def.EverStorable(true) && thing.def.CountAsResource && !thing.IsNotFresh())
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

    #region Find Ammo
    [HarmonyPatch(typeof(JobDriver_ManTurret), "FindAmmoForTurret")]
    static class JobDriver_ManTurret_FindAmmoForTurret
    {
        static void Prefix(Pawn pawn, Building_TurretGun gun)
        {
#if DEBUG
            Log.Warning("Find Ammo");
#endif
            if (pawn.IsColonist && pawn.Map == gun.Map)
            {
                StorageSettings allowedShellsSettings = gun.gun.TryGetComp<CompChangeableProjectile>().allowedShellsSettings;
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(gun.Map))
                {
#if DEBUG
                    Log.Warning("    Storeage: " + storage.Label);
#endif
                    List<Thing> l;
                    if (storage.TryRemove(allowedShellsSettings.filter, out l))
                    {
                        foreach (Thing t in l)
                        {
#if DEBUG
                        Log.Warning("        Ammo fouynd: " + t.Label);
#endif
                            List<Thing> dropped = new List<Thing>();
                            BuildingUtil.DropThing(t, t.stackCount, storage, storage.Map, dropped);
                        }
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

            Map map = Find.CurrentMap;

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
                    if (storage.IsOperational &&
                        storage.Spawned &&
                        need != null && need.thing != null)
                    {
                        Thing thing;
                        if (storage.TryGetValue(need.thing.def, out thing))
                        {
                            if (thing.stackCount >= need.Count)
                            {
                                List<Thing> removed;
                                int toDrop = (need.Count < thing.def.stackLimit) ? thing.def.stackLimit : need.Count;
                                if (storage.TryRemove(thing, toDrop, out removed))
                                {
                                    foreach (Thing t in removed)
                                    {
                                        BuildingUtil.DropThing(t, t.stackCount, storage, storage.Map);
                                    }

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
    }

    [HarmonyPatch(typeof(ScribeSaver), "InitSaving")]
    static class Patch_ScribeSaver_InitSaving
    {
        static void Prefix()
        {
            try
            {
                foreach (Building_InfiniteStorage s in WorldComp.GetAllInfiniteStorages())
                {
                    try
                    {
                        s.ForceReclaim();
                    }
                    catch (Exception e)
                    {
                        Log.Warning("Error while reclaiming apparel for infinite storage\n" + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("Error while reclaiming items\n" + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(MoveColonyUtility), "MoveColonyAndReset")]
    static class Patch_MoveColonyUtility_MoveColonyAndReset
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            WorldComp.ClearAll();
        }
    }

    [HarmonyPatch(typeof(Building_Storage), "Accepts")]
    static class Patch_Building_Storage_Accepts
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Building_Storage __instance, ref bool __result, Thing t)
        {
            if (__instance is Building_InfiniteStorage s)
            {
                __result = s.DoesAccept(t);
                return false;
            }
            return true;
        }
    }

    #region Feed Self (for animals)
    /*[HarmonyPatch(typeof(FoodUtility), "TryFindBestFoodSourceFor")]
    static class Patch_FoodUtility_TryFindBestFoodSourceFor
    {
        static void Postfix(
            bool __result, Pawn getter, Pawn eater, bool desperate, ref Thing foodSource, ref ThingDef foodDef,
            bool canRefillDispenser, bool canUseInventory, bool allowForbidden,
            bool allowCorpse, bool allowSociallyImproper, bool allowHarvest)
        {
            if (eater == null || eater.needs == null || eater.needs.food == null || eater.Map == null)
                return;

            RaceProperties race = eater.def.race;
#if DEBUG || DROP_DEBUG
            Log.Warning("Patch_FoodUtility_TryFindBestFoodSourceFor.Postfix eater: [" + eater.Label + "] IsAnimal: [" + race.Animal + "] Faction: [" + eater.Faction + "] hasTrough: [" + WorldComp.HasNonGlobalInfiniteStorages(eater.Map) + "]");
#endif
            if (!__result &&
                race.Animal &&
                eater.needs.food.CurCategory >= HungerCategory.Hungry &&
                eater.Faction == Faction.OfPlayer &&
                WorldComp.HasNonGlobalInfiniteStorages(eater.Map))
            {
                bool eatsHay = race.Eats(FoodTypeFlags.Plant);
                bool eatsKibble = race.Eats(FoodTypeFlags.Kibble);
                int hayNeeded = FoodUtility.WillIngestStackCountOf(eater, ThingDefOf.Hay);
                int kibbleNeeded = FoodUtility.WillIngestStackCountOf(eater, ThingDefOf.Kibble);
#if DEBUG || DROP_DEBUG
                Log.Warning("    eatsHay: [" + eatsHay + "] eatsKibble: [" + eatsKibble + "] hayNeeded: [" + hayNeeded + "] kibbleNeeded: [" + kibbleNeeded + "]");
#endif
                if (eatsHay || eatsKibble)
                {
                    foreach (Building_InfiniteStorage trough in WorldComp.GetNonGlobalInfiniteStorages(eater.Map))
                    {
#if DEBUG || DROP_DEBUG
                        Log.Warning("    Trough: [" + trough.Label + "]");
#endif
                        if (eater.Map.reachability.CanReach(eater.Position, trough, PathEndMode.Touch, TraverseMode.PassDoors))
                        {
                            List<Thing> l = null;
                            if (eatsHay)
                            {
                                __result = trough.TryRemove(ThingDefOf.Hay, hayNeeded, out l);
#if DEBUG || DROP_DEBUG
                                Log.Warning("        Hay: [" + __result + "] " + ListToString(l));
#endif
                            }
                            if (!__result && eatsKibble)
                            {
                                __result = trough.TryRemove(ThingDefOf.Kibble, kibbleNeeded, out l);
#if DEBUG || DROP_DEBUG
                                Log.Warning("        Kibble: [" + __result + "]");
#endif
                            }

                            if (__result && l != null)
                            {
#if DEBUG || DROP_DEBUG
                                Log.Warning("        Drop: [" + foodSource.Label + "]");
#endif
                                try
                                {
                                    __result = false;
                                    foreach (Thing t in l)
                                    {
                                        foodSource = t;
                                        foodDef = t.def;
                                        if (BuildingUtil.DropThing(foodSource, foodSource.stackCount, trough, eater.Map, false))
                                        {
                                            __result = true;
                                        }
#if DEBUG || DROP_DEBUG
                                        Log.Warning("            foodSource: [" + foodSource.Label + "] foodDef: [" + foodDef.label + "]");
#endif
                                    }
                                }
                                catch (Exception e)
                                {
#if DEBUG || DROP_DEBUG
                                    Log.Warning("            Exception: " + e.Message + "\n" + e.StackTrace);
#endif
                                }
                                break;
                            }
                        }
                    }
                }
            }
#if DEBUG || DROP_DEBUG
            Log.Warning("    result: " + __result + " foodSource: [" + foodSource.Label + "] foodDef: [" + foodDef.label + "]");
#endif
        }

#if DEBUG || DROP_DEBUG
        static string ListToString<T>(List<T> l) where T : Thing
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[");
            if (l != null)
            {
                foreach (T t in l)
                {
                    if (sb.Length > 1)
                        sb.Append(", ");
                    if (t == null)
                        sb.Append("null");
                    else
                    {
                        sb.Append(t.Label);
                    }
                }
                sb.Append("]");
            }
            else
                sb.Append("null list");
            return sb.ToString();
        }
#endif
    }*/
    #endregion

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

    [HarmonyPatch(typeof(TradeDeal), "Reset")]
    static class Patch_TradeDeal_Reset
    {
        // On Reset from Trade Dialog
        static void Prefix()
        {
            TradeUtil.ReclaimThings();
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

                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                {
                    storage.Empty();
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
            foreach (Building_InfiniteStorage storage in WorldComp.GetAllInfiniteStorages())
            {
                storage.Reclaim();
            }
        }
    }

    [HarmonyPatch(
        typeof(CaravanExitMapUtility), "ExitMapAndCreateCaravan",
        new Type[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    static class Patch_CaravanExitMapUtility_ExitMapAndCreateCaravan
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(IEnumerable<Pawn> pawns, Faction faction, int exitFromTile, int directionTile, int destinationTile, bool sendMessage)
        {
            if (faction == Faction.OfPlayer)
            {
                List<Pawn> p = new List<Pawn>(pawns);
                if (p.Count > 0)
                {
                    foreach (Building_InfiniteStorage storage in WorldComp.GetAllInfiniteStorages())
                    {
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
            List<ThingDefCountClass> products = __instance.recipe.products;
            if (WorldComp.HasInfiniteStorages(bill.Map) && products != null)
            {
                foreach (ThingDefCountClass product in products)
                {
                    ThingDef def = product.thingDef;
                    foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStorages(bill.Map))
                    {
                        __result += s.StoredThingCount(def, bill.ingredientFilter);
                    }
                }
            }
        }
    }
    #endregion

    #region Fix Broken Tool
    [HarmonyPatch(typeof(WorkGiver_FixBrokenDownBuilding), "FindClosestComponent")]
    static class Patch_WorkGiver_FixBrokenDownBuilding_FindClosestComponent
    {
        static void Postfix(Thing __result, Pawn pawn)
        {
            bool found = false;
            if (pawn != null && __result == null)
            {
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(pawn.Map))
                {
                    if (storage.TryRemove(ThingDefOf.ComponentIndustrial, 1, out List<Thing>  list))
                    {
                        found = true;
                        foreach (Thing t in list)
                        {
                            BuildingUtil.DropThing(t, 1, storage, storage.Map, null);
                        }
                    }
                }
                if (found)
                {
                    __result = GenClosest.ClosestThingReachable(
                        pawn.Position, pawn.Map, ThingRequest.ForDef(ThingDefOf.ComponentIndustrial), PathEndMode.InteractionCell, TraverseParms.For(pawn, pawn.NormalMaxDanger(),
                        TraverseMode.ByPawn, false), 9999f, (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x, 1, -1, null, false), null, 0, -1, false, RegionType.Set_Passable, false);
                }
            }
        }
    }
    #endregion
}