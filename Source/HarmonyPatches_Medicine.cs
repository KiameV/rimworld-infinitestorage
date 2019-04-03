using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;
using Verse.AI;

namespace InfiniteStorage
{
	partial class HarmonyPatches
    {
		[HarmonyPatch]
		static class WorkGiver_Tend_Smart_Medicine_Patch
		{
			static MethodBase target;

			static bool Prepare()
			{
				var mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.Name == "Smart Medicine");
				if (mod == null)
				{
					return false;
				}

				var type = mod.assemblies.loadedAssemblies
							.FirstOrDefault(a => a.GetName().Name == "SmartMedicine")?
							.GetType("SmartMedicine.FindBestMedicine");

				if (type == null)
				{
					Log.Warning("InfiniteStorage can't patch 'Smart Medicine'");

					return false;
				}

				target = AccessTools.DeclaredMethod(type, "Find");
				if (target == null)
				{
					Log.Warning("InfiniteStorage can't patch 'Smart Medicine' Find");

					return false;
				}

				return true;
			}

			static MethodBase TargetMethod()
			{
				return target;
			}

			static void Postfix(ref List<ThingCount>  __result, Pawn healer, Pawn patient, ref int totalCount)
			{
				if (healer.Map != patient.Map)
					return;

				foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStoragesWithinRadius(healer.Map, patient.Position, 20))
				{
					IEnumerable<Thing> removed = storage.GetMedicalThings(false, true);
					foreach (Thing r in removed)
					{
						List<Thing> dropped = new List<Thing>();
						BuildingUtil.DropThing(r, r.stackCount, storage, storage.Map, false, dropped);
						foreach (Thing t in dropped)
						{
							__result.Add(new ThingCount(t, t.stackCount));
							t.Map.reservationManager.CanReserveStack(healer, t, 1, ReservationLayerDefOf.Floor, true);
						}
					}
				}
			}
		}

		[HarmonyPatch(typeof(HealthAIUtility), "FindBestMedicine")]
		static class Patch_HealthAIUtility_FindBestMedicine
		{
			/*struct StorageMedicine
            {
                public readonly Building_InfiniteStorage Storage;
                public readonly IEnumerable<Thing> Medicine;
                public StorageMedicine(Building_InfiniteStorage storage, IEnumerable<Thing> medicine)
                {
                    this.Storage = storage;
                    this.Medicine = medicine;
                }
            }
            static void Prefix(ref Thing __result, Pawn healer, Pawn patient)
            {
                List<StorageMedicine> meds = new List<StorageMedicine>();
                List<Thing> searchSet = new List<Thing>();
                searchSet.Add(__result);
                foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(patient.Map))
                {
                    IEnumerable<Thing> l = storage.GetMedicalThings(false);
                    if (l != null)
                    {
                        meds.Add(new StorageMedicine(storage, l));
                    }
                }

                GenClosest.ClosestThing_Global_Reachable(
                    patient.Position, patient.map, searchSet, peMode, traverseParams, 9999f, validator, priorityGetter);
            }*/
			//private readonly static List<Thing> dropped = new List<Thing>();

			[HarmonyPriority(Priority.First)]
			static void Prefix(Pawn healer, Pawn patient)
			{
				foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(patient.Map))
				{
					IEnumerable<Thing> removed = storage.GetMedicalThings(false, true);
					foreach (Thing r in removed)
					{
						BuildingUtil.DropThing(r, r.stackCount, storage, storage.Map, false);//, dropped);
					}
				}
			}

			/*static void Postfix(Thing __result)
            {
                if (__result != null)
                    Log.Message("Tend with: " + __result + " is reserved: " + __result.Map.reservationManager.IsReservedByAnyoneOf(__result, Faction.OfPlayer));
                else
                    Log.Message("Tend no med");
            }*/
		}



        [HarmonyPatch(typeof(HealthCardUtility), "DrawMedOperationsTab")]
        static class Patch_HealthCardUtility_DrawMedOperationsTab
        {
            private static long lastUpdate = 0;
            private static List<Thing> cache = new List<Thing>();

            [HarmonyPriority(Priority.First)]
            static void Prefix()
            {
#if MED_DEBUG
                Log.Warning("HealthCardUtility.DrawMedOperationsTab");
#endif

                if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Count > 0)
                {
                    Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
                }

                Map map = Find.CurrentMap;
                if (map != null)
                {
#if MED_DEBUG
                    Log.Warning("    Map is not null: " + (map != null).ToString());
#endif
                    long now = DateTime.Now.Ticks;
                    if (cache == null || now - lastUpdate > TimeSpan.TicksPerSecond)
                    {
                        cache.Clear();
                        foreach (Building_InfiniteStorage storage in WorldComp.GetInfiniteStorages(map))
                        {
#if MED_DEBUG
                            Log.Warning("    Storage: " + storage.Label);
#endif
                            if (storage.def.defName.Equals("IS_BodyPartStorage") ||
                                storage.def.defName.Equals("InfiniteStorage"))
                            {
                                cache.AddRange(storage.GetMedicalThings(true, false));
                            }
                        }
                        lastUpdate = now;
                    }
                    Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.AddRange(cache);
                }
            }

            static void Postfix()
            {
                if (Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Count > 0)
                {
                    Patch_ListerThings_ThingsInGroup.AvailableMedicalThing.Clear();
                }
            }

            /*public static IEnumerable<ThingDef> PotentiallyMissingIngredients(Pawn billDoer, Map map)
            {
                for (int i = 0; i < this.ingredients.Count; i++)
                {
                    IngredientCount ing = this.ingredients[i];
                    bool foundIng = false;
                    List<Thing> thingList = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
                    for (int j = 0; j < thingList.Count; j++)
                    {
                        Thing thing = thingList[j];
                        if ((billDoer == null || !thing.IsForbidden(billDoer)) && !thing.Position.Fogged(map) && (ing.IsFixedIngredient || this.fixedIngredientFilter.Allows(thing)) && ing.filter.Allows(thing))
                        {
                            foundIng = true;
                            break;
                        }
                    }
                    if (!foundIng)
                    {
                        if (ing.IsFixedIngredient)
                        {
                            yield return ing.filter.AllowedThingDefs.First<ThingDef>();
                        }
                        else
                        {
                            ThingDef def = (from x in ing.filter.AllowedThingDefs
                                            orderby x.BaseMarketValue
                                            select x).FirstOrDefault((ThingDef x) => this.$this.fixedIngredientFilter.Allows(x));
                            if (def != null)
                            {
                                yield return def;
                            }
                        }
                    }
                }
            }

            public static void AddToCache(Thing t)
            {
                if (cache == null)
                    cache = new Dictionary<string, List<Thing>>();
                if (thingsCached == null)
                    thingsCached = new HashSet<int>();

                if (!thingsCached.Contains(t.thingIDNumber))
                {
                    if (!cache.TryGetValue(t.def.defName, out List<Thing> l))
                    {
                        l = new List<Thing>();
                        cache.Add(t.def.defName, l);
                    }
                    l.Add(t);
                    thingsCached.Add(t.thingIDNumber);
                }
            }*/
        }

        [HarmonyPatch(typeof(ListerThings), "ThingsInGroup")]
        static class Patch_ListerThings_ThingsInGroup
        {
            public readonly static List<Thing> AvailableMedicalThing = new List<Thing>();
            static void Postfix(ref List<Thing> __result, ThingRequestGroup group)
            {
#if MED_DEBUG
                //Log.Warning("ListerThings.ThingsInGroup");
#endif
                if (AvailableMedicalThing.Count > 0)
                {
#if MED_DEBUG
                    Log.Warning("ListerThings.ThingsInGroup");
#endif
#if MED_DEBUG
                    foreach (Thing t in AvailableMedicalThing)
                        Log.Warning("    " + t.Label);
#endif
                    __result.AddRange(AvailableMedicalThing);
                }
            }
        }
    }
}