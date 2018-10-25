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
        [HarmonyPatch(typeof(HealthAIUtility), "FindBestMedicine")]
        static class Patch_HealthAIUtility_FindBestMedicine
        {
            /*struct StorageMedicine
            {
                public readonly Building_InfiniteStorage Storage;
                public readonly IEnumerable<Thing> Medicine;
                public StorageMedicine(Building_InfiniteStorage storage, IEnumerable<Thing> medicine)
                {
                    this.Storage = storage;
                    this.Medicine = medicine;
                }
            }
            static void Prefix(ref Thing __result, Pawn healer, Pawn patient)
            {
                List<StorageMedicine> meds = new List<StorageMedicine>();
                List<Thing> searchSet = new List<Thing>();
                searchSet.Add(__result);
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(patient.Map))
                {
                    IEnumerable<Thing> l = storage.GetMedicalThings(false);
                    if (l != null)
                    {
                        meds.Add(new StorageMedicine(storage, l));
                    }
                }

                GenClosest.ClosestThing_Global_Reachable(
                    patient.Position, patient.map, searchSet, peMode, traverseParams, 9999f, validator, priorityGetter);
            }*/
            //private readonly static List<Thing> dropped = new List<Thing>();

            [HarmonyPriority(Priority.First)]
            static void Prefix(Pawn healer, Pawn patient)
            {
                Log.Warning("FindBestMedicine Prefix");
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(patient.Map))
                {
                    IEnumerable<Thing> removed = storage.GetMedicalThings(false, true);
                    foreach (Thing r in removed)
                    {
                        BuildingUtil.DropThing(r, r.stackCount, storage, storage.Map, false);//, dropped);
                    }
                }
            }

            /*static void Postfix(Thing __result)
            {
                if (__result != null)
                    Log.Message("Tend with: " + __result + " is reserved: " + __result.Map.reservationManager.IsReservedByAnyoneOf(__result, Faction.OfPlayer));
                else
                    Log.Message("Tend no med");
            }*/
        }



        [HarmonyPatch(typeof(HealthCardUtility), "DrawMedOperationsTab")]
        static class Patch_HealthCardUtility_DrawMedOperationsTab
        {
            private static long lastUpdate = 0;
            private static IEnumerable<Thing> cache = null;
            [HarmonyPriority(Priority.First)]
            static void Prefix()
            {
#if MED_DEBUG
                Log.Warning("HealthCardUtility.DrawMedOperationsTab");
#endif

                if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Count > 0)
                {
                    Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
                }

                Map map = Find.CurrentMap;
                if (map != null)
                {
#if MED_DEBUG
                    Log.Warning("    Map is not null: " + (map != null).ToString());
#endif
                    foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                    {
#if MED_DEBUG
                        Log.Warning("    Storage: " + storage.Label);
#endif
                        long now = DateTime.Now.Ticks;
                        if (cache == null || now - lastUpdate > TimeSpan.TicksPerSecond)
                        {
                            cache = storage.GetMedicalThings(true, false);
                        }

                        Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.AddRange(cache);
                    }
                }
            }

            static void Postfix()
            {
                if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Count > 0)
                {
                    Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
                }
            }
        }

        [HarmonyPatch(typeof(ListerThings), "ThingsInGroup")]
        static class Patch_ListerThings_ThingsInGroup
        {
            public readonly static List<Thing> AvailableMedicalThing = new List<Thing>();
            static void Postfix(ref List<Thing> __result, ThingRequestGroup group)
            {
#if MED_DEBUG
                //Log.Warning("ListerThings.ThingsInGroup");
#endif
                if (AvailableMedicalThing.Count > 0)
                {
#if MED_DEBUG
                    Log.Warning("ListerThings.ThingsInGroup");
#endif
#if MED_DEBUG
                    foreach (Thing t in AvailableMedicalThing)
                        Log.Warning("    " + t.Label);
#endif
                    __result.AddRange(AvailableMedicalThing);
                }
            }
        }
    }
}