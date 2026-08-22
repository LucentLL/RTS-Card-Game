using UnityEngine;

namespace SpawnRowDuel.View.Cards
{
    /// <summary>
    /// The one shared unlit sprite material every world-space card surface uses.
    ///
    /// A runtime SpriteRenderer defaults to the 2D renderer's Sprite-Unlit shader, which in a
    /// 3D URP scene has no light texture to sample and drew every blob shadow as a bright
    /// ellipse instead of a dark one. Sprites/Default is the plain multiply-by-vertex-colour
    /// path, and the scene's sprite anchor keeps it out of the WebGL stripper's way.
    ///
    /// ZWrite is off on that shader, so the plate, the ground shadow and the standee figure sort
    /// among themselves by sortingOrder alone - only the opaque cell cube depth-tests them, which
    /// is why every one of them still has to sit ABOVE the cell's 0.06 top face.
    /// </summary>
    public static class SpriteMat
    {
        static Material _mat;

        public static Material Unlit
        {
            get
            {
                if (_mat != null) return _mat;
                var shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    Debug.LogWarning("Sprites/Default was stripped - sprite surfaces fall back");
                    return null;                              // keep the engine default
                }
                _mat = new Material(shader) { name = "SRD Sprite", hideFlags = HideFlags.HideAndDontSave };
                return _mat;
            }
        }
    }
}
