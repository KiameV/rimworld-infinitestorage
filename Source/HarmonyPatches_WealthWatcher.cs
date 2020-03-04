using HarmonyLib;
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
        [HarmonyPatch(typeof(HistoryAutoRecorder), "ExposeData")]
        static class Patch_HistoryAutoRecorder_ExposeData
        {
            static void Postfix(HistoryAutoRecorder __instance)
            {
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    //StringBuilder sb = new StringBuilder("!!!! " + __instance.def + " !!!!\n");
                    List<float> records = __instance.records;
                    for (int i = 0; i < records.Count; ++i)
                    {
                        if (i > 0 && records[i] > 10000)
                        {
                            float max = records[i - 1] * 1.5f;

                            if (records[i] > max)
                            {
                                //sb.AppendLine("        i: " + i + ": records["+i+"] " + records[i] + " ---- records[" + (i - 1) + "] " + records[i - 1] + " ---- max: " + max);
                                int j = i + 1;
                                while(j < records.Count)
                                {
                                    //sb.AppendLine("        j: " + j + ": records[" + j + "] " + records[j] + " ---- records["+(i - 1)+"] " + records[i - 1] + " ---- max: " + max);
                                    if (records[j] < max)
                                        break;
                                    ++j;
                                }
                                float average;
                                if (j != records.Count)
                                {
                                    //sb.AppendLine("        --" + j + ": " + records[j]);
                                    average = (records[i - 1] + records[j]) * 0.5f;
                                }
                                else
                                {
                                    //sb.AppendLine("        --" + j + ": end of list");
                                    average = records[i];
                                }
                                while (i < j)
                                {
                                    //sb.AppendLine("        apply average of " + average + " to: " + i);
                                    records[i] = average;
                                    //sb.AppendLine("    i: " + i + ": " + records[i] + " (adj)");
                                    ++i;
                                }
                            }
                            //sb.AppendLine("    i: " + i + ": " + records[i]);
                        }
                    }
                    __instance.records = records;
                    //foreach (float f in __instance.records)
                    //{
                    //    sb.AppendLine("    " + f);
                    //}
                    //Log.Warning(sb.ToString());
                }
            }
        }

        [HarmonyPatch(typeof(WealthWatcher), "ForceRecount")]
        static class Patch_WealthWatcher_ForceRecount
        {
            static float lastItemWealth = -1f;

            [HarmonyPriority(Priority.Last)]
            static void Postfix(WealthWatcher __instance)
            {
                Map map = (Map)__instance.GetType().GetField("map", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
                //float wealthBuildings = __instance.GetType().GetField("wealthBuildings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance) as float;
                FieldInfo wealthItemsFI = __instance.GetType().GetField("wealthItems", BindingFlags.NonPublic | BindingFlags.Instance);
                float wealthItems = (float)wealthItemsFI.GetValue(__instance);

                wealthItems = TallyWealth(WorldComp.GetInfiniteStorages(map), wealthItems);
                //wealthItems = TallyWealth(WorldComp.GetNonGlobalInfiniteStorages(map), wealthItems);
                
                if (lastItemWealth < 1)
                {
                    lastItemWealth = wealthItems;
                }
                else if (wealthItems > lastItemWealth * 5)
                {
                    float temp = wealthItems;
                    wealthItems = lastItemWealth;
                    lastItemWealth = temp;
                }
                else
                {
                    lastItemWealth = wealthItems;
                }

                wealthItemsFI.SetValue(__instance, wealthItems);
            }

            private static float TallyWealth(IEnumerable<Building_InfiniteStorage> storages, float wealthItems)
            {
                foreach (Building_InfiniteStorage storage in storages)
                {
                    foreach (Thing t in storage.StoredThings)
                    {
                        if (t is ThingWithComps)
                        {
                            wealthItems += (float)t.stackCount * t.MarketValue;
                        }
                        else
                        {
                            wealthItems += (float)t.stackCount * t.def.BaseMarketValue;
                        }
                    }
                }
                return wealthItems;
            }
        }
    }
}