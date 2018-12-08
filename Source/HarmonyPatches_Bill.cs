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
    /*partial class HarmonyPatches
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
        }* /

        [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
        static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
        {
            private static Stack<Building_InfiniteStorage> emptied = new Stack<Building_InfiniteStorage>();

            [HarmonyPriority(Priority.VeryHigh)]
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> l = new List<CodeInstruction>(instructions);
                for (int i = l.Count - 1, clearCount = 0; i > l.Count - 20 && clearCount < 4; --i)
                {
                    /*
                     * Remove clear calls for:
                     *  WorkGiver_DoBill.relevantThings.Clear();
                     *  WorkGiver_DoBill.newRelevantThings.Clear();
                     *  WorkGiver_DoBill.processedThings.Clear();
                     *  WorkGiver_DoBill.ingredientsOrdered.Clear();
                     * /
                    if (l[i].opcode == OpCodes.Callvirt)
                    {
                        l[i].opcode = OpCodes.Nop;
                        l[i].operand = null;
                        l[i - 1].opcode = OpCodes.Nop;
                        l[i - 1].operand = null;
                        ++clearCount;
                    }
                }
                return l;
            }

            [HarmonyPriority(Priority.First)]
            static void Prefix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
            {
                if (bill == null ||
                    bill.recipe == null ||
                    bill.recipe.workSkill == SkillDefOf.Cooking)
                {
                    foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
                    {
                        if (bill is Bill_Production && storage.def.defName.Equals("IS_MeatStorage"))
                        {
                            if (!bill.suspended && !((Bill_Production)bill).paused)
                            {
                                if (storage.DropMeatThings(bill))
                                {
                                    emptied.Add(storage);
                                }
                            }
                        }
                    }
                }
            }

            [HarmonyPriority(Priority.VeryHigh)]
            static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
            {
                List<Thing> processedThings = new List<Thing>((HashSet<Thing>)typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
                List<Thing> relevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                List<Thing> newRelevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                List<IngredientCount> ingredientsOrdered = (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

                if ((bill == null || bill.recipe == null || bill.recipe.workSkill == SkillDefOf.Cooking) ||
                    (__result == true || !WorldComp.HasInfiniteStorages(bill.Map) || bill.Map != pawn.Map))
                {
                    while (emptied.Count != 0)
                    {
                        Building_InfiniteStorage storage = emptied.Pop();
                        storage.CanAutoCollect = true;
                        storage.Reclaim(true, chosen);
                    }
                    return;
                }

                try
                {
                    Process(ref __result, bill, pawn, billGiver, chosen, processedThings, relevantThings, newRelevantThings, ingredientsOrdered);
                }
                finally
                {
                    processedThings.Clear();
                    relevantThings.Clear();
                    newRelevantThings.Clear();
                    ingredientsOrdered.Clear();
                }
            }

            private static void Process(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen, List<Thing> processedThings, List<Thing> relevantThings, List<Thing> newRelevantThings, List<IngredientCount> ingredientsOrdered)
            {

#if BILL_DEBUG
                Log.Warning("TryFindBestBillIngredients.Postfix __result: " + __result + " bill: " + bill.Label + " chosenAmounts orig count: " + chosen.Count + " processed thigns: " + processedThings.Count);

                Log.Warning("    Chosen:");
                foreach (ThingCount a in chosen)
                {
                    Log.Warning("        " + a.Thing.Label + " " + a.Thing.stackCount + " " + a.Count);
                }

                Log.Warning("    WorkGiver_DoBill.Processed:");
                foreach (Thing t in processedThings)
                {
                    Log.Warning("        " + t.Label);
                }

                Log.Warning("    WorkGiver_DoBill.RelevantThings:");
                foreach (Thing t in (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                {
                    Log.Warning("        " + t.Label);
                }

                Log.Warning("    WorkGiver_DoBill.NewRelevantThings:");
                foreach (Thing t in (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                {
                    Log.Warning("        " + t.Label);
                }

                Log.Warning("    WorkGiver_DoBill.IngredientsOrdered:");
                foreach (IngredientCount i in (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                {
                    Log.Warning("        " + i.Summary);
                }
#endif
                Dictionary<ThingDef, int> chosenAmounts = new Dictionary<ThingDef, int>();
                foreach (ThingCount c in chosen)
                {
                    int count;
                    if (chosenAmounts.TryGetValue(c.Thing.def, out count))
                    {
                        count += c.Count;
                    }
                    else
                    {
                        count = c.Count;
                    }
                    chosenAmounts[c.Thing.def] = count;
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
                                    chosen.Add(new ThingCount(t, amount));
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
                                Log.Warning("                    -removing ing " + neededIng.Count);
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
                                        chosen.Add(new ThingCount(t, t.stackCount));
                                    }
                                }
                            }
                        }
                    }
                }

#if BILL_DEBUG
                StringBuilder sb = new StringBuilder();
                foreach (ThingCount ta in chosen)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(ta.Thing.def.defName + "x" + ta.Count);
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
    }*/
}