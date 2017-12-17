using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace InfiniteStorage
{
    public class WorldComp : WorldComponent
    {
        [Unsaved]
        private static readonly LinkedList<Building_InfiniteStorage> thingStorages = new LinkedList<Building_InfiniteStorage>();
        public static IEnumerable<Building_InfiniteStorage> InfiniteStorages { get { return thingStorages; } }
        public static bool HasInfiniteStorages { get { return thingStorages.Count > 0; } }

        public WorldComp(World world) : base(world)
        {
            thingStorages.Clear();
        }

        public static void Add(Building_InfiniteStorage bps)
        {
            if (!thingStorages.Contains(bps))
            {
                thingStorages.AddLast(bps);
            }
        }

        public static void Remove(Building_InfiniteStorage bps) { thingStorages.Remove(bps); }
    }
}
