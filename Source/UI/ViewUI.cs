using RimWorld;
using UnityEngine;
using Verse;
using System;
using System.Collections.Generic;

namespace InfiniteStorage.UI
{
    [StaticConstructorOnStartup]
    public class ViewUI : Window
    {
        static ViewUI()
        {
            DropTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/drop", true);
            BodyPartViewTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/viewbodyparts", true);
            TextileViewTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/viewtextile", true);
            InfiniteStorageViewTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/viewif", true);
            emptyTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/empty", true);
            collectTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/collect", true);
            yesSellTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/yessell", true);
            noSellTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/nosell", true);
        }

        private readonly Building_InfiniteStorage ThingStorage;

        public static Texture2D BodyPartViewTexture;
        public static Texture2D TextileViewTexture;
        public static Texture2D InfiniteStorageViewTexture;
        public static Texture2D DropTexture;
        public static Texture2D emptyTexture;
        public static Texture2D collectTexture;
        public static Texture2D yesSellTexture;
        public static Texture2D noSellTexture;

        private Vector2 scrollPosition = new Vector2(0, 0);

        public ViewUI(Building_InfiniteStorage thingStorage)
        {
            this.ThingStorage = thingStorage;

            this.closeOnEscapeKey = true;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.forcePause = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(500f, 600f);
            }
        }

#if DEBUG
        int i = 600;
#endif
        public override void DoWindowContents(Rect inRect)
        {
#if DEBUG
            ++i;
#endif
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            try
            {
                const int HEIGHT = 30;
                const int BUFFER = 2;
                int rows = this.ThingStorage.DefsCount;
                Rect r = new Rect(0, 20, 384, (rows + 1) * (HEIGHT + BUFFER));
                scrollPosition = GUI.BeginScrollView(new Rect(50, 0, 400, 400), scrollPosition, r);

                int i = 0;
                foreach (Thing thing in this.ThingStorage.StoredThings)
                {
                    if (thing != null)
                    {
                        GUI.BeginGroup(new Rect(0, 22 + i * (HEIGHT + BUFFER), r.width, HEIGHT));

                        Widgets.ThingIcon(new Rect(0f, 0f, HEIGHT, HEIGHT), thing);

                        Widgets.Label(new Rect(40, 0, 200, HEIGHT), thing.Label);

                        if (this.ThingStorage.IsOperational &&
                            Widgets.ButtonImage(new Rect(r.xMax - 20, 0, 20, 20), DropTexture))
                        {
                            this.ThingStorage.Remove(thing, thing.stackCount);
                            BuildingUtil.DropThing(thing, thing.stackCount, this.ThingStorage, this.ThingStorage.Map, false);
                            break;
                        }
                        GUI.EndGroup();
                        ++i;
                    }
                }

                GUI.EndScrollView();
            }
            catch (Exception e)
            {
                String msg = this.GetType().Name + " closed due to: " + e.GetType().Name + " " + e.Message;
                Log.Error(msg);
                Messages.Message(msg, MessageTypeDefOf.NegativeEvent);
                base.Close();
            }
            finally
            {
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }
    }
}
