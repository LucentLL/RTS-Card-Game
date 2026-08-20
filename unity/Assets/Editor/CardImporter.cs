using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using SpawnRowDuel.Data;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.EditorPipeline
{
    public sealed class ImportReport
    {
        public readonly List<string> Created = new List<string>();
        public readonly List<string> Updated = new List<string>();
        public readonly List<string> Orphans = new List<string>();
        public readonly List<string> MissingCardArt = new List<string>();   // V12 - info, never fatal
        public readonly List<string> MissingFieldArt = new List<string>();
        public int Unchanged;
        public bool DatabaseChanged;

        public int Drift { get { return Created.Count + Updated.Count + Orphans.Count; } }

        public void Log()
        {
            Debug.Log("[cards] import: " + Created.Count + " created, " + Updated.Count +
                      " updated, " + Unchanged + " unchanged, " + Orphans.Count + " orphaned, db " +
                      (DatabaseChanged ? "updated" : "unchanged") +
                      " | art missing: " + MissingCardArt.Count + " card / " +
                      MissingFieldArt.Count + " field (placeholders are the shipped decision - G1)");
            foreach (var p in Created) Debug.Log("[cards]   + " + p);
            foreach (var p in Updated) Debug.Log("[cards]   ~ " + p);
            foreach (var p in Orphans) Debug.LogWarning("[cards]   orphan: " + p);
        }
    }

    /// <summary>
    /// cards.json -> one CardDefinition asset per row + the CardDatabase index. Idempotent by
    /// construction (design 03 s5.4): deterministic paths from export keys, load-then-mutate so
    /// GUIDs are minted exactly once, JSON change-detection so a no-op import touches zero files,
    /// orphans reported and deleted only when pruning is asked for.
    ///
    /// Validation runs the SAME pure V1-V11 battery the loader and the EditMode tests use; a bad
    /// registry throws before anything is written. Missing art is REPORTED, never fatal - the
    /// placeholder frame is the shipped decision (DECISIONS.md G1).
    /// </summary>
    public static class CardImporter
    {
        public const string GeneratedRoot = "Assets/Game/Data/Cards";
        public const string DatabasePath = "Assets/Game/Data/CardDatabase.asset";
        public const string ArtRoot = "Assets/Game/Art/Cards";

        /// <summary>cards.json lives OUTSIDE Assets/ on purpose - one copy, no dead TextAsset.</summary>
        public static string CardsJsonPath
        {
            get
            {
                return Path.GetFullPath(
                    Path.Combine(Application.dataPath, "../../docs/unity/spec/cards.json"));
            }
        }

        [MenuItem("Tools/Spawn Row Duel/Import Cards from cards.json %#i")]
        public static void ImportMenu()
        {
            Run(false, false);
        }

        [MenuItem("Tools/Spawn Row Duel/Import Cards (prune orphans)")]
        public static void ImportPruneMenu()
        {
            if (EditorUtility.DisplayDialog("Prune orphans?",
                "Deletes generated card assets that no longer appear in cards.json. " +
                "References to them will break. Continue?", "Prune", "Cancel"))
                Run(true, false);
        }

        public static ImportReport Run(bool prune, bool dryRun)
        {
            var report = new ImportReport();
            var json = File.ReadAllText(CardsJsonPath, Encoding.UTF8);

            var doc = CardsJsonDoc.Parse(json);
            var catalog = CardCatalogBuilder.Build(doc);         // V11 fail-loud enum resolution
            var validation = CatalogValidator.Validate(doc, catalog);
            foreach (var w in validation.Warnings) Debug.LogWarning("[cards] " + w);
            if (!validation.Ok)
                throw new CardsJsonException("cards.json failed validation:\n" + validation.Describe());

            // Self-heal the one-time ordering race: art that imported before this script
            // compiled came in as texture type Default and is invisible to t:Sprite queries.
            if (!dryRun)
            {
                int repaired = FixArtImporters();
                if (repaired > 0) Debug.Log("[cards] repaired " + repaired + " art importer(s) to Sprite");
            }

            var art = ArtIndex.Build();
            var rows = EnumerateRows(doc);
            rows.Sort(delegate (CardRow a, CardRow b) { return string.CompareOrdinal(a.Key, b.Key); });

            var expected = new HashSet<string>(StringComparer.Ordinal);
            var byPath = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var row in rows)
                {
                    var path = AssetPathFor(row);
                    expected.Add(path);
                    var so = UpsertOne(row, path, art, report, dryRun);
                    if (so != null) byPath[path] = so;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Orphans: everything generated that this run did not produce. Reported always,
            // deleted only on request - a silent delete would destroy hand-linked art.
            foreach (var guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { GeneratedRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (expected.Contains(p)) continue;
                report.Orphans.Add(p);
                if (prune && !dryRun) AssetDatabase.DeleteAsset(p);
            }

            if (!dryRun) UpsertDatabase(doc, json, expected, byPath, report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.Log();
            return report;
        }

        // ---- row model ------------------------------------------------------------------------

        private sealed class CardRow
        {
            public string Key;
            public CardKind Kind;
            public int RegistryIndex;
            public CardsJsonDoc.CreatureRow Creature;
            public CardsJsonDoc.SpellRow Spell;
            public CardsJsonDoc.StructureRow Structure;
            public CardsJsonDoc.CommanderRow Commander;
            public CardsJsonDoc.ElementRow Element;
            public bool Deckable;
        }

        private static List<CardRow> EnumerateRows(CardsJsonDoc doc)
        {
            var rows = new List<CardRow>(160);

            for (int i = 0; i < doc.Creatures.Count; i++)
                rows.Add(new CardRow { Key = doc.Creatures[i].Key, Kind = CardKind.Creature, RegistryIndex = i, Creature = doc.Creatures[i], Deckable = true });
            for (int i = 0; i < doc.Divine.Count; i++)
                rows.Add(new CardRow { Key = doc.Divine[i].Key, Kind = CardKind.Creature, RegistryIndex = doc.Creatures.Count + i, Creature = doc.Divine[i], Deckable = false });
            for (int i = 0; i < doc.Spells.Count; i++)
                rows.Add(new CardRow { Key = doc.Spells[i].Key, Kind = CardKind.Spell, RegistryIndex = i, Spell = doc.Spells[i] });
            for (int i = 0; i < doc.Structures.Count; i++)
                rows.Add(new CardRow { Key = doc.Structures[i].Key, Kind = CardKind.Structure, RegistryIndex = i, Structure = doc.Structures[i] });
            for (int i = 0; i < doc.Forges.Count; i++)
                rows.Add(new CardRow { Key = doc.Forges[i].Key, Kind = CardKind.Structure, RegistryIndex = doc.Structures.Count + i, Structure = doc.Forges[i] });
            for (int i = 0; i < doc.Commanders.Count; i++)
                rows.Add(new CardRow { Key = "cc|" + doc.Commanders[i].Id, Kind = CardKind.Commander, RegistryIndex = i, Commander = doc.Commanders[i] });
            for (int i = 0; i < doc.Elements.Count; i++)
                rows.Add(new CardRow { Key = "el|" + doc.Elements[i].Id, Kind = CardKind.Element, RegistryIndex = i, Element = doc.Elements[i] });
            if (doc.Worker != null)
                rows.Add(new CardRow { Key = doc.Worker.Key, Kind = CardKind.Token, RegistryIndex = 0, Creature = doc.Worker });

            return rows;
        }

        private static string AssetPathFor(CardRow r)
        {
            string safe = CardCatalogBuilder.SafeFileName(r.Key);
            switch (r.Kind)
            {
                case CardKind.Creature:
                    return GeneratedRoot + "/Creatures/" + Cap(r.Creature.ElementRaw) + "/" + safe + ".asset";
                case CardKind.Spell:
                    return GeneratedRoot + (r.Spell.Trap ? "/Traps/" : "/Spells/") + safe + ".asset";
                case CardKind.Structure:
                    return GeneratedRoot + "/Structures/" + safe + ".asset";
                case CardKind.Commander:
                    return GeneratedRoot + "/Commanders/" + CardCatalogBuilder.SafeFileName(r.Commander.Id) + ".asset";
                case CardKind.Element:
                    return GeneratedRoot + "/Elements/" + CardCatalogBuilder.SafeFileName(r.Element.Id) + ".asset";
                default:
                    return GeneratedRoot + "/Tokens/" + safe + ".asset";
            }
        }

        private static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return "None";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // ---- upsert ---------------------------------------------------------------------------

        private static CardDefinition UpsertOne(CardRow row, string path, ArtIndex art,
                                                ImportReport report, bool dryRun)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            var so = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            bool created = so == null;
            if (created)
            {
                so = ScriptableObject.CreateInstance<CardDefinition>();
                if (!dryRun) AssetDatabase.CreateAsset(so, path);   // GUID minted ONCE, here
            }

            var before = EditorJsonUtility.ToJson(so);
            so.__EditorApply(delegate (CardDefinition c) { Populate(c, row, art, report); });
            var after = EditorJsonUtility.ToJson(so);

            if (created) report.Created.Add(path);
            else if (before != after)
            {
                report.Updated.Add(path);
                if (!dryRun) EditorUtility.SetDirty(so);
            }
            else report.Unchanged++;

            return so;
        }

        private static void Populate(CardDefinition c, CardRow row, ArtIndex art, ImportReport report)
        {
            c.exportKey = row.Key;
            c.kind = row.Kind;
            c.registryIndex = row.RegistryIndex;

            switch (row.Kind)
            {
                case CardKind.Creature:
                case CardKind.Token:
                    PopulateCreature(c, row, report);
                    break;
                case CardKind.Spell:
                    PopulateSpell(c, row.Spell);
                    break;
                case CardKind.Structure:
                    PopulateStructure(c, row.Structure);
                    break;
                case CardKind.Commander:
                    PopulateCommander(c, row.Commander);
                    break;
                case CardKind.Element:
                    PopulateElement(c, row.Element);
                    break;
            }

            // Art resolves by slug from the junctioned folder, assigned only when found so a
            // hand-linked override for a still-missing file is never clobbered.
            if (!string.IsNullOrEmpty(c.slug))
            {
                var cardArt = art.FindCardArt(c.slug);
                if (cardArt != null) c.cardArt = cardArt;
                else if (row.Kind == CardKind.Creature || row.Kind == CardKind.Spell)
                    report.MissingCardArt.Add(c.exportKey);

                var fieldArt = art.FindFieldArt(c.slug);
                if (fieldArt != null) c.fieldArt = fieldArt;
                else if (row.Kind == CardKind.Creature)
                    report.MissingFieldArt.Add(c.exportKey);
            }
        }

        private static void PopulateCreature(CardDefinition c, CardRow row, ImportReport report)
        {
            var r = row.Creature;
            string ctx = "creature '" + r.Nm + "'";
            c.displayName = r.Nm;
            c.slug = r.Slug ?? "";
            c.element = r.ElementRaw == null ? Element.None
                : CardCatalogBuilder.ParseElementReq(r.ElementRaw, ctx);
            c.isNeutral = r.ElementRaw == null;
            c.isPlayable = row.Deckable;
            c.poolIndex = r.PoolIndex;
            c.cost = r.C;
            c.attack = r.A;
            c.health = r.H;
            c.upkeep = r.Up;
            c.firstStrike = r.Fs;
            c.keyword = CardCatalogBuilder.ParseKeyword(r.KwRaw, ctx);
            c.detonate = r.Det ?? -1;
            c.reap = r.Reap ?? -1;
            c.wardHp = r.WardHp ?? -1;
            c.grow = r.Grow ?? -1;
            c.hatch = r.Hatch ?? -1;
            c.entrench = r.Entrench;
            c.tribe = CardCatalogBuilder.ParseTribe(r.TribeRaw, ctx);
            c.subtype = CardCatalogBuilder.ParseSubtype(r.SubtypeRaw, ctx);
            c.intoName = r.IntoNm ?? "";
            c.intoAttack = r.IntoNm == null ? 0 : r.IntoA;
            c.intoHealth = r.IntoNm == null ? 0 : r.IntoH;
        }

        private static void PopulateSpell(CardDefinition c, CardsJsonDoc.SpellRow r)
        {
            string ctx = "spell '" + r.Nm + "'";
            c.displayName = r.Nm;
            c.slug = r.Slug ?? "";
            c.element = Element.None;
            c.isNeutral = true;
            c.isPlayable = true;
            c.cost = r.C;
            c.isTrap = r.Trap;
            c.spellEffect = CardCatalogBuilder.ParseSpellEffect(r.EffectRaw, ctx);
            c.spellValue = r.Val ?? -1;
            c.spellTarget = CardCatalogBuilder.ParseSpellTarget(r.TargetRaw, ctx);
            c.trapTrigger = CardCatalogBuilder.ParseTrapTrigger(r.TriggerRaw, ctx);
            c.glyph = r.Ic ?? "";
        }

        private static void PopulateStructure(CardDefinition c, CardsJsonDoc.StructureRow r)
        {
            string ctx = "structure '" + r.Key + "'";
            c.displayName = r.Nm;
            c.slug = r.Slug ?? "";
            c.element = r.ColorRaw == null ? Element.None
                : CardCatalogBuilder.ParseElementReq(r.ColorRaw, ctx);
            c.isNeutral = r.ColorRaw == null;
            c.isPlayable = r.Buildable;
            c.cost = r.C;
            c.health = r.H;
            c.buildId = r.Bid;
            c.structEffect = CardCatalogBuilder.ParseStructEffect(r.EffRaw, ctx);
            c.structValue = r.Val;
            c.support = r.Sup;
            c.prereq = r.Prereq ?? new string[0];
            c.upgradedFrom = r.From ?? "";
            c.upgradesTo = r.Up2 ?? new string[0];
            c.rowGate = CardCatalogBuilder.ParseRowGate(r.RowRaw, ctx);
            c.buildable = r.Buildable;
            c.glyph = r.Ic ?? "";
            c.description = r.Desc ?? "";
        }

        private static void PopulateCommander(CardDefinition c, CardsJsonDoc.CommanderRow r)
        {
            string ctx = "commander '" + r.Id + "'";
            c.displayName = r.Name;
            c.slug = "";
            c.exportKey = r.Id;              // commanders keep their bare id as the key
            c.isNeutral = false;
            c.isPlayable = true;
            c.life = r.Hp;
            c.baseWorkers = r.Wk;
            c.dual = r.Dual;
            var colors = new Element[r.Colors.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = CardCatalogBuilder.ParseElementReq(r.Colors[i], ctx);
            c.colors = colors;
            c.element = colors.Length > 0 ? colors[0] : Element.None;
            c.buildList = r.BuildList ?? new string[0];
            c.description = r.Desc ?? "";
        }

        private static void PopulateElement(CardDefinition c, CardsJsonDoc.ElementRow r)
        {
            c.displayName = r.Name;
            c.slug = "";
            c.exportKey = "el|" + r.Id;
            c.element = CardCatalogBuilder.ParseElementReq(r.Id, "element '" + r.Id + "'");
            c.isNeutral = false;
            c.isPlayable = r.Deckable;       // == deckable for elements
            c.life = r.Hp;                   // == element hp
            c.baseWorkers = r.Wk;            // == element wk
            c.glyphKanji = r.Glyph ?? "";
            c.colorHex = r.ColorHex ?? "";
            c.accentHex = r.AccentHex ?? "";
            c.deepHex = r.DeepHex ?? "";
            c.bgStops = r.Bg ?? new string[0];
            c.description = r.Lore ?? "";
        }

        // ---- database -------------------------------------------------------------------------

        private static void UpsertDatabase(CardsJsonDoc doc, string json, HashSet<string> expected,
                                           Dictionary<string, CardDefinition> byPath,
                                           ImportReport report)
        {
            var db = AssetDatabase.LoadAssetAtPath<CardDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<CardDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
                report.Created.Add(DatabasePath);
            }

            var before = EditorJsonUtility.ToJson(db);

            // The index array is sorted by asset path (== sorted by export key fold), so the
            // YAML diff is minimal; registry order lives on each row's registryIndex.
            var paths = new List<string>(expected);
            paths.Sort(StringComparer.Ordinal);
            var defs = new List<CardDefinition>(paths.Count);
            foreach (var p in paths)
            {
                CardDefinition d;
                if (byPath.TryGetValue(p, out d) && d != null) defs.Add(d);
                else
                {
                    var loaded = AssetDatabase.LoadAssetAtPath<CardDefinition>(p);
                    if (loaded != null) defs.Add(loaded);
                }
            }

            db.all = defs.ToArray();
            db.sourceHash = HashOf(json);
            db.sourceGeneratedAt = doc.GeneratedAt ?? "";
            db.deckSize = doc.DeckSize;
            db.maxCopies = doc.MaxCopies;
            db.boardSlots = doc.Slots;
            db.baseColumn = doc.BaseCol;
            db.centerLanes = doc.CenterLanes ?? new int[0];

            var after = EditorJsonUtility.ToJson(db);
            if (before != after)
            {
                report.DatabaseChanged = true;
                EditorUtility.SetDirty(db);
            }
        }

        /// <summary>SHA-256 of cards.json with the generatedAt stamp normalised away, so a
        /// re-export that changed nothing does not read as a data change.</summary>
        public static string HashOf(string json)
        {
            var normalised = Regex.Replace(json, "\"generatedAt\"\\s*:\\s*\"[^\"]*\"", "\"generatedAt\":\"\"");
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalised));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string HashOfFile(string path)
        {
            return HashOf(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>Force any non-Sprite texture under the art junction back to Sprite settings.</summary>
        private static int FixArtImporters()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot)) return 0;
            int repaired = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.Sprite) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
                repaired++;
            }
            return repaired;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---- art ------------------------------------------------------------------------------

        /// <summary>
        /// Filename -> sprite over the junctioned art folder. Missing art is the EXPECTED case;
        /// the extension preference mirrors the JS probe order (png, jpg, jpeg for card art;
        /// webp never imports in Unity and simply won't be found).
        /// </summary>
        private sealed class ArtIndex
        {
            private readonly Dictionary<string, Sprite> _byName =
                new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

            public static ArtIndex Build()
            {
                var index = new ArtIndex();
                if (!AssetDatabase.IsValidFolder(ArtRoot)) return index;   // junction not set up

                foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { ArtRoot }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var name = Path.GetFileNameWithoutExtension(path);
                    var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                    var key = name + "|" + ext;
                    if (!index._byName.ContainsKey(key))
                        index._byName[key] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                }
                return index;
            }

            private static readonly string[] CardExts = { "png", "jpg", "jpeg" };
            private static readonly string[] FieldExts = { "png", "jpg" };

            public Sprite FindCardArt(string slug) { return Find(slug + "_cardart", CardExts); }
            public Sprite FindFieldArt(string slug) { return Find(slug + "_fieldart", FieldExts); }

            private Sprite Find(string baseName, string[] exts)
            {
                for (int i = 0; i < exts.Length; i++)
                {
                    Sprite s;
                    if (_byName.TryGetValue(baseName + "|" + exts[i], out s)) return s;
                }
                return null;
            }
        }
    }
}
