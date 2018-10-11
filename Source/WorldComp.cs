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
        [Unsaved]
        private static readonly Dictionary<Map, LinkedList<Building_InfiniteStorage>> ifNonGlobalStorages = new Dictionary<Map, LinkedList<Building_InfiniteStorage>>();

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
            if (storage == null)
            {
                Log.Error("Tried to add a null storage");
                return;
            }

            if (map == null)
            {
                Log.Error("Tried to add " + storage.Label + " to a null map. Please let me know if this ever happens!");
                return;
            }

            if (!storage.IncludeInWorldLookup)
            {
                Add(map, storage, ifNonGlobalStorages);
            }
            else
            {
                Add(map, storage, ifStorages);
            }
        }

        private static void Add(Map map, Building_InfiniteStorage storage, Dictionary<Map, LinkedList<Building_InfiniteStorage>> storages)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (!storages.TryGetValue(map, out l))
            {
                l = new LinkedList<Building_InfiniteStorage>();
                storages.Add(map, l);
            }

            if (!l.Contains(storage))
            {
                l.AddLast(storage);
            }
        }

        public static IEnumerable<Building_InfiniteStorage> GetAllInfiniteStorages()
        {
            if (ifStorages != null)
            {
                foreach (LinkedList<Building_InfiniteStorage> l in ifStorages.Values)
                {
                    foreach (Building_InfiniteStorage storage in l)
                    {
                        yield return storage;
                    }
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

        public static IEnumerable<Building_InfiniteStorage> GetNonGlobalInfiniteStorages(Map map)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && ifNonGlobalStorages.TryGetValue(map, out l))
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

        public static bool HasNonGlobalInfiniteStorages(Map map)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && ifNonGlobalStorages.TryGetValue(map, out l))
            {
                return l.Count > 0;
            }
            return false;
        }
        
        public static void Remove(Map map, Building_InfiniteStorage storage)
        {
            if (!storage.IncludeInWorldLookup)
            {
                Remove(map, storage, ifNonGlobalStorages);
            }
            else
            {
                Remove(map, storage, ifStorages);
            }
        }

        private static void Remove(Map map, Building_InfiniteStorage storage, Dictionary<Map, LinkedList<Building_InfiniteStorage>> storages)
        {
            LinkedList<Building_InfiniteStorage> l;
            if (map != null && storages.TryGetValue(map, out l))
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
