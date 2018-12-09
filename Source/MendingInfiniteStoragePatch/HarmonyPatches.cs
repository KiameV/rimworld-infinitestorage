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
	
	
}