using System;
using System.Collections.Generic;

namespace SpawnRowDuel.Campaign
{
    /// <summary>A unit direction on the globe. Doubles, because the JS this ports is doubles and
    /// the tile ORDER falls out of arithmetic on them.</summary>
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public Vec3 Normalized
        {
            get
            {
                double l = Length;
                return l > 0.0 ? new Vec3(X / l, Y / l, Z / l) : this;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, double k) { return new Vec3(a.X * k, a.Y * k, a.Z * k); }

        public static double Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        /// <summary>Straight-line distance through the sphere, not around it - the JS measures
        /// seed spacing with a chord and the seeding is sensitive to which one you use.</summary>
        public static double Chord(Vec3 a, Vec3 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public override string ToString() { return X.ToString("F4") + "," + Y.ToString("F4") + "," + Z.ToString("F4"); }
    }

    /// <summary>One tile of the world: where it is, what it is shaped like, and who it touches.</summary>
    public sealed class HexTile
    {
        public readonly Vec3 Center;      // unit vector
        public readonly int[] Corners;    // 5 or 6, CCW seen from outside; indices into HexSphere.Corners
        public readonly int[] Adjacent;   // 5 or 6 tile ids

        public HexTile(Vec3 center, int[] corners, int[] adjacent)
        {
            Center = center; Corners = corners; Adjacent = adjacent;
        }

        public bool IsPentagon { get { return Corners.Length == 5; } }
    }

    /// <summary>
    /// The world: a Goldberg polyhedron GP(f,0), which is the DUAL of a frequency-f subdivided
    /// icosahedron. Tiles are the triangle mesh's vertices; tile corners are its triangle centroids.
    /// At f = 4 that is 162 tiles - 12 pentagons and 150 hexagons. Never assume a hexagon.
    ///
    /// The whole thing is DETERMINISTIC from f, which is the reason a campaign save is three
    /// kilobytes: it stores the tile-to-territory assignment and rebuilds the sphere on load. That
    /// only holds while the tile INDEX ORDER is stable, and the index order is the order vertices
    /// are first seen while walking the 20 icosahedron faces in their listed order. Do not
    /// reorder the face table, and do not change how vertices are welded, or every save ever
    /// written points at different ground.
    /// </summary>
    public sealed class HexSphere
    {
        public const int DefaultFrequency = 4;

        public int Frequency { get; private set; }
        public HexTile[] Tiles { get; private set; }
        public Vec3[] Corners { get; private set; }

        public static int TileCount(int f) { return 10 * f * f + 2; }

        static readonly Dictionary<int, HexSphere> _cache = new Dictionary<int, HexSphere>();

        public static HexSphere Get(int frequency)
        {
            HexSphere s;
            if (_cache.TryGetValue(frequency, out s)) return s;
            s = Build(frequency);
            _cache[frequency] = s;
            return s;
        }

        // the 12 icosahedron vertices, from the golden ratio
        static Vec3[] IcosaVerts()
        {
            double p = (1.0 + Math.Sqrt(5.0)) / 2.0;
            return new[]
            {
                new Vec3(-1, p, 0), new Vec3(1, p, 0), new Vec3(-1, -p, 0), new Vec3(1, -p, 0),
                new Vec3(0, -1, p), new Vec3(0, 1, p), new Vec3(0, -1, -p), new Vec3(0, 1, -p),
                new Vec3(p, 0, -1), new Vec3(p, 0, 1), new Vec3(-p, 0, -1), new Vec3(-p, 0, 1),
            };
        }

        /// <summary>The 20 faces, in THIS order. It decides tile indices, and tile indices are in
        /// every save file.</summary>
        static readonly int[][] IcosaFaces =
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
        };

        static HexSphere Build(int f)
        {
            if (f < 1) throw new ArgumentOutOfRangeException("f");

            var iv = IcosaVerts();
            var verts = new List<Vec3>();
            var weld = new Dictionary<long, int>();     // quantised position -> vertex index

            Func<Vec3, int> addVertex = v =>
            {
                v = v.Normalized;
                long key = WeldKey(v);
                int idx;
                if (weld.TryGetValue(key, out idx)) return idx;
                idx = verts.Count;
                verts.Add(v);
                weld[key] = idx;
                return idx;
            };

            var tris = new List<int[]>();

            foreach (var face in IcosaFaces)
            {
                var a = iv[face[0]].Normalized;
                var b = iv[face[1]].Normalized;
                var c = iv[face[2]].Normalized;

                // a triangular lattice over the face; row i has i+1 points
                var grid = new int[f + 1][];
                for (int i = 0; i <= f; i++)
                {
                    grid[i] = new int[i + 1];
                    for (int j = 0; j <= i; j++)
                    {
                        double s = (double)i / f;
                        double t = i == 0 ? 0.0 : (double)j / f;
                        var p = a + (b - a) * s + (c - b) * t;
                        grid[i][j] = addVertex(p);
                    }
                }

                for (int i = 1; i <= f; i++)
                    for (int j = 0; j < i; j++)
                    {
                        tris.Add(new[] { grid[i - 1][j], grid[i][j], grid[i][j + 1] });
                        if (j < i - 1) tris.Add(new[] { grid[i - 1][j], grid[i][j + 1], grid[i - 1][j + 1] });
                    }
            }

            // a tile corner is a triangle's centroid, pushed back out to the unit sphere
            var corners = new Vec3[tris.Count];
            for (int t = 0; t < tris.Count; t++)
            {
                var tri = tris[t];
                var sum = verts[tri[0]] + verts[tri[1]] + verts[tri[2]];
                corners[t] = (sum * (1.0 / 3.0)).Normalized;
            }

            int n = verts.Count;
            var inc = new List<int>[n];
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) { inc[i] = new List<int>(6); adj[i] = new List<int>(6); }

            for (int t = 0; t < tris.Count; t++)
            {
                var tri = tris[t];
                for (int k = 0; k < 3; k++)
                {
                    inc[tri[k]].Add(t);
                    // insertion order matters: the JS uses a Set, which iterates in insertion
                    // order, and the map's BFS floods follow that order
                    AddOnce(adj[tri[k]], tri[(k + 1) % 3]);
                    AddOnce(adj[tri[k]], tri[(k + 2) % 3]);
                }
            }

            var tiles = new HexTile[n];
            for (int vi = 0; vi < n; vi++)
            {
                var c = verts[vi];

                // a tangent basis at the tile, so its corners can be sorted by angle around it
                var seed = Math.Abs(c.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
                var u = Vec3.Cross(c, seed).Normalized;
                var v = Vec3.Cross(c, u);

                var ring = inc[vi].ToArray();
                var angle = new double[ring.Length];
                for (int i = 0; i < ring.Length; i++)
                {
                    var p = corners[ring[i]];
                    angle[i] = Math.Atan2(Vec3.Dot(p, v), Vec3.Dot(p, u));
                }
                Array.Sort(angle, ring);                       // CCW seen from outside

                tiles[vi] = new HexTile(c, ring, adj[vi].ToArray());
            }

            return new HexSphere { Frequency = f, Tiles = tiles, Corners = corners };
        }

        static void AddOnce(List<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return;
            list.Add(value);
        }

        /// <summary>
        /// The weld key that shares a vertex between two icosahedron faces.
        ///
        /// The JS welds on the STRING `x.toFixed(6),y.toFixed(6),z.toFixed(6)`, which the spec
        /// says not to reproduce, and it is right: that key makes -1e-9 and +1e-9 different
        /// vertices ("-0.000000" vs "0.000000") and would silently produce a sphere with the wrong
        /// number of tiles. Quantising to a micro-unit lattice welds the same pairs and cannot
        /// split a zero.
        /// </summary>
        static long WeldKey(Vec3 v)
        {
            long qx = (long)Math.Round(v.X * 1e6, MidpointRounding.AwayFromZero);
            long qy = (long)Math.Round(v.Y * 1e6, MidpointRounding.AwayFromZero);
            long qz = (long)Math.Round(v.Z * 1e6, MidpointRounding.AwayFromZero);
            unchecked
            {
                long h = 17;
                h = h * 1000003L + qx;
                h = h * 1000003L + qy;
                h = h * 1000003L + qz;
                return h;
            }
        }

        /// <summary>The yaw/pitch that put a direction dead centre, facing the viewer.</summary>
        public static void AimAt(Vec3 c, out double yaw, out double pitch)
        {
            yaw = Math.Atan2(-c.X, c.Z);
            double z1 = -c.X * Math.Sin(yaw) + c.Z * Math.Cos(yaw);
            pitch = Math.Atan2(c.Y, z1);
        }
    }
}
