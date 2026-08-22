using System;
using System.Collections.Generic;
using System.IO;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;
using SpawnRowDuel.View.Cards;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SpawnRowDuel.EditorPipeline
{
    /// <summary>
    /// Renders view surfaces to a PNG from batchmode, so the look can be CHECKED without a WebGL
    /// build, a deploy and a browser.
    ///
    /// This exists because the feedback loop is the bottleneck in a presentation milestone: the
    /// engine has tests, but "does the card frame look like the reference" is not a thing a test
    /// asserts - somebody has to look at it. A build-and-deploy round trip is minutes; this is
    /// seconds, and it is the same UI Toolkit panel the game runs.
    ///
    /// Must run WITHOUT -nographics: rendering needs a graphics device.
    /// </summary>
    public static class ViewProbe
    {
        const string ThemePath = "Assets/Game/Resources/SpawnRowTheme.tss";   // Resources: the runtime panel loads it too
        const string OutDir = "../unity/Build/Probe";

        public static void CaptureAndExit()
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("SRD_PROBE_OUT");
                if (string.IsNullOrEmpty(path))
                    path = Path.GetFullPath(Path.Combine(Application.dataPath, OutDir + "/cards.png"));

                var written = CaptureCardSheet(path);
                Debug.Log("probe wrote " + written);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>A sheet of representative card faces: one per kind, plus board states.</summary>
        public static string CaptureCardSheet(string outPath)
        {
            var catalog = LoadCatalog();
            var db = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Game/Data/CardDatabase.asset");
            var art = new CardArtIndex(db);
            var text = new CardTextService(catalog);
            var palette = new ElementPalette(catalog);

            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.flexWrap = Wrap.Wrap;
            root.style.paddingLeft = 16; root.style.paddingTop = 16;
            root.style.backgroundColor = new Color(0.07f, 0.05f, 0.09f);

            const float W = 150f;
            foreach (var model in SampleCards(catalog, text, art))
            {
                var face = new CardFace();
                face.Bind(model, palette, W);
                face.style.marginRight = 12;
                face.style.marginBottom = 12;
                root.Add(face);
            }

            int cols = 5;
            int rows = Mathf.CeilToInt(root.childCount / (float)cols);
            int width = Mathf.RoundToInt(16 + cols * (W + 12));
            int height = Mathf.RoundToInt(16 + rows * (W * CardFace.Aspect + 12));

            return Render(root, width, height, outPath);
        }

        static IEnumerable<CardFaceModel> SampleCards(ICardCatalog catalog, CardTextService text,
                                                      CardArtIndex art)
        {
            // one creature from each of four elements, so the accent thread is visible
            var wanted = new[] { Element.Fire, Element.Water, Element.Forest, Element.Dark };
            foreach (var el in wanted)
            {
                var pool = catalog.PoolOf(el);
                if (pool.Count == 0) continue;
                yield return CardFaceModel.OfCreature(pool[0], text, art);
            }

            // a keyworded creature, so the ability box has something long to wrap
            foreach (var c in catalog.Creatures)
            {
                if (c.Keyword != Keyword.None && c.Upkeep > 0)
                {
                    yield return CardFaceModel.OfCreature(c, text, art);
                    break;
                }
            }

            foreach (var s in catalog.Spells)
            {
                yield return CardFaceModel.OfSpell(s, text, art);
                break;
            }

            foreach (var s in catalog.Spells)
            {
                if (s.IsTrap) { yield return CardFaceModel.OfSpell(s, text, art); break; }
            }

            foreach (var d in catalog.Structures)
            {
                if (d.Effect == StructEffect.Mana)
                {
                    yield return CardFaceModel.OfStructure(d, text, art);
                    break;
                }
            }

            // the same creature as it looks on the board: damaged, tapped, summoning-sick, banked
            var live = catalog.Creatures[0];
            var m = CardFaceModel.OfCreature(live, text, art);
            m.Hp = Mathf.Max(1, m.MaxHp / 2);
            m.Sick = true; m.Tapped = true; m.Bank = 2;
            yield return m;
        }

        /// <summary>Draw a visual tree into a texture through a real runtime panel.</summary>
        static string Render(VisualElement root, int width, int height, string outPath)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "SRD Probe RT",
            };
            rt.Create();

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = LoadTheme();
            panel.targetTexture = rt;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.clearColor = true;
            panel.colorClearValue = new Color(0.07f, 0.05f, 0.09f, 1f);

            var go = new GameObject("SRD Probe", typeof(UIDocument));
            go.hideFlags = HideFlags.HideAndDontSave;
            var doc = go.GetComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.rootVisualElement.Add(root);

            // A runtime panel repaints from the player loop, and -executeMethod blocks before the
            // editor ever ticks one - which is why the first version of this probe wrote a blank
            // white image. Drive the panel directly instead. The entry points are internal, so
            // this asks the type what it has rather than pinning a signature that moves between
            // Unity versions; the probe is a dev tool and a loud failure here costs nothing.
            var panelObj = root.panel;
            if (panelObj == null) throw new Exception("the probe tree never attached to a panel");

            bool painted = Invoke(panelObj, "ValidateLayout");
            painted |= Invoke(panelObj, "UpdateWithoutRepaint");
            painted |= Invoke(panelObj, "Repaint", new Event());
            painted |= Invoke(panelObj, "Render");
            if (!painted) Debug.LogWarning("no repaint entry point found on " + panelObj.GetType().FullName);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            return outPath;
        }

        /// <summary>Call an internal method by name if this Unity version has it. Logs what it used.</summary>
        static bool Invoke(object target, string method, params object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();

            var t = target.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic, null, types, null);
                if (m != null)
                {
                    m.Invoke(target, args);
                    Debug.Log("probe: " + t.Name + "." + method + "()");
                    return true;
                }
                t = t.BaseType;
            }
            return false;
        }

        static ThemeStyleSheet LoadTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) return theme;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", ThemePath))));
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", ThemePath)),
                "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        }

        static CardCatalog LoadCatalog()
        {
            var json = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../docs/unity/spec/cards.json"));
            ValidationReport report;
            return CardsJsonCatalog.Load(File.ReadAllText(json), out report);
        }
    }
}
