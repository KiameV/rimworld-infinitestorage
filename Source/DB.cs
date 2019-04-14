using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using System.Collections;

namespace InfiniteStorage
{
    internal class DB : IExposable, IEnumerable<KeyValuePair<ThingDef, IEntry>>
    {
        private Dictionary<ThingDef, IEntry> db = new Dictionary<ThingDef, IEntry>();

        public IEnumerator<KeyValuePair<ThingDef, IEntry>> GetEnumerator() { return this.db.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return this.db.GetEnumerator(); }
        public ICollection<ThingDef> Keys => this.db.Keys;
        public ICollection<IEntry> Values => this.db.Values;
        public void Clear() { this.db.Clear(); }

        public bool Add(Thing thing)
        {
            if (db.TryGetValue(thing.def, out IEntry e))
                return e.Add(thing);
            else
            {
                IEntry i;
                if (thing.def.stackLimit > 1)
                    i = new StackableEntry(thing.def);
                else
                    i = new SingleEntry(thing.def);
                if (i.Add(thing))
                {
                    db.Add(thing.def, i);
                    return true;
                }
            }
            return false;
        }

        public int Count
        {
            get
            {
                int count = 0;
                foreach (var i in this.db.Values)
                    count += i.Count;
                return count;
            }
        }

        private List<IEntry> temp = null;
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                temp = new List<IEntry>();
            if (Scribe.mode == LoadSaveMode.Saving)
                temp = new List<IEntry>(db.Values);

            Scribe_Collections.Look(ref temp, "entries", LookMode.Deep, new object[0]);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                db.Clear();
                if (temp != null)
                {
                    foreach (IEntry i in temp)
                        this.db.Add(i.ThingDef, i);
                }
            }

            if (temp != null &&
                (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.PostLoadInit))
            {
                temp.Clear();
                temp = null;
            }
        }
    }

    internal interface IEntry : IExposable
    {
        ThingDef ThingDef { get; }
        bool Add(Thing thing);
        int Count { get; }
        int ThingCount(ThingFilter filter);
        bool TryRemove(ThingFilter filter, int count, out List<Thing> things, bool allowPartial = false);
        float Weight { get; }
        IEnumerable<Thing> RemoveAll();
    }

    internal class StackableEntry : IEntry
    {
        public ThingDef def;

        public ThingDef ThingDef => this.def;
        int IEntry.Count => this.Count;

        public StackableEntry(ThingDef def) { this.def = def; }

        public bool Add(Thing thing)
        {
            def.hasstuf
            if (thing.Destroyed)
                return false;
            Count += thing.stackCount;
            if (thing.Spawned)
                thing.DeSpawn();
            return true;
        }

        public IEnumerable<Thing> RemoveAll()
        {
            return this.Remove(this.Count);
        }

        public bool TryRemove(ThingFilter filter, int count, out List<Thing> things, bool allowPartial = false)
        {
            if (this.Count > 0 &&
                (this.Count >= count || allowPartial) &&
                filter.Allows(this.ThingDef))
            {
                things = new List<Thing>();
                if (this.Count < count)
                    count = this.Count;
                things = this.Remove(count);
                return true;
            }
            things = null;
            return false;
        }

        private List<Thing> Remove(int count)
        {
            List<Thing> l = new List<Thing>();
            while (count > 0 && this.Count > 0)
            {
                Thing t = ThingMaker.MakeThing(this.ThingDef);
                int c = this.ThingDef.stackLimit;
                if (count < c)
                    c = count;
                if (c > this.Count)
                    c = this.Count;
                t.stackCount = c;
                count -= c;
                this.Count -= c;
                l.Add(t);
            }
            return l;
        }

        public int ThingCount(ThingFilter filter)
        {
            if (this.Count > 0 && filter.Allows(this.ThingDef))
                return this.Count;
            return 0;
        }

        public float Weight
        {
            get
            {
                foreach (StatModifier s in ThingDef?.statBases)
                    if (s.stat == StatDefOf.Mass)
                        return s.value * this.Count;
                return 0f;
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref this.def, "def");
            Scribe_Values.Look(ref this.Count, "count");
        }
    }

    internal class SingleEntry : IEntry
    {
        public ThingDef def;
        public HashSet<Thing> Things = new HashSet<Thing>();

        public ThingDef ThingDef => this.def;
        public int Count => this.Things.Count;

        public SingleEntry(ThingDef def) { this.def = def; }

        public bool Add(Thing thing)
        {
            if (thing.Destroyed)
                return false;
            if (!this.Things.Contains(thing))
                this.Things.Add(thing);
            if (thing.Spawned)
                thing.DeSpawn();
            return true;
        }

        public IEnumerable<Thing> RemoveAll()
        {
            IEnumerable<Thing> temp = new List<Thing>(this.Things);
            this.Things.Clear();
            return temp;
        }

        public bool TryRemove(ThingFilter filter, int count, out List<Thing> removed, bool allowPartial = false)
        {
            if (this.Count > 0 &&
                (this.Count >= count || allowPartial) &&
                filter.Allows(this.ThingDef))
            {
                removed = new List<Thing>();
                foreach (Thing t in this.Things)
                {
                    if (filter.Allows(t))
                        removed.Add(t);
                    if (removed.Count == count)
                        break;
                }

                if (removed.Count == count || 
                    (removed.Count > 0 && allowPartial))
                {
                    foreach (Thing t in removed)
                        this.Things.Remove(t);
                    return true;
                }
            }
            removed = null;
            return false;
        }

        public int ThingCount(ThingFilter filter)
        {
            int count = 0;
            if (this.Count > 0 && filter.Allows(this.ThingDef))
            {
                foreach (Thing t in this.Things)
                    if (filter.Allows(t))
                        ++count;
            }
            return count;
        }

        public float Weight
        {
            get
            {
                float weight = 0;
                foreach (Thing t in this.Things)
                    weight += t.GetStatValue(StatDefOf.Mass, true);
                return weight;
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref this.def, "def");
            Scribe_Collections.Look(ref this.Things, false, "things", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && this.Things == null)
                this.Things = new HashSet<Thing>();
        }
    }
}
