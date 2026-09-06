using NUnit.Framework;
using SpawnRowDuel.Campaign;
using SpawnRowDuel.View.Campaign;
using UnityEngine;

namespace SpawnRowDuel.Rules.Tests
{
    /// <summary>
    /// Which way the campaign globe turns, as arithmetic.
    ///
    /// This file exists because "the globe often seems to rotate the opposite direction I expect"
    /// is not one bug, it is three, and only one of them is a constant inversion. The signs were
    /// ported out of the browser build, whose viewer sits on the +Z side of its globe, into a
    /// camera at (0,0,-3.9) looking the other way - so every horizontal drag was backwards. And
    /// the rotation was assembled as Quaternion.Euler(pitch, yaw, 0), which composes as
    /// Ry(yaw)*Rx(pitch) and leaves the PITCH axis being dragged round by the yaw: correct at
    /// home, pure roll a quarter turn away, exactly inverted half a turn away.
    ///
    /// A screenshot cannot fail either of those and a dot product can. The invariant asserted
    /// here is DIRECT MANIPULATION - the ground under the finger goes where the finger goes - at
    /// enough orientations that a yaw-dependent axis cannot hide between them.
    ///
    /// The camera frame is the one SceneBootstrap builds: position (0,0,-3.9), rotation identity,
    /// so it looks along +Z with world +X to screen-right and world +Y to screen-up. That makes
    /// the near face of the globe -Z, and lets a world delta be read as a screen delta directly.
    /// </summary>
    public class GlobeDragTests
    {
        /// <summary>The point of the globe facing the camera, in world space.</summary>
        static readonly Vector3 UnderTheCursor = new Vector3(0f, 0f, -1f);

        /// <summary>Every orientation the drag has to behave the same at. The quarter-turn and
        /// half-turn entries are the ones the old Euler composition got wrong.</summary>
        static readonly float[] Yaws = { 0f, 0.6f, Mathf.PI * 0.5f, 2.4f, Mathf.PI, 4.2f, -Mathf.PI * 0.5f };
        static readonly float[] Pitches = { 0f, 0.5f, -0.5f, 1.1f, -1.1f };

        static Vector3 Grab(float yaw, float pitch)
        {
            // the material point of the globe that is currently under the cursor
            return Quaternion.Inverse(GlobeView.Orientation(yaw, pitch)) * UnderTheCursor;
        }

        [Test]
        public void DragRight_CarriesTheGroundRight_AtEveryOrientation()
        {
            foreach (var y in Yaws)
                foreach (var p in Pitches)
                {
                    var onGlobe = Grab(y, p);
                    float yaw = y, pitch = p;
                    GlobeView.Drag(new Vector2(20f, 0f), ref yaw, ref pitch);
                    var moved = GlobeView.Orientation(yaw, pitch) * onGlobe;

                    Assert.Greater(moved.x, UnderTheCursor.x + 1e-4f,
                        "drag right must carry the grabbed ground right at yaw " + y + " pitch " + p);
                }
        }

        [Test]
        public void DragLeft_CarriesTheGroundLeft_AtEveryOrientation()
        {
            foreach (var y in Yaws)
                foreach (var p in Pitches)
                {
                    var onGlobe = Grab(y, p);
                    float yaw = y, pitch = p;
                    GlobeView.Drag(new Vector2(-20f, 0f), ref yaw, ref pitch);
                    Assert.Less((GlobeView.Orientation(yaw, pitch) * onGlobe).x, UnderTheCursor.x - 1e-4f,
                        "drag left must carry the grabbed ground left at yaw " + y + " pitch " + p);
                }
        }

        /// <summary>
        /// The one that catches the Euler composition. A vertical drag has to tip the SAME way
        /// wherever you have spun to - under Ry(yaw)*Rx(pitch) it rolled at yaw 90 degrees and
        /// reversed at 180, which is what the player was reporting as "often".
        /// </summary>
        [Test]
        public void DragDown_CarriesTheGroundDown_AtEveryOrientation()
        {
            foreach (var y in Yaws)
                foreach (var p in new[] { 0f, 0.5f, -0.5f })
                {
                    var onGlobe = Grab(y, p);
                    float yaw = y, pitch = p;
                    GlobeView.Drag(new Vector2(0f, -20f), ref yaw, ref pitch);
                    var moved = GlobeView.Orientation(yaw, pitch) * onGlobe;

                    Assert.Less(moved.y, UnderTheCursor.y - 1e-4f,
                        "drag down must carry the grabbed ground down at yaw " + y + " pitch " + p);
                    Assert.Less(Mathf.Abs(moved.x), 1e-3f,
                        "...and a vertical drag must not spin the globe sideways at yaw " + y);
                }
        }

        /// <summary>
        /// A vertical drag near the pole must not tumble past it. The clamp is the reason the
        /// globe never ends up upside down, and it lives in Drag now rather than only in Apply.
        /// </summary>
        [Test]
        public void Pitch_IsClampedShortOfTheVertical()
        {
            float yaw = 0f, pitch = 0f;
            for (int i = 0; i < 400; i++) GlobeView.Drag(new Vector2(0f, 40f), ref yaw, ref pitch);
            Assert.LessOrEqual(pitch, 1.2501f, "the north pole never tips past the clamp");

            pitch = 0f;
            for (int i = 0; i < 400; i++) GlobeView.Drag(new Vector2(0f, -40f), ref yaw, ref pitch);
            Assert.GreaterOrEqual(pitch, -1.2501f, "and neither does the south");
        }

        // ── where the map opens ───────────────────────────────────────────────────────────

        /// <summary>
        /// AimAt has to put the tile in front of the camera, and the camera is at NEGATIVE z.
        ///
        /// HexSphere.AimAt is ported browser maths and solves for a viewer on the +Z side, so the
        /// pair it returns aimed the campaign at the exact antipode of the player's capital. The
        /// view corrects the frame; this is the assertion that says which way is forward.
        /// </summary>
        [Test]
        public void AimAt_PutsTheTileInFrontOfTheCamera_NotBehindIt()
        {
            var sphere = HexSphere.Get(4);          // the campaign's own GP(4,0), 162 tiles
            int checked_ = 0;

            for (int t = 0; t < sphere.Tiles.Length; t++)
            {
                var c = sphere.Tiles[t].Center;
                var centre = new Vector3((float)c.X, (float)c.Y, (float)c.Z);

                // A capital near a pole is clamped by the pitch limit and cannot come dead
                // centre; the rest must land exactly on the camera axis.
                if (Mathf.Abs(centre.y) > 0.90f) continue;
                checked_++;

                double yaw, pitch;
                HexSphere.AimAt(c, out yaw, out pitch);
                var aimed = GlobeView.Orientation((float)yaw + Mathf.PI, -(float)pitch) * centre;

                Assert.Less(aimed.z, 0f, "tile " + t + " opened on the far side of the planet");
                Assert.AreEqual(-1f, aimed.z, 1e-3f, "tile " + t + " is not dead centre");
                Assert.AreEqual(0f, aimed.x, 1e-3f);
                Assert.AreEqual(0f, aimed.y, 1e-3f);
            }

            Assert.Greater(checked_, 100, "the sphere should have plenty of non-polar tiles");
        }
    }
}
