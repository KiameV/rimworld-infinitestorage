using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using System;

namespace InfiniteStorage
{
    public class WorldComp : WorldComponent
    {
        [Unsaved]
        private static readonly Dictionary<Map, LinkedList<Building_InfiniteStorage>> ifStorages = new Dictionary<Map, LinkedList<Building_InfiniteStorage>>();

        public WorldComp(World world) : base(world)
        {
            foreach (LinkedList<Building_InfiniteStorage> l in ifStorages.Values)
            {
                l.Clear();
            }
            ifStorages.Clear();
        }

        public static void Add(Map map, Building_InfiniteStorage storage)
        {
            if (map == null)
            {
                Log.Error("Tried to add " + storage.Label + " to a null map. Please let me know if this ever happens!");
                return;
            }

            LinkedList<Building_InfiniteStorage> l;
            if (!ifStorages.TryGetValue(map, out l))
            {
                l = new LinkedList<Building_InfiniteStorage>();
                ifStorages.Add(map, l);
            }

            if (!l.Contains(storage))
            {
                l.AddLast(storage);
            }
        }

        public static IEnumerable<Building_InfiniteStorage> GetAllInfiniteStorages()
        {
            foreach (LinkedList<Building_InfiniteStorage> l in ifStorages.Values)
            {
                foreach (Building_InfiniteStorage storage in l)
                {
                    yield return storage;
                }
            }
        }

        public static IEnumerable<Building_InfiniteStorage> GetInfiniteStorages(Map map)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && ifStorages.TryGetValue(map, out l))
            {
                return l;
            }
            return new List<Building_InfiniteStorage>(0);
        }

        public static bool HasInfiniteStorages(Map map)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && ifStorages.TryGetValue(map, out l))
            {
                return l.Count > 0;
            }
            return false;
        }


        public static void Remove(Map map, Building_InfiniteStorage storage)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && ifStorages.TryGetValue(map, out l))
            {
                l.Remove(storage);
                if (l.Count == 0)
                {
                    ifStorages.Remove(map);
                }
            }
        }
    }
}
