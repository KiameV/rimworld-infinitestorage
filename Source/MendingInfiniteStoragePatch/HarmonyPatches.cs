using InfiniteStorage;
using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace MendingInfiniteStoragePatch
{
    [StaticConstructorOnStartup]
    class HarmonyPatches
    {
        static HarmonyPatches()
        {
            //if (ModsConfig.ActiveModsInLoadOrder.Any(m => "MendAndRecycle".Equals(m.Name)))
            {
                try
                {
                    var harmony = HarmonyInstance.Create("com.MendingInfiniteStoragePatch.rimworld.mod");

                    harmony.PatchAll(Assembly.GetExecutingAssembly());

                    Log.Message(
                        "MendingInfiniteStoragePatch Harmony Patches:" + Environment.NewLine +
                        "  Postfix:" + Environment.NewLine +
                        "    WorkGiver_DoBill.TryFindBestBillIngredients - Priority Last");
                }
                catch (Exception e)
                {
                    Log.Error("Failed to patch Mending & Recycling." + Environment.NewLine + e.Message);
                }
            }
           // else
            {
             //   Log.Message("MendingInfiniteStoragePatch did not find MendAndRecycle. Will not load patch.");
            }
        }
    }

    [HarmonyPriority(Priority.Last)]
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
					if (chosen == null)
						Log.Warning("Chosen is null");
					else
						__result = Building_InfiniteStorage.TryGetIngredients(bill, pawn, true, chosen);
				}
			}
        }
    }
}