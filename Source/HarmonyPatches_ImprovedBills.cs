using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
    /*class StoredDef : Thing
    {
        public readonly Building_InfiniteStorage Storage;
        public readonly ThingDef Def;
        public readonly int Count;
        public readonly bool Forced;
        public StoredDef(Building_InfiniteStorage storage, ThingDef def, int count, bool forced)
        {
            this.Storage = storage;
            this.Def = def;
            this.Count = count;
            this.Forced = forced;
        }
        public StoredDef(List<Building_InfiniteStorage> storages, IntVec3 center, ThingDef def, int count, bool forced)
        {
            if (storages.Count == 1)
                this.Storage = storages[0];
            else
                this.Storage = GenClosest.ClosestThing_Global(center, storages, 1000f) as Building_InfiniteStorage;
            this.Def = def;
            this.Count = count;
            this.Forced = forced;
        }
    }


    [HarmonyPatch(typeof(Pawn_JobTracker), "StartJob")]
    static class Patch_Pawn_JobTracker_StartJob
    {
        static void Prefix(Pawn_JobTracker __instance, Job newJob, JobCondition lastJobEndCondition, ThinkNode jobGiver, bool resumeCurJobAfterwards, bool cancelBusyStances, ThinkTreeDef thinkTree, JobTag? tag, bool fromQueue)
        {
            if (newJob != null && newJob.targetB.Thing is StoredDef sd)
            {
                if (sd.Storage.TryRemove(sd.Def, sd.Count, out List<Thing> things))
                {
                    Pawn pawn = __instance.GetType().GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as Pawn;

                    try
                    {
                        sd.Storage.AllowAdds = false;

                        IntVec3 pos;
                        if (sd.Forced)
                            pos = pawn.Position;
                        else
                            pos = sd.Storage.InteractionCell;

                        if (things.Count == 1)
                        {
                            if (BuildingUtil.DropSingleThing(things[0], pos, pawn.Map, out Thing result))
                            {
                                newJob.targetB = result;
                                newJob.count = sd.Count;
                            }
                            else
                            {
                                Log.Error("Could not drop " + things[0]);
                                sd.Storage.Add(things[0]);
                            }
                        }
                        else
                        {
                            newJob.targetB = null;
                            newJob.count = 0;
                            Log.Error("Does not support multiple drop");
                        }
                    }
                    finally
                    {
                        sd.Storage.AllowAdds = true;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_FixBrokenDownBuilding), "HasJobOnThing")]
    static class Patch_WorkGiver_FixBrokenDownBuilding_HasJobOnThing
    {
        internal static StoredDef StoredDef = null;
        static void Postfix(WorkGiver_FixBrokenDownBuilding __instance, ref bool __result, Pawn pawn, Thing t, bool forced)
        {
            if (__result == false)
            {
                Building building = t as Building;
                LocalTargetInfo target = building;
                if (building == null ||
                    pawn == null ||
                    !building.def.building.repairable ||
                    t.Faction != pawn.Faction ||
                    t.IsForbidden(pawn) ||
                    !t.IsBrokenDown() ||
                    pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[t.Position] ||
                    !pawn.CanReserve(target, 1, -1, null, forced) ||
                    pawn.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null ||
                    building.IsBurning())
                {
                    __result = false;
                    return;
                }
            }

            List<Building_InfiniteStorage> storages = new List<Building_InfiniteStorage>();
            foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStorages(pawn.Map))
            {
                if (s.storedThings.TryGetValue(ThingDefOf.ComponentIndustrial.defName, out LinkedList<Thing> l) && 
                    l.Count > 0)
                {
                    storages.Add(s);
                }
            }
            if (storages.Count > 0)
            {
                __result = true;
                StoredDef = new StoredDef(storages, pawn.Position, ThingDefOf.ComponentIndustrial, 1, forced);
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_FixBrokenDownBuilding), "JobOnThing")]
    static class Patch_WorkGiver_FixBrokenDownBuilding_JobOnThing
    {
        public static StoredDef StoredDef = null;
        static void Postfix(WorkGiver_FixBrokenDownBuilding __instance, ref Job __result, Pawn pawn, Thing t, bool forced)
        {
            StoredDef sd = Patch_WorkGiver_FixBrokenDownBuilding_HasJobOnThing.StoredDef;
            if (sd != null)
            {
                if (__result == null || 
                    forced || 
                    IsStorageCloser(__result.targetA.Thing, sd.Storage, pawn))
                {
                    __result = new Job(JobDefOf.FixBrokenDownBuilding, t, sd)
                    {
                        count = 1
                    };
                }
            }
        }

        private static bool IsStorageCloser(Thing thing, Building_InfiniteStorage storage, Pawn pawn)
        {
            List<Thing> toSearch = new List<Thing>(2) { thing, storage };
            return storage == GenClosest.ClosestThing_Global(pawn.Position, toSearch);
        }
    }

    
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
    static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
    {
        static void Postfix(ref bool __result, WorkGiver_DoBill __instance, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            if (__result == false &&
                pawn != null &&
                bill != null &&
                bill.recipe != null &&
                bill.Map == pawn.Map)
            {
                if (bill.recipe.defName.StartsWith("Mend") || bill.recipe.defName.StartsWith("Recycle"))
                {
                    IEnumerable<Building_InfiniteStorage> storages = WorldComp.GetInfiniteStorages(bill.Map);
                    if (storages == null)
                    {
                        Log.Message("MendingChangeDresserPatch failed to retrieve InfiniteStorages");
                        return;
                    }

                    foreach (Building_InfiniteStorage storage in storages)
                    {
                        if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius)
                        {
                            Thing t;
                            if (storage.TryGetFirstFilteredItemForMending(bill, bill.ingredientFilter, true, out t))
                            {
                                if (t.Spawned == false)
                                {
                                    Log.Error("Failed to spawn item-to-mend [" + t.Label + "] from storage [" + storage.Label + "].");
                                    __result = false;
                                    chosen = null;
                                }
                                else
                                {
                                    __result = true;
                                    chosen.Add(new ThingCount(t, 1));
                                }
                                return;
                            }
                        }
                    }
                }
                else
                {
                    __result = HarmonyPatches.TryFindBestBillIngredients(bill, pawn, billGiver, chosen);
                }
            }
        }
    }

    struct FoundIng
    {
        public Building_InfiniteStorage storage;
        public Thing Thing;
        public int Count;

        public FoundIng(Building_InfiniteStorage s, Thing thing, int count)
        {
            this.storage = s;
            this.Thing = thing;
            this.Count = count;
        }
    }

    class IngsNeeded
    {
        public ThingFilter filter;
        public float needed;
        public List<FoundIng> FoundIngs = new List<FoundIng>();
        public IngsNeeded(ThingFilter filter, float needed)
        {
#if BILL_DEBUG
			Log.Warning("            IngsNeeded " + needed);
#endif
            this.filter = filter;
            this.needed = needed;
        }

        internal void AddFoundIng(FoundIng foundIng, float factor)
        {
#if BILL_DEBUG
			Log.Warning("            IngsNeeded.AddFoundIng " + foundIng.Thing.def.defName + " " + (foundIng.Count * factor));
#endif
            this.needed -= foundIng.Count * factor;
            if (needed < 0.0001f)
                needed = 0f;
            this.FoundIngs.Add(foundIng);
        }
    }
    partial class HarmonyPatches
    {
        private static List<Thing> newRelevantThings = null;
        private static List<Thing> relevantThings;
        private static HashSet<Thing> processedThings;
        private static List<IngredientCount> ingredientsOrdered;
        private static List<Thing> tmpMedicine;
        private static Dictionary<int, FoundIng> foundIngs = new Dictionary<int, FoundIng>();

        private static void Init()
        {
            if (newRelevantThings == null)
            {
                newRelevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                relevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                processedThings = (HashSet<Thing>)typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                ingredientsOrdered = (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                tmpMedicine = (List<Thing>)typeof(WorkGiver_DoBill).GetField("tmpMedicine", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
#if BILL_DEBUG
				if (newRelevantThings == null)
					Log.Error("newRelevantThings is null");
				if (relevantThings == null)
					Log.Error("relevantThings is null");
				if (processedThings == null)
					Log.Error("processedThings is null");
				if (ingredientsOrdered == null)
					Log.Error("ingredientsOrdered is null");
				if (tmpMedicine == null)
					Log.Error("tmpMedicine is null");
#endif
            }
        }

        // RimWorld.WorkGiver_DoBill
        public static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            Init();
            foundIngs.Clear();
            chosen.Clear();
            newRelevantThings.Clear();
            if (bill.recipe.ingredients.Count == 0)
            {
                return true;
            }
            IntVec3 rootCell = (IntVec3)typeof(WorkGiver_DoBill).GetMethod("GetBillGiverRootCell", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { billGiver, pawn });
            Region rootReg = rootCell.GetRegion(pawn.Map, RegionType.Set_Passable);
            if (rootReg == null)
            {
                return false;
            }
            typeof(WorkGiver_DoBill).GetMethod("MakeIngredientsListInProcessingOrder", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { ingredientsOrdered, bill });
            relevantThings.Clear();
            processedThings.Clear();
            bool foundAll = false;
            Predicate<Thing> baseValidator = (Thing t) => t.Spawned && !t.IsForbidden(pawn) && (float)(t.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius && bill.IsFixedOrAllowedIngredient(t) && bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t)) && pawn.CanReserve(t, 1, -1, null, false);
            bool billGiverIsPawn = billGiver is Pawn;
            if (billGiverIsPawn)
            {
                AddEveryMedicineToRelevantThings(pawn, billGiver, relevantThings, baseValidator, pawn.Map, bill);
                if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
                {
                    DropChosen(chosen);

                    relevantThings.Clear();
                    ingredientsOrdered.Clear();
                    foundIngs.Clear();
                    return true;
                }
            }
            TraverseParms traverseParams = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
            RegionEntryPredicate entryCondition = (Region from, Region r) => r.Allows(traverseParams, false);
            int adjacentRegionsAvailable = rootReg.Neighbors.Count((Region region) => entryCondition(rootReg, region));
            int regionsProcessed = 0;
            processedThings.AddRange(relevantThings);

            Comparison<Thing> comparison = delegate (Thing t1, Thing t2)
            {
                float num = (float)(t1.Position - rootCell).LengthHorizontalSquared;
                float value = (float)(t2.Position - rootCell).LengthHorizontalSquared;
                return num.CompareTo(value);
            };

            RegionProcessor regionProcessor = delegate (Region r)
            {
                List<Thing> list = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
                for (int i = 0; i < list.Count; i++)
                {
                    Thing thing = list[i];
                    if (!processedThings.Contains(thing))
                    {
                        if (ReachabilityWithinRegion.ThingFromRegionListerReachable(thing, r, PathEndMode.ClosestTouch, pawn))
                        {
                            if (baseValidator(thing) && (!thing.def.IsMedicine || !billGiverIsPawn))
                            {
                                newRelevantThings.Add(thing);
                                processedThings.Add(thing);
                            }
                        }
                    }
                }
                regionsProcessed++;
                if (newRelevantThings.Count > 0 && regionsProcessed > adjacentRegionsAvailable)
                {
                    /*Comparison<Thing> comparison = delegate (Thing t1, Thing t2)
					{
						float num = (float)(t1.Position - rootCell).LengthHorizontalSquared;
						float value = (float)(t2.Position - rootCell).LengthHorizontalSquared;
						return num.CompareTo(value);
					};* /
    newRelevantThings.Sort(comparison);
                    relevantThings.AddRange(newRelevantThings);
                    newRelevantThings.Clear();
                    if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
                    {
                        foundAll = true;
                        return true;
                    }
                }
                return false;
            };

            foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStoragesWithinRadius(pawn.Map, billGiver.Position, bill.ingredientSearchRadius))
            {
                foreach (IngredientCount ing in bill.recipe.ingredients)
                {
#if BILL_DEBUG
					//Log.Warning("        need ing count: " + needed);
#endif
                    foreach (LinkedList<Thing> l in s.storedThings.Values)
                    {
                        if (l != null && l.Count > 0 && ing.filter.Allows(l.First.Value.def))
                        {
                            foreach (Thing t in l)
                            {
                                if (ing.filter.Allows(t))
                                {
                                    newRelevantThings.Add(t);
                                    int count = (int)(ing.GetBaseCount() / bill.recipe.IngredientValueGetter.ValuePerUnitOf(t.def));
                                    if (count > t.stackCount)
                                        count = t.stackCount;
                                    foundIngs[t.thingIDNumber] = new FoundIng(s, t, count);
                                }
                            }
                        }
                    }
                }
            }
            newRelevantThings.Sort(comparison);
            relevantThings.AddRange(newRelevantThings);
            newRelevantThings.Clear();
            if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
            {
                foundAll = true;
            }
            else if (foundIngs.Count > 0)
            {
                RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999, RegionType.Set_Passable);
            }

            relevantThings.Clear();
            newRelevantThings.Clear();
            processedThings.Clear();
            ingredientsOrdered.Clear();

#if BILL_DEBUG
			Log.Warning("Found All: " + foundAll);
			Log.Warning("Chosen: " + chosen.Count);
			chosen.ForEach(v => Log.Warning("    - " + v.Thing.ThingID + " -- " + v.Count));
			Log.Warning("FoundIngs: " + foundIngs.Count);
			foreach (FoundIng f in foundIngs.Values)
				Log.Warning("    - " + f.Thing.ThingID + " -- " + f.Count + " -- " + f.storage.ThingID);
#endif

            if (foundAll && DropChosen(chosen))
            {
                foundIngs.Clear();
                return true;
            }
            foundIngs.Clear();
            return false;
        }

        private static bool DropChosen(List<ThingCount> chosen)
        {
            List<ThingCount> newChosen = new List<ThingCount>();
            foreach (var tc in chosen)
            {
                FoundIng f;
                if (foundIngs.TryGetValue(tc.Thing.thingIDNumber, out f))
                {
                    if (f.storage != null)
                    {
                        List<Thing> removed;
                        if (f.storage.TryRemove(f.Thing, tc.Count, out removed))
                        {
                            foreach (var t in removed)
                            {
                                //Log.Error("Drop meat: " + t.ThingID + " " + t.stackCount + " " + t.Destroyed);
                                if (t.stackCount > 0)
                                {
                                    if (BuildingUtil.DropSingleThing(t, f.storage, f.storage.Map, false))
                                        newChosen.Add(new ThingCount(t, t.stackCount));
                                    else
                                    {
                                        Log.Warning("Failed to spawn item " + t.Label);
                                        f.storage.Add(t);
                                        return false;
                                    }
                                }
                            }
                        }
                        else
                        {
                            Log.Warning("Failed to remove " + f.Thing.Label);
                            return false;
                        }
                    }
                }
                else
                    newChosen.Add(tc);
            }
            chosen.Clear();
            chosen.AddRange(newChosen);
            return true;
        }

        private static void AddEveryMedicineToRelevantThings(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map, Bill bill)
        {
            MedicalCareCategory medicalCareCategory = (MedicalCareCategory)typeof(WorkGiver_DoBill).GetMethod("GetMedicalCareCategory", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { billGiver });
            List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine);
            tmpMedicine.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                Thing thing = list[i];
                if (medicalCareCategory.AllowsMedicine(thing.def) && baseValidator(thing) && pawn.CanReach(thing, PathEndMode.OnCell, Danger.Deadly, false, TraverseMode.ByPawn))
                {
                    tmpMedicine.Add(thing);
                }
            }
            tmpMedicine.SortBy((Thing x) => -x.GetStatValue(StatDefOf.MedicalPotency, true), (Thing x) => x.Position.DistanceToSquared(billGiver.Position));
            relevantThings.AddRange(tmpMedicine);

            //(float)(t.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius && bill.IsFixedOrAllowedIngredient(t) && bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t))
            foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStoragesWithinRadius(map, billGiver.Position, bill.ingredientSearchRadius))
            {
                List<Thing> gotten;
                if (s.TryGetFilteredThings(bill, bill.ingredientFilter, out gotten))
                {
                    relevantThings.AddRange(gotten);
                    gotten.ForEach(v => foundIngs[v.thingIDNumber] = new FoundIng(s, v, v.stackCount));
                }
            }

            tmpMedicine.Clear();
        }
    }*/
}