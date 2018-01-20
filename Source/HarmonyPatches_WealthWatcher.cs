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
        [HarmonyPatch(typeof(WealthWatcher), "ForceRecount")]
        static class Patch_WealthWatcher_ForceRecount
        {
            static void Postfix(WealthWatcher __instance)
            {
                Map map = (Map)__instance.GetType().GetField("map", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
                //float wealthBuildings = __instance.GetType().GetField("wealthBuildings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as float;
                FieldInfo wealthItemsFI = __instance.GetType().GetField("wealthItems", BindingFlags.NonPublic | BindingFlags.Instance);
                float wealthItems = (float)wealthItemsFI.GetValue(__instance);

                wealthItems = TallyWealth(WorldComp.GetInfiniteStorages(map), wealthItems);
                //wealthItems = TallyWealth(WorldComp.GetNonGlobalInfiniteStorages(map), wealthItems);
                

                wealthItemsFI.SetValue(__instance, wealthItems);
            }

            private static float TallyWealth(IEnumerable<Building_InfiniteStorage> storages, float wealthItems)
            {
                foreach (Building_InfiniteStorage storage in storages)
                {
                    foreach (Thing t in storage.StoredThings)
                    {
                        wealthItems += (float)t.stackCount * t.def.BaseMarketValue;
                    }
                }
                return wealthItems;
            }
        }
    }
}