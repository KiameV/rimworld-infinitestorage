using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace InfiniteStorage
{
    internal class StorageManagement
    {
        public SortedDictionary<string, LinkedList<Thing>> StoredThings = new SortedDictionary<string, LinkedList<Thing>>();
        public Building_InfiniteStorage Parent;

        [Unsaved]
		private int storedCount = 0;
        public int StoredCount => this.storedCount;
		[Unsaved]
		private float storedWeight = 0;
        public float StoredWeight => this.storedWeight;

        public bool CanAdd(Thing t)
        {
            if (t == null || !Parent.settings.AllowedToAccept(t) || !Parent.AllowAdds)
                return false;
            if (this.Parent.UsesPower == false)
                return true;
            return this.Parent.IsOperational && (GetThingWeight(t, t.stackCount) + this.storedWeight) * Settings.EnergyFactor > this.Parent.GetPowerAvailable();
        }

        public bool Add(Thing thing, bool force)
        {
            int count = thing.stackCount;
            if (count == 0)
                return true;

            if (!this.StoredThings.TryGetValue(thing.def.ToString(), out LinkedList<Thing> l))
            {
                l = new LinkedList<Thing>();
                l.AddFirst(thing);
                this.StoredThings.Add(thing.def.ToString(), l);
            }
            else
            {
                var id = thing.ThingID;
                if (thing.def.stackLimit == 1)
                {
                    if (l.FirstIndexOf(t => t.ThingID == id) == -1)
                    {
                        l.AddLast(thing);
                    }
                    return true;
                }
                // thing.def.stackLimit > 1
                bool absorbed = false;
                foreach (Thing t in l)
                {
                    if (t.TryAbsorbStack(thing, false))
                    {
                        absorbed = true;
                    }
                }
                if (!absorbed)
                {
                    l.AddLast(thing);
                }
            }
            return true;
        }

        public void Clear()
        {
            this.StoredThings?.Clear();
            this.storedCount = 0;
            this.storedWeight = 0;
        }

        public void Remove(LinkedList<Thing> l, LinkedListNode<Thing> n)
        {
            Thing t = n.Value;
            l.Remove(n);
            if (l.Count == 0)
                this.StoredThings.Remove(t.def.ToString());
            this.UpdateCountAndWeight(t, uwc.Add);
        }

        public bool Remove(Thing thing)
        {
            if (thing != null && this.StoredThings.TryGetValue(thing.def.ToString(), out var l) && l.Remove(thing))
            {
                this.UpdateCountAndWeight(thing, thing.stackCount, uwc.Remove);
                return true;
            }
            return false;
        }

        public bool TryRemove(ThingDef def, out IEnumerable<Thing> removed)
        {
            if (this.StoredThings.TryGetValue(def.ToString(), out var l))
            {
                this.StoredThings.Remove(def.ToString());
                removed = l;
                foreach (Thing t in l)
                {
                    this.UpdateCountAndWeight(t, t.stackCount, uwc.Remove);
                }
                return true;
            }
            removed = null;
            return false;
        }

        public bool TryRemove(ThingDef def, int count, List<Thing> removed)
        {
            if (this.StoredThings.TryGetValue(def.ToString(), out var l))
            {
                return this.TryRemove(l, count, removed);
            }
            return false;
        }

        public bool TryRemove(LinkedList<Thing> l, int count, List<Thing> removed)
        {
            int need = count;
            int removeCount = 0;
            LinkedListNode<Thing> n = l.First;
            while (n != null && need > 0)
            {
                Thing t = n.Value;
                LinkedListNode<Thing> next = n.Next;
                if (removed == null)
                {
                    removed = new List<Thing>();
                }

                if (t.stackCount == 0 || t.Destroyed)
                {
                    l.Remove(n);
                }
                else if (need >= t.stackCount)
                {
                    need -= t.stackCount;
                    removeCount += t.stackCount;
                    l.Remove(n);
                    removed.Add(t);
                }
                else
                {
                    removeCount += need;
                    while (need > 0)
                    {
                        int toRemove = Math.Min(need, t.def.stackLimit);
                        need -= toRemove;
                        Thing split = t.SplitOff(toRemove);
                        removed.Add(split);
                    }
                }
                n = next;
            }

            if (removed?.Count > 0 && removeCount > 0)
            {
                this.UpdateCountAndWeight(removed[0], removeCount, uwc.Remove);
                return true;
            }
            return false;
        }

        public void Empty(List<Thing> droppedThings = null)
        {
            foreach (var l in this.StoredThings.Values)
                foreach (var t in l)
                    BuildingUtil.DropThing(t, t.stackCount, this.Parent, this.Parent.CurrentMap, droppedThings);
        }

        private enum uwc { Add, Remove }
        private void UpdateCountAndWeight(Thing t, uwc uwc)
        {
            this.UpdateCountAndWeight(t, t.stackCount, uwc);
        }

        private void UpdateCountAndWeight(Thing t, int count, uwc uwc)
        {
            if (t != null)
            {
                if (uwc == uwc.Add)
                {
                    storedCount += t.stackCount;
                    storedWeight += GetThingWeight(t, t.stackCount);
                }
                else // remove
                {
                    storedCount -= count;
                    storedWeight -= GetThingWeight(t, t.stackCount);
                }
            }
            if (this.storedCount < 0 || this.storedWeight < 0)
            {
                this.ResetCountAndWeight();
            }
        }

        public void ResetCountAndWeight()
        {
            Thing t;
            this.storedCount = 0;
            this.storedWeight = 0;
            foreach(var l in this.StoredThings.Values)
            {
                for (var n = l.First; n.Next != null; n = n.Next)
                {
                    t = n.Value;
                    if (t == null)
                        l.Remove(n);
                    else
                    {
                        this.storedCount += t.stackCount;
                        this.storedWeight += GetThingWeight(t, t.stackCount);
                    }
                }
            }
        }

        public static float GetThingWeight(Thing thing, int count)
        {
            return thing.GetStatValue(StatDefOf.Mass, true) * count;
        }

        public void Destroy()
        {
            Clear();
        }

        public void DeSpawn()
        {
            Clear();
        }

        public IEnumerable<Thing> Things
        {
            get
            {
                foreach (var l in this.StoredThings.Values)
                    foreach (var t in l)
                        yield return t;
            }
        }
    }
}
