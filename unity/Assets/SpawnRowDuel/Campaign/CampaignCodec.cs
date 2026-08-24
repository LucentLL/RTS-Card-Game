using System.Collections.Generic;
using System.Text;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// The campaign save: a whole-object write, JSON, with a schema version and a real migration
    /// hook rather than the JS's "delete last version's key and start over".
    ///
    /// The sphere is NOT serialised. Geometry is a pure function of the frequency, so the file is
    /// the tile-to-territory assignment, the territory records and the thrones - about three
    /// kilobytes. It also stores the SEED, which the JS could not: a map you can re-derive is a map
    /// you can audit when someone reports that their capital spawned somewhere impossible.
    /// </summary>
    public static class CampaignCodec
    {
        public const string FileName = "campaign.json";

        public static string Write(CampaignState s)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":").Append(CampaignState.SchemaVersion);
            sb.Append(",\"faction\":\"").Append(CampaignRules.Name(s.Faction).ToLowerInvariant()).Append('"');
            sb.Append(",\"turn\":").Append(s.Turn);
            sb.Append(",\"seed\":").Append(s.Seed);
            sb.Append(",\"completed\":").Append(s.Completed ? "true" : "false");
            sb.Append(",\"lost\":").Append(s.Lost ? "true" : "false");

            sb.Append(",\"allies\":[");
            bool first = true;
            foreach (var el in CampaignRules.Majors)
            {
                if (!s.Allies.Contains(el)) continue;
                if (!first) sb.Append(',');
                sb.Append('"').Append(CampaignRules.Name(el).ToLowerInvariant()).Append('"');
                first = false;
            }
            sb.Append(']');

            var m = s.Map;
            sb.Append(",\"map\":{\"f\":").Append(m.Frequency);
            sb.Append(",\"tileTerr\":[");
            for (int i = 0; i < m.TileTerritory.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(m.TileTerritory[i]);
            }
            sb.Append("],\"terr\":[");
            for (int i = 0; i < m.Territories.Length; i++)
            {
                var t = m.Territories[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(t.Id);
                sb.Append(",\"owner\":\"").Append(CampaignRules.Name(t.Owner).ToLowerInvariant()).Append('"');
                sb.Append(",\"garrison\":").Append(t.Garrison);
                sb.Append(",\"anchor\":").Append(t.AnchorTile);
                sb.Append(",\"tiles\":["); Ints(sb, t.Tiles); sb.Append(']');
                sb.Append(",\"adj\":["); Ints(sb, t.Adjacent); sb.Append(']');
                sb.Append('}');
            }
            sb.Append("],\"capitals\":{");
            first = true;
            foreach (var el in CampaignRules.Majors)
            {
                int tid;
                if (!m.Capitals.TryGetValue(el, out tid)) continue;
                if (!first) sb.Append(',');
                sb.Append('"').Append(CampaignRules.Name(el).ToLowerInvariant()).Append("\":").Append(tid);
                first = false;
            }
            sb.Append("}}}");
            return sb.ToString();
        }

        static void Ints(StringBuilder sb, int[] v)
        {
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i]); }
        }

        /// <summary>
        /// Read a save back, or null if it is unusable. "Unusable" is deliberately broad: a save
        /// whose tile list does not match the sphere its own frequency rebuilds would index past
        /// the end of the world on the first render, and a corrupt campaign is better dropped than
        /// nursed.
        /// </summary>
        public static CampaignState Read(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var root = JsonValue.Parse(json);
                if (root == null || root.Type != JsonType.Object) return null;
                if (root.IntOr("schema", 0) != CampaignState.SchemaVersion) return null;

                var faction = CampaignRules.FromName(root.StrOrNull("faction"));
                if (!CampaignRules.IsMajor(faction)) return null;

                var mv = root.Get("map");
                if (mv == null || mv.Type != JsonType.Object) return null;

                var map = new CampaignMap
                {
                    Frequency = mv.IntOr("f", 0),
                    Capitals = new Dictionary<Element, int>(),
                };

                var tt = mv.Get("tileTerr");
                if (tt == null || tt.Type != JsonType.Array) return null;
                map.TileTerritory = new int[tt.Count];
                for (int i = 0; i < tt.Count; i++) map.TileTerritory[i] = tt[i].AsInt;

                var tv = mv.Get("terr");
                if (tv == null || tv.Type != JsonType.Array) return null;
                map.Territories = new Territory[tv.Count];
                for (int i = 0; i < tv.Count; i++)
                {
                    var o = tv[i];
                    map.Territories[i] = new Territory
                    {
                        Id = o.IntOr("id", i),
                        Owner = CampaignRules.FromName(o.StrOrNull("owner")),
                        Garrison = o.IntOr("garrison", 0),
                        AnchorTile = o.IntOr("anchor", 0),
                        Tiles = IntArray(o.Get("tiles")),
                        Adjacent = IntArray(o.Get("adj")),
                    };
                }

                var cv = mv.Get("capitals");
                if (cv != null && cv.Type == JsonType.Object)
                    foreach (var key in cv.Keys)
                    {
                        var el = CampaignRules.FromName(key);
                        if (el != Element.None) map.Capitals[el] = cv.Get(key).AsInt;
                    }

                var s = new CampaignState
                {
                    Faction = faction,
                    Turn = root.IntOr("turn", 1),
                    Map = map,
                    Completed = root.BoolOr("completed", false),
                    Lost = root.BoolOr("lost", false),
                    Seed = 0,
                    // a pending target NEVER survives a load: it is the flag that routes a
                    // finished duel back into the campaign, and a stale one resolves the next
                    // match you play into ground you were not fighting for
                    TargetTerritory = null,
                };
                if (s.Turn < 1) s.Turn = 1;

                var av = root.Get("allies");
                if (av != null && av.Type == JsonType.Array)
                    for (int i = 0; i < av.Count; i++)
                    {
                        var el = CampaignRules.FromName(av[i].AsString);
                        if (el != Element.None) s.Allies.Add(el);
                    }

                return s.IsValid ? s : null;
            }
            catch
            {
                return null;
            }
        }

        static int[] IntArray(JsonValue v)
        {
            if (v == null || v.Type != JsonType.Array) return new int[0];
            var a = new int[v.Count];
            for (int i = 0; i < v.Count; i++) a[i] = v[i].AsInt;
            return a;
        }
    }
}
