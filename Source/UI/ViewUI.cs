using RimWorld;
using UnityEngine;
using Verse;
using System;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
            TroughViewTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/viewtrough", true);
            emptyTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/empty", true);
            collectTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/collect", true);
            yesSellTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/yessell", true);
            noSellTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/nosell", true);
            applyFiltersTexture = ContentFinder<Texture2D>.Get("InfiniteStorage/filter", true);
        }

        private class MatchedThings : Thing
        {
            public List<Thing> Things = new List<Thing>();
            public int Count = 0;
            public MatchedThings() { }
            public MatchedThings(Thing t) { this.Add(t); }
            public Thing First => Things[0];
            public void Add(Thing t)
            {
                this.Things.Add(t);
                ++this.Count;
            }
        }

        public static Texture2D BodyPartViewTexture;
        public static Texture2D TextileViewTexture;
        public static Texture2D InfiniteStorageViewTexture;
        public static Texture2D TroughViewTexture;
        public static Texture2D DropTexture;
        public static Texture2D emptyTexture;
        public static Texture2D collectTexture;
        public static Texture2D yesSellTexture;
        public static Texture2D noSellTexture;
        public static Texture2D applyFiltersTexture;

        private enum Tabs
        {
            Unknown,
            InfiniteStorage_Misc,
            InfiniteStorage_Minified,
            InfiniteStorage_Apparel,
            InfiniteStorage_Weapons,
            InfiniteStorage_Chunks
        };

        private readonly Building_InfiniteStorage InfiniteStorage;
        private Dictionary<string, Thing> Misc = new Dictionary<string, Thing>();
        private Dictionary<string, Thing> Chunks = new Dictionary<string, Thing>();
        private List<Thing> Minified = new List<Thing>();
        private List<Thing> Apparel = new List<Thing>();
        private List<Thing> Weapons = new List<Thing>();

        private List<TabRecord> tabs = new List<TabRecord>();
        private Tabs selectedTab = Tabs.InfiniteStorage_Misc;

        private Vector2 scrollPosition = new Vector2(0, 0);
        private String searchText = "";
        private bool itemsDropped = false;

        const int HEIGHT = 30;
        const int BUFFER = 2;

        private static ThingCategoryDef WeaponsMeleeCategoryDef = null;
        private static ThingCategoryDef WeaponsRangedCategoryDef = null;

        public ViewUI(Building_InfiniteStorage thingStorage)
        {
            this.InfiniteStorage = thingStorage;

            this.closeOnClickedOutside = true;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.forcePause = true;

            this.PopulateDisplayThings();
        }

        public override Vector2 InitialSize
        {
            get
            {
                return new Vector2(550f, 650f);
            }
        }

        private void PopulateDisplayThings()
        {
            if (WeaponsMeleeCategoryDef == null)
            {
                foreach (ThingCategoryDef d in DefDatabase<ThingCategoryDef>.AllDefsListForReading)
                {
                    if (WeaponsMeleeCategoryDef != null && WeaponsRangedCategoryDef != null)
                        break;
                    else if (d.defName.EqualsIgnoreCase("WeaponsMelee"))
                        WeaponsMeleeCategoryDef = d;
                    else if (d.defName.EqualsIgnoreCase("WeaponsRanged"))
                        WeaponsRangedCategoryDef = d;
                }
            }

            this.Misc.Clear();
            this.Minified.Clear();
            this.Apparel.Clear();
            this.Weapons.Clear();
            this.Chunks.Clear();
            foreach (Thing t in this.InfiniteStorage.StoredThings)
            {
#if DEBUG
                StringBuilder sb = new StringBuilder("Thing: " + t.def.label + " [");
                foreach (ThingCategoryDef d in t.def.thingCategories)
                {
                    sb.Append(d.label + ", ");
                }
                sb.Append("]");
                sb.Append(" is Melee: " + t.def.thingCategories.Contains(WeaponsMeleeCategoryDef));
                sb.Append(" is Ranged: " + t.def.thingCategories.Contains(WeaponsRangedCategoryDef));
                Log.Warning(sb.ToString());
#endif
                if (t.def.IsApparel)
                {
                    this.Apparel.Add(t);
                }
                else if (t.def.thingCategories.Contains(WeaponsMeleeCategoryDef) ||
                         t.def.thingCategories.Contains(WeaponsRangedCategoryDef))
                {
                    this.Weapons.Add(t);
                }
                else if (t is MinifiedThing)
                {
                    this.Minified.Add(t);
                }
                else if (this.IsChunk(t.def))
                {
                    this.AddSpecial(t, this.Chunks);
                }
                else
                {
                    this.AddSpecial(t, this.Misc);
                }
            }

            if ((this.selectedTab == Tabs.InfiniteStorage_Misc && this.Misc.Count == 0) || 
                (this.selectedTab == Tabs.InfiniteStorage_Minified && this.Minified.Count == 0) || 
                (this.selectedTab == Tabs.InfiniteStorage_Apparel && this.Apparel.Count == 0) || 
                (this.selectedTab == Tabs.InfiniteStorage_Weapons && this.Weapons.Count == 0) ||
                (this.selectedTab == Tabs.InfiniteStorage_Chunks && this.Chunks.Count == 0))
            {
                this.selectedTab = Tabs.Unknown;
            }

            if (this.selectedTab == Tabs.Unknown)
            {
                if (this.Misc.Count > 0)
                    this.selectedTab = Tabs.InfiniteStorage_Misc;
                else if (this.Minified.Count > 0)
                    this.selectedTab = Tabs.InfiniteStorage_Minified;
                else if (this.Apparel.Count > 0)
                    this.selectedTab = Tabs.InfiniteStorage_Apparel;
                else if (this.Weapons.Count > 0)
                    this.selectedTab = Tabs.InfiniteStorage_Weapons;
                else if (this.Chunks.Count > 0)
                    this.selectedTab = Tabs.InfiniteStorage_Chunks;
            }
        }

        private void AddSpecial(Thing t, Dictionary<string, Thing> d)
        {
            if (t.def.stackLimit == 1)
            {
                if (d.TryGetValue(t.def.defName, out Thing mt) &&
                    mt is MatchedThings)
                {
                    (mt as MatchedThings).Add(t);
                }
                else
                {
                    d.Add(t.def.defName, new MatchedThings(t));
                }
            }
            else
            {
                d.Add(t.def.defName, t);
            }
        }

        private bool IsChunk(ThingDef def)
        {
            foreach(ThingCategoryDef d in def.thingCategories)
            {
                if (d == ThingCategoryDefOf.Chunks || 
                    d == ThingCategoryDefOf.StoneChunks)
                {
                    return true;
                }
            }
            return false;
        }

        public override void PreClose()
        {
            base.PreClose();
            this.Misc.Clear();
            this.Chunks.Clear();
            this.Minified.Clear();
            this.Apparel.Clear();
            this.Weapons.Clear();
            if (this.itemsDropped && this.InfiniteStorage != null)
            {
                this.InfiniteStorage.ResetAutoReclaimTime();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            try
            {
                int y = 90;
                IEnumerable<Thing> thingsToShow = this.GetThingsToShow(out int rows);

                if (rows == 0)
                {
                    return;
                }

                this.searchText = Widgets.TextEntryLabeled(new Rect(20, 15, 300, 32), "InfiniteStorage.Search".Translate() + ": ", this.searchText).ToLower().Trim();
                this.searchText = Regex.Replace(this.searchText, @"\t|\n|\r", "");

                if (this.searchText.Length == 0)
                {
                    TabDrawer.DrawTabs(new Rect(0, y, inRect.width, inRect.height - y), this.tabs);
                    y += 32;
                }

                Rect r = new Rect(0, y, 368, (rows + 1) * (HEIGHT + BUFFER));
                scrollPosition = GUI.BeginScrollView(
                    new Rect(50, y, r.width + 18, inRect.height - y - 75), scrollPosition, r);
                int i = 0;
                foreach (Thing t in thingsToShow)
                {
                    if (t != null)
                    {
                        string label = this.FormatLabel(t);
                        if (searchText.Length == 0 || label.ToLower().Contains(searchText))
                        {
                            if (this.DrawRow(t, label, y, i, r))
                            {
                                break;
                            }
                            ++i;
                        }
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

        private bool DrawRow(Thing thing, String label, float y, int i, Rect r)
        {
            Thing t = (thing is MatchedThings) ? (thing as MatchedThings).First : thing;

            GUI.BeginGroup(new Rect(0, y + i * (HEIGHT + BUFFER), r.width, HEIGHT));

            Widgets.ThingIcon(new Rect(0f, 0f, HEIGHT, HEIGHT), t);

            if (Widgets.InfoCardButton(40, 0, t))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(t));
            }

            Widgets.Label(new Rect(70, 0, r.width - (80 + HEIGHT), HEIGHT), label);

            if (this.InfiniteStorage.IsOperational &&
                Widgets.ButtonImage(new Rect(r.xMax - 20, 0, 20, 20), DropTexture))
            {
                this.InfiniteStorage.AllowAdds = false;
                if (this.InfiniteStorage.TryRemove(t))
                {
                    BuildingUtil.DropThing(t, t.stackCount, this.InfiniteStorage, this.InfiniteStorage.Map, false);
                    this.itemsDropped = true;
                }
                this.PopulateDisplayThings();
                return true;
            }
            GUI.EndGroup();
            return false;
        }

        private string FormatLabel(Thing thing)
        {
            Thing t = (thing is MatchedThings) ? (thing as MatchedThings).First : thing;
            
            if (t is MinifiedThing || 
                t.def.IsApparel ||
                t.def.thingCategories.Contains(WeaponsMeleeCategoryDef) ||
                t.def.thingCategories.Contains(WeaponsRangedCategoryDef))
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
            if (thing is MatchedThings)
            {
                sb.Append(" x");
                sb.Append((thing as MatchedThings).Count);
            }
            else if (t.stackCount > 0)
            {
                sb.Append(" x");
                sb.Append(t.stackCount);
            }
            return sb.ToString();
        }

        private IEnumerable<Thing> GetThingsToShow(out int rows)
        {
            IEnumerable<Thing> thingsToShow;
            if (this.searchText.Length > 0)
            {
                thingsToShow = this.InfiniteStorage.StoredThings;
                rows = this.InfiniteStorage.DefsCount;
            }
            else
            {
                this.tabs.Clear();
                if (this.Misc.Count > 0)
                {
                    this.tabs.Add(new TabRecord(
                        Tabs.InfiniteStorage_Misc.ToString().Translate(),
                        delegate { this.selectedTab = Tabs.InfiniteStorage_Misc; },
                        this.selectedTab == Tabs.InfiniteStorage_Misc));
                }
                if (this.Minified.Count > 0)
                {
                    this.tabs.Add(new TabRecord(
                        Tabs.InfiniteStorage_Minified.ToString().Translate(),
                        delegate { this.selectedTab = Tabs.InfiniteStorage_Minified; },
                        this.selectedTab == Tabs.InfiniteStorage_Minified));
                }
                if (this.Apparel.Count > 0)
                {
                    this.tabs.Add(new TabRecord(
                        Tabs.InfiniteStorage_Apparel.ToString().Translate(),
                        delegate { this.selectedTab = Tabs.InfiniteStorage_Apparel; },
                        this.selectedTab == Tabs.InfiniteStorage_Apparel));
                }
                if (this.Weapons.Count > 0)
                {
                    this.tabs.Add(new TabRecord(
                        Tabs.InfiniteStorage_Weapons.ToString().Translate(),
                        delegate { this.selectedTab = Tabs.InfiniteStorage_Weapons; },
                        this.selectedTab == Tabs.InfiniteStorage_Weapons));
                }
                if (this.Chunks.Count > 0)
                {
                    this.tabs.Add(new TabRecord(
                        Tabs.InfiniteStorage_Chunks.ToString().Translate(),
                        delegate { this.selectedTab = Tabs.InfiniteStorage_Chunks; },
                        this.selectedTab == Tabs.InfiniteStorage_Chunks));
                }

                if (this.selectedTab == Tabs.InfiniteStorage_Misc)
                {
                    thingsToShow = this.Misc.Values;
                    rows = this.Misc.Count;
                }
                else if (this.selectedTab == Tabs.InfiniteStorage_Minified)
                {
                    thingsToShow = this.Minified;
                    rows = this.Minified.Count;
                }
                else if (this.selectedTab == Tabs.InfiniteStorage_Apparel)
                {
                    thingsToShow = this.Apparel;
                    rows = this.Apparel.Count;
                }
                else if (this.selectedTab == Tabs.InfiniteStorage_Weapons)
                {
                    thingsToShow = this.Weapons;
                    rows = this.Weapons.Count;
                }
                else
                {
                    thingsToShow = this.Chunks.Values;
                    rows = this.Chunks.Count;
                }
            }
            return thingsToShow;
        }
    }
}
