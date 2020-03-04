using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace InfiniteStorage
{
    partial class HarmonyPatches
    {
        [HarmonyPatch(typeof(FactionDialogMaker), "AmountSendableSilver")]
        static class Patch_FactionDialogMaker_AmountSendableSilver
        {
            static void Prefix(Map map)
            {
                foreach(Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                {
                    if (storage.TryRemove(ThingDefOf.Silver, out IEnumerable<Thing> silver))
                    {
                        foreach (Thing s in silver)
                        {
                            BuildingUtil.DropThing(s, s.stackCount, storage, storage.Map, null);
                        }
                    }
                }
            }
        }

        /*class NeededSilver
        {
            readonly int NeededSilver;
            int FoundSilver = 0;
            readonly List<Pair<Building_InfiniteStorage, Thing>> silverInStorage = new List<Pair<Building_InfiniteStorage, Thing>>();

            public NeededSilver(int neededSilver)
            {
                this.NeededSilver = neededSilver;
            }

            public void AddSilver(Building_InfiniteStorage storage, Thing silver)
            {
                FoundSilver += silver.stackCount;
                this.silverInStorage.Add(new Pair<Building_InfiniteStorage, Thing>(storage, silver));
            }

            public bool CountReached()
            {
                return (NeededSilver - FoundSilver) > 0;
            }
        }

        [HarmonyPatch(typeof(TradeUtility), "LaunchThingsOfType")]
        static class Patch_TradeUtility_LaunchThingsOfType
        {
            static bool Prefix(ThingDef resDef, int debt, Map map, TradeShip trader)
            {
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                {
                    Thing removed;
                    if (storage.TryRemove(ThingDefOf.Silver, debt, out removed))
                    {
                        debt -= removed.stackCount;
                    }

                    if (debt <= 0)
                    {
                        if (debt < 0)
                        {
                            Log.Warning("Too many " + ThingDefOf.Silver.label + " was removed. Debt is " + debt);
                        }

                    }
                }
                return true;
            }
        }*/
    }
}
