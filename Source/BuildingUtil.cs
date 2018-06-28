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
        
        public static void DropThing(Thing toDrop, Building_InfiniteStorage from, Map map, bool makeForbidden = true)
        {
#if DEBUG || DROP_DEBUG
            Log.Warning("DropThing: toDrop.stackCount: " + toDrop.stackCount + " toDrop.def.stackLimit: " + toDrop.def.stackLimit);
#endif
            if (toDrop.stackCount <= toDrop.def.stackLimit)
            {
                try
                {
                    from.AllowAdds = false;
                    if (toDrop.stackCount != 0)
                    {
#if DEBUG || DROP_DEBUG
                        Log.Warning(" Drop Single Thing no loop");
#endif
                        DropSingleThing(toDrop, from, map, makeForbidden);
                    }
                }
                finally
                {
                    from.AllowAdds = true;
                }
            }
            else
            {
                DropThing(toDrop, toDrop.stackCount, from, map, makeForbidden);
            }
        }

        public static void DropThing(Thing toDrop, int amountToDrop, Building_InfiniteStorage from, Map map, List<ThingCount> droppedThings)
        {
            try
            {
                from.AllowAdds = false;

                List<Thing> d = new List<Thing>();
                DropThing(toDrop, amountToDrop, from, map, false, d);
                foreach (Thing dropped in d)
                {
                    droppedThings.Add(new ThingCount(dropped, dropped.stackCount));
                }
                d.Clear();
            }
            finally
            {
                from.AllowAdds = true;
            }
        }

        public static bool DropThing(Thing toDrop, int amountToDrop, Building_InfiniteStorage from, Map map, bool makeForbidden = true, List<Thing> droppedThings = null)
        {
#if DEBUG || DROP_DEBUG
            Log.Warning("DropThing: toDrop: " + toDrop.Label + " toDrop.stackCount: " + toDrop.stackCount + " amountToDrop: " + amountToDrop + " toDrop.def.stackLimit: " + toDrop.def.stackLimit);
#endif
            bool anyDropped = false;
            try
            {
                from.AllowAdds = false;

                Thing t;
                bool done = false;
                while(!done)
                {
#if DEBUG || DROP_DEBUG
                    Log.Warning("    Loop not done toDrop.stackCount: " + toDrop.stackCount);
#endif
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
#if DEBUG || DROP_DEBUG
                    Log.Warning("        toTake: " + toTake);
#endif
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
#if DEBUG || DROP_DEBUG
            Log.Warning("    Return: " + anyDropped);
#endif
            return anyDropped;
        }

        public static bool DropSingleThing(Thing toDrop, Building_InfiniteStorage from, Map map, bool makeForbidden)
        {
#if DEBUG || DROP_DEBUG
            Log.Warning("DropSingleThing");
#endif
            try
            {
                from.AllowAdds = false;
                Thing t;
                if (!toDrop.Spawned)
                {
                    if (!GenThing.TryDropAndSetForbidden(toDrop, from.InteractionCell, map, ThingPlaceMode.Near, out t, makeForbidden))
                    {
#if DEBUG || DROP_DEBUG
                        Log.Warning("    First Drop Attempt failed " + t.Label);
#endif
                    }
                    if (!toDrop.Spawned)
                    {
                        if (!GenPlace.TryPlaceThing(toDrop, from.Position, map, ThingPlaceMode.Near))
                        {
#if DEBUG || DROP_DEBUG
                            Log.Warning("    Second Drop Attempt failed " + t.Label);
#endif
                        }
                    }
                }

                toDrop.Position = from.InteractionCell;
            }
            catch (Exception e)
            {
                Log.Error(
                    typeof(BuildingUtil).Name + ".DropApparel\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }
            finally
            {
                from.AllowAdds = true;
            }

            if (toDrop == null)
            {
#if DEBUG || DROP_DEBUG
                Log.Warning("    null toDrop - return false");
#endif
                return false;
            }
#if DEBUG || DROP_DEBUG
            Log.Warning("    Return: " + toDrop.Spawned);
#endif
            return toDrop.Spawned;
        }
    }
}
