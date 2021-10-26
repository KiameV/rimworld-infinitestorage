using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace InfiniteStorage
{
	/*struct DroppedItemsFrom
    {
		public Building_InfiniteStorage Storage;
		public List<Thing> Dropped;
		public DroppedItemsFrom(Building_InfiniteStorage storage)
        {
			this.Storage = storage;
			this.Dropped = new List<Thing>();
        }
	}*/

	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestIngredientsHelper")]
	static class Patch_WorkGiver_DoBill_TryFindBestIngredientsHelper
	{
		static void Prefix(List<IngredientCount> ingredients, Pawn pawn, ref List<Pair<Building_InfiniteStorage, List<Thing>>> __state)
		{
			foreach (var s in WorldComp.GetInfiniteStorages(pawn.Map))
			{
				List<Thing> d = null;
				foreach (var i in ingredients)
				{
					if (s.TryDropThings(i, out List<Thing> dropped))
					{
						if (d == null)
							d = new List<Thing>();
						d.AddRange(dropped);
					}
				}
				if (d?.Count > 0)
				{
					if (__state == null)
						__state = new List<Pair<Building_InfiniteStorage, List<Thing>>>();
					__state.Add(new Pair<Building_InfiniteStorage, List<Thing>>(s, d));
				}
			}
		}
		static void Postfix(ref bool __result, Pawn pawn, List<IngredientCount> ingredients, List<ThingCount> chosen, ref List<Pair<Building_InfiniteStorage, List<Thing>>> __state)
		{
			/*
			Log.Warning($"{pawn.Name.ToStringShort} Result: {__result}");
			foreach (var c in chosen)
				Log.Warning($" - Chose: {c.Thing.def.defName} x{c.Count}");
			foreach (var i in ingredients)
				Log.Warning($"- Ing: {i.GetBaseCount()}");
			*/

			if (__result == false)
			{
				foreach (var p in __state)
				{
					foreach (var t in p.Second)
					{
						p.First.Add(t);
					}
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