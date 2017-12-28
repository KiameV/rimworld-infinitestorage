using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

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
        }

        [HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
        static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
        {
            static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingAmount> chosen)
            {
                if (bill.Map == null)
                {
                    Log.Error("Bill's map is null");
                    return;
                }

                if (__result == true || !WorldComp.HasInfiniteStorages(bill.Map) || bill.Map != pawn.Map)
                    return;

#if DEBUG || DROP_DEBUG || BILL_DEBUG
            Log.Warning("TryFindBestBillIngredients.Postfix __result: " + __result);
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
                    foreach (KeyValuePair<ThingDef, int> kv in chosenAmounts)
                    {
                        if ((int)ing.GetBaseCount() == kv.Value)
                        {
#if DEBUG || DROP_DEBUG || BILL_DEBUG
                        Log.Warning("    Needed Ing population count is the same");
#endif
                            if (ing.filter.Allows(kv.Key))
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
                    if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < Math.Pow(bill.ingredientSearchRadius, 2))
                    {
                        LinkedListNode<NeededIngrediants> n = neededIngs.First;
                        while (n != null)
                        {
                            var next = n.Next;
                            NeededIngrediants neededIng = n.Value;

                            List<Thing> gotten;
                            if (storage.TryGetFilteredThings(bill, neededIng.Filter, out gotten))
                            {
                                foreach (Thing got in gotten)
                                {
                                    neededIng.Add(new StoredThings(storage, got));
                                }
                                if (neededIng.CountReached())
                                {
                                    thingsToUse.Add(new ThingsToUse(neededIng.GetFoundThings(), neededIng.Count));
                                    neededIng.Clear();
                                    neededIngs.Remove(n);
                                }
                            }
                            n = next;
                        }
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
                        int count = ttu.Count;
                        foreach (StoredThings st in ttu.Things)
                        {
                            if (count <= 0)
                                break;

                            Thing removed;
                            if (st.Storage.TryRemove(st.Thing, count, out removed))
                            {
                                count -= removed.stackCount;
                                List<Thing> dropped = new List<Thing>();
                                BuildingUtil.DropThing(removed, removed.stackCount, st.Storage, st.Storage.Map, false, dropped);
                                foreach (Thing t in dropped)
                                {
                                    chosen.Add(new ThingAmount(t, t.stackCount));
                                }
                            }
                        }
                    }
                }

                thingsToUse.Clear();
                foreach (NeededIngrediants n in neededIngs)
                    n.Clear();
                neededIngs.Clear();
                chosenAmounts.Clear();
            }
        }
    }
}
