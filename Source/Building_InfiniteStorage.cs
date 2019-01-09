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
		internal SortedDictionary<string, LinkedList<Thing>> storedThings = new SortedDictionary<string, LinkedList<Thing>>();
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
		public void ResetAutoReclaimTime() { this.lastAutoReclaim = DateTime.Now.Ticks; }

		private List<Thing> ToDumpOnSpawn = null;

		private bool includeInWorldLookup;
		public bool IncludeInWorldLookup { get { return this.includeInWorldLookup; } }

		[Unsaved]
		private int storedCount = 0;
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

			this.includeInWorldLookup = this.GetIncludeInWorldLookup();

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

		public bool TryGetFirstFilteredItemForMending(Bill bill, ThingFilter filter, bool remove, out Thing gotten)
		{
			gotten = null;
			foreach (LinkedList<Thing> l in this.storedThings.Values)
			{
				if (l != null && l.Count > 0)
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

							gotten = t;
							l.Remove(n);
							this.DropThing(t, false);
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

		public void Empty(List<Thing> droppedThings = null, bool force = false)
		{
			if (!force && !this.IsOperational && !Settings.EmptyOnPowerLoss)
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
					l.Clear();
				}
				this.storedCount = 0;
				this.storedWeight = 0;
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
					if (chosen == null || !this.ChosenContains(t, chosen))
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
			LinkedList<Thing> l;
			if (this.storedThings.TryGetValue(expectedDef.ToString(), out l))
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
				BuildingUtil.DropSingleThing(newItem, this, this.Map, false);
			}
			else if (!this.Add(newItem))
			{
				this.DropThing(newItem, false);
			}
		}

		public new bool Accepts(Thing thing)
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
			return true;
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
				if (!this.Accepts(thing))
					return false;
				if (thing.stackCount == 0)
				{
#if DEBUG
                Log.Warning("Add " + thing.Label + " has 0 stack count");
#endif
					return true;
				}
			}
			if (thing.Spawned)
			{
				thing.DeSpawn();
			}

			int thingsAdded = thing.stackCount;
			LinkedList<Thing> l;
			if (this.storedThings.TryGetValue(thing.def.ToString(), out l))
			{
				bool absorbed = false;
				if (thing.def.stackLimit > 1)
				{
					foreach (Thing t in l)
					{
						if (t.TryAbsorbStack(thing, false))
						{
							absorbed = true;
						}
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
				this.storedThings.Add(thing.def.ToString(), l);
			}
			this.UpdateStoredStats(thing, thingsAdded, true);
			return true;
		}

		private float GetThingWeight(Thing thing, int count)
		{
			return thing.GetStatValue(StatDefOf.Mass, true) * count;
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
			foreach (LinkedList<Thing> l in this.storedThings.Values)
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
						rv.AddRange(l);
						if (remove == true)
						{
							l.Clear();
						}
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
			foreach (LinkedList<Thing> l in this.storedThings.Values)
			{
				if (l.Count > 0)
				{
					if (bill.IsFixedOrAllowedIngredient(l.First.Value.def))
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
			}
			return gotten != null;
		}

		public bool TryGetValue(ThingDef def, out Thing t)
		{
			if (def != null)
			{
				LinkedList<Thing> l;
				if (this.storedThings.TryGetValue(def.ToString(), out l))
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
			LinkedList<Thing> l;
			if (this.storedThings.TryGetValue(thing.def.ToString(), out l))
			{
				if (l.Remove(thing))
				{
					this.UpdateStoredStats(thing, thing.stackCount, false);
					return true;
				}
			}
			return false;
		}

		public bool DropMeatThings(Bill bill)
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
								this.DropThing(n.Value, false);
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

            return toDrop.Count > 0;*/
		}

		public bool TryRemove(ThingDef def, out IEnumerable<Thing> removed)
		{
			LinkedList<Thing> l;
			if (this.storedThings.TryGetValue(def.ToString(), out l))
			{
				this.storedThings.Remove(def.ToString());
				removed = l;
				foreach (Thing t in l)
				{
					this.UpdateStoredStats(t, t.stackCount, false);
				}
				return true;
			}
			removed = null;
			return false;
		}

		public bool TryRemove(Thing thing, int count, out List<Thing> removed)
		{
			return this.TryRemove(thing.def, count, out removed);
		}

		public bool TryRemove(ThingDef def, int count, out List<Thing> removed)
		{
#if DEBUG || DROP_DEBUG
            Log.Warning("TryRemove(ThingDef, int, out List<Thing>)");
#endif
			LinkedList<Thing> l;
			removed = null;
			if (this.storedThings.TryGetValue(def.ToString(), out l))
			{
				int need = count;
				int removeCount = 0;
				LinkedListNode<Thing> n = l.First;
				while (n != null && need > 0)
				{
					Thing t = n.Value;
					LinkedListNode<Thing> next = n.Next;
#if DEBUG || DROP_DEBUG
                    Log.Warning("    Iteration: " + t.Label);
#endif

					if (removed == null)
					{
						removed = new List<Thing>();
					}

					if (t.stackCount == 0 || t.Destroyed)
					{
#if DEBUG || DROP_DEBUG
                        Log.Warning("        0 stack count");
#endif
						l.Remove(n);
					}
					else if (need >= t.stackCount)
					{
#if DEBUG || DROP_DEBUG
                        Log.Warning("        need >= t.stackCount");
#endif
						//Log.Error("Using meat: " + t.ThingID + " " + t.stackCount + " " + t.Destroyed);
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
							//Log.Error("toRemove: " + toRemove + " -- Math.Min " + need + ", " + t.def.stackLimit);
							need -= toRemove;
							Thing split = t.SplitOff(toRemove);
							//Log.Error("Parent meat: " + t.ThingID + " " + t.stackCount + " " + t.Destroyed);
							//Log.Error("Split meat: " + split.ThingID + " " + split.stackCount + " " + split.Destroyed);
#if DEBUG || DROP_DEBUG
                        Log.Warning("        else split off to remove - " + split.ToString() + " - " + split.Label);
#endif
							removed.Add(split);
						}
					}
					n = next;
				}

				if (removed != null && removed.Count > 0 && removeCount > 0)
				{
#if DEBUG || DROP_DEBUG
                    Log.Warning("    UpdateStoredStats: List.Count: " + removed.Count + " removeCount: " + removeCount);
#endif
					this.UpdateStoredStats(removed[0], removeCount, false);
					return true;
				}
			}

			return false;
		}

		private void UpdateStoredStats(Thing thing, int count, bool isAdding, bool force = false)
		{
			float weight = this.GetThingWeight(thing, count);
			if (!isAdding)
			{
				weight *= -1;
				count *= -1;
			}
			this.storedCount += count;
			this.storedWeight += weight;
			if (this.storedWeight < 0)
			{
				this.storedCount = 0;
				this.storedWeight = 0;
				foreach (LinkedList<Thing> l in this.storedThings.Values)
				{
					foreach (Thing t in l)
					{
						this.UpdateStoredStats(thing, count, true, true);
					}
				}
			}
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

			if (!this.IsOperational && Settings.EmptyOnPowerLoss && this.storedCount > 0 && Settings.EnergyFactor > 0.00001f)
			{
				this.Empty(null, true);
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

			if (this.includeInWorldLookup)
			{
				return SaveStorageSettingsUtil.AddSaveLoadGizmos(l, this.GetSaveStorageSettingType(), this.settings.filter);
			}
			return l;
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
			return SaveTypeEnum.Zone_Stockpile.ToString();
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