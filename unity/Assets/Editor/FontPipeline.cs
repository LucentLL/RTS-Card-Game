using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>
    /// Builds the game's font assets from the OFL source files in `Assets/Game/Fonts/Source`.
    ///
    /// This closes the open P0 the GAPS list called "tofu risk". The reference build renders 76
    /// non-ASCII characters - ♥ ◆ ⚔ ⚒ and the eight element kanji among them - and a default Unity
    /// font has almost none of them. Rather than hoping, the pipeline reads the generated glyph
    /// list (docs/unity/spec/glyphs.txt) and REPORTS every character no font in the chain can draw;
    /// FontCoverageTests then turns that report into a red build.
    ///
    /// Two kinds of asset, for a reason:
    ///
    ///   * The display faces (Cinzel, EB Garamond) are DYNAMIC - they must render arbitrary card
    ///     names and rules text, so their small TTFs ship with the build and rasterise on demand.
    ///   * The fallbacks (symbols, emoji, kanji) are STATIC, baked to a fixed atlas from the glyph
    ///     list and stripped of their source font. Noto Sans JP alone is 5.3 MB; shipping it to
    ///     draw eleven characters would be absurd, and a WebGL download pays for every byte.
    ///
    /// Fallback ORDER is the chain the reference build implies: symbols before emoji (⛭ and ✦ are
    /// symbols, not emoji, and Noto Emoji would draw them as colour-era pictographs), emoji before
    /// kanji.
    /// </summary>
    public static class FontPipeline
    {
        const string SourceDir = "Assets/Game/Fonts/Source";
        const string OutDir = "Assets/Game/Fonts";

        const int PointSize = 90;      // TMP's own default sampling size - crisp to ~40 px on screen
        const int Padding = 9;
        const int AtlasSize = 1024;
        const int FallbackAtlasSize = 512;

        public struct FontSpec
        {
            public string Ttf;          // file name inside SourceDir
            public string Asset;        // output asset name
            public bool Dynamic;
            public string Characters;   // static assets only: exactly what to bake
        }

        /// <summary>The glyph list, minus ASCII, as the export script wrote it.</summary>
        public static string RequiredGlyphs()
        {
            var path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../docs/unity/spec/glyphs.txt"));
            if (!File.Exists(path))
                throw new FileNotFoundException("run tools/export_glyphs.mjs first", path);

            var sb = new StringBuilder();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#") || line.Length == 0) continue;
                sb.Append(line.Trim());
            }
            return sb.ToString();
        }

        /// <summary>
        /// The fallback faces, in the order a glyph is offered to them. Order is meaning: ⚔ and ⚒
        /// exist in both a symbol face and an emoji face, and the symbol drawing is the one that
        /// belongs next to a card's attack number.
        /// </summary>
        static readonly string[] FallbackSources =
        {
            "NotoSansSymbols2-Regular.ttf",
            "NotoSansSymbols-Regular.ttf",
            "NotoSansMath-Regular.ttf",
            "NotoEmoji-Regular.ttf",
            "NotoSansJP-Regular.ttf",
        };

        static readonly string[] FallbackNames =
        {
            "SRD-Symbols2", "SRD-Symbols", "SRD-Math", "SRD-Emoji", "SRD-CJK",
        };

        /// <summary>
        /// Route each required glyph to the FIRST fallback that actually has it, measured rather
        /// than guessed. The first pass of this pipeline partitioned by Unicode block and lost ⚔,
        /// ⚒, ⚙ and every arrow, because block membership says nothing about which Noto family
        /// drew them - Miscellaneous Symbols is split across three faces by design.
        /// </summary>
        static string[] RouteGlyphs(string glyphs, FontAsset[] probes, FontAsset[] faces, List<string> uncovered)
        {
            var sets = new StringBuilder[probes.Length];
            for (int i = 0; i < sets.Length; i++) sets[i] = new StringBuilder();

            for (int i = 0; i < glyphs.Length; i++)
            {
                int cp = char.ConvertToUtf32(glyphs, i);
                if (char.IsHighSurrogate(glyphs[i])) i++;
                var s = char.ConvertFromUtf32(cp);

                // Skipped only if EVERY face draws it: Cinzel has ², EB Garamond Italic does not,
                // and routing off one face alone left that hole for the coverage test to find.
                bool allFaces = true;
                for (int f = 0; f < faces.Length && allFaces; f++) allFaces = HasGlyph(faces[f], (uint)cp);
                if (allFaces) continue;

                int home = -1;
                for (int p = 0; p < probes.Length && home < 0; p++)
                    if (HasGlyph(probes[p], (uint)cp)) home = p;

                if (home < 0) uncovered.Add(s + " U+" + cp.ToString("X4"));
                else sets[home].Append(s);
            }

            var result = new string[probes.Length];
            for (int i = 0; i < sets.Length; i++) result[i] = sets[i].ToString();
            return result;
        }

        [MenuItem("Spawn Row Duel/Rebuild Font Assets")]
        public static void Rebuild()
        {
            var report = Run();
            Debug.Log(report);
        }

        public static string Run()
        {
            var glyphs = RequiredGlyphs();
            Directory.CreateDirectory(OutDir);

            // The faces come first because a glyph they can draw themselves needs no fallback at
            // all - Cinzel has ’, ×, § and the rest of the Latin-1 punctuation.
            var faces = new[]
            {
                Build("Cinzel-Regular.ttf", "SRD-Display-Regular", true, null),
                Build("Cinzel-Bold.ttf", "SRD-Display-Bold", true, null),
                Build("Cinzel-Black.ttf", "SRD-Display-Black", true, null),
                Build("EBGaramond-Regular.ttf", "SRD-Body-Regular", true, null),
                Build("EBGaramond-Bold.ttf", "SRD-Body-Bold", true, null),
                Build("EBGaramond-Italic.ttf", "SRD-Body-Italic", true, null),
            };

            // Probe assets are throwaway: built dynamic so HasCharacter can consult the real face,
            // used only to decide where each glyph lives, then rebuilt as baked static assets.
            var probes = new FontAsset[FallbackSources.Length];
            for (int i = 0; i < probes.Length; i++) probes[i] = Probe(FallbackSources[i]);

            var uncovered = new List<string>();
            var routed = RouteGlyphs(glyphs, probes, faces, uncovered);

            var chain = new List<FontAsset>();
            for (int i = 0; i < FallbackSources.Length; i++)
            {
                if (routed[i].Length == 0) { AssetDatabase.DeleteAsset(OutDir + "/" + FallbackNames[i] + ".asset"); continue; }
                chain.Add(Build(FallbackSources[i], FallbackNames[i], false, routed[i]));
            }

            foreach (var face in faces)
            {
                face.fallbackFontAssetTable = new List<FontAsset>(chain);
                EditorUtility.SetDirty(face);
            }

            WriteViewAssets(faces);
            WriteHudPanel();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.Append("font pipeline: ").Append(CountGlyphs(glyphs)).Append(" required glyphs, ")
              .Append(uncovered.Count).Append(" uncovered");
            for (int i = 0; i < FallbackSources.Length; i++)
                sb.Append("\n  ").Append(FallbackNames[i]).Append(": ")
                  .Append(CountGlyphs(routed[i])).Append(" glyphs  ").Append(routed[i]);
            foreach (var m in uncovered) sb.Append("\n  MISSING ").Append(m);
            return sb.ToString();
        }

        /// <summary>
        /// Publish the six faces to the one object runtime code can reach. Without this the fonts
        /// exist and nothing can load them: `Resources.Load` is the only door out of an asset
        /// folder at runtime, and the view must not know an asset path.
        /// </summary>
        static void WriteViewAssets(FontAsset[] faces)
        {
            const string dir = "Assets/Game/Resources";
            const string path = dir + "/ViewAssets.asset";
            Directory.CreateDirectory(dir);

            var assets = AssetDatabase.LoadAssetAtPath<SpawnRowDuel.View.Cards.ViewAssets>(path);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<SpawnRowDuel.View.Cards.ViewAssets>();
                AssetDatabase.CreateAsset(assets, path);
            }

            var so = new SerializedObject(assets);
            so.FindProperty("displayRegular").objectReferenceValue = faces[0];
            so.FindProperty("displayBold").objectReferenceValue = faces[1];
            so.FindProperty("displayBlack").objectReferenceValue = faces[2];
            so.FindProperty("bodyRegular").objectReferenceValue = faces[3];
            so.FindProperty("bodyBold").objectReferenceValue = faces[4];
            so.FindProperty("bodyItalic").objectReferenceValue = faces[5];
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(assets);
        }

        /// <summary>
        /// The HUD's PanelSettings, as a real ASSET in Resources.
        ///
        /// A PanelSettings created with CreateInstance at runtime resolves its UI shaders by name,
        /// and WebGL strips any shader no serialized asset references - the same class of failure
        /// that silently deleted the Physics module and killed board picking in M6. An asset in
        /// Resources is a serialized reference, so the shaders survive the build.
        /// </summary>
        static void WriteHudPanel()
        {
            const string dir = "Assets/Game/Resources";
            const string path = dir + "/HudPanelSettings.asset";
            const string themePath = dir + "/SpawnRowTheme.tss";
            Directory.CreateDirectory(dir);

            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", themePath))))
            {
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", themePath)),
                    "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var panel = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(path);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                AssetDatabase.CreateAsset(panel, path);
            }

            panel.themeStyleSheet =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(themePath);
            panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ConstantPixelSize;
            panel.clearColor = false;         // the board renders behind the HUD
            panel.sortingOrder = 0;
            EditorUtility.SetDirty(panel);
        }

        static int CountGlyphs(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) { if (char.IsHighSurrogate(s[i])) i++; n++; }
            return n;
        }

        /// <summary>An in-memory dynamic asset, used only to ask "can you draw this?".</summary>
        static FontAsset Probe(string ttf)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourceDir + "/" + ttf);
            if (font == null) throw new FileNotFoundException("missing source font", SourceDir + "/" + ttf);
            return FontAsset.CreateFontAsset(font, PointSize, Padding, GlyphRenderMode.SDFAA,
                256, 256, AtlasPopulationMode.Dynamic, true);
        }

        /// <summary>Which required glyphs no font in the chain can draw. Empty is the only pass.</summary>
        public static List<string> MissingGlyphs(string glyphs, FontAsset face, IList<FontAsset> chain)
        {
            var missing = new List<string>();
            for (int i = 0; i < glyphs.Length; i++)
            {
                int cp = char.ConvertToUtf32(glyphs, i);
                if (char.IsHighSurrogate(glyphs[i])) i++;

                bool found = HasGlyph(face, (uint)cp);
                for (int f = 0; f < chain.Count && !found; f++) found = HasGlyph(chain[f], (uint)cp);
                if (!found) missing.Add(char.ConvertFromUtf32(cp) + " U+" + cp.ToString("X4"));
            }
            return missing;
        }

        static bool HasGlyph(FontAsset font, uint unicode)
        {
            if (font == null) return false;
            if (font.characterLookupTable.ContainsKey(unicode)) return true;

            // a dynamic asset has not rasterised anything yet - ask the source face directly
            if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic && font.sourceFontFile != null)
                return font.HasCharacter(unicode, true, true);

            return false;
        }

        static FontAsset Build(string ttf, string assetName, bool dynamic, string characters)
        {
            var ttfPath = SourceDir + "/" + ttf;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null) throw new FileNotFoundException("missing source font", ttfPath);

            // A baked fallback holds a dozen glyphs; a 1024 atlas for those is two megabytes of
            // empty texture per asset. The faces get the big page because they rasterise every
            // card name in the game at runtime.
            int atlas = dynamic ? AtlasSize : FallbackAtlasSize;
            var asset = FontAsset.CreateFontAsset(font, PointSize, Padding, GlyphRenderMode.SDFAA,
                atlas, atlas, AtlasPopulationMode.Dynamic, true);
            asset.name = assetName;

            if (!string.IsNullOrEmpty(characters))
            {
                string missing;
                asset.TryAddCharacters(characters, out missing);
                if (!string.IsNullOrEmpty(missing))
                    Debug.Log(assetName + " has no glyph for: " + missing);
            }

            var path = OutDir + "/" + assetName + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);

            // The atlas and material are sub-assets; without this they are runtime-only objects and
            // the saved font asset comes back from a fresh import with a blank atlas.
            for (int i = 0; i < asset.atlasTextures.Length; i++)
            {
                if (asset.atlasTextures[i] == null) continue;
                asset.atlasTextures[i].name = assetName + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[i], asset);
            }
            if (asset.material != null)
            {
                asset.material.name = assetName + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            if (!dynamic)
            {
                // Static: the atlas is all there is. Dropping the source font keeps a 5.3 MB kanji
                // face out of the player build for the sake of eleven characters.
                asset.atlasPopulationMode = AtlasPopulationMode.Static;
                var so = new SerializedObject(asset);
                var prop = so.FindProperty("m_SourceFontFile");
                if (prop != null) { prop.objectReferenceValue = null; so.ApplyModifiedProperties(); }
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }
    }

    public static class FontPipelineCli
    {
        /// <summary>tools/regen-fonts.sh</summary>
        public static void BuildAndExit()
        {
            try
            {
                var report = FontPipeline.Run();
                Debug.Log(report);
                EditorApplication.Exit(report.Contains("MISSING") ? 2 : 0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
