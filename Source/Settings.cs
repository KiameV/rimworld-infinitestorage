using RimWorld;
using UnityEngine;
using Verse;

namespace InfiniteStorage
{
    public class SettingsController : Mod
    {
        public SettingsController(ModContentPack content) : base(content)
        {
            base.GetSettings<Settings>();
        }

        public override string SettingsCategory()
        {
            return "InfiniteStorage".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
        }
    }

    public class Settings : ModSettings
    {
        private const float DEFAULT_ENERGY_FACTOR = 1f;

        private static float energyFactor = DEFAULT_ENERGY_FACTOR;
        public static float EnergyFactor { get { return energyFactor; } }

        private static string energyFactorUserInput = energyFactor.ToString();

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look<float>(ref energyFactor, "InfiniteStorage.EnergyFactor", DEFAULT_ENERGY_FACTOR, false);
        }

        public static void DoSettingsWindowContents(Rect rect)
        {
            energyFactorUserInput = Widgets.TextEntryLabeled(new Rect(0, 60, 300, 32), "InfiniteStorage.EnergyFactor".Translate() + ":   ", energyFactorUserInput);
            if (Widgets.ButtonText(new Rect(50, 110, 100, 32), "Confirm".Translate()))
            {
                float f;
                if (!float.TryParse(energyFactorUserInput, out f) || f < 0)
                {
                    Messages.Message("InfiniteStorage.EnergyFactorNumberGreaterThanZero".Translate(), MessageTypeDefOf.RejectInput);
                }
                else
                {
                    Messages.Message("InfiniteStorage.EnergyFactorSet".Translate().Replace("{v}", energyFactor.ToString()), MessageTypeDefOf.PositiveEvent);
                    energyFactor = f;
                }
            }
            if (Widgets.ButtonText(new Rect(175, 110, 100, 32), "default".Translate().CapitalizeFirst()))
            {
                energyFactor = DEFAULT_ENERGY_FACTOR;
                energyFactorUserInput = energyFactor.ToString();
                Messages.Message("InfiniteStorage.EnergyFactorSet".Translate().Replace("{v}", energyFactor.ToString()), MessageTypeDefOf.PositiveEvent);
            }

            Widgets.Label(new Rect(25, 160, rect.width - 50, 32), "InfiniteStorage.EnergyFactorDesc".Translate());
        }
    }
}
