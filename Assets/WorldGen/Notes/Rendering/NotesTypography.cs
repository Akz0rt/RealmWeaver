using TMPro;
using UnityEngine;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// The page's editorial typography, in one place. Every number here comes from the approved design and
    /// none of them is a judgement call at the call site: a view that wants a different body size is wrong,
    /// not configurable.
    ///
    /// WHY THESE VALUES AND NOT THE PANEL ONES. The rest of this application is panels over a map at 11–13px
    /// with uppercase labels, and applying that density to prose is exactly what made the first notes editor
    /// unreadable. Long-form text needs a larger size, a looser line, and above all a CAPPED COLUMN: prose
    /// stretched across a wide pane is prose nobody can follow from the end of one line to the start of the
    /// next.
    ///
    /// SCOPE, and it is a constraint rather than an accident: only page BODY text is TextMeshPro. Panels,
    /// buttons, the toolbar, the legend and every piece of map chrome stay on legacy uGUI and the built-in
    /// font. If panels start moving to TMP this arc doubles in size.
    ///
    /// THE BUILD TRAP. These assets are reached by Resources.Load and by nothing else, because the notes
    /// shell is built at runtime and no scene object references them. An asset that no scene references and
    /// no Resources folder holds is stripped from a player build — it works in the Editor and vanishes when
    /// shipped, which this project has already paid for twice (Shader.Find, then instanced-shader variants).
    /// Living under Assets/Resources/Fonts is what prevents it. Moving these files means moving them into
    /// another Resources folder, never out of one. Generate them with RealmWeaver ▸ Fonts ▸ Rebuild
    /// Literata SDF (see LiterataFontAssetBuilder).
    /// </summary>
    public static class NotesTypography
    {
        public const float BodySize = 16f;

        /// <summary>~1.66. TMP expresses line spacing as a PERCENTAGE OF THE FONT SIZE ADDED to the default
        /// single line, not as a multiplier, so the number a view assigns to lineSpacing is not this one —
        /// use LineSpacingPercent below and let this stay the value the design states.</summary>
        public const float LineHeightMultiplier = 1.66f;

        /// <summary>What TMP_Text.lineSpacing actually wants: the EXTRA leading, as a percentage of the font
        /// size, on top of the font's own single line. Converting here rather than at each call site means
        /// the design number and the engine number can never drift apart in someone's head.</summary>
        public const float LineSpacingPercent = (LineHeightMultiplier - 1f) * 100f;

        /// <summary>The measure: how wide a column of prose is allowed to get, in ems. ~34em is about 70
        /// characters, the width long-form text has been set at since before screens.</summary>
        public const float MeasureEm = 34f;

        /// <summary>The measure in pixels. A pane wider than this does NOT widen the prose — the column is
        /// centred and the extra space stays margin.</summary>
        public static float MeasureWidthPx => BodySize * MeasureEm;

        /// <summary>Section headings stay on the PANEL type — Arial, small, uppercase, letter-spaced, in the
        /// accent colour. They are labels, not prose, and reading them as prose was part of what made the
        /// rejected editor feel like a form.</summary>
        public const float HeadingSize = 11f;
        public const float HeadingLetterSpacing = 0.12f;

        const string ResourcePrefix = "Fonts/Literata-";
        const string ResourceSuffix = " SDF";

        static TMP_FontAsset body;
        static TMP_FontAsset bold;
        static TMP_FontAsset italic;

        public static TMP_FontAsset Body => body != null ? body : (body = Load("Regular"));
        public static TMP_FontAsset Bold => bold != null ? bold : (bold = Load("Bold"));
        public static TMP_FontAsset Italic => italic != null ? italic : (italic = Load("Italic"));

        /// <summary>Loads one weight, complaining LOUDLY and exactly once if it is absent. A null font asset
        /// makes TMP fall back to LiberationSans, which renders Cyrillic perfectly well — so the failure
        /// would otherwise look like "the font choice did not take" rather than "the asset is not in the
        /// build", and those have completely different fixes.</summary>
        static TMP_FontAsset Load(string weight)
        {
            string path = ResourcePrefix + weight + ResourceSuffix;
            var loaded = Resources.Load<TMP_FontAsset>(path);
            if (loaded == null)
                Debug.LogError($"NotesTypography: Resources/{path} not found. Run RealmWeaver ▸ Fonts ▸ " +
                               "Rebuild Literata SDF. If this appears in a BUILD but not in the Editor, the " +
                               "asset left its Resources folder and was stripped.");
            return loaded;
        }

        /// <summary>Applies the body style to one text component, so no view writes these five lines itself
        /// and no view gets four of them right.</summary>
        public static void ApplyBody(TMP_Text text)
        {
            if (text == null) return;
            if (Body != null) text.font = Body;
            text.fontSize = BodySize;
            text.lineSpacing = LineSpacingPercent;
            text.richText = true;
        }
    }
}
