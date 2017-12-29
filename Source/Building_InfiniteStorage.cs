using InfiniteStorage.UI;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace InfiniteStorage
{
    public class Building_InfiniteStorage : Building_Storage
    {
        private SortedDictionary<string, LinkedList<Thing>> storedThings = new SortedDictionary<string, LinkedList<Thing>>();
        public IEnumerable<Thing> StoredThings
        {
            get
            {
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                    foreach (Thing t in l)
                        yield return t;
            }
        }
        public int DefsCount
        {
            get
            {
                int count = 0;
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                    count += l.Count;
                return count;
            }
        }

        public bool AllowAdds { get; set; }

        private Map CurrentMap { get; set; }

        private bool includeInTradeDeals = true;
        public bool IncludeInTradeDeals { get { return this.includeInTradeDeals; } }

        private CompPowerTrader compPowerTrader = null;
        public bool UsesPower { get { return this.compPowerTrader != null; } }
        public bool IsOperational { get { return this.compPowerTrader == null || this.compPowerTrader.PowerOn; } }

        public bool CanAutoCollect { get; set; }

        private long lastAutoReclaim = 0;

        private List<Thing> ToDumpOnSpawn = null;

        [Unsaved]
        private float storedCount = 0;
        [Unsaved]
        private float storedWeight = 0;

        public Building_InfiniteStorage()
        {
            this.AllowAdds = true;
            this.CanAutoCollect = true;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.CurrentMap = map;

            if (settings == null)
            {
                base.settings = new StorageSettings(this);
                base.settings.CopyFrom(this.def.building.defaultStorageSettings);
                base.settings.filter.SetDisallowAll();
            }

            WorldComp.Add(map, this);

            this.compPowerTrader = this.GetComp<CompPowerTrader>();

            if (this.ToDumpOnSpawn != null)
            {
                foreach (Thing t in this.ToDumpOnSpawn)
                {
                    BuildingUtil.DropThing(t, this, this.Map, false);
                }
                this.ToDumpOnSpawn.Clear();
                this.ToDumpOnSpawn = null;
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            try
            {
                this.Dispose();
                base.Destroy(mode);
            }
            catch (Exception e)
            {
                Log.Error(
                    this.GetType().Name + ".Destroy\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }
        }

        public override void DeSpawn()
        {
            try
            {
                this.Dispose();
                base.DeSpawn();
            }
            catch (Exception e)
            {
                Log.Error(
                    this.GetType().Name + ".DeSpawn\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }
        }

        private void Dispose()
        {
            try
            {
                this.AllowAdds = false;
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                {
                    foreach (Thing t in l)
                    {
                        BuildingUtil.DropThing(t, t.stackCount, this, this.CurrentMap, false);
                    }
                }
                this.storedThings.Clear();
            }
            catch (Exception e)
            {
                Log.Error(
                    this.GetType().Name + ".Dispose\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }

            WorldComp.Remove(this.CurrentMap, this);
        }

        private void DropThing(Thing t, bool makeForbidden = true)
        {
            BuildingUtil.DropThing(t, this, this.CurrentMap, makeForbidden);
        }
        
        public void Empty(List<Thing> droppedThings = null)
        {
            if (!this.IsOperational)
                return;
            try
            {
                this.AllowAdds = false;
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                {
                    foreach (Thing t in l)
                    {
                        BuildingUtil.DropThing(t, t.stackCount, this, this.CurrentMap, false, droppedThings);
                    }
#if DEBUG
                    else
                        Log.Message("Empty " + t.Label + " has 0 stack count");
#endif
                }
                this.storedCount = 0;
                this.storedWeight = 0;
                this.storedThings.Clear();
            }
            finally
            {
                this.AllowAdds = true;
            }
        }

        public void Reclaim(bool respectReserved = false)
        {
            if (this.IsOperational && this.CanAutoCollect)
            {
                float powerAvailable = 0;
                if (this.UsesPower && Settings.EnableEnergyBuffer)
                {
                    powerAvailable = this.compPowerTrader.PowerNet.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick;
                    if (powerAvailable <= Settings.DesiredEnergyBuffer)
                    {
                        return;
                    }
                    powerAvailable -= Settings.DesiredEnergyBuffer;
                }

                foreach (Thing t in BuildingUtil.FindThingsOfTypeNextTo(base.Map, base.Position, 1))
                {
                    if (this.UsesPower && Settings.EnableEnergyBuffer)
                    {
                        float newWeight = this.storedWeight + this.GetThingWeight(t, t.stackCount);
                        if (newWeight * Settings.EnergyFactor > powerAvailable)
                        {
                            continue;
                        }
                    }
                    this.Add(t);
                }
            }
        }

        public int StoredThingCount(ThingDef def)
        {
            int count = 0;
            LinkedList<Thing> l;
            if (this.storedThings.TryGetValue(def.label, out l))
            {
                foreach (Thing t in l)
                {
                    count += t.stackCount;
                }
            }
            return count;
        }

        public override void Notify_ReceivedThing(Thing newItem)
        {
            if (!this.AllowAdds)
            {
                BuildingUtil.DropSingleThing(newItem, this, this.Map, false);
            }
            else if (!this.Add(newItem))
            {
                this.DropThing(newItem, false);
            }
        }

        public bool Add(Thing thing)
        {
            if (thing == null ||
                !base.settings.AllowedToAccept(thing) ||
                !this.IsOperational)
            {
                return false;
            }

            if (this.UsesPower && Settings.EnableEnergyBuffer)
            {
                if (this.compPowerTrader.PowerNet.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick < 
                    Settings.DesiredEnergyBuffer + this.GetThingWeight(thing, thing.stackCount))
                {
                    return false;
                }
            }

            if (thing.stackCount == 0)
            {
#if DEBUG
                Log.Warning("Add " + thing.Label + " has 0 stack count");
#endif
                return true;
            }

            if (thing.Spawned)
            {
                thing.DeSpawn();
            }

            int thingsAdded = thing.stackCount;
            LinkedList<Thing> l;
            if (this.storedThings.TryGetValue(thing.def.label, out l))
            {
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
            else
            {
                l = new LinkedList<Thing>();
                l.AddFirst(thing);
                this.storedThings.Add(thing.def.label, l);
            }
            this.UpdateStoredStats(thing, thingsAdded);
            return true;
        }

        private float GetThingWeight(Thing thing, int count)
        {
            return thing.GetStatValue(StatDefOf.Mass, true) * count;
        }

        public bool TryGetFilteredThings(Bill bill, ThingFilter filter, out List<Thing> gotten)
        {
            gotten = null;
            foreach (LinkedList<Thing> l in this.storedThings.Values)
            {
                if (l.Count > 0 &&
                    bill.IsFixedOrAllowedIngredient(l.First.Value.def) && filter.Allows(l.First.Value.def))
                {
                    foreach (Thing t in l)
                    {
                        if (bill.IsFixedOrAllowedIngredient(t) && filter.Allows(t))
                        {
                            if (gotten == null)
                            {
                                gotten = new List<Thing>();
                            }
                            gotten.Add(t);
                        }
                    }
                }
            }
            return gotten != null;
        }

        public bool TryGetValue(ThingDef def, out Thing t)
        {
            LinkedList<Thing> l;
            if (this.storedThings.TryGetValue(def.label, out l))
            {
                if (l.Count > 0)
                {
                    t = l.First.Value;
                    return true;
                }
            }
            t = null;
            return false;
        }

        public bool TryRemove(ThingFilter filter, out Thing removed)
        {
            foreach (LinkedList<Thing> l in this.storedThings.Values)
            {
                if (l.Count > 0 && 
                    filter.Allows(l.First.Value.def))
                {
                    Thing t = l.First.Value;
                    int count = Math.Min(t.stackCount, t.def.stackLimit);
                    return this.TryRemove(t, count, out removed);
                }
            }
            removed = null;
            return false;
        }

        public bool TryRemove(Thing thing)
        {
            LinkedList<Thing> l;
            if (this.storedThings.TryGetValue(thing.def.label, out l))
            {
                return l.Remove(thing);
            }
            return false;
        }

        public bool TryRemove(Thing thing, int count, out Thing removed)
        {
            return this.TryRemove(thing.def, count, out removed);
        }

        public bool TryRemove(ThingDef def, int count, out Thing removed)
        {
            LinkedList<Thing> l;
            if (this.storedThings.TryGetValue(def.label, out l))
            {
                if (l.Count > 0)
                {
                    removed = l.First.Value;
                    if (removed.stackCount <= count)
                    {
                        count = removed.stackCount;
                        l.RemoveFirst();
                        if (l.Count <= 0)
                        {
                            this.storedThings.Remove(def.label);
                        }
                    }
                    else
                    {
                        removed = removed.SplitOff(count);
                    }
                    this.UpdateStoredStats(removed, -1 * count);
                    return true;
                }
            }
            removed = null;
            return false;
        }
        
        private void UpdateStoredStats(Thing thing, int count, bool force = false)
        {
            this.storedCount += count;
            this.storedWeight += this.GetThingWeight(thing, count);
            if (this.storedWeight < 0)
            {
                this.storedCount = 0;
                this.storedWeight = 0;
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                {
                    foreach (Thing t in l)
                    {
                        this.UpdateStoredStats(thing, count, true);
                    }
                }
            }
        }

        public void HandleThingsOnTop()
        {
            if (this.Spawned)
            {
                foreach (Thing t in base.Map.thingGrid.ThingsAt(this.Position))
                {
                    if (t != null && t != this && !(t is Blueprint) && !(t is Building))
                    {
                        if (!this.Add(t))
                        {
                            if (t.Spawned)
                            {
                                IntVec3 p = t.Position;
                                p.x = p.x + 1;
                                t.Position = p;
#if DEBUG
                                Log.Warning("TextureStorage: Moving " + t.Label);
#endif
                            }
                        }
                    }
                }
            }
        }

        public List<Thing> temp = null;
        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                this.temp = new List<Thing>();
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                {
                    foreach (Thing t in l)
                    {
                        this.temp.Add(t);
                    }
                }
            }

            Scribe_Collections.Look(ref this.temp, "storedThings", LookMode.Deep, new object[0]);
            Scribe_Values.Look<bool>(ref this.includeInTradeDeals, "includeInTradeDeals", true, false);

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                this.storedThings.Clear();

                if (this.temp != null)
                {
                    foreach (Thing t in this.temp)
                    {
                        if (!this.Add(t))
                        {
                            if (this.ToDumpOnSpawn == null)
                            {
                                this.ToDumpOnSpawn = new List<Thing>();
                            }
                            this.ToDumpOnSpawn.Add(t);
                        }
                    }
                }
                this.temp.Clear();
                this.temp = null;
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0)
                sb.Append(Environment.NewLine);
            sb.Append("InfiniteStorage.StoragePriority".Translate());
            sb.Append(": ");
            sb.Append(("StoragePriority" + base.settings.Priority).Translate());
            sb.Append(Environment.NewLine);
            if (this.compPowerTrader != null)
            {
                sb.Append("InfiniteStorage.StoredWeight".Translate());
                sb.Append(": ");
                sb.Append(this.storedWeight.ToString("N1"));
            }
            else
            {
                sb.Append("InfiniteStorage.Count".Translate());
                sb.Append(": ");
                sb.Append(this.storedCount);
            }
            sb.Append(Environment.NewLine);
            sb.Append("InfiniteStorage.IncludeInTradeDeals".Translate());
            sb.Append(": ");
            sb.Append(this.includeInTradeDeals.ToString());
#if DEBUG
            sb.Append(Environment.NewLine + "Allow Adds: " + this.AllowAdds);
#endif
            return sb.ToString();
        }
        
        public override void TickRare()
        {
            base.TickRare();
            if (this.Spawned && base.Map != null && this.compPowerTrader != null)
            {
                this.compPowerTrader.PowerOutput = -1 * Settings.EnergyFactor * this.storedWeight;
            }

            long now = DateTime.Now.Ticks;
            if (Settings.CollectThingsAutomatically &&
                now - this.lastAutoReclaim > Settings.TimeBetweenAutoCollectsTicks)
            {
                this.Reclaim(true);
                this.lastAutoReclaim = now;
            }
        }

        #region Gizmos
        public override IEnumerable<Gizmo> GetGizmos()
        {
            IEnumerable<Gizmo> enumerables = base.GetGizmos();

            List<Gizmo> l;
            if (enumerables != null)
                l = new List<Gizmo>(enumerables);
            else
                l = new List<Gizmo>(1);

            int key = "InfiniteStorage".GetHashCode();

            l.Add(new Command_Action
            {
                icon = GetGizmoViewTexture(),
                defaultDesc = "InfiniteStorage.ViewDesc".Translate(),
                defaultLabel = "InfiniteStorage.View".Translate(),
                activateSound = SoundDef.Named("Click"),
                action = delegate { Find.WindowStack.Add(new UI.ViewUI(this)); },
                groupKey = key
            });
            ++key;

            if (this.IsOperational)
            {
                l.Add(new Command_Action
                {
                    icon = ViewUI.emptyTexture,
                    defaultDesc = "InfiniteStorage.EmptyDesc".Translate(),
                    defaultLabel = "InfiniteStorage.Empty".Translate(),
                    activateSound = SoundDef.Named("Click"),
                    action = delegate { this.Empty(); },
                    groupKey = key
                });
                ++key;

                l.Add(new Command_Action
                {
                    icon = ViewUI.collectTexture,
                    defaultDesc = "InfiniteStorage.CollectDesc".Translate(),
                    defaultLabel = "InfiniteStorage.Collect".Translate(),
                    activateSound = SoundDef.Named("Click"),
                    action = delegate {
                        this.CanAutoCollect = true;
                        this.Reclaim();
                    },
                    groupKey = key
                });
                ++key;
            }

            l.Add(new Command_Action
            {
                icon = (this.includeInTradeDeals) ? ViewUI.yesSellTexture : ViewUI.noSellTexture,
                defaultDesc = "InfiniteStorage.IncludeInTradeDealsDesc".Translate(),
                defaultLabel = "InfiniteStorage.IncludeInTradeDeals".Translate(),
                activateSound = SoundDef.Named("Click"),
                action = delegate { this.includeInTradeDeals = !this.includeInTradeDeals; },
                groupKey = key
            });
            ++key;

            l.Add(new Command_Action
            {
                icon = ViewUI.applyFiltersTexture,
                defaultDesc = "InfiniteStorage.ApplyFiltersDesc".Translate(),
                defaultLabel = "InfiniteStorage.ApplyFilters".Translate(),
                activateSound = SoundDef.Named("Click"),
                action = delegate { this.ApplyFilters(); },
                groupKey = key
            });
            ++key;

            return SaveStorageSettingsUtil.SaveStorageSettingsGizmoUtil.AddSaveLoadGizmos(l, this.GetSaveStorageSettingType(), this.settings.filter);
        }

        private string GetSaveStorageSettingType()
        {
            InfiniteStorageType s = this.def.GetModExtension<InfiniteStorageType>();
            if (s != null)
            {
#if DEBUG
                Log.Warning("Found InfiniteStorageType = " + s.SaveSettingsType);
#endif
                return s.SaveSettingsType;
            }
#if DEBUG
                Log.Warning("Did not find InfiniteStorageType def = " + def.label);
#endif
            return SaveStorageSettingsUtil.SaveTypeEnum.Zone_Stockpile.ToString();
        }

        private Texture2D GetGizmoViewTexture()
        {
            InfiniteStorgeGizmoViewTexture t = this.def.GetModExtension<InfiniteStorgeGizmoViewTexture>();
            if (t != null)
            {
#if DEBUG
                Log.Warning("Found View Texture = " + t.GizmoViewTexture);
#endif
                switch (t.GizmoViewTexture)
                {
                    case "viewbodyparts":
                        return ViewUI.BodyPartViewTexture;
                    case "viewtextile":
                        return ViewUI.TextileViewTexture;
                }
            }
#if DEBUG
                Log.Warning("Did not find View Texture def = " + def.label);
#endif
            return ViewUI.InfiniteStorageViewTexture;
        }
#endregion

#region ThingFilters
        public void ApplyFilters()
        {
            List<Thing> removed = new List<Thing>();
            List<string> keysToRemove = new List<string>();
            foreach (KeyValuePair<string, LinkedList<Thing>> kv in this.storedThings)
            {
                LinkedList<Thing> l = kv.Value;
                if (l.Count > 0)
                {
                    LinkedListNode<Thing> n = l.First;
                    while (n != null)
                    {
                        var next = n.Next;
                        if (!this.settings.AllowedToAccept(n.Value))
                        {
                            removed.Add(n.Value);
                            l.Remove(n);
                        }
                        n = next;
                    }
                }
                if (l.Count == 0)
                {
                    keysToRemove.Add(kv.Key);
                }
            }
            
            foreach (string key in keysToRemove)
            {
                this.storedThings.Remove(key);
            }
            keysToRemove.Clear();
            keysToRemove = null;

            foreach (Thing t in removed)
            {
                BuildingUtil.DropThing(t, this, this.CurrentMap, false);
            }
            removed.Clear();
            removed = null;
        }
        #endregion
    }
}