using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
    partial class HarmonyPatches
    {
        struct StoredThings
        {
            public readonly Building_InfiniteStorage Storage;
            public readonly Thing Thing;
            public StoredThings(Building_InfiniteStorage storage, Thing thing)
            {
                this.Storage = storage;
                this.Thing = thing;
            }
        }

        struct ThingsToUse
        {
            public readonly List<StoredThings> Things;
            public readonly int Count;
            public ThingsToUse(List<StoredThings> things, int count)
            {
                this.Things = things;
                this.Count = count;
            }
        }

        class NeededIngrediants
        {
            public readonly ThingFilter Filter;
            public int Count;
            public readonly Dictionary<Def, List<StoredThings>> FoundThings;

            public NeededIngrediants(ThingFilter filter, int count)
            {
                this.Filter = filter;
                this.Count = count;
                this.FoundThings = new Dictionary<Def, List<StoredThings>>();
            }
            public void Add(StoredThings things)
            {
                List<StoredThings> l;
                if (!this.FoundThings.TryGetValue(things.Thing.def, out l))
                {
                    l = new List<StoredThings>();
                    this.FoundThings.Add(things.Thing.def, l);
                }
                l.Add(things);
            }
            public void Clear()
            {
                this.FoundThings.Clear();
            }
            public bool CountReached()
            {
                foreach (List<StoredThings> l in this.FoundThings.Values)
                {
                    if (this.CountReached(l))
                        return true;
                }
                return false;
            }
            private bool CountReached(List<StoredThings> l)
            {
                int count = this.Count;
                foreach (StoredThings st in l)
                {
                    count -= st.Thing.stackCount;
                }
                return count <= 0;
            }
            public List<StoredThings> GetFoundThings()
            {
                foreach (List<StoredThings> l in this.FoundThings.Values)
                {
                    if (this.CountReached(l))
                    {
#if DEBUG
                        Log.Warning("Count [" + Count + "] reached with: " + l[0].Thing.def.label);
#endif
                        return l;
                    }
                }
                return null;
            }

            internal bool HasFoundThings()
            {
                return this.FoundThings.Count > 0;
            }
        }

        [HarmonyPatch(typeof(WorkGiver_DoBill), "AddEveryMedicineToRelevantThings")]
        static class Patch_WorkGiver_DoBill_AddEveryMedicineToRelevantThings
        {
            static void PostFix(ref bool __result, Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map)
            {
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                {
                    relevantThings.AddRange(storage.GetMedicalThings());
                }
            }
        }

        /*[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet")]
        static class Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet
        {
            internal readonly static List<Thing> ReservedThings = new List<Thing>();

            private static FieldInfo RelevantThingsFI = null;

            static void PostFix()
            {
                if (RelevantThingsFI == null)
                {
                    RelevantThingsFI = typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic);
                }

                List<Thing> rt = (List<Thing>)RelevantThingsFI.GetValue(null);
#if BILL_DEBUG
                StringBuilder sb = new StringBuilder("TryFindBestBillIngredientsInSet: RT to add: [");
                foreach (Thing t in rt)
                {
                    sb.Append(t.Label);
                    sb.Append(", ");
                }
                Log.Warning(sb.ToString());
#endif
                ReservedThings.Clear();
                ReservedThings.AddRange(rt);
            }
        }*/

        [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
        static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
        {
            /*static FieldInfo NewRelativeThingsFI = null, ProcessedThingsFI = null, IngredientsOrderedFI = null, RelevantThingsFI = null;
            static MethodInfo GetBillGiverRootCellFI = null, MakeIngredientsListInProcessingOrderFI = null, TryFindBestBillIngredientsInSetFI = null, AddEveryMedicineToRelevantThingsFI = null;

            static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
            {
#if BILL_DEBUG
                Log.Warning("TryFindBestBillIngredients postfix");
#endif
                if (__result == true)
                {
                    return;
                }

                if (NewRelativeThingsFI == null)
                {
                    NewRelativeThingsFI = typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic);
                    ProcessedThingsFI = typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic);
                    IngredientsOrderedFI = typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic);
                    RelevantThingsFI = typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic);

                    GetBillGiverRootCellFI = typeof(WorkGiver_DoBill).GetMethod("GetBillGiverRootCell", BindingFlags.Static | BindingFlags.NonPublic);
                    MakeIngredientsListInProcessingOrderFI = typeof(WorkGiver_DoBill).GetMethod("MakeIngredientsListInProcessingOrder", BindingFlags.Static | BindingFlags.NonPublic);
                    TryFindBestBillIngredientsInSetFI = typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic);
                    AddEveryMedicineToRelevantThingsFI = typeof(WorkGiver_DoBill).GetMethod("AddEveryMedicineToRelevantThings", BindingFlags.Static | BindingFlags.NonPublic);
                }

                List<Thing> newRelevantThings = (List<Thing>)NewRelativeThingsFI.GetValue(null);
                HashSet<Thing> processedThings = (HashSet<Thing>)ProcessedThingsFI.GetValue(null);
                List<IngredientCount> ingredientsOrdered = (List<IngredientCount>)IngredientsOrderedFI.GetValue(null);
                List<Thing> relevantThings = (List<Thing>)RelevantThingsFI.GetValue(null);

                chosen.Clear();
                newRelevantThings.Clear();
                if (bill.recipe.ingredients.Count == 0)
                {
#if BILL_DEBUG
                    Log.Warning("bill.recipe.ingredients.Count == 0");
#endif
                    __result = true;
                    return;
                }
                IntVec3 rootCell = (IntVec3)GetBillGiverRootCellFI.Invoke(null, new object[] { billGiver, pawn });
                Region rootReg = rootCell.GetRegion(pawn.Map, RegionType.Set_Passable);
                if (rootReg == null)
                {
#if BILL_DEBUG
                    Log.Warning("rootReg == null");
#endif
                    __result = true;
                    return;
                }
                MakeIngredientsListInProcessingOrderFI.Invoke(null, new object[] { ingredientsOrdered, bill });
                newRelevantThings.Clear();
                processedThings.Clear();
                bool foundAll = false;
                
                // BEGIN inserted Code
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
                {
                    if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < Math.Pow(bill.ingredientSearchRadius, 2))
                    {
                        List<Thing> gotten;
                        if (storage.TryGetFilteredThings(bill, bill.ingredientFilter, out gotten))
                        {
#if BILL_DEBUG
                            StringBuilder sb = new StringBuilder("Adding From Storage: " + gotten.Count + Environment.NewLine);
#endif
                            relevantThings.AddRange(gotten);
                        }
                    }
                }
                // END inserted code

                Predicate<Thing> baseValidator = (Thing t) => t.Spawned && !t.IsForbidden(pawn) && (float)(t.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius && bill.IsFixedOrAllowedIngredient(t) && bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t)) && pawn.CanReserve(t, 1, -1, null, false);
                bool billGiverIsPawn = billGiver is Pawn;
                if (billGiverIsPawn)
                {
                    AddEveryMedicineToRelevantThingsFI.Invoke(null, new object[] { pawn, billGiver, relevantThings, baseValidator, pawn.Map });
                    if ((bool)TryFindBestBillIngredientsInSetFI.Invoke(null, new object[] { relevantThings, bill, chosen }))
                    {
#if BILL_DEBUG
                        Log.Warning("billGiverIsPawn && medicin && TryFindBestBillIngredientsInSet");
#endif
                        __result = true;
                        return;
                    }
                }
                TraverseParms traverseParams = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
                RegionEntryPredicate entryCondition = (Region from, Region r) => r.Allows(traverseParams, false);
                int adjacentRegionsAvailable = rootReg.Neighbors.Count((Region region) => entryCondition(rootReg, region));
                int regionsProcessed = 0;
                processedThings.AddRange(relevantThings);
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
                        Comparison<Thing> comparison = delegate (Thing t1, Thing t2)
                        {
                            float num = (float)(t1.Position - rootCell).LengthHorizontalSquared;
                            float value = (float)(t2.Position - rootCell).LengthHorizontalSquared;
                            return num.CompareTo(value);
                        };
                        newRelevantThings.Sort(comparison);
                        relevantThings.AddRange(newRelevantThings);
                        newRelevantThings.Clear();
#if BILL_DEBUG
                        Log.Warning("1 relative things: " + relevantThings.Count + "chosen: " + chosen.Count);
                        //foreach (Thing t in relevantThings)
                        //    Log.Error("    " + t.Label);
#endif
                        if ((bool)TryFindBestBillIngredientsInSetFI.Invoke(null, new object[] { relevantThings, bill, chosen }))
                        {
#if BILL_DEBUG
                            Log.Warning("2 relative things: " + relevantThings.Count + "chosen: " + chosen.Count);
#endif
                            foundAll = true;
                            return true;
                        }
                    }
                    return false;
                };

                RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999, RegionType.Set_Passable);
                newRelevantThings.Clear();
                __result = foundAll;

                // BEGIN inserted Code
                if (__result == true)
                {
                    foreach (ThingAmount a in chosen)
                    {
#if BILL_DEBUG
                        Log.Warning("3 ThingAmount: Thing: " + a.thing.Label + " Count: " + a.count + " Is Spawned: " + a.thing.Spawned);
#endif
                        if (!a.thing.Spawned)
                        {
                            foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
                            {
                                int remaining = a.count;
                                Thing removed;
                                while (remaining > 0 &&
                                       storage.TryRemove(a.thing.def, a.count, out removed))
                                {
                                    remaining -= removed.stackCount;

                                    BuildingUtil.DropThing(removed, removed.stackCount, storage, storage.Map, false);
                                }
                            }
                        }
                    }
                }
                relevantThings.Clear();
                // END inserted code
            }*/

            static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
            {
                if (bill == null || 
                    bill.recipe == null || 
                    bill.recipe.workSkill == SkillDefOf.Cooking)
                {
                    return;
                }

                if (bill.Map == null)
                {
                    Log.Error("Bill's map is null");
                    return;
                }

                if (__result == true || !WorldComp.HasInfiniteStorages(bill.Map) || bill.Map != pawn.Map)
                    return;
                
                List<Thing> processedThings = new List<Thing>((HashSet<Thing>)typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
#if BILL_DEBUG
                Log.Warning("TryFindBestBillIngredients.Postfix __result: " + __result + " bill: " + bill.Label + " chosenAmounts orig count: " + chosen.Count + " processed thigns: " + processedThings.Count);
                foreach(ThingAmount a in chosen)
                {
                    Log.Warning("    pre chosen: " + a.thing.Label + " " + a.thing.stackCount + " " + a.count);
                }
                foreach(Thing t in processedThings)
                {
                    Log.Warning("    pre processed: " + t.Label);
                }
#endif
                Dictionary<ThingDef, int> chosenAmounts = new Dictionary<ThingDef, int>();
                foreach (ThingAmount c in chosen)
                {
                    int count;
                    if (chosenAmounts.TryGetValue(c.thing.def, out count))
                    {
                        count += c.count;
                    }
                    else
                    {
                        count = c.count;
                    }
                    chosenAmounts[c.thing.def] = count;
                }

#if BILL_DEBUG
                Log.Warning("    ChosenAmounts:");
                foreach (KeyValuePair<ThingDef, int> kv in chosenAmounts)
                {
                    Log.Warning("        " + kv.Key.label + " - " + kv.Value);
                }
                Log.Warning("    ProcessedThings:");
                foreach (Thing t in processedThings)
                {
                    Log.Warning("        " + t.Label);
                }
#endif

                LinkedList<NeededIngrediants> neededIngs = new LinkedList<NeededIngrediants>();
                foreach (IngredientCount ing in bill.recipe.ingredients)
                {
                    bool found = false;
                    foreach (KeyValuePair<ThingDef, int> kv in chosenAmounts)
                    {
                        if ((int)ing.GetBaseCount() >= kv.Value)
                        {
#if BILL_DEBUG
                            Log.Warning("    Needed Ing population count: " + kv.Key.label + " count: " + kv.Value);
#endif
                            if (ing.filter.Allows(kv.Key))
                            {
#if BILL_DEBUG
                                Log.Warning("    Needed Ing population found: " + kv.Key.label + " count: " + kv.Value);
#endif
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
#if BILL_DEBUG
                        Log.Warning("    Needed Ing population not found");
#endif
                        neededIngs.AddLast(new NeededIngrediants(ing.filter, (int)ing.GetBaseCount()));
                    }
                }

#if BILL_DEBUG
                Log.Warning("    Needed Ings:");
                foreach (NeededIngrediants ings in neededIngs)
                {
                    Log.Warning("        " + ings.Count);
                }
#endif

                List<ThingsToUse> thingsToUse = new List<ThingsToUse>();
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
                {
                    if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < Math.Pow(bill.ingredientSearchRadius, 2))
                    {
                        LinkedListNode<NeededIngrediants> n = neededIngs.First;
                        while (n != null)
                        {
                            var next = n.Next;
                            NeededIngrediants neededIng = n.Value;
                            
                            for (int i = processedThings.Count - 1; i >= 0 && neededIng.Count > 0; --i)
                            {
                                Thing t = processedThings[i];
                                if (neededIng.Filter.Allows(t))
                                {
                                    int amount = Math.Min(neededIng.Count, t.stackCount);
                                    neededIng.Count -= amount;
                                    chosen.Add(new ThingAmount(t, amount));
                                    processedThings.RemoveAt(i);
#if BILL_DEBUG
                                    Log.Warning("            neededIng processedThings found: " + t.Label + " count: " + amount + " neededIng count: " + neededIng.Count);
                                    Log.Warning("            neededIng.CountReached: " + neededIng.CountReached());
#endif
                                }
                            }
                            if (!neededIng.CountReached())
                            {
                                List<Thing> gotten;
                                if (storage.TryGetFilteredThings(bill, neededIng.Filter, out gotten))
                                {
#if BILL_DEBUG
                                    Log.Warning("            Found ings: " + gotten.Count + " need count: " + neededIng.Count);
#endif
                                    foreach (Thing got in gotten)
                                    {
#if BILL_DEBUG
                                        Log.Warning("                ing: " + got.Label);
#endif
                                        neededIng.Add(new StoredThings(storage, got));
                                    }
                                }
                            }
                            if (neededIng.CountReached() || neededIng.Count <= 0)
                            {
#if BILL_DEBUG
                                Log.Warning("                    -removing ing ");
                                /*foreach(ThingDef d in neededIng.Filter.AllowedThingDefs)
                                {
                                    Log.Warning("                        " + d.label);
                                }*/
#endif
                                if (neededIng.HasFoundThings())
                                    thingsToUse.Add(new ThingsToUse(neededIng.GetFoundThings(), neededIng.Count));
                                neededIng.Clear();
                                neededIngs.Remove(n);
                            }
                            n = next;
                        }
                    }
                }

#if BILL_DEBUG
                Log.Warning("    neededIngs.count: " + neededIngs.Count);
                Log.Warning("    ThingsToUse.count: " + thingsToUse.Count);
#endif

                if (neededIngs.Count == 0)
                {
                    __result = true;
                    foreach (ThingsToUse ttu in thingsToUse)
                    {
                        int count = ttu.Count;
                        foreach (StoredThings st in ttu.Things)
                        {
                            if (count <= 0)
                                break;
                            List<Thing> removed;
                            if (st.Storage.TryRemove(st.Thing, count, out removed))
                            {
#if BILL_DEBUG
                                Log.Warning("    Storage Removing: " + st.Thing + " Count: " + count + " removed: " + removed.ToString());
#endif
                                foreach (Thing r in removed)
                                {
                                    count -= r.stackCount;
                                    List<Thing> dropped = new List<Thing>();
                                    BuildingUtil.DropThing(r, r.stackCount, st.Storage, st.Storage.Map, false, dropped);
                                    foreach (Thing t in dropped)
                                    {
#if BILL_DEBUG
                                    Log.Warning("        Dropped: " + t.Label);
#endif
                                        chosen.Add(new ThingAmount(t, t.stackCount));
                                    }
                                }
                            }
                        }
                    }
                }
                
#if BILL_DEBUG
                StringBuilder sb = new StringBuilder();
                foreach(ThingAmount ta in chosen)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(ta.thing.def.defName + "x" + ta.count);
                }
                Log.Warning("        Chosen: [" + sb.ToString() + "]");
#endif
                
                thingsToUse.Clear();
                foreach (NeededIngrediants n in neededIngs)
                {
                    if (n != null)
                        n.Clear();
                }
                neededIngs.Clear();
                chosenAmounts.Clear();
            }
        }
    }
}