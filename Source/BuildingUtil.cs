using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
    class BuildingUtil
    {
        public static IEnumerable<Thing> FindThingsOfTypeNextTo(Map map, IntVec3 position, int distance)
        {
            int minX = Math.Max(0, position.x - distance);
            int maxX = Math.Min(map.info.Size.x, position.x + distance);
            int minZ = Math.Max(0, position.z - distance);
            int maxZ = Math.Min(map.info.Size.z, position.z + distance);

            ReservationManager rsvmgr = map.reservationManager;

            List<Thing> list = new List<Thing>();
            for (int x = minX - 1; x <= maxX; ++x)
            {
                for (int z = minZ - 1; z <= maxZ; ++z)
                {
                    foreach (Thing t in map.thingGrid.ThingsAt(new IntVec3(x, position.y, z)))
                    {
                        if (rsvmgr == null || !rsvmgr.IsReservedByAnyoneOf(new LocalTargetInfo(t), Faction.OfPlayer))
                        {
                            list.Add(t);
                        }
                    }
                }
            }
            return list;
        }

        public static bool DropThing(Thing toDrop, Building_InfiniteStorage from, Map map, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            try
            {
                from.AllowAdds = false;
                if (toDrop.stackCount <= toDrop.def.stackLimit)
                {
                    return DropSingleThing(toDrop, from, map, makeForbidden);
                }
                return DropThing(toDrop, toDrop.stackCount, from, map, null, makeForbidden);
            }
            finally
            {
                from.AllowAdds = true;
            }
        }

        public static bool DropThing(Thing toDrop, IntVec3 from, Map map, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            if (toDrop.stackCount <= toDrop.def.stackLimit)
            {
                return DropSingleThing(toDrop, from, map, makeForbidden);
            }
            return DropThing(toDrop, toDrop.stackCount, from, map, null, makeForbidden);
        }

        public static bool DropThing(Thing toDrop, int amountToDrop, Building_InfiniteStorage from, Map map, List<Thing> droppedThings = null, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            bool anyDropped = false;
            try
            {
                from.AllowAdds = false;

                Thing t;
                bool done = false;
                while (!done)
                {
                    int toTake = toDrop.def.stackLimit;
                    if (toTake > amountToDrop)
                    {
                        toTake = amountToDrop;
                        done = true;
                    }
                    if (toTake >= toDrop.stackCount)
                    {
                        if (amountToDrop > toTake)
                        {
                            Log.Error("        ThingStorage: Unable to drop " + (amountToDrop - toTake).ToString() + " of " + toDrop.def.label);
                        }
                        toTake = toDrop.stackCount;
                        done = true;
                    }
                    if (toTake > 0)
                    {
                        amountToDrop -= toTake;
                        t = toDrop.SplitOff(toTake);
                        if (droppedThings != null)
                        {
                            droppedThings.Add(t);
                        }
                        if (DropSingleThing(t, from, map, makeForbidden))
                        {
                            anyDropped = true;
                        }
                    }
                }
            }
            finally
            {
                from.AllowAdds = true;
            }
            return anyDropped;
        }

        public static bool DropThing(Thing toDrop, int amountToDrop, IntVec3 from, Map map, List<Thing> droppedThings = null, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            bool anyDropped = false;
            Thing t;
            bool done = false;
            while (!done)
            {
                int toTake = toDrop.def.stackLimit;
                if (toTake > amountToDrop)
                {
                    toTake = amountToDrop;
                    done = true;
                }
                if (toTake >= toDrop.stackCount)
                {
                    if (amountToDrop > toTake)
                    {
                        Log.Error("        ThingStorage: Unable to drop " + (amountToDrop - toTake).ToString() + " of " + toDrop.def.label);
                    }
                    toTake = toDrop.stackCount;
                    done = true;
                }
                if (toTake > 0)
                {
                    amountToDrop -= toTake;
                    t = toDrop.SplitOff(toTake);
                    if (droppedThings != null)
                    {
                        droppedThings.Add(t);
                    }
                    if (DropSingleThing(t, from, map, makeForbidden))
                    {
                        anyDropped = true;
                    }
                }
            }
            return anyDropped;
        }

        public static bool DropSingleThing(Thing toDrop, Building_InfiniteStorage from, Map map, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            try
            {
                from.AllowAdds = false;
                return DropSingleThing(toDrop, from.InteractionCell, map, makeForbidden);
            }
            finally
            {
                from.AllowAdds = true;
            }
        }

        public static bool DropSingleThing(Thing toDrop, IntVec3 from, Map map, bool makeForbidden = false)
        {
            if (toDrop.stackCount == 0)
            {
                Log.Warning("To Drop Thing " + toDrop.Label + " had stack count of 0");
                return false;
            }

            try
            {
                if (!toDrop.Spawned)
                {
                    GenThing.TryDropAndSetForbidden(toDrop, from, map, ThingPlaceMode.Near, out Thing result, makeForbidden);
                    if (!toDrop.Spawned &&
                        !GenPlace.TryPlaceThing(toDrop, from, map, ThingPlaceMode.Near))
                    {
                        Log.Error("Failed to spawn " + toDrop.Label + " x" + toDrop.stackCount);
                        return false;
                    }
                }

                toDrop.Position = from;
            }
            catch (Exception e)
            {
                Log.Error(
                    typeof(BuildingUtil).Name + ".DropApparel\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }
            return toDrop != null && toDrop.Spawned;
        }
    }
}
