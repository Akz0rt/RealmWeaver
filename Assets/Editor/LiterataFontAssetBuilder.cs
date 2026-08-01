using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace WorldGen.EditorTools
{
    /// <summary>
    /// Builds the three Literata SDF font assets the notes page renders prose with.
    ///
    /// WHY A COMMAND AND NOT THE FONT ASSET CREATOR WINDOW. The setting that decides whether this project's
    /// Russian text renders at all is the character set, and in the wizard it lives in a dialog nobody can
    /// review, re-run identically, or diff. Here it is CharacterSet() below, in code, next to the reason for
    /// every range. Re-running this command reproduces the same atlases from the same sources.
    ///
    /// The generated assets go under Assets/Resources/Fonts/ ON PURPOSE. The notes shell is built at RUNTIME
    /// (the NotesRootBuilder pattern), so nothing in a scene references these assets, and an asset that no
    /// scene references and no Resources folder holds is stripped from a player build — it works in the
    /// Editor and vanishes when shipped. This project has already paid that bill twice, with Shader.Find and
    /// with instanced-shader variants. Anyone moving these files must move them into another Resources
    /// folder, not out of one.
    /// </summary>
    public static class LiterataFontAssetBuilder
    {
        const string SourceDir = "Assets/Fonts";
        const string OutputDir = "Assets/Resources/Fonts";

        /// <summary>48 rather than the body's 16: sampling point size governs how much detail the signed
        /// distance field carries, and a field sampled at the display size looks soft at the display size.
        /// 48 is comfortably above every size the page uses.</summary>
        const int SamplingPointSize = 48;
        const int AtlasPadding = 5;
        const int AtlasWidth = 1024;
        const int AtlasHeight = 1024;

        static readonly string[] Weights = { "Regular", "Bold", "Italic" };

        [MenuItem("RealmWeaver/Fonts/Rebuild Literata SDF")]
        public static void Rebuild()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

            string characters = CharacterSet();
            bool allOk = true;

            foreach (var weight in Weights)
            {
                string sourcePath = $"{SourceDir}/Literata-{weight}.ttf";
                var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
                if (source == null)
                {
                    Debug.LogError($"Literata: source font not found at {sourcePath}. Place the three static " +
                                   "TTFs from Google Fonts (SIL OFL) in Assets/Fonts and run this again.");
                    allOk = false;
                    continue;
                }

                allOk &= BuildOne(source, weight, characters);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(allOk
                ? $"Literata: all {Weights.Length} font assets built with 0 missing characters."
                : "Literata: FINISHED WITH PROBLEMS — read the errors above. Do not treat the page's typography " +
                  "as verified until this line reports 0 missing characters.");
        }

        static bool BuildOne(Font source, string weight, string characters)
        {
            string assetName = $"Literata-{weight} SDF";
            string assetPath = $"{OutputDir}/{assetName}.asset";

            // Created DYNAMIC so TryAddCharacters can rasterise exactly the set below, then frozen to STATIC
            // so a build cannot silently rasterise anything else at runtime. A dynamic asset that quietly
            // grows is the opposite of a reviewable character set.
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                source, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasWidth, AtlasHeight, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

            if (fontAsset == null)
            {
                Debug.LogError($"Literata: CreateFontAsset returned null for {weight}.");
                return false;
            }

            fontAsset.name = assetName;

            bool complete = fontAsset.TryAddCharacters(characters, out string missing);
            if (!complete || !string.IsNullOrEmpty(missing))
            {
                // The whole reason this command prints anything. A font subset without Cyrillic builds
                // perfectly and renders Russian as blank boxes; a silent success here is the failure mode.
                Debug.LogError($"Literata-{weight}: {missing.Length} character(s) MISSING from the source font " +
                               $"— «{missing}». Cyrillic missing means the wrong TTF subset was downloaded.");
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // The atlas textures and the material are sub-assets of the font asset. Left loose they would be
            // separate files that a move or a reimport can part from their owner.
            foreach (var tex in fontAsset.atlasTextures)
            {
                if (tex == null) continue;
                tex.name = assetName + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
            if (fontAsset.material != null)
            {
                fontAsset.material.name = assetName + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            Debug.Log($"Literata-{weight}: built {assetPath} — {fontAsset.characterTable.Count} glyphs, " +
                      $"{fontAsset.atlasTextures.Length} atlas page(s).");
            return complete && string.IsNullOrEmpty(missing);
        }

        /// <summary>Exactly what the page must be able to render, and nothing speculative.
        ///
        /// NOT INCLUDED, deliberately: ⊕ and 📄. Those are row affordances drawn as buttons in legacy uGUI
        /// with the built-in font, not glyphs inside prose — the arc's constraint is that only page BODY text
        /// moves to TextMeshPro. 📄 is outside the Basic Multilingual Plane besides, and no text serif face
        /// carries it.</summary>
        static string CharacterSet()
        {
            var seen = new HashSet<char>();
            var sb = new StringBuilder();

            AppendRange(sb, seen, 0x0020, 0x007E);   // Basic Latin — ASCII, including digits and punctuation
            AppendRange(sb, seen, 0x00A0, 0x00FF);   // Latin-1 Supplement — accented Latin, ©, °, ×
            AppendRange(sb, seen, 0x0400, 0x04FF);   // Cyrillic — the language this application is written in

            // The typographic punctuation this project's prose actually uses. Russian typesetting needs the
            // guillemets and the em dash the way English needs the apostrophe; without them every «слово»
            // in the UI renders as a blank box.
            foreach (char c in "«»“”„‘’—–…·№₽†‡•→←↑↓±≈≤≥")
                if (seen.Add(c)) sb.Append(c);

            return sb.ToString();
        }

        static void AppendRange(StringBuilder sb, HashSet<char> seen, int first, int last)
        {
            for (int c = first; c <= last; c++)
                if (seen.Add((char)c)) sb.Append((char)c);
        }
    }
}
