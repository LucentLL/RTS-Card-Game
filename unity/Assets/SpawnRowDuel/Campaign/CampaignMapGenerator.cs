using System.Collections.Generic;
using SpawnRowDuel.Rules;

namespace SpawnRowDuel.Campaign
{
    /// <summary>
    /// Draws a fresh world: carve the sphere into 22 contiguous territories, then hand 8 elements
    /// contiguous empires, then garrison everything.
    ///
    /// Both carves are multi-source BFS from seeds pushed into ONE shared queue, which is what
    /// makes contiguity a property of the construction rather than something to check afterwards:
    /// a graph-distance Voronoi on a connected graph cannot produce an island. The JS validated
    /// that claim with an 800-map Monte-Carlo and found zero fragments; the test here does the
    /// same over a thousand seeds, because the guarantee is easy to lose in a refactor and
    /// impossible to notice by eye.
    ///
    /// Every draw goes through the passed RNG. The JS used bare Math.random in nine places and so
    /// could never re-derive or audit a map; a campaign that stores its seed can.
    /// </summary>
    public sealed class CampaignMapGenerator
    {
        public const int Territories = 22;
        public const int Empires = 8;
        const int MitchellCandidates = 8;

        public CampaignMap Generate(Element playerFaction, IRandomSource rng)
        {
            var sphere = HexSphere.Get(HexSphere.DefaultFrequency);
            var tiles = sphere.Tiles;
            int n = tiles.Length;
            int k = Territories < n ? Territories : n;

            var seeds = TerritorySeeds(tiles, n, k, rng);
            var tileTerr = Flood(tiles, n, seeds);
            var terr = BuildTerritories(tiles, tileTerr, n, k);

            var map = new CampaignMap
            {
                Frequency = sphere.Frequency,
                TileTerritory = tileTerr,
                Territories = terr,
                Capitals = new Dictionary<Element, int>(),
            };

            AssignEmpires(map, tiles, playerFaction, rng);
            Garrison(map, rng);
            return map;
        }

        /// <summary>
        /// Mitchell's best-candidate sampling: draw 8 tiles, keep the one furthest from every seed
        /// so far. Organic like pure random, but without the clustered seeds that produce one
        /// giant blob beside a sliver.
        /// </summary>
        static List<int> TerritorySeeds(HexTile[] tiles, int n, int k, IRandomSource rng)
        {
            var seeds = new List<int> { rng.NextInt(n) };
            while (seeds.Count < k)
            {
                int best = -1;
                double bd = -1.0;
                for (int c = 0; c < MitchellCandidates; c++)
                {
                    int cand = rng.NextInt(n);
                    if (seeds.Contains(cand)) continue;
                    double d = double.MaxValue;
                    for (int i = 0; i < seeds.Count; i++)
                        d = System.Math.Min(d, Vec3.Chord(tiles[cand].Center, tiles[seeds[i]].Center));
                    if (d > bd) { bd = d; best = cand; }
                }
                if (best < 0) continue;         // all eight collided; draw again
                seeds.Add(best);
            }
            return seeds;
        }

        static int[] Flood(HexTile[] tiles, int n, List<int> seeds)
        {
            var tileTerr = new int[n];
            for (int i = 0; i < n; i++) tileTerr[i] = -1;

            var fringe = new List<int>(n);
            for (int i = 0; i < seeds.Count; i++) { tileTerr[seeds[i]] = i; fringe.Add(seeds[i]); }

            for (int fi = 0; fi < fringe.Count; fi++)
            {
                int t = fringe[fi];
                var adj = tiles[t].Adjacent;
                for (int a = 0; a < adj.Length; a++)
                {
                    int u = adj[a];
                    if (tileTerr[u] >= 0) continue;
                    tileTerr[u] = tileTerr[t];
                    fringe.Add(u);
                }
            }
            return tileTerr;
        }

        static Territory[] BuildTerritories(HexTile[] tiles, int[] tileTerr, int n, int k)
        {
            var members = new List<int>[k];
            for (int i = 0; i < k; i++) members[i] = new List<int>();
            for (int t = 0; t < n; t++) members[tileTerr[t]].Add(t);

            var adj = new List<int>[k];
            for (int i = 0; i < k; i++) adj[i] = new List<int>();
            for (int t = 0; t < n; t++)
            {
                var neigh = tiles[t].Adjacent;
                for (int a = 0; a < neigh.Length; a++)
                {
                    int x = tileTerr[t], y = tileTerr[neigh[a]];
                    if (x == y) continue;
                    if (!adj[x].Contains(y)) adj[x].Add(y);
                    if (!adj[y].Contains(x)) adj[y].Add(x);
                }
            }

            var terr = new Territory[k];
            for (int i = 0; i < k; i++)
            {
                // the anchor is the member tile facing most directly along the territory's own
                // centroid direction - the spot a marker looks pinned to rather than adrift
                double sx = 0, sy = 0, sz = 0;
                for (int m = 0; m < members[i].Count; m++)
                {
                    var c = tiles[members[i][m]].Center;
                    sx += c.X; sy += c.Y; sz += c.Z;
                }
                var cn = new Vec3(sx, sy, sz).Normalized;

                int anchor = members[i][0];
                double bd = -2.0;
                for (int m = 0; m < members[i].Count; m++)
                {
                    double d = Vec3.Dot(tiles[members[i][m]].Center, cn);
                    if (d > bd) { bd = d; anchor = members[i][m]; }
                }

                terr[i] = new Territory
                {
                    Id = i,
                    Tiles = members[i].ToArray(),
                    Adjacent = adj[i].ToArray(),
                    Owner = Element.None,
                    Garrison = 0,
                    AnchorTile = anchor,
                };
            }
            return terr;
        }

        /// <summary>
        /// Eight empires by farthest-point sampling on the territory anchors, then one more BFS
        /// flood over the TERRITORY graph. The player's faction always takes the first seed, which
        /// is the uniformly random one - the other seven are the spread-out picks.
        /// </summary>
        static void AssignEmpires(CampaignMap map, HexTile[] tiles, Element faction, IRandomSource rng)
        {
            var terr = map.Territories;
            int k = terr.Length;

            System.Func<int, Vec3> pos = i => tiles[terr[i].AnchorTile].Center;

            var eseeds = new List<int> { rng.NextInt(k) };
            while (eseeds.Count < Empires && eseeds.Count < k)
            {
                int best = -1;
                double bd = -1.0;
                for (int t = 0; t < k; t++)
                {
                    if (eseeds.Contains(t)) continue;
                    double d = double.MaxValue;
                    for (int i = 0; i < eseeds.Count; i++)
                        d = System.Math.Min(d, Vec3.Chord(pos(t), pos(eseeds[i])));
                    if (d > bd) { bd = d; best = t; }       // first max wins ties, scanning in id order
                }
                if (best < 0) break;
                eseeds.Add(best);
            }

            var others = new List<Element>();
            for (int i = 0; i < CampaignRules.Majors.Length; i++)
                if (CampaignRules.Majors[i] != faction) others.Add(CampaignRules.Majors[i]);
            Shuffle(others, rng);

            var order = new List<Element> { faction };
            order.AddRange(others);

            var owner = new Element[k];
            var queue = new List<int>(k);
            for (int i = 0; i < eseeds.Count && i < order.Count; i++)
            {
                int tid = eseeds[i];
                owner[tid] = order[i];
                map.Capitals[order[i]] = tid;
                queue.Add(tid);
            }

            for (int qi = 0; qi < queue.Count; qi++)
            {
                int t = queue[qi];
                var neigh = terr[t].Adjacent;
                for (int a = 0; a < neigh.Length; a++)
                {
                    int u = neigh[a];
                    if (owner[u] != Element.None) continue;
                    owner[u] = owner[t];
                    queue.Add(u);
                }
            }

            // topologically impossible on a sphere, but a fallback that cannot leave an ownerless
            // territory is cheaper than a render that trips over one
            for (int t = 0; t < k; t++)
            {
                if (owner[t] != Element.None) continue;
                int best = eseeds[0];
                double bd = double.MaxValue;
                for (int i = 0; i < eseeds.Count; i++)
                {
                    double d = Vec3.Chord(pos(t), pos(eseeds[i]));
                    if (d < bd) { bd = d; best = eseeds[i]; }
                }
                owner[t] = owner[best];
            }

            for (int t = 0; t < k; t++) terr[t].Owner = owner[t];
        }

        static void Garrison(CampaignMap map, IRandomSource rng)
        {
            for (int t = 0; t < map.Territories.Length; t++)
            {
                bool isCapital = CampaignRules.CapitalDesignation(map, t) != Element.None;
                map.Territories[t].Garrison = 5 + rng.NextInt(7) + (isCapital ? 7 : 0);
            }
        }

        static void Shuffle(List<Element> list, IRandomSource rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
