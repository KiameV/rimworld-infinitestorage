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
        private const int DEFAULT_ENERGY_BUFFER = 100;
        private const float DEFAULT_ENERGY_FACTOR = 1f;
        private const long DEFAULT_TIME_BETWEEN_COLLECTS_TICKS = 10 * TimeSpan.TicksPerSecond;

        private static int desiredEnergyBuffer = DEFAULT_ENERGY_BUFFER;
        public static int DesiredEnergyBuffer { get { return desiredEnergyBuffer; } }

        private static float energyFactor = DEFAULT_ENERGY_FACTOR;
        public static float EnergyFactor { get { return energyFactor; } }

        private static bool collectThingsAutomatically = true;
        public static bool CollectThingsAutomatically { get { return collectThingsAutomatically; } }

        private static long timeBetweenAutoCollects = DEFAULT_TIME_BETWEEN_COLLECTS_TICKS;
        public static long TimeBetweenAutoCollectsTicks { get { return timeBetweenAutoCollects; } }
        private static long TimeBetweenAutoCollectsSeconds { get { return timeBetweenAutoCollects / TimeSpan.TicksPerSecond; } }

        private static string desiredEnergyBufferUserInput = desiredEnergyBuffer.ToString();
        private static string energyFactorUserInput = energyFactor.ToString();
        private static string timeBetweenAutoCollectsUserInput = TimeBetweenAutoCollectsSeconds.ToString();

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look<int>(ref desiredEnergyBuffer, "InfiniteStorage.DesiredEnergyBuffer", DEFAULT_ENERGY_BUFFER, false);
            Scribe_Values.Look<float>(ref energyFactor, "InfiniteStorage.EnergyFactor", DEFAULT_ENERGY_FACTOR, false);
            Scribe_Values.Look<bool>(ref collectThingsAutomatically, "InfiniteStorage.CollectThingsAutomatically", true, false);
            Scribe_Values.Look<long>(ref timeBetweenAutoCollects, "InfiniteStorage.TimeBetweenAutoCollects", DEFAULT_TIME_BETWEEN_COLLECTS_TICKS, false);
        }

        public static void DoSettingsWindowContents(Rect rect)
        {
            int y = 60;
            desiredEnergyBufferUserInput = Widgets.TextEntryLabeled(new Rect(0, y, 300, 32), "InfiniteStorage.EnergyBuffer".Translate() + ":   ", desiredEnergyBufferUserInput);

            y += 50;
            if (Widgets.ButtonText(new Rect(50, y, 100, 32), "Confirm".Translate()))
            {
                int f;
                if (!int.TryParse(desiredEnergyBufferUserInput, out f) || f < 0)
                {
                    Messages.Message("InfiniteStorage.NumberGreaterThanZero".Translate(), MessageTypeDefOf.RejectInput);
                }
                else
                {
                    desiredEnergyBuffer = f;
                    Messages.Message("InfiniteStorage.EnergyBufferSet".Translate().Replace("{v}", desiredEnergyBuffer.ToString()), MessageTypeDefOf.PositiveEvent);
                }
            }
            if (Widgets.ButtonText(new Rect(175, y, 100, 32), "default".Translate().CapitalizeFirst()))
            {
                desiredEnergyBuffer = DEFAULT_ENERGY_BUFFER;
                desiredEnergyBufferUserInput = desiredEnergyBuffer.ToString();
                Messages.Message("InfiniteStorage.EnergyBufferSet".Translate().Replace("{v}", desiredEnergyBuffer.ToString()), MessageTypeDefOf.PositiveEvent);
            }

            y += 50;
            Widgets.Label(new Rect(25, y, rect.width - 50, 32), "InfiniteStorage.EnergyBufferDesc".Translate());

            y += 29;
            Widgets.DrawLineHorizontal(0, y, rect.width);

            y += 29;
            energyFactorUserInput = Widgets.TextEntryLabeled(new Rect(0, y, 300, 32), "InfiniteStorage.EnergyFactor".Translate() + ":   ", energyFactorUserInput);

            y += 50;
            if (Widgets.ButtonText(new Rect(50, y, 100, 32), "Confirm".Translate()))
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
            if (Widgets.ButtonText(new Rect(175, y, 100, 32), "default".Translate().CapitalizeFirst()))
            {
                energyFactor = DEFAULT_ENERGY_FACTOR;
                energyFactorUserInput = energyFactor.ToString();
                Messages.Message("InfiniteStorage.EnergyFactorSet".Translate().Replace("{v}", energyFactor.ToString()), MessageTypeDefOf.PositiveEvent);
            }

            y += 50;
            Widgets.Label(new Rect(25, y, rect.width - 50, 32), "InfiniteStorage.EnergyFactorDesc".Translate());


            y += 29;
            Widgets.DrawLineHorizontal(0, y, rect.width);

            y += 29;
            Widgets.CheckboxLabeled(new Rect(25, y, 200, 32), "InfiniteStorage.CollectThingsAutomatically".Translate(), ref collectThingsAutomatically);

            y += 40;
            if (collectThingsAutomatically)
            {
                Widgets.Label(new Rect(25, y, 300, 32), "InfiniteStorage.TimeBetweenAutoCollects".Translate() + ":");
                timeBetweenAutoCollectsUserInput = Widgets.TextField(new Rect(310, y - 6, 75, 32), timeBetweenAutoCollectsUserInput);

                y += 40;
                if (Widgets.ButtonText(new Rect(50, y, 100, 32), "Confirm".Translate()))
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
                if (Widgets.ButtonText(new Rect(175, y, 100, 32), "default".Translate().CapitalizeFirst()))
                {
                    timeBetweenAutoCollects = DEFAULT_TIME_BETWEEN_COLLECTS_TICKS;
                    timeBetweenAutoCollectsUserInput = TimeBetweenAutoCollectsSeconds.ToString();
                    Messages.Message("InfiniteStorage.TimeBetweenAutoCollectsSet".Translate().Replace("{v}", TimeBetweenAutoCollectsSeconds.ToString()), MessageTypeDefOf.PositiveEvent);
                }
            }
        }
    }
}
