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
    }
}