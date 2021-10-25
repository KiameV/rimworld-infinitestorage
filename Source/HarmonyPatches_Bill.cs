using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace InfiniteStorage
{
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestIngredientsHelper")]
	static class Patch_WorkGiver_DoBill_TryFindBestIngredientsHelper
	{
		static void Prefix(List<IngredientCount> ingredients, Pawn pawn, ref List<Pair<Building_InfiniteStorage, List<Thing>>> __state)
		{
			List<Thing> d = null;
			foreach (var s in WorldComp.GetInfiniteStorages(pawn.Map))
			{
				foreach (var i in ingredients)
				{
					if (s.TryDropFilteredThings(i.filter, out List<Thing> dropped))
					{
						if (d == null)
							d = new List<Thing>();
						d.AddRange(dropped);
					}
				}
				if (d.Count > 0)
				{
					if (__state == null)
						__state = new List<Pair<Building_InfiniteStorage, List<Thing>>>();
					__state.Add(new Pair<Building_InfiniteStorage, List<Thing>>(s, d));
				}
				d?.Clear();
			}
		}
		static void Postfix(ref bool __result, Pawn pawn, List<ThingCount> chosen, ref List<Pair<Building_InfiniteStorage, List<Thing>>> __state)
		{
			Log.Warning($"Result: {__result}");
			if (__result == false)
			{
				foreach (var p in __state)
					foreach (var t in p.Second)
					{
						p.First.Add(t);
					}

			}
			else
			{
				Dictionary<int, int> chosenLookup = new Dictionary<int, int>();
				foreach (var t in chosen)
				{
					chosenLookup.Add(t.Thing.thingIDNumber, t.Count);
				}
				foreach (var s in WorldComp.GetInfiniteStorages(pawn.Map))
				{
					s.ReclaimFaster(true, chosenLookup);
				}
			}
		}
	}
}