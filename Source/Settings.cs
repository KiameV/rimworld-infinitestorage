using RimWorld;
using System;
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
        private const long DEFAULT_TIME_BETWEEN_COLLECTS_TICKS = 10 * TimeSpan.TicksPerSecond;
            
        private static float energyFactor = DEFAULT_ENERGY_FACTOR;
        public static float EnergyFactor { get { return energyFactor; } }

        private static bool collectThingsAutomatically = true;
        public static bool CollectThingsAutomatically { get { return collectThingsAutomatically; } }

        private static long timeBetweenAutoCollects = DEFAULT_TIME_BETWEEN_COLLECTS_TICKS;
        public static long TimeBetweenAutoCollectsTicks { get { return timeBetweenAutoCollects; } }
        private static long TimeBetweenAutoCollectsSeconds { get { return timeBetweenAutoCollects / TimeSpan.TicksPerSecond; } }

        private static string energyFactorUserInput = energyFactor.ToString();
        private static string timeBetweenAutoCollectsUserInput = TimeBetweenAutoCollectsSeconds.ToString();

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look<float>(ref energyFactor, "InfiniteStorage.EnergyFactor", DEFAULT_ENERGY_FACTOR, false);
            Scribe_Values.Look<bool>(ref collectThingsAutomatically, "InfiniteStorage.CollectThingsAutomatically", true, false);
            Scribe_Values.Look<long>(ref timeBetweenAutoCollects, "InfiniteStorage.TimeBetweenAutoCollects", DEFAULT_TIME_BETWEEN_COLLECTS_TICKS, false);
        }

        public static void DoSettingsWindowContents(Rect rect)
        {
            energyFactorUserInput = Widgets.TextEntryLabeled(new Rect(0, 60, 300, 32), "InfiniteStorage.EnergyFactor".Translate() + ":   ", energyFactorUserInput);
            if (Widgets.ButtonText(new Rect(50, 110, 100, 32), "Confirm".Translate()))
            {
                float f;
                if (!float.TryParse(energyFactorUserInput, out f) || f < 0)
                {
                    Messages.Message("InfiniteStorage.NumberGreaterThanZero".Translate(), MessageTypeDefOf.RejectInput);
                }
                else
                {
                    energyFactor = f;
                    Messages.Message("InfiniteStorage.EnergyFactorSet".Translate().Replace("{v}", energyFactor.ToString()), MessageTypeDefOf.PositiveEvent);
                }
            }
            if (Widgets.ButtonText(new Rect(175, 110, 100, 32), "default".Translate().CapitalizeFirst()))
            {
                energyFactor = DEFAULT_ENERGY_FACTOR;
                energyFactorUserInput = energyFactor.ToString();
                Messages.Message("InfiniteStorage.EnergyFactorSet".Translate().Replace("{v}", energyFactor.ToString()), MessageTypeDefOf.PositiveEvent);
            }

            Widgets.Label(new Rect(25, 160, rect.width - 50, 32), "InfiniteStorage.EnergyFactorDesc".Translate());

            Widgets.CheckboxLabeled(new Rect(25, 220, 200, 32), "InfiniteStorage.CollectThingsAutomatically".Translate(), ref collectThingsAutomatically);

            if (collectThingsAutomatically)
            {
                Widgets.Label(new Rect(25, 260, 300, 32), "InfiniteStorage.TimeBetweenAutoCollects".Translate() + ":");
                timeBetweenAutoCollectsUserInput = Widgets.TextField(new Rect(320, 255, 75, 32), timeBetweenAutoCollectsUserInput);
                if (Widgets.ButtonText(new Rect(50, 300, 100, 32), "Confirm".Translate()))
                {
                    long l;
                    if (!long.TryParse(timeBetweenAutoCollectsUserInput, out l) || l < 0)
                    {
                        Messages.Message("InfiniteStorage.NumberGreaterThanZero".Translate(), MessageTypeDefOf.RejectInput);
                    }
                    else
                    {
                        timeBetweenAutoCollects = l * TimeSpan.TicksPerSecond;
                        Messages.Message("InfiniteStorage.TimeBetweenAutoCollectsSet".Translate().Replace("{v}", TimeBetweenAutoCollectsSeconds.ToString()), MessageTypeDefOf.PositiveEvent);
                    }
                }
                if (Widgets.ButtonText(new Rect(175, 300, 100, 32), "default".Translate().CapitalizeFirst()))
                {
                    timeBetweenAutoCollects = DEFAULT_TIME_BETWEEN_COLLECTS_TICKS;
                    timeBetweenAutoCollectsUserInput = TimeBetweenAutoCollectsSeconds.ToString();
                    Messages.Message("InfiniteStorage.TimeBetweenAutoCollectsSet".Translate().Replace("{v}", TimeBetweenAutoCollectsSeconds.ToString()), MessageTypeDefOf.PositiveEvent);
                }
            }
        }
    }
}
