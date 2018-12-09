using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
	static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
	{
		static void Postfix(ref bool __result, WorkGiver_DoBill __instance, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
		{
			if (__result == false &&
				pawn != null &&
				bill != null &&
				bill.recipe != null &&
				bill.Map == pawn.Map)
			{
				if (bill.recipe.defName.StartsWith("Mend") || bill.recipe.defName.StartsWith("Recycle"))
				{
					IEnumerable<Building_InfiniteStorage> storages = WorldComp.GetInfiniteStorages(bill.Map);
					if (storages == null)
					{
						Log.Message("MendingChangeDresserPatch failed to retrieve InfiniteStorages");
						return;
					}

					foreach (Building_InfiniteStorage storage in storages)
					{
						if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius)
						{
							Thing t;
							if (storage.TryGetFirstFilteredItemForMending(bill, bill.ingredientFilter, true, out t))
							{
								if (t.Spawned == false)
								{
									Log.Error("Failed to spawn item-to-mend [" + t.Label + "] from storage [" + storage.Label + "].");
									__result = false;
									chosen = null;
								}
								else
								{
									__result = true;
									chosen.Add(new ThingCount(t, 1));
								}
								return;
							}
						}
					}
				}
				else
				{
					__result = HarmonyPatches.TryFindBestBillIngredients(bill, pawn, billGiver, chosen);
				}
			}
		}
	}

	struct FoundIng
	{
		public Building_InfiniteStorage storage;
		public Thing Thing;
		public int Count;

		public FoundIng(Building_InfiniteStorage s, Thing thing, int count)
		{
			this.storage = s;
			this.Thing = thing;
			this.Count = count;
		}
	}

	class IngsNeeded
	{
		public ThingFilter filter;
		public float needed;
		public List<FoundIng> FoundIngs = new List<FoundIng>();
		public IngsNeeded(ThingFilter filter, float needed)
		{
#if BILL_DEBUG
			Log.Warning("            IngsNeeded " + needed);
#endif
			this.filter = filter;
			this.needed = needed;
		}

		internal void AddFoundIng(FoundIng foundIng, float factor)
		{
#if BILL_DEBUG
			Log.Warning("            IngsNeeded.AddFoundIng " + foundIng.Thing.def.defName + " " + (foundIng.Count * factor));
#endif
			this.needed -= foundIng.Count * factor;
			if (needed < 0.0001f)
				needed = 0f;
			this.FoundIngs.Add(foundIng);
		}
	}
	partial class HarmonyPatches
	{
		private static List<Thing> newRelevantThings = null;
		private static List<Thing> relevantThings;
		private static HashSet<Thing> processedThings;
		private static List<IngredientCount> ingredientsOrdered;
		private static List<Thing> tmpMedicine;
		private static Dictionary<int, FoundIng> foundIngs = new Dictionary<int, FoundIng>();

		private static void Init()
		{
			if (newRelevantThings == null)
			{
				newRelevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
				relevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
				processedThings = (HashSet<Thing>)typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
				ingredientsOrdered = (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
				tmpMedicine = (List<Thing>)typeof(WorkGiver_DoBill).GetField("tmpMedicine", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
#if BILL_DEBUG
				if (newRelevantThings == null)
					Log.Error("newRelevantThings is null");
				if (relevantThings == null)
					Log.Error("relevantThings is null");
				if (processedThings == null)
					Log.Error("processedThings is null");
				if (ingredientsOrdered == null)
					Log.Error("ingredientsOrdered is null");
				if (tmpMedicine == null)
					Log.Error("tmpMedicine is null");
#endif
			}
		}

		// RimWorld.WorkGiver_DoBill
		public static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
		{
			Init();
			foundIngs.Clear();
			chosen.Clear();
			newRelevantThings.Clear();
			if (bill.recipe.ingredients.Count == 0)
			{
				return true;
			}
			IntVec3 rootCell = (IntVec3)typeof(WorkGiver_DoBill).GetMethod("GetBillGiverRootCell", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { billGiver, pawn });
			Region rootReg = rootCell.GetRegion(pawn.Map, RegionType.Set_Passable);
			if (rootReg == null)
			{
				return false;
			}
			typeof(WorkGiver_DoBill).GetMethod("MakeIngredientsListInProcessingOrder", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { ingredientsOrdered, bill });
			relevantThings.Clear();
			processedThings.Clear();
			bool foundAll = false;
			Predicate<Thing> baseValidator = (Thing t) => t.Spawned && !t.IsForbidden(pawn) && (float)(t.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius && bill.IsFixedOrAllowedIngredient(t) && bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t)) && pawn.CanReserve(t, 1, -1, null, false);
			bool billGiverIsPawn = billGiver is Pawn;
			if (billGiverIsPawn)
			{
				AddEveryMedicineToRelevantThings(pawn, billGiver, relevantThings, baseValidator, pawn.Map, bill);
				if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
				{
					DropChosen(chosen);

					relevantThings.Clear();
					ingredientsOrdered.Clear();
					foundIngs.Clear();
					return true;
				}
			}
			TraverseParms traverseParams = TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false);
			RegionEntryPredicate entryCondition = (Region from, Region r) => r.Allows(traverseParams, false);
			int adjacentRegionsAvailable = rootReg.Neighbors.Count((Region region) => entryCondition(rootReg, region));
			int regionsProcessed = 0;
			processedThings.AddRange(relevantThings);

			Comparison<Thing> comparison = delegate (Thing t1, Thing t2)
			{
				float num = (float)(t1.Position - rootCell).LengthHorizontalSquared;
				float value = (float)(t2.Position - rootCell).LengthHorizontalSquared;
				return num.CompareTo(value);
			};

			RegionProcessor regionProcessor = delegate (Region r)
			{
				List<Thing> list = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
				for (int i = 0; i < list.Count; i++)
				{
					Thing thing = list[i];
					if (!processedThings.Contains(thing))
					{
						if (ReachabilityWithinRegion.ThingFromRegionListerReachable(thing, r, PathEndMode.ClosestTouch, pawn))
						{
							if (baseValidator(thing) && (!thing.def.IsMedicine || !billGiverIsPawn))
							{
								newRelevantThings.Add(thing);
								processedThings.Add(thing);
							}
						}
					}
				}
				regionsProcessed++;
				if (newRelevantThings.Count > 0 && regionsProcessed > adjacentRegionsAvailable)
				{
					/*Comparison<Thing> comparison = delegate (Thing t1, Thing t2)
					{
						float num = (float)(t1.Position - rootCell).LengthHorizontalSquared;
						float value = (float)(t2.Position - rootCell).LengthHorizontalSquared;
						return num.CompareTo(value);
					};*/
					newRelevantThings.Sort(comparison);
					relevantThings.AddRange(newRelevantThings);
					newRelevantThings.Clear();
					if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
					{
						foundAll = true;
						return true;
					}
				}
				return false;
			};

			foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStoragesWithinRadius(pawn.Map, billGiver.Position, bill.ingredientSearchRadius))
			{
				foreach (IngredientCount ing in bill.recipe.ingredients)
				{
#if BILL_DEBUG
					//Log.Warning("        need ing count: " + needed);
#endif
					foreach (LinkedList<Thing> l in s.storedThings.Values)
					{
						if (l != null && l.Count > 0 && ing.filter.Allows(l.First.Value.def))
						{
							foreach (Thing t in l)
							{
								if (ing.filter.Allows(t))
								{
									newRelevantThings.Add(t);
									int count = (int)(ing.GetBaseCount() / bill.recipe.IngredientValueGetter.ValuePerUnitOf(t.def));
									if (count > t.stackCount)
										count = t.stackCount;
									foundIngs[t.thingIDNumber] = new FoundIng(s, t, count);
								}
							}
						}
					}
				}
			}
			newRelevantThings.Sort(comparison);
			relevantThings.AddRange(newRelevantThings);
			newRelevantThings.Clear();
			if ((bool)typeof(WorkGiver_DoBill).GetMethod("TryFindBestBillIngredientsInSet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { relevantThings, bill, chosen }))
			{
				foundAll = true;
			}
			else if (foundIngs.Count > 0)
			{
				RegionTraverser.BreadthFirstTraverse(rootReg, entryCondition, regionProcessor, 99999, RegionType.Set_Passable);
			}

			relevantThings.Clear();
			newRelevantThings.Clear();
			processedThings.Clear();
			ingredientsOrdered.Clear();

#if BILL_DEBUG
			Log.Warning("Found All: " + foundAll);
			Log.Warning("Chosen: " + chosen.Count);
			chosen.ForEach(v => Log.Warning("    - " + v.Thing.ThingID + " -- " + v.Count));
			Log.Warning("FoundIngs: " + foundIngs.Count);
			foreach (FoundIng f in foundIngs.Values)
				Log.Warning("    - " + f.Thing.ThingID + " -- " + f.Count + " -- " + f.storage.ThingID);
#endif

			if (foundAll && DropChosen(chosen))
			{
				foundIngs.Clear();
				return true;
			}
			foundIngs.Clear();
			return false;
		}

		private static bool DropChosen(List<ThingCount> chosen)
		{
			List<ThingCount> newChosen = new List<ThingCount>();
			foreach (var tc in chosen)
			{
				FoundIng f;
				if (foundIngs.TryGetValue(tc.Thing.thingIDNumber, out f))
				{
					if (f.storage != null)
					{
						List<Thing> removed;
						if (f.storage.TryRemove(f.Thing, tc.Count, out removed))
						{
							foreach (var t in removed)
							{
								if (BuildingUtil.DropSingleThing(t, f.storage, f.storage.Map, false))
									newChosen.Add(new ThingCount(t, t.stackCount));
								else
								{
									Log.Warning("Failed to spawn item " + t.Label);
									f.storage.Add(t);
									return false;
								}
							}
						}
						else
						{
							Log.Warning("Failed to remove " + f.Thing.Label);
							return false;
						}
					}
				}
				else
					newChosen.Add(tc);
			}
			chosen.Clear();
			chosen.AddRange(newChosen);
			return true;
		}

		private static void AddEveryMedicineToRelevantThings(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map, Bill bill)
		{
			MedicalCareCategory medicalCareCategory = (MedicalCareCategory)typeof(WorkGiver_DoBill).GetMethod("GetMedicalCareCategory", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { billGiver });
			List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine);
			tmpMedicine.Clear();
			for (int i = 0; i < list.Count; i++)
			{
				Thing thing = list[i];
				if (medicalCareCategory.AllowsMedicine(thing.def) && baseValidator(thing) && pawn.CanReach(thing, PathEndMode.OnCell, Danger.Deadly, false, TraverseMode.ByPawn))
				{
					tmpMedicine.Add(thing);
				}
			}
			tmpMedicine.SortBy((Thing x) => -x.GetStatValue(StatDefOf.MedicalPotency, true), (Thing x) => x.Position.DistanceToSquared(billGiver.Position));
			relevantThings.AddRange(tmpMedicine);

			//(float)(t.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius && bill.IsFixedOrAllowedIngredient(t) && bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t))
			foreach (Building_InfiniteStorage s in WorldComp.GetInfiniteStoragesWithinRadius(map, billGiver.Position, bill.ingredientSearchRadius))
			{
				List<Thing> gotten;
				if (s.TryGetFilteredThings(bill, bill.ingredientFilter, out gotten))
				{
					relevantThings.AddRange(gotten);
					gotten.ForEach(v => foundIngs[v.thingIDNumber] = new FoundIng(s, v, v.stackCount));
				}
			}

			tmpMedicine.Clear();
		}











		/*

		public static bool TryGetIngredients(Bill bill, Pawn pawn, bool remove)
		{
#if BILL_DEBUG
			Log.Warning("Start TryGetIngredients");
#endif
			Log.Warning("    Chosen:");
			foreach (var c in chosen)
				Log.Warning("      - " + c.Thing.def.defName + " " + c.Thing.stackCount);
			Log.Warning("    Available:");
			if (availableThings == null)
				Log.Warning("        Null");
			else
				foreach (var a in availableThings)
					Log.Warning("      - " + a.def.defName);

			if (bill.recipe.ingredients == null)
			{
#if BILL_DEBUG
				Log.Warning("No ingredients for bill " + bill.recipe.defName);
#endif
				return false;
			}

#if BILL_DEBUG
			Log.Warning("    Ings Needed:");
#endif
			List<IngsNeeded> toGet = new List<IngsNeeded>(bill.recipe.ingredients.Count);
			foreach (var ing in bill.recipe.ingredients)
			{
				//Log.Message("        Filters:");
				//	foreach (var v in ing.filter.AllowedThingDefs)
				//		Log.Message("          - " + v.defName);
				float needed = ing.GetBaseCount();
#if BILL_DEBUG
				Log.Warning("        need ing count: " + needed);
#endif
				foreach (var c in chosen)
				{
					if (ing.filter.Allows(c.Thing))
					{
#if BILL_DEBUG
						Log.Warning("            From chosen " + c.Thing.def.defName);
#endif
						needed -= c.Count * bill.recipe.IngredientValueGetter.ValuePerUnitOf(c.Thing.def);
#if BILL_DEBUG
						Log.Warning("            still need " + needed);
#endif
					}
				}
				if (needed >= 0)
				{
#if BILL_DEBUG
					Log.Warning("        Still need " + needed + " of ing");
#endif
					toGet.Add(new IngsNeeded(ing.filter, needed));
				}
#if BILL_DEBUG
				else
				{
					Log.Warning("        Chosen has all needed of ing");
				}
#endif
			}

			if (toGet.Count == 0)
			{
#if BILL_DEBUG
				Log.Warning("    Chosen has all ings. Exiting True.");
#endif
				return true;
			}

#if BILL_DEBUG
			Log.Warning("    Find Ings In Storages");
#endif
			var storages = WorldComp.GetInfiniteStoragesWithinRadius(pawn.Map, pawn.Position, bill.ingredientSearchRadius);
			foreach (var ing in toGet)
			{
				Log.Warning("    " + ing.needed);
				foreach (var storage in storages)
				{
					if (ing.needed <= 0)
						break;
#if BILL_DEBUG
					Log.Warning("    - Check " + storage.def.defName);
#endif
					foreach (LinkedList<Thing> l in storage.storedThings.Values)
					{
						if (ing.needed <= 0)
							break;
#if BILL_DEBUG
						Log.Warning("        - Does filter allow def " + ((l == null && l.Count > 0) ? "null false" : l.First.Value.def + " " + ing.filter.Allows(l.First.Value.def)));
#endif
						if (l != null && l.Count > 0 && ing.filter.Allows(l.First.Value.def))
						{
							foreach (Thing t in l)
							{
#if BILL_DEBUG
								Log.Warning("            " + t.def.defName + " " + t.stackCount);
#endif
								if (ing.needed <= 0)
									break;

								//foreach (var v in ing.filter.AllowedThingDefs)
								//	Log.Message("              - " + v.defName);
								if (ing.filter.Allows(t))
								{
									var factor = bill.recipe.IngredientValueGetter.ValuePerUnitOf(t.def);
									float toRemove = ing.needed / factor;
#if BILL_DEBUG
									Log.Warning("                toremove: " + toRemove + " = " + ing.needed + " / " + factor);
#endif
									if (t.stackCount < ing.needed)
										toRemove = t.stackCount;
#if BILL_DEBUG
									Log.Warning("                going to remove: " + toRemove);
#endif
									ing.AddFoundIng(new FoundIng(storage, t, (int)toRemove), factor);
#if BILL_DEBUG
									Log.Warning("              Matches! Still need: " + ing.needed);
#endif
								}
#if BILL_DEBUG
								else
								{
									Log.Warning("              Does not match...");
								}
#endif
							}
						}
					}
				}
			}

#if BILL_DEBUG
			Log.Warning("    Check Result:");
#endif
			foreach (var ing in toGet)
			{
				if (ing.needed != 0)
				{
#if BILL_DEBUG
					Log.Warning("        Not all ings found. Exit False.");
#endif
					return false;
				}
			}

#if BILL_DEBUG
			Log.Warning("    Spawn all chosen");
#endif
			foreach (var ing in toGet)
			{
				foreach (var f in ing.FoundIngs)
				{
					List<Thing> removed;
					if (f.storage.TryRemove(f.Thing, f.Count, out removed))
					{
						foreach (var v in removed)
						{
							BuildingUtil.DropThing(v, f.storage, f.storage.Map, false);
							chosen.Add(new ThingCount(v, v.stackCount));
						}
					}
#if BILL_DEBUG
					else
					{
						Log.Error("Failed to remove ingredient " + f.Thing.def.defName + " " + f.Count);
					}
#endif
				}
			}

#if BILL_DEBUG
			Log.Warning("    Result:");
			foreach (var v in chosen)
			{
				Log.Warning("        Chosen: Thing " + v.Thing.Label + " Count " + v.Count + " --- stack count " + v.Thing.stackCount);
			}
			Log.Warning("End TryGetIngredients");
#endif
			return true;
		}
	}*/













		/*partial class HarmonyPatches
		{
			struct StoredThings
			{
				public readonly Building_InfiniteStorage Storage;
				public readonly Thing Thing;
				public StoredThings(Building_InfiniteStorage storage, Thing thing)
				{
					this.Storage = storage;
					this.Thing = thing;
				}
			}

			struct ThingsToUse
			{
				public readonly List<StoredThings> Things;
				public readonly int Count;
				public ThingsToUse(List<StoredThings> things, int count)
				{
					this.Things = things;
					this.Count = count;
				}
			}

			class NeededIngrediants
			{
				public readonly ThingFilter Filter;
				public int Count;
				public readonly Dictionary<Def, List<StoredThings>> FoundThings;

				public NeededIngrediants(ThingFilter filter, int count)
				{
					this.Filter = filter;
					this.Count = count;
					this.FoundThings = new Dictionary<Def, List<StoredThings>>();
				}
				public void Add(StoredThings things)
				{
					List<StoredThings> l;
					if (!this.FoundThings.TryGetValue(things.Thing.def, out l))
					{
						l = new List<StoredThings>();
						this.FoundThings.Add(things.Thing.def, l);
					}
					l.Add(things);
				}
				public void Clear()
				{
					this.FoundThings.Clear();
				}
				public bool CountReached()
				{
					foreach (List<StoredThings> l in this.FoundThings.Values)
					{
						if (this.CountReached(l))
							return true;
					}
					return false;
				}
				private bool CountReached(List<StoredThings> l)
				{
					int count = this.Count;
					foreach (StoredThings st in l)
					{
						count -= st.Thing.stackCount;
					}
					return count <= 0;
				}
				public List<StoredThings> GetFoundThings()
				{
					foreach (List<StoredThings> l in this.FoundThings.Values)
					{
						if (this.CountReached(l))
						{
#if DEBUG
							Log.Warning("Count [" + Count + "] reached with: " + l[0].Thing.def.label);
#endif
							return l;
						}
					}
					return null;
				}

				internal bool HasFoundThings()
				{
					return this.FoundThings.Count > 0;
				}
			}

			[HarmonyPatch(typeof(WorkGiver_DoBill), "AddEveryMedicineToRelevantThings")]
			static class Patch_WorkGiver_DoBill_AddEveryMedicineToRelevantThings
			{
				static void PostFix(ref bool __result, Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map)
				{
					foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
					{
						relevantThings.AddRange(storage.GetMedicalThings());
					}
				}
			}

			/*[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredientsInSet")]
			static class Patch_WorkGiver_DoBill_TryFindBestBillIngredientsInSet
			{
				internal readonly static List<Thing> ReservedThings = new List<Thing>();

				private static FieldInfo RelevantThingsFI = null;

				static void PostFix()
				{
					if (RelevantThingsFI == null)
					{
						RelevantThingsFI = typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic);
					}

					List<Thing> rt = (List<Thing>)RelevantThingsFI.GetValue(null);
#if BILL_DEBUG
					StringBuilder sb = new StringBuilder("TryFindBestBillIngredientsInSet: RT to add: [");
					foreach (Thing t in rt)
					{
						sb.Append(t.Label);
						sb.Append(", ");
					}
					Log.Warning(sb.ToString());
#endif
					ReservedThings.Clear();
					ReservedThings.AddRange(rt);
				}
			}* /

			[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
			static class Patch_WorkGiver_DoBill_TryFindBestBillIngredients
			{
				private static Stack<Building_InfiniteStorage> emptied = new Stack<Building_InfiniteStorage>();

				[HarmonyPriority(Priority.VeryHigh)]
				static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
				{
					List<CodeInstruction> l = new List<CodeInstruction>(instructions);
					for (int i = l.Count - 1, clearCount = 0; i > l.Count - 20 && clearCount < 4; --i)
					{
						/*
						 * Remove clear calls for:
						 *  WorkGiver_DoBill.relevantThings.Clear();
						 *  WorkGiver_DoBill.newRelevantThings.Clear();
						 *  WorkGiver_DoBill.processedThings.Clear();
						 *  WorkGiver_DoBill.ingredientsOrdered.Clear();
						 * /
						if (l[i].opcode == OpCodes.Callvirt)
						{
							l[i].opcode = OpCodes.Nop;
							l[i].operand = null;
							l[i - 1].opcode = OpCodes.Nop;
							l[i - 1].operand = null;
							++clearCount;
						}
					}
					return l;
				}

				[HarmonyPriority(Priority.First)]
				static void Prefix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
				{
					if (bill == null ||
						bill.recipe == null ||
						bill.recipe.workSkill == SkillDefOf.Cooking)
					{
						foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
						{
							if (bill is Bill_Production && storage.def.defName.Equals("IS_MeatStorage"))
							{
								if (!bill.suspended && !((Bill_Production)bill).paused)
								{
									if (storage.DropMeatThings(bill))
									{
										emptied.Add(storage);
									}
								}
							}
						}
					}
				}

				[HarmonyPriority(Priority.VeryHigh)]
				static void Postfix(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
				{
					List<Thing> processedThings = new List<Thing>((HashSet<Thing>)typeof(WorkGiver_DoBill).GetField("processedThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
					List<Thing> relevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
					List<Thing> newRelevantThings = (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
					List<IngredientCount> ingredientsOrdered = (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

					if ((bill == null || bill.recipe == null || bill.recipe.workSkill == SkillDefOf.Cooking) ||
						(__result == true || !WorldComp.HasInfiniteStorages(bill.Map) || bill.Map != pawn.Map))
					{
						while (emptied.Count != 0)
						{
							Building_InfiniteStorage storage = emptied.Pop();
							storage.CanAutoCollect = true;
							storage.Reclaim(true, chosen);
						}
						return;
					}

					try
					{
						Process(ref __result, bill, pawn, billGiver, chosen, processedThings, relevantThings, newRelevantThings, ingredientsOrdered);
					}
					finally
					{
						processedThings.Clear();
						relevantThings.Clear();
						newRelevantThings.Clear();
						ingredientsOrdered.Clear();
					}
				}

				private static void Process(ref bool __result, Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen, List<Thing> processedThings, List<Thing> relevantThings, List<Thing> newRelevantThings, List<IngredientCount> ingredientsOrdered)
				{

#if BILL_DEBUG
					Log.Warning("TryFindBestBillIngredients.Postfix __result: " + __result + " bill: " + bill.Label + " chosenAmounts orig count: " + chosen.Count + " processed thigns: " + processedThings.Count);

					Log.Warning("    Chosen:");
					foreach (ThingCount a in chosen)
					{
						Log.Warning("        " + a.Thing.Label + " " + a.Thing.stackCount + " " + a.Count);
					}

					Log.Warning("    WorkGiver_DoBill.Processed:");
					foreach (Thing t in processedThings)
					{
						Log.Warning("        " + t.Label);
					}

					Log.Warning("    WorkGiver_DoBill.RelevantThings:");
					foreach (Thing t in (List<Thing>)typeof(WorkGiver_DoBill).GetField("relevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
					{
						Log.Warning("        " + t.Label);
					}

					Log.Warning("    WorkGiver_DoBill.NewRelevantThings:");
					foreach (Thing t in (List<Thing>)typeof(WorkGiver_DoBill).GetField("newRelevantThings", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
					{
						Log.Warning("        " + t.Label);
					}

					Log.Warning("    WorkGiver_DoBill.IngredientsOrdered:");
					foreach (IngredientCount i in (List<IngredientCount>)typeof(WorkGiver_DoBill).GetField("ingredientsOrdered", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
					{
						Log.Warning("        " + i.Summary);
					}
#endif
					Dictionary<ThingDef, int> chosenAmounts = new Dictionary<ThingDef, int>();
					foreach (ThingCount c in chosen)
					{
						int count;
						if (chosenAmounts.TryGetValue(c.Thing.def, out count))
						{
							count += c.Count;
						}
						else
						{
							count = c.Count;
						}
						chosenAmounts[c.Thing.def] = count;
					}

#if BILL_DEBUG
					Log.Warning("    ChosenAmounts:");
					foreach (KeyValuePair<ThingDef, int> kv in chosenAmounts)
					{
						Log.Warning("        " + kv.Key.label + " - " + kv.Value);
					}
					Log.Warning("    ProcessedThings:");
					foreach (Thing t in processedThings)
					{
						Log.Warning("        " + t.Label);
					}
#endif

					LinkedList<NeededIngrediants> neededIngs = new LinkedList<NeededIngrediants>();
					foreach (IngredientCount ing in bill.recipe.ingredients)
					{
						bool found = false;
						foreach (KeyValuePair<ThingDef, int> kv in chosenAmounts)
						{
							if ((int)ing.GetBaseCount() >= kv.Value)
							{
#if BILL_DEBUG
								Log.Warning("    Needed Ing population count: " + kv.Key.label + " count: " + kv.Value);
#endif
								if (ing.filter.Allows(kv.Key))
								{
#if BILL_DEBUG
									Log.Warning("    Needed Ing population found: " + kv.Key.label + " count: " + kv.Value);
#endif
									found = true;
									break;
								}
							}
						}
						if (!found)
						{
#if BILL_DEBUG
							Log.Warning("    Needed Ing population not found");
#endif
							neededIngs.AddLast(new NeededIngrediants(ing.filter, (int)ing.GetBaseCount()));
						}
					}

#if BILL_DEBUG
					Log.Warning("    Needed Ings:");
					foreach (NeededIngrediants ings in neededIngs)
					{
						Log.Warning("        " + ings.Count);
					}
#endif

					List<ThingsToUse> thingsToUse = new List<ThingsToUse>();
					foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(bill.Map))
					{
						if ((float)(storage.Position - billGiver.Position).LengthHorizontalSquared < Math.Pow(bill.ingredientSearchRadius, 2))
						{
							LinkedListNode<NeededIngrediants> n = neededIngs.First;
							while (n != null)
							{
								var next = n.Next;
								NeededIngrediants neededIng = n.Value;

								for (int i = processedThings.Count - 1; i >= 0 && neededIng.Count > 0; --i)
								{
									Thing t = processedThings[i];
									if (neededIng.Filter.Allows(t))
									{
										int amount = Math.Min(neededIng.Count, t.stackCount);
										neededIng.Count -= amount;
										chosen.Add(new ThingCount(t, amount));
										processedThings.RemoveAt(i);
#if BILL_DEBUG
										Log.Warning("            neededIng processedThings found: " + t.Label + " count: " + amount + " neededIng count: " + neededIng.Count);
										Log.Warning("            neededIng.CountReached: " + neededIng.CountReached());
#endif
									}
								}
								if (!neededIng.CountReached())
								{
									List<Thing> gotten;
									if (storage.TryGetFilteredThings(bill, neededIng.Filter, out gotten))
									{
#if BILL_DEBUG
										Log.Warning("            Found ings: " + gotten.Count + " need count: " + neededIng.Count);
#endif
										foreach (Thing got in gotten)
										{
#if BILL_DEBUG
											Log.Warning("                ing: " + got.Label);
#endif
											neededIng.Add(new StoredThings(storage, got));
										}
									}
								}
								if (neededIng.CountReached() || neededIng.Count <= 0)
								{
#if BILL_DEBUG
									Log.Warning("                    -removing ing " + neededIng.Count);
#endif
									if (neededIng.HasFoundThings())
										thingsToUse.Add(new ThingsToUse(neededIng.GetFoundThings(), neededIng.Count));
									neededIng.Clear();
									neededIngs.Remove(n);
								}
								n = next;
							}
						}
					}

#if BILL_DEBUG
					Log.Warning("    neededIngs.count: " + neededIngs.Count);
					Log.Warning("    ThingsToUse.count: " + thingsToUse.Count);
#endif

					if (neededIngs.Count == 0)
					{
						__result = true;
						foreach (ThingsToUse ttu in thingsToUse)
						{
							int count = ttu.Count;
							foreach (StoredThings st in ttu.Things)
							{
								if (count <= 0)
									break;
								List<Thing> removed;
								if (st.Storage.TryRemove(st.Thing, count, out removed))
								{
#if BILL_DEBUG
									Log.Warning("    Storage Removing: " + st.Thing + " Count: " + count + " removed: " + removed.ToString());
#endif
									foreach (Thing r in removed)
									{
										count -= r.stackCount;
										List<Thing> dropped = new List<Thing>();
										BuildingUtil.DropThing(r, r.stackCount, st.Storage, st.Storage.Map, false, dropped);
										foreach (Thing t in dropped)
										{
#if BILL_DEBUG
											Log.Warning("        Dropped: " + t.Label);
#endif
											chosen.Add(new ThingCount(t, t.stackCount));
										}
									}
								}
							}
						}
					}

#if BILL_DEBUG
					StringBuilder sb = new StringBuilder();
					foreach (ThingCount ta in chosen)
					{
						if (sb.Length > 0)
							sb.Append(", ");
						sb.Append(ta.Thing.def.defName + "x" + ta.Count);
					}
					Log.Warning("        Chosen: [" + sb.ToString() + "]");
#endif

					thingsToUse.Clear();
					foreach (NeededIngrediants n in neededIngs)
					{
						if (n != null)
							n.Clear();
					}
					neededIngs.Clear();
					chosenAmounts.Clear();
				}
			}
		}*/
	}
}