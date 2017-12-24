using RimWorld;
using UnityEngine;
using Verse;
using System;
using System.Text;

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
            applyFiltersTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/filter", true);
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
        public static Texture2D applyFiltersTexture;

        private Vector2 scrollPosition = new Vector2(0, 0);
        private String searchText = "";

        const int HEIGHT = 30;
        const int BUFFER = 2;

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
#if DEBUG
                StringBuilder sb = new StringBuilder("search for: " + searchText);
#endif
                this.searchText = Widgets.TextEntryLabeled(new Rect(20, 20, 300, 32), "InfiniteStorage.Search".Translate() + ": ", this.searchText);

                int rows = this.ThingStorage.DefsCount;
                Rect r = new Rect(0, 52, 368, (rows + 1) * (HEIGHT + BUFFER));
                scrollPosition = GUI.BeginScrollView(new Rect(50, 52, r.width + 18, inRect.height - 100), scrollPosition, r);
                int i = 0;
                foreach (Thing thing in this.ThingStorage.StoredThings)
                {
                    if (thing != null)
                    {
                        string label = this.FormatLabel(thing);
                        if (searchText.Length == 0 || label.Contains(searchText))
                        {
                            GUI.BeginGroup(new Rect(0, 22 + i * (HEIGHT + BUFFER), r.width, HEIGHT));

                            Widgets.ThingIcon(new Rect(0f, 0f, HEIGHT, HEIGHT), thing);

                            Widgets.Label(new Rect(40, 0, r.width - (80 + HEIGHT), HEIGHT), label);

                            if (this.ThingStorage.IsOperational &&
                                Widgets.ButtonImage(new Rect(r.xMax - 20, 0, 20, 20), DropTexture))
                            {
                                this.ThingStorage.AllowAdds = false;
                                if (this.ThingStorage.TryRemove(thing))
                                {
                                    BuildingUtil.DropThing(thing, thing.stackCount, this.ThingStorage, this.ThingStorage.Map, false);
                                }
                                break;
                            }
                            GUI.EndGroup();
                            ++i;
                        }
#if DEBUG
                        else
                        {
                            sb.Append("[" + thing.def.label + "], ");
                        }
#endif
                    }
                }

#if DEBUG
                Log.Warning(sb.ToString());
#endif
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

        private string FormatLabel(Thing t)
        {
            if (t is MinifiedThing)
            {
                return t.Label;
            }
            StringBuilder sb = new StringBuilder(t.def.label);
            if (t.Stuff != null)
            {
                sb.Append(" (");
                sb.Append(t.Stuff.LabelAsStuff);
                sb.Append(")");
            }
            if (t.stackCount > 0)
            {
                sb.Append(" x");
                sb.Append(t.stackCount);
            }
            return sb.ToString();
        }
    }
}
