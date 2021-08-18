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
		internal readonly StorageManagement StorageManagement = new StorageManagement();

		public IEnumerable<Thing> StoredThings => this.StorageManagement.Things;
		public int DefsCount => this.StorageManagement.StoredThings.Count;

		public bool AllowAdds { get; set; }

		internal Map CurrentMap { get; set; }

		private bool includeInTradeDeals = true;
		public bool IncludeInTradeDeals { get { return this.includeInTradeDeals; } }

		private CompPowerTrader compPowerTrader = null;
		public bool UsesPower { get { return this.compPowerTrader != null; } }
		public bool IsOperational { get { return this.compPowerTrader == null || this.compPowerTrader.PowerOn; } }

		public bool CanAutoCollect { get; set; }

		private long lastAutoReclaim = 0;
		public void ResetAutoReclaimTime() { this.lastAutoReclaim = DateTime.Now.Ticks; }

		private List<Thing> ToDumpOnSpawn = null;

		private bool includeInWorldLookup;
		public bool IncludeInWorldLookup { get { return this.includeInWorldLookup; } }

		public Building_InfiniteStorage()
		{
			this.AllowAdds = true;
			this.CanAutoCollect = true;
			this.StorageManagement.Parent = this;
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

			this.includeInWorldLookup = this.GetIncludeInWorldLookup();

			WorldComp.Add(map, this);

			this.compPowerTrader = this.GetComp<CompPowerTrader>();

			if (this.ToDumpOnSpawn != null)
			{
				foreach (Thing t in this.ToDumpOnSpawn)
				{
					BuildingUtil.DropThing(t, t.stackCount, this, this.Map);
				}
				this.ToDumpOnSpawn.Clear();
				this.ToDumpOnSpawn = null;
			}
		}

		public bool TryGetFirstFilteredItemForMending(Bill bill, ThingFilter filter, bool remove, out Thing gotten)
		{
			LinkedList<Thing> l = null;
			gotten = null;
			foreach (var kv in this.StorageManagement.StoredThings)
			{
				l = kv.Value;
				if (l?.Count > 0)
				{
					for (LinkedListNode<Thing> n = l.First; n.Next != null; n = n.Next)
					{
						Thing t = n.Value;
						if (!bill.IsFixedOrAllowedIngredient(t.def) || !filter.Allows(t.def))
							break;

						if (bill.IsFixedOrAllowedIngredient(t) && filter.Allows(t))
						{
							if (t.HitPoints == t.MaxHitPoints)
							{
								continue;
							}
                            
							this.StorageManagement.Remove(l, n);
							BuildingUtil.DropSingleThing(t, this, this.Map, out gotten);
							return true;
						}
					}
				}
			}
			return gotten != null;
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			try
			{
				this.StorageManagement.Destroy();
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

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			try
			{
				this.StorageManagement.DeSpawn();
				this.Dispose();
				base.DeSpawn(mode);
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
				this.StorageManagement.Empty();
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

		private void DropThing(Thing t, List<Thing> result)
		{
			BuildingUtil.DropThing(t, t.stackCount, this, this.CurrentMap, result);
		}

		public void Empty(List<Thing> droppedThings = null, bool force = false)
		{
			if (!force && !this.IsOperational && !Settings.EmptyOnPowerLoss)
				return;

			try
			{
				this.AllowAdds = false;
				this.StorageManagement.Empty(droppedThings);
			}
			finally
			{
				this.ResetAutoReclaimTime();
				this.AllowAdds = true;
			}
		}

		public void Reclaim(bool respectReserved = true, List<ThingCount> chosen = null)
		{
			if (this.IsOperational && this.CanAutoCollect)
			{
				foreach (Thing t in BuildingUtil.FindThingsOfTypeNextTo(base.Map, base.Position, 1))
				{
					if (chosen == null || !this.ChosenContains(t, chosen))
					{
						this.Add(t);
					}
				}
			}
		}

		public float GetPowerAvailable()
		{
			float powerAvailable = 0;
			if (this.UsesPower && Settings.EnableEnergyBuffer)
			{
				powerAvailable = this.compPowerTrader.PowerNet.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick;
				if (powerAvailable <= Settings.DesiredEnergyBuffer)
				{
					return -1;
				}
				powerAvailable -= Settings.DesiredEnergyBuffer;
			}
			return powerAvailable;
		}

		public void ForceReclaim()
		{
			if (base.Map == null)
				return;

			foreach (Thing t in BuildingUtil.FindThingsOfTypeNextTo(base.Map, base.Position, 1))
			{
				if (!(t is Building_InfiniteStorage) && t != this && t.def.category == ThingCategory.Item)
					this.Add(t, true);
			}
		}

		private bool ChosenContains(Thing t, List<ThingCount> chosen)
		{
			if (chosen != null)
			{
				foreach (ThingCount ta in chosen)
				{
					if (ta.Thing == t)
						return true;
				}
			}
			return false;
		}

		public int StoredThingCount(ThingDef expectedDef, ThingFilter ingrediantFilter)
		{
			int count = 0;
			if (this.StorageManagement.StoredThings.TryGetValue(expectedDef.ToString(), out var l))
			{
				foreach (Thing t in l)
				{
					if (this.Allows(t, expectedDef, ingrediantFilter))
					{
						count += t.stackCount;
					}
				}
			}
#if DEBUG || DEBUG_DO_UNTIL_X
            else
            {
                Log.Warning("Building_InfiniteStorage.StoredThingCount Def of [" + expectedDef.label + "] Not Found. Stored Defs:");
                foreach (string label in this.storedThings.Keys)
                {
                    Log.Warning("    Def: " + label);
                }
            }
#endif
			return count;
		}

		private bool Allows(Thing t, ThingDef expectedDef, ThingFilter filter)
		{
			if (filter == null)
			{
				return true;
			}

#if DEBUG || DEBUG_DO_UNTIL_X
            Log.Warning("Building_InfiniteStorage.Allows Begin [" + t.Label + "]");
#endif
			if (t.def != expectedDef)
			{
#if DEBUG || DEBUG_DO_UNTIL_X
                Log.Warning("    Building_InfiniteStorage.Allows End Def Does Not Match [False]");
#endif
				return false;
			}
			if (expectedDef.useHitPoints &&
				filter.AllowedHitPointsPercents.min != 0f && filter.AllowedHitPointsPercents.max != 100f)
			{
				float num = (float)t.HitPoints / (float)t.MaxHitPoints;
				num = GenMath.RoundedHundredth(num);
				if (!filter.AllowedHitPointsPercents.IncludesEpsilon(Mathf.Clamp01(num)))
				{
#if DEBUG || DEBUG_DO_UNTIL_X
                    Log.Warning("    Building_InfiniteStorage.Allows End Hit Points [False]");
#endif
					return false;
				}
			}
			if (filter.AllowedQualityLevels != QualityRange.All && t.def.FollowQualityThingFilter())
			{
				QualityCategory p;
				if (!t.TryGetQuality(out p))
				{
					p = QualityCategory.Normal;
				}
				if (!filter.AllowedQualityLevels.Includes(p))
				{
#if DEBUG || DEBUG_DO_UNTIL_X
                    Log.Warning("    Building_InfiniteStorage.Allows End Quality [False]");
#endif
					return false;
				}
			}
#if DEBUG || DEBUG_DO_UNTIL_X
            Log.Warning("    Building_InfiniteStorage.Allows End [True]");
#endif
			return true;
		}

		public override void Notify_ReceivedThing(Thing newItem)
		{
			if (!this.AllowAdds)
			{
				BuildingUtil.DropSingleThing(newItem, this, this.Map, out Thing result);
			}
			else if (!this.Add(newItem))
			{
				this.DropThing(newItem, null);
			}
		}

		public new bool Accepts(Thing thing)
		{
			return this.StorageManagement.CanAdd(thing);
		}

		public bool Add(Thing thing, bool force = false)
		{
			if (force)
			{
				if (thing == null ||
					!base.settings.AllowedToAccept(thing))
				{
					return false;
				}
			}
			else
			{
				if (!this.StorageManagement.CanAdd(thing))
					return false;
			}

			if (thing.Spawned)
				thing.DeSpawn();

			return this.StorageManagement.Add(thing, force);
		}

		private static bool IsBodyPart(ThingDef td)
		{
			foreach (ThingCategoryDef d in td.thingCategories)
			{
				if (d.defName.Contains("BodyPart"))
				{
					return true;
				}
			}
			return false;
		}

		public IEnumerable<Thing> GetMedicalThings(bool includeBodyParts = true, bool remove = false)
		{
#if MED_DEBUG
            Log.Warning("Start GetMedicalThings (includeBodyParts: " + includeBodyParts + ", remove: " + remove + ")");
#endif
			List<Thing> rv = new List<Thing>();
			foreach (LinkedList<Thing> l in this.StorageManagement.StoredThings.Values)
			{
				if (l.Count > 0)
				{
					ThingDef def = l.First.Value.def;
					if (def != null &&
						def.IsMedicine ||
						(includeBodyParts && IsBodyPart(def)))
					{
#if MED_DEBUG
                        Log.Message("    " + def.defName + " is medicine");
#endif
						if (remove == true)
						{
							if (this.StorageManagement.TryRemove(def, out var removed))
								rv.AddRange(removed);
						}
						else
							rv.AddRange(l);

					}
#if MED_DEBUG
                    else
                        Log.Message("    " + def.defName + " is not medicine");
#endif
				}
			}
#if MED_DEBUG
            Log.Warning("End GetMedicalThings " + rv.Count);
#endif
			return rv;
		}

		public bool TryGetFilteredThings(Bill bill, ThingFilter filter, out List<Thing> gotten)
		{
			gotten = null;
			foreach (LinkedList<Thing> l in this.StorageManagement.StoredThings.Values)
			{
				if (l.Count > 0 && bill.IsFixedOrAllowedIngredient(l.First.Value.def))
				{
					foreach (Thing t in l)
					{
						if (filter.Allows(t))
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
			if (def != null)
			{
				LinkedList<Thing> l;
				if (this.StorageManagement.StoredThings.TryGetValue(def.ToString(), out l))
				{
					if (l.Count > 0)
					{
						t = l.First.Value;
						return true;
					}
				}
			}
			t = null;
			return false;
		}

		public bool TryRemove(ThingFilter filter, out List<Thing> removed)
		{
			foreach (LinkedList<Thing> l in this.StorageManagement.StoredThings.Values)
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

		/*public bool TryRemove(Thing thing, int count, out Thing removed)
		{
			foreach (LinkedList<Thing> l in this.storedThings.Values)
			{
				Thing t;
				var n = l.First;
				while (n != null)
				{
					var next = n.Next;
					t = n.Value;
					if (t == thing)
					{
						if (t.stackCount <= count)
						{
							if (t.stackCount < count)
								Log.Warning("Trying to remove more of " + t.Label + " than its stack count allows");
							removed = t;
							l.Remove(n);
							return true;
						}
						else
						{
							removed = t.SplitOff(count);
							return true;
						}
					}
					else if (t.stackCount == 0)
					{
						Log.Warning(t.Label + " has 0 stack count. Removing.");
						l.Remove(n);
					}
					n = next;
				}
			}
			removed = null;
			return false;
		}*/

		public bool TryRemove(Thing thing)
		{
			return this.StorageManagement.Remove(thing);
		}

		/*public bool DropMeatThings(Bill bill)
		{
			const int NEEDED = 75;
			LinkedList<Thing> l;
			int needed = NEEDED;
			foreach (KeyValuePair<string, LinkedList<Thing>> kv in this.storedThings)
			{
				l = kv.Value;
				if (l != null && l.Count > 0 && l.First.Value != null)
				{
					if (bill.ingredientFilter.Allows(l.First.Value))
					{
						LinkedListNode<Thing> next;
						LinkedListNode<Thing> n = l.First;
						while (needed > 0 && n != null)
						{
							next = n.Next;
							if (n.Value == null)
							{
								l.Remove(n);
							}
							else
							{
								//UpdateStoredStats(n.Value, n.Value.stackCount, false);
								this.stackCount -= n.Value.stackCount;
								needed -= n.Value.stackCount;
                                BuildingUtil.DropSingleThing
								l.Remove(n);
							}
							n = next;
						}
					}
				}
			}
			return needed < NEEDED;
			/*List<string> toDrop = new List<string>();
            LinkedList<Thing> l;
            foreach (KeyValuePair<string, LinkedList<Thing>> kv in this.storedThings)
            {
                l = kv.Value;
                if (l != null && l.Count > 0 && l.First.Value != null)
                {
                    if (filter.Allows(l.First.Value.def))
                    {
                        toDrop.Add(kv.Key);
                    }
                }
            }

            foreach (string s in toDrop)
            {
                l = this.storedThings[s];
                foreach(Thing t in l)
                {
                    this.DropThing(t, false);
                }
                l.Clear();
                this.storedThings.Remove(s);
            }

            return toDrop.Count > 0;* /
		}*/

		public bool TryRemove(ThingDef def, out IEnumerable<Thing> removed)
		{
			return this.StorageManagement.TryRemove(def, out removed);
		}

		public bool TryRemove(Thing thing, int count, out List<Thing> removed)
		{
			return this.TryRemove(thing.def, count, out removed);
		}

		public bool TryRemove(ThingDef def, int count, out List<Thing> removed)
		{
			removed = new List<Thing>();
			return this.StorageManagement.TryRemove(def, count, removed);
		}

		/*ublic new float MarketValue
        {
            get
            {
                float v = base.MarketValue;
                foreach (LinkedList<Thing> l in this.storedThings.Values)
                {
                    foreach (Thing t in l)
                    {
                        v += (float)t.stackCount * t.MarketValue;
                    }
                }
                Log.Warning("Market Value: " + v);
                return v;
            }
        }*/

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
				foreach (LinkedList<Thing> l in this.StorageManagement.StoredThings.Values)
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
				this.StorageManagement.Clear();

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
				sb.Append(this.StorageManagement.StoredWeight.ToString("N1"));
			}
			else
			{
				sb.Append("InfiniteStorage.Count".Translate());
				sb.Append(": ");
				sb.Append(this.StorageManagement.StoredCount);
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
				this.compPowerTrader.PowerOutput = -1 * Settings.EnergyFactor * this.StorageManagement.StoredWeight;
			}
			long now = DateTime.Now.Ticks;
#if DEBUG
            Log.Warning("TickRare: Now: " + now + " Last Reclaim: " + this.lastAutoReclaim + " Now - Last Reclaim " + (now - this.lastAutoReclaim) + " Time Between: " + Settings.TimeBetweenAutoCollectsTicks + " Time Between - Diff: " + (Settings.TimeBetweenAutoCollectsTicks - (now - this.lastAutoReclaim)));
#endif
			if (Settings.CollectThingsAutomatically &&
				now - this.lastAutoReclaim > Settings.TimeBetweenAutoCollectsTicks)
			{
#if DEBUG
                Log.Warning("Perform auto-reclaim");
#endif
				this.Reclaim(true);
				this.lastAutoReclaim = now;
			}

			if (!this.IsOperational && Settings.EmptyOnPowerLoss && this.StorageManagement.StoredCount > 0 && Settings.EnergyFactor > 0.00001f)
			{
				this.Empty(null, true);
			}

			/*if ((DateTime.Now - lastCountUpdate).Ticks > TEN_SECONDS)
            {
				this.UpdateCount();
				lastCountUpdate = DateTime.Now;
            }*/
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

			l.Add(new Command_Action
			{
				icon = ViewUI.emptyTexture,
				defaultDesc = "InfiniteStorage.EmptyDesc".Translate(),
				defaultLabel = "InfiniteStorage.Empty".Translate(),
				activateSound = SoundDef.Named("Click"),
				action = delegate { this.Empty(null, true); },
				groupKey = key
			});
			++key;

			if (this.IsOperational)
			{
				l.Add(new Command_Action
				{
					icon = ViewUI.collectTexture,
					defaultDesc = "InfiniteStorage.CollectDesc".Translate(),
					defaultLabel = "InfiniteStorage.Collect".Translate(),
					activateSound = SoundDef.Named("Click"),
					action = delegate
					{
						this.CanAutoCollect = true;
						this.Reclaim();
					},
					groupKey = key
				});
				++key;
			}

			if (this.IncludeInWorldLookup)
			{
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
			}

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

			return l;
		}

		private bool GetIncludeInWorldLookup()
		{
			InfiniteStorageType s = this.def.GetModExtension<InfiniteStorageType>();
			if (s != null && s.IncludeInWorldLookup.Length > 0)
			{
#if DEBUG
                Log.Warning("Found InfiniteStorageType = " + s.SaveSettingsType);
#endif
				if (s.IncludeInWorldLookup.ToLower()[0] == 'f')
				{
					Log.Warning("    return false");
					return false;
				}
			}
#if DEBUG
                Log.Warning("Did not find InfiniteStorageType def = " + def.label);
#endif
			return true;
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
					case "viewtrough":
						return ViewUI.TroughViewTexture;
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
			this.Empty();
			this.Reclaim();
			/*List<Thing> removed = new List<Thing>();
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
                LinkedList<Thing> l;
                if (this.storedThings.TryGetValue(key, out l))
                {
                    foreach(Thing t in l)
                    {
                        this.UpdateStoredStats(t, -1 * t.stackCount);
                    }
                }
                this.storedThings.Remove(key);
            }
            keysToRemove.Clear();
            keysToRemove = null;

            foreach (Thing t in removed)
            {
                BuildingUtil.DropThing(t, this, this.CurrentMap, false);
            }
            removed.Clear();
            removed = null;*/
		}
		#endregion
	}
}