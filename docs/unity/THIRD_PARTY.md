# Third-party notices

Everything the Unity build ships that somebody else wrote, and what its licence asks of us.

Keep this file current: a licence obligation that only lives in a commit message is one nobody
will find at ship time.

---

## Dynamic 2D Grass — technique, ported

**Source:** https://github.com/jomoho/dynamic-2d-grass
**Authors:** Jomoho Games, based on original work by [Dylearn](https://github.com/Dylearn)
**Licence:** MIT (code / shaders). The project's art is CC BY 4.0 — **we ship none of it.**

Ported into `unity/Assets/Game/Shaders/SRD_Noise.hlsl`, `SRD_Grass.shader` and
`SRD_CloudShadow.shader`. No file was copied: the shaders are new HLSL for URP, and what came
across is the *method*, which is worth naming precisely because it is the part that makes the
effect work at all:

* **Dual scrolling noise.** One scrolling noise reads as a texture sliding past. Two of them,
  rotated a few degrees apart and scrolled at different rates and scales, multiply into a field
  with no visible period — wind that looks like weather rather than a loop.
* **A quantised clock with a per-blade phase.** Snapping the sway to ~7 steps a second is what
  makes it read as drawn rather than tweened; the per-blade phase offset is what stops the whole
  field stepping on the same frame, which just looks like dropped frames.
* **Shear from the base.** The blade's foot is pinned and only its tip moves, weighted by height
  along the quad, so a field bends instead of sliding.
* **Cloud shadows as a multiply pass** over the finished frame, contrast-shaped and domain-warped.

What deliberately did NOT come across: the chunk streaming, the terrain data texture and the
effector system. All three exist to serve a scrolling tilemap world, and this board is a fixed
7×5 under a fixed camera.

The MIT notice, in full:

> MIT License
>
> Copyright (c) 2026 Jomoho Games
> Based on original work by Dylearn
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The reference checkout lives outside the build at `dynamic-2d-grass-main*/` and is git-ignored.
