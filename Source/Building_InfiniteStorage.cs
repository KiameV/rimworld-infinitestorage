using InfiniteStorage.UI;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace InfiniteStorage
{
    public class Building_InfiniteStorage : Building_Storage
    {
        private SortedDictionary<string, Thing> storedThings = new SortedDictionary<string, Thing>();
        public IEnumerable<Thing> StoredThings { get { return this.storedThings.Values; } }
        public int DefsCount { get { return this.storedThings.Count; } }

        public bool AllowAdds { get; set; }

        private Map CurrentMap { get; set; }

        private bool includeInTradeDeals = true;
        public bool IncludeInTradeDeals { get { return this.includeInTradeDeals; } }

        private CompPowerTrader compPowerTrader = null;
        public bool UsesPower { get { return this.compPowerTrader != null; } }
        public bool IsOperational { get { return this.compPowerTrader == null || this.compPowerTrader.PowerOn; } }

        private long lastAutoReclaim = 0;

        [Unsaved]
        private float storedCount = 0;
        [Unsaved]
        private float storedWeight = 0;

        public Building_InfiniteStorage()
        {
            this.AllowAdds = true;
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

            WorldComp.Add(this);

            this.compPowerTrader = this.GetComp<CompPowerTrader>();
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
                if (this.storedThings != null)
                {
                    foreach (Thing t in this.storedThings.Values)
                    {
                        this.DropThing(t, true);
                    }
                    this.storedThings.Clear();
                }
            }
            catch (Exception e)
            {
                Log.Error(
                    this.GetType().Name + ".Dispose\n" +
                    e.GetType().Name + " " + e.Message + "\n" +
                    e.StackTrace);
            }

            WorldComp.Remove(this);
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
                foreach (Thing t in this.storedThings.Values)
                {
                    if (t.stackCount > 0)
                    {
                        BuildingUtil.DropThing(t, t.stackCount, this, this.CurrentMap, false, droppedThings);
                    }
#if DEBUG
                    else
                    {
                        Log.Message("Empty " + t.Label + " has 0 stack count");
                    }
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
            if (this.IsOperational)
            {
                this.Add(BuildingUtil.FindThingsOfTypeNextTo(base.Map, base.Position, 1));
                lastAutoReclaim = DateTime.Now.Ticks;
            }
        }

        public int StoredThingCount(ThingDef def)
        {
            Thing t;
            if (this.storedThings.TryGetValue(def.label, out t))
            {
                return t.stackCount;
            }
            return 0;
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

        internal void Add(IEnumerable<Thing> things)
        {
            if (things == null)
                return;

            foreach (Thing t in things)
            {
                this.Add(t);
            }
        }

        internal bool Add(Thing thing)
        {
            if (thing == null || 
                !base.settings.AllowedToAccept(thing) || 
                !this.IsOperational)
            {
                return false;
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

            Thing t;
            int thingsAdded = thing.stackCount;
            if (this.storedThings.TryGetValue(thing.def.label, out t))
            {
                if (!t.TryAbsorbStack(thing, false))
                {
                    Log.Warning("Unable to add " + thing.Label);
                    thingsAdded = thingsAdded - thing.stackCount;
                    this.DropThing(thing, false);
                }
            }
            else
            {
                this.storedThings.Add(thing.def.label, thing);
            }
            this.UpdateStoredStats(thing, thingsAdded);
            return true;
        }

        public bool TryGetValue(ThingDef def, out Thing t)
        {
            return this.storedThings.TryGetValue(def.label, out t);
        }

        public bool TryRemove(ThingFilter filter, out Thing removed)
        {
            foreach (Thing t in this.storedThings.Values)
            {
                if (filter.Allows(t.def))
                {
                    int count = Math.Min(t.stackCount, t.def.stackLimit);
                    removed = this.Remove(t, count);
                    return true;
                }
            }
            removed = null;
            return false;
        }

        public Thing Remove(Thing thing, int count)
        {
            Thing t;
            if (this.storedThings.TryGetValue(thing.def.label, out t))
            {
                if (t.stackCount <= count)
                {
                    count = t.stackCount;
                    this.storedThings.Remove(t.def.label);
                }
                else
                {
                    t = t.SplitOff(count);
                }

                this.UpdateStoredStats(t, count);
            }
            return t;
        }

        private void UpdateStoredStats(Thing thing, int count, bool force = false)
        {
            this.storedCount += count;
            this.storedWeight += thing.GetStatValue(StatDefOf.Mass, true) * count;

            if (this.storedWeight < 0)
            {
                this.storedCount = 0;
                this.storedWeight = 0;
                foreach (Thing t in this.storedThings.Values)
                {
                    this.UpdateStoredStats(thing, count, true);
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
                foreach (Thing t in this.storedThings.Values)
                {
                    this.temp.Add(t);
                }
            }

            Scribe_Collections.Look(ref this.temp, "storedThings", LookMode.Deep, new object[0]);
            Scribe_Values.Look<bool>(ref this.includeInTradeDeals, "includeInTradeDeals", true, false);

            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                this.storedThings.Clear();

                if (this.temp != null)
                {
                    this.Add(this.temp);
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
                sb.Append(this.storedWeight);
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

            if (Settings.CollectThingsAutomatically && 
                DateTime.Now.Ticks - lastAutoReclaim > Settings.TimeBetweenAutoCollectsTicks)
            {
                this.Reclaim(true);
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
                    action = delegate { this.Reclaim(); },
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
        private ThingFilter previousStorageFilters = new ThingFilter();
        private FieldInfo AllowedDefsFI = typeof(ThingFilter).GetField("allowedDefs", BindingFlags.Instance | BindingFlags.NonPublic);
        protected bool AreStorageSettingsEqual()
        {
            ThingFilter currentFilters = base.settings.filter;
            if (currentFilters.AllowedDefCount != this.previousStorageFilters.AllowedDefCount ||
                currentFilters.AllowedQualityLevels != this.previousStorageFilters.AllowedQualityLevels ||
                currentFilters.AllowedHitPointsPercents != this.previousStorageFilters.AllowedHitPointsPercents)
            {
                return false;
            }

            HashSet<ThingDef> currentAllowed = AllowedDefsFI.GetValue(currentFilters) as HashSet<ThingDef>;
            foreach (ThingDef previousAllowed in AllowedDefsFI.GetValue(this.previousStorageFilters) as HashSet<ThingDef>)
            {
                if (!currentAllowed.Contains(previousAllowed))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdatePreviousStorageFilter()
        {
            ThingFilter currentFilters = base.settings.filter;

            this.previousStorageFilters.AllowedHitPointsPercents = currentFilters.AllowedHitPointsPercents;
            this.previousStorageFilters.AllowedQualityLevels = currentFilters.AllowedQualityLevels;

            HashSet<ThingDef> previousAllowed = AllowedDefsFI.GetValue(this.previousStorageFilters) as HashSet<ThingDef>;
            previousAllowed.Clear();
            previousAllowed.AddRange(AllowedDefsFI.GetValue(currentFilters) as HashSet<ThingDef>);
        }
        #endregion
    }
}