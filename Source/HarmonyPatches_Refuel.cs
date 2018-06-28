using Harmony;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace InfiniteStorage
{
    partial class HarmonyPatches
    {
        [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindBestFuel")]
        static class Patch_RefuelWorkGiverUtility_FindBestFuel
        {
            static void Prefix(Pawn pawn, Thing refuelable)
            {
                RefuelPatchUtil.Prefix(pawn, refuelable);
            }

            static void Postfix(Thing __result)
            {
                RefuelPatchUtil.Postfix(__result);
            }
        }
        [HarmonyPatch(typeof(RefuelWorkGiverUtility), "FindAllFuel")]
        static class Patch_RefuelWorkGiverUtility_FindAllFuel
        {
            static void Prefix(Pawn pawn, Thing refuelable)
            {
                RefuelPatchUtil.Prefix(pawn, refuelable);
            }

            static void Postfix(List<Thing> __result)
            {
                RefuelPatchUtil.Postfix(__result);
            }
        }

        internal static class RefuelPatchUtil
        {
            private static Dictionary<Thing, Building_InfiniteStorage> droppedAndStorage = null;
            internal static void Prefix(Pawn pawn, Thing refuelable)
            {
                if (WorldComp.HasInfiniteStorages(refuelable.Map))
                {
                    droppedAndStorage = new Dictionary<Thing, Building_InfiniteStorage>();

                    ThingFilter filter = refuelable.TryGetComp<CompRefuelable>().Props.fuelFilter;

                    foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(refuelable.Map))
                    {
                        if (storage.Spawned && storage.Map == pawn.Map && storage.IsOperational)
                        {
                            List<Thing> removed;
                            if (storage.TryRemove(filter, out removed))
                            {
                                List<Thing> removedThings = new List<Thing>();
                                foreach (Thing t in removed)
                                {
                                    BuildingUtil.DropThing(t, t.def.stackLimit, storage, storage.Map, false, removedThings);
                                }

                                if (removedThings.Count > 0)
                                {
                                    droppedAndStorage.Add(removedThings[0], storage);
                                }
                            }
                        }
                    }
                }
            }

            internal static void Postfix(Thing __result)
            {
                if (droppedAndStorage != null)
                {
                    foreach (KeyValuePair<Thing, Building_InfiniteStorage> kv in droppedAndStorage)
                    {
                        if (kv.Key != __result)
                        {
                            Building_InfiniteStorage storage = kv.Value;
                            storage.Add(kv.Key);
                        }
                    }
                    droppedAndStorage.Clear();
                }
            }

            internal static void Postfix(List<Thing> __result)
            {
                if (droppedAndStorage != null && __result != null)
                {
                    HashSet<Thing> results = new HashSet<Thing>();
                    foreach(Thing t in __result)
                    {
                        results.Add(t);
                    }

                    foreach (KeyValuePair<Thing, Building_InfiniteStorage> kv in droppedAndStorage)
                    {
                        if (!results.Contains(kv.Key))
                        {
                            Building_InfiniteStorage storage = kv.Value;
                            storage.Add(kv.Key);
                        }
                    }
                    results.Clear();
                    results = null;
                    droppedAndStorage.Clear();
                }
            }
        }
    }
}