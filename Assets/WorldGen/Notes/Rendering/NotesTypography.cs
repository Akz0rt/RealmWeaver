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

        /// <summary>The measure: how wide a column of prose is allowed to get, in ems.
        ///
        /// Started at 34em (~70 characters, the classical measure) and widened to 40 (~80) at the DM's first
        /// checkpoint: the classical number assumes a printed page, and a pane inside a workspace already
        /// spends width on the navigator and the other pane, so the same rule left the column looking pinched
        /// rather than composed. 40em is still well inside the range the eye can return from — the failure
        /// this cap exists to prevent is the FULL-WIDTH line, not the merely generous one.</summary>
        public const float MeasureEm = 40f;

        /// <summary>The measure in pixels. A pane wider than this does NOT widen the prose — the column is
        /// centred and the extra space stays margin.</summary>
        public static float MeasureWidthPx => BodySize * MeasureEm;

        /// <summary>Section headings stay on the PANEL type — Arial, small, uppercase, letter-spaced, in the
        /// accent colour. They are labels, not prose, and reading them as prose was part of what made the
        /// rejected editor feel like a form.</summary>
        /// <summary>Raised from 11 to 15 at the DM's second checkpoint. 11px is the size of a PANEL label,
        /// where the label sits above a control that explains it; a section heading sits above prose at 16px
        /// and has to hold its own against it. Still below the body size on purpose — the heading earns its
        /// weight from being bold, uppercase, letter-spaced and in the accent colour, and a heading LARGER
        /// than its own prose would turn a page of sections into a page of banners.</summary>
        public const float HeadingSize = 15f;
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

        /// <summary>The face section headings are set in. NOT Literata: a heading here is a LABEL in the
        /// panel's own type — small, uppercase, letter-spaced — and setting it in the prose face would make it
        /// read as prose. TMP's default asset is LiberationSans, the metric-compatible Arial the rest of this
        /// application's chrome already uses, so this keeps headings on the same face as every panel around
        /// them without introducing a fourth font.</summary>
        public static TMP_FontAsset Heading => TMP_Settings.defaultFontAsset;

        /// <summary>Applies the body style to one text component, so no view writes these lines itself and no
        /// view gets three of the four right.</summary>
        public static void ApplyBody(TMP_Text text)
        {
            if (text == null) return;
            if (Body != null) text.font = Body;
            text.fontSize = BodySize;
            text.fontStyle = FontStyles.Normal;
            text.lineSpacing = LineSpacingPercent;
        }

        /// <summary>Applies the section-heading style. Everything about it differs from the body — face,
        /// size, weight, case, tracking — which is exactly why it lives here beside ApplyBody rather than as
        /// four overrides at a call site.</summary>
        public static void ApplySectionHeading(TMP_Text text)
        {
            if (text == null) return;
            if (Heading != null) text.font = Heading;
            text.fontSize = HeadingSize;
            text.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            // TMP measures character spacing in font units, roughly hundredths of an em, so the design's
            // .12em becomes 12. Converted here so the design value and the engine value stay in one place.
            text.characterSpacing = HeadingLetterSpacing * 100f;
            text.lineSpacing = 0f;
        }
    }
}
