using System.Collections.Generic;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.Rules;
using UnityEngine;

namespace SpawnRowDuel.View.Campaign
{
    /// <summary>
    /// The world, as an actual sphere of tiles you can spin with a finger.
    ///
    /// The JS drew this with an orthographic canvas projection, painter-sorted quads, a hand-rolled
    /// light and an inverse-ray pick that had to be corrected against the extruded radius because
    /// dividing by the plain one mis-picked about one tap in seven. None of that survives: a
    /// prism mesh, a z-buffer, a real light and a raycast do the same job and cannot drift out of
    /// agreement with each other.
    ///
    /// What DOES survive is the feel - drag to spin with inertia, a pitch clamp so the poles never
    /// tumble past vertical, and an idle spin that starts a couple of seconds after you let go.
    ///
    /// One mesh, not 162. A tile is a fan of triangles for its top face plus a skirt down to the
    /// sphere, and a baked triangle-to-tile table turns a RaycastHit back into ground.
    /// </summary>
    public sealed class GlobeView : MonoBehaviour
    {
        public const float Radius = 1f;
        public const float Extrude = 1.05f;      // the top face sits slightly proud of the sphere
        public const float Inset = 0.93f;        // corners pulled in, so tiles read as tiles

        /// <summary>
        /// The CRUST: how dark the ground under the plates is, as a fraction of its tile's colour.
        ///
        /// Without it the world is hollow, and visibly so. The plates are inset by 7%, so there is
        /// a gap along every edge, and behind that gap was the skybox - you could see space
        /// through the planet, and its silhouette was a ragged fringe of loose tiles rather than a
        /// horizon. The dual's tiles cover the sphere EXACTLY, so the same fan drawn at full width
        /// and without the extrusion closes it.
        ///
        /// It is part of the TILE MESH rather than a second renderer under it. Two renderers
        /// sharing one material and one bounds centre do not reliably sort against each other -
        /// the crust drew over the plates it was supposed to be beneath - and a mesh cannot
        /// disagree with itself about depth. It also keeps the globe at one draw call.
        ///
        /// Each tile's crust carries that tile's own colour, deeply shaded, so a chasm between two
        /// Fire plates is a Fire chasm: depth reads as depth, where flat black would read as
        /// another hole.
        /// </summary>
        public const float CoreShade = 0.22f;

        public Material TileMaterial;            // vertex-coloured, unlit
        public Material BorderMaterial;

        /// <summary>
        /// Drag feel, ported from the browser build (spec 08 §11.5).
        ///
        /// The MAGNITUDES came from there. The SIGNS could not, and that was the bug: the browser
        /// viewer sits on the +Z side of its globe and this camera sits at (0,0,-3.9) looking the
        /// other way, so "toward the viewer" changed sign while screen-right did not, and every
        /// horizontal drag spun the planet the wrong way. See <see cref="Orientation"/>.
        /// </summary>
        const float YawPerPixel = 0.005f, PitchPerPixel = 0.005f, PitchClamp = 1.25f;
        const float InertiaSeed = 0.0009f, InertiaDecay = 0.93f;
        const float IdleDelay = 2.6f, IdleSpin = 0.0011f;
        const float DragSlopMouse = 7f, DragSlopTouch = 15f;

        HexSphere _sphere;
        CampaignMap _map;
        Element _faction;

        Mesh _tileMesh, _borderMesh;
        MeshRenderer _tileRenderer, _borderRenderer;
        int[] _triToTile;
        int[] _tileVertexStart, _tileVertexCount;
        Color[] _colors;

        float _yaw, _pitch, _vyaw, _idleSince;
        bool _dragging, _moved;
        Vector2 _dragFrom;
        int _pointerId = -1;

        /// <summary>Set by the map screen: taps land here as territory ids.</summary>
        public System.Action<int> OnTerritoryPicked;

        /// <summary>Territory the pointer is over, or -1.</summary>
        public int Hover { get; private set; }

        public Camera Cam { get; set; }

        /// <summary>The built globe, for the winding test - see PresentationTests.</summary>
        public Mesh TileMesh { get { return _tileMesh; } }

        /// <summary>Which tile each triangle of <see cref="TileMesh"/> belongs to - the same table
        /// <see cref="Pick"/> reads a RaycastHit through.</summary>
        public int[] TriangleTiles { get { return _triToTile; } }

        void Awake() { Hover = -1; }

        // ── build ───────────────────────────────────────────────────────────────────────

        public void Build(CampaignMap map, Element faction)
        {
            _map = map;
            _faction = faction;
            _sphere = map.Sphere;

            if (_tileMesh == null) BuildTileMesh();
            BuildBorderMesh();
            Recolour();
        }

        void BuildTileMesh()
        {
            var tiles = _sphere.Tiles;
            var corners = _sphere.Corners;

            var verts = new List<Vector3>(tiles.Length * 13);
            var cols = new List<Color>(tiles.Length * 13);
            var tris = new List<int>(tiles.Length * 36);
            var triTile = new List<int>(tiles.Length * 12);

            _tileVertexStart = new int[tiles.Length];
            _tileVertexCount = new int[tiles.Length];

            for (int t = 0; t < tiles.Length; t++)
            {
                var tile = tiles[t];
                int ring = tile.Corners.Length;
                int start = verts.Count;
                _tileVertexStart[t] = start;

                var c = ToUnity(tile.Center);

                // Four rings per tile, in this order - Recolour and the skirt/crust shading both
                // count on it:
                //   [0]              the plate's centre, extruded
                //   [1 .. ring]      the plate's inset rim, extruded
                //   [ring+1 .. 2r]   the same rim dropped to the sphere - the plate's SIDE
                //   [2r+1]           the crust's centre, on the sphere
                //   [2r+2 .. 3r+1]   the crust's rim at FULL width, on the sphere
                verts.Add(c * Extrude);
                for (int i = 0; i < ring; i++)
                {
                    var p = ToUnity(corners[tile.Corners[i]]);
                    var insetDir = Vector3.Lerp(p, c, 1f - Inset).normalized;
                    verts.Add(insetDir * Extrude);
                }
                for (int i = 0; i < ring; i++)
                {
                    var p = ToUnity(corners[tile.Corners[i]]);
                    var insetDir = Vector3.Lerp(p, c, 1f - Inset).normalized;
                    verts.Add(insetDir * Radius);
                }
                verts.Add(c * Radius);
                for (int i = 0; i < ring; i++)
                    verts.Add(ToUnity(corners[tile.Corners[i]]) * Radius);

                _tileVertexCount[t] = verts.Count - start;
                int crust = start + 1 + 2 * ring;              // the crust's centre vertex

                // BOTTOM-UP, and that order is load-bearing: crust, then the plate's side, then
                // its face. The globe is drawn back-to-front rather than depth-sorted - see the
                // shader - so within a tile the last thing written wins, and a crust emitted after
                // the face paints over the face it is supposed to lie under.
                for (int i = 0; i < ring; i++)
                {
                    // the crust, wound like the plate's face so it faces out of the sphere too.
                    // Its triangles map to this tile as well, so a tap that lands in a chasm picks
                    // the ground it fell on rather than nothing.
                    int a3 = crust + 1 + i;
                    int b3 = crust + 1 + (i + 1) % ring;
                    tris.Add(crust); tris.Add(a3); tris.Add(b3);
                    triTile.Add(t);
                }

                for (int i = 0; i < ring; i++)
                {
                    int a = start + 1 + i;
                    int b = start + 1 + (i + 1) % ring;
                    int a2 = start + 1 + ring + i;
                    int b2 = start + 1 + ring + (i + 1) % ring;
                    tris.Add(a); tris.Add(a2); tris.Add(b);
                    triTile.Add(t);
                    tris.Add(b); tris.Add(a2); tris.Add(b2);
                    triTile.Add(t);
                }

                for (int i = 0; i < ring; i++)
                {
                    int a = start + 1 + i;
                    int b = start + 1 + (i + 1) % ring;
                    tris.Add(start); tris.Add(a); tris.Add(b);
                    triTile.Add(t);
                }
            }

            _tileMesh = new Mesh { name = "SRD Globe", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _tileMesh.SetVertices(verts);
            _tileMesh.SetTriangles(tris, 0);
            _colors = new Color[verts.Count];
            _tileMesh.RecalculateNormals();
            _tileMesh.RecalculateBounds();
            _triToTile = triTile.ToArray();

            var go = new GameObject("Tiles");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _tileMesh;
            _tileRenderer = go.AddComponent<MeshRenderer>();
            _tileRenderer.sharedMaterial = TileMaterial;
            _tileRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>().sharedMesh = _tileMesh;
        }

        /// <summary>
        /// Borders, as thin quads laid over the shared edge of two tiles in different territories.
        ///
        /// Adjacent tiles share exactly two corners - that is a property of the dual, and it is
        /// what lets an edge be drawn once rather than twice with z-fighting between the halves.
        /// </summary>
        void BuildBorderMesh()
        {
            var tiles = _sphere.Tiles;
            var corners = _sphere.Corners;

            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();

            var gold = new Color(0.851f, 0.714f, 0.290f);
            var pale = new Color(0.957f, 0.941f, 1f, 0.85f);
            var dark = new Color(0f, 0f, 0f, 0.35f);

            for (int i = 0; i < tiles.Length; i++)
            {
                foreach (int j in tiles[i].Adjacent)
                {
                    if (j < i) continue;
                    int ta = _map.TileTerritory[i], tb = _map.TileTerritory[j];
                    if (ta == tb) continue;

                    var oa = _map.Of(ta).Owner;
                    var ob = _map.Of(tb).Owner;

                    Color col; float width;
                    if (oa == ob) { col = dark; width = 0.006f; }
                    else if (oa == _faction || ob == _faction) { col = gold; width = 0.016f; }
                    else { col = pale; width = 0.011f; }

                    int c0 = -1, c1 = -1;
                    foreach (int a in tiles[i].Corners)
                        foreach (int b in tiles[j].Corners)
                            if (a == b) { if (c0 < 0) c0 = a; else c1 = a; }
                    if (c1 < 0) continue;

                    var p0 = ToUnity(corners[c0]) * (Extrude + 0.004f);
                    var p1 = ToUnity(corners[c1]) * (Extrude + 0.004f);
                    var mid = ((p0 + p1) * 0.5f).normalized;
                    var side = Vector3.Cross((p1 - p0).normalized, mid) * width;

                    int b0 = verts.Count;
                    verts.Add(p0 - side); verts.Add(p0 + side);
                    verts.Add(p1 + side); verts.Add(p1 - side);
                    for (int k = 0; k < 4; k++) cols.Add(col);
                    tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                    tris.Add(b0); tris.Add(b0 + 2); tris.Add(b0 + 3);
                }
            }

            if (_borderMesh == null)
            {
                _borderMesh = new Mesh { name = "SRD Globe Borders", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var go = new GameObject("Borders");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _borderMesh;
                _borderRenderer = go.AddComponent<MeshRenderer>();
                _borderRenderer.sharedMaterial = BorderMaterial != null ? BorderMaterial : TileMaterial;
                _borderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            _borderMesh.Clear();
            _borderMesh.SetVertices(verts);
            _borderMesh.SetColors(cols);
            _borderMesh.SetTriangles(tris, 0);
            _borderMesh.RecalculateBounds();
        }

        /// <summary>Repaint ownership. Colours only - the topology never changes.</summary>
        public void Recolour()
        {
            for (int t = 0; t < _sphere.Tiles.Length; t++)
            {
                int tid = _map.TileTerritory[t];
                var terr = _map.Of(tid);
                var c = ElementColour(terr.Owner);

                // your own ground reads brighter, the way the browser build boosted it
                if (terr.Owner == _faction) c = Color.Lerp(c, Color.white, 0.30f);
                if (tid == Hover) c = Color.Lerp(c, Color.white, 0.35f);

                int start = _tileVertexStart[t];
                int ring = _sphere.Tiles[t].Corners.Length;

                var side = c * 0.42f;                  // the plate's edge: in shadow, not its face
                var deep = c * CoreShade;              // the crust: a chasm, not a hole
                side.a = 1f;
                deep.a = 1f;

                _colors[start] = c;
                for (int i = 0; i < ring; i++) _colors[start + 1 + i] = c;
                for (int i = 0; i < ring; i++) _colors[start + 1 + ring + i] = side;

                int crust = start + 1 + 2 * ring;
                for (int i = 0; i <= ring; i++) _colors[crust + i] = deep;
            }
            _tileMesh.SetColors(_colors);
            BuildBorderMesh();
        }

        public static Color ElementColour(Element el)
        {
            switch (el)
            {
                case Element.Fire: return Hex("#e0613f");
                case Element.Water: return Hex("#3fa3e0");
                case Element.Earth: return Hex("#c0863c");
                case Element.Wind: return Hex("#48c9c0");
                case Element.Forest: return Hex("#5fbf6a");
                case Element.Electric: return Hex("#e3c93f");
                case Element.Light: return Hex("#e8dfa8");
                case Element.Dark: return Hex("#9a5cc6");
                default: return Hex("#6a6a76");
            }
        }

        static Color Hex(string s)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(s, out c) ? c : Color.grey;
        }

        static Vector3 ToUnity(Vec3 v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }

        // ── camera aim ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Spin so a tile faces the viewer - used to open on your own capital.
        ///
        /// <see cref="HexSphere.AimAt"/> is ported JS maths and solves for the browser's frame,
        /// where the viewer is on the +Z side: it hands back the pair that puts the tile at
        /// (0,0,+1). This camera is at (0,0,-3.9), so the same pair aimed the campaign at the
        /// ANTIPODE of your capital - the map opened on the far side of the planet with the
        /// capital's own pin hidden by <see cref="AnchorFacing"/>.
        ///
        /// Corrected here rather than in HexSphere, because HexSphere is parity code in the rules
        /// assembly and the frame mismatch is a view concern. Half a turn of yaw walks the tile
        /// round to the near side and the pitch negates with it; both are exact, not a fudge -
        /// with R = Rx(p)*Ry(y), y = yaw+PI sends the centre to (0, cy, -r) and p = -pitch sends
        /// that to (0,0,-1), dead centre and facing the camera.
        /// </summary>
        public void AimAt(int tileIndex)
        {
            double yaw, pitch;
            HexSphere.AimAt(_sphere.Tiles[tileIndex].Center, out yaw, out pitch);
            _yaw = (float)yaw + Mathf.PI;
            _pitch = -(float)pitch;
            _vyaw = 0f;
            Apply();
        }

        /// <summary>
        /// Yaw and pitch as one rotation: pitch about WORLD X, yaw about the globe's own pole.
        ///
        /// The order is the whole point, and it is why this is not a Quaternion.Euler triple.
        /// Euler(pitch, yaw, 0) composes as Ry(yaw)*Rx(pitch), which makes yaw the outer rotation
        /// and leaves PITCH turning about the globe's own X - an axis the yaw drags round with it.
        /// A quarter turn away from home a vertical drag was pure ROLL, and half a turn away it
        /// was exactly backwards, which is what "often seems to rotate the opposite direction"
        /// was: not a constant inversion, a response that changed with where you had spun to.
        ///
        /// Rx(pitch)*Ry(yaw) pins the pitch axis to the screen's horizontal for good, and leaves
        /// yaw spinning the globe about its own (already tilted) pole - a turntable, which is what
        /// a planet is. Unity applies the RIGHT operand first, so this reads back to front.
        /// </summary>
        public static Quaternion Orientation(float yaw, float pitch)
        {
            return Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.right)
                 * Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up);
        }

        /// <summary>
        /// One frame of drag, as DIRECT MANIPULATION: the ground under the finger goes where the
        /// finger goes, at every orientation. Static and by-ref so the invariant can be tested
        /// without a scene - see GlobeDragTests.
        ///
        /// Both signs are negatives of the browser's because the camera is on the other side of
        /// the planet from the browser's viewer; the vertical had accidentally survived that flip
        /// in the port because Unity's mouse Y counts upward where the browser's clientY counts
        /// down, and the two inversions cancelled.
        /// </summary>
        public static void Drag(Vector2 delta, ref float yaw, ref float pitch)
        {
            yaw -= delta.x * YawPerPixel;
            pitch += delta.y * PitchPerPixel;
            pitch = Mathf.Clamp(pitch, -PitchClamp, PitchClamp);
        }

        void Apply()
        {
            _pitch = Mathf.Clamp(_pitch, -PitchClamp, PitchClamp);
            transform.rotation = Orientation(_yaw, _pitch);
        }

        // ── input ───────────────────────────────────────────────────────────────────────

        /// <summary>Driven by the map screen, so the globe never steals a tap meant for the HUD.</summary>
        public void Tick(bool inputAllowed)
        {
            if (_sphere == null) return;

            if (inputAllowed) ReadPointer();
            else if (_dragging) { _dragging = false; _pointerId = -1; }

            if (!_dragging)
            {
                _yaw += _vyaw;
                _vyaw *= InertiaDecay;
                if (Mathf.Abs(_vyaw) < 1e-5f) _vyaw = 0f;

                // Negated with the drag, so the planet drifts the way a flick would send it and
                // the way a planet turns: the ground travels left to right across the frame.
                if (Time.unscaledTime - _idleSince > IdleDelay) _yaw -= IdleSpin;
            }
            Apply();
        }

        void ReadPointer()
        {
            bool down = Input.GetMouseButtonDown(0);
            bool held = Input.GetMouseButton(0);
            bool up = Input.GetMouseButtonUp(0);
            Vector2 pos = Input.mousePosition;

            if (down && !_dragging)
            {
                _dragging = true;
                _moved = false;
                _dragFrom = pos;
                _idleSince = Time.unscaledTime;
                _pointerId = Input.touchCount > 0 ? Input.GetTouch(0).fingerId : -1;
            }
            else if (_dragging && held)
            {
                // a second finger must not hijack the drag or reset the travel into a false tap
                if (_pointerId >= 0 && Input.touchCount > 0 && Input.GetTouch(0).fingerId != _pointerId) return;

                var d = pos - _dragFrom;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) > (_pointerId >= 0 ? DragSlopTouch : DragSlopMouse))
                    _moved = true;

                Drag(d, ref _yaw, ref _pitch);
                _vyaw = -d.x * InertiaSeed;      // the flick carries the drag's sign
                _dragFrom = pos;
                _idleSince = Time.unscaledTime;
            }
            else if (_dragging && up)
            {
                _dragging = false;
                _idleSince = Time.unscaledTime;
                if (!_moved)
                {
                    int tid = Pick(pos);
                    if (tid >= 0 && OnTerritoryPicked != null) OnTerritoryPicked(tid);
                }
            }

            if (!_dragging)
            {
                int over = Pick(pos);
                if (over != Hover) { Hover = over; Recolour(); }
            }
        }

        /// <summary>Screen point to territory, or -1. A raycast, and nothing to correct.</summary>
        public int Pick(Vector2 screenPos)
        {
            if (Cam == null || _triToTile == null) return -1;
            RaycastHit hit;
            if (!Physics.Raycast(Cam.ScreenPointToRay(screenPos), out hit, 100f)) return -1;
            if (hit.collider == null || hit.collider.gameObject != _tileRenderer.gameObject) return -1;
            int tri = hit.triangleIndex;
            if (tri < 0 || tri >= _triToTile.Length) return -1;
            return _map.TileTerritory[_triToTile[tri]];
        }

        /// <summary>Where a territory's marker should be drawn, in world space.</summary>
        public Vector3 AnchorWorld(int territoryId)
        {
            var t = _map.Of(territoryId);
            if (t == null) return Vector3.zero;
            return transform.TransformPoint(ToUnity(_sphere.Tiles[t.AnchorTile].Center) * (Extrude + 0.02f));
        }

        /// <summary>True when the anchor is on the near side and worth labelling.</summary>
        public bool AnchorFacing(int territoryId)
        {
            var p = AnchorWorld(territoryId) - transform.position;
            var toCam = Cam != null ? (Cam.transform.position - transform.position) : Vector3.back;
            return Vector3.Dot(p.normalized, toCam.normalized) > 0.18f;
        }
    }
}
