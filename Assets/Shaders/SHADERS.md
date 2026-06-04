# Shader & Visual Feedback System

Toon (cel) shading + outline + proximity dither + hit feedback for a URP project.
All shaders are **pure ASCII** (Unity's HLSL compiler rejects non-ASCII characters,
even in comments) and use **custom ShadowCaster/DepthOnly passes** (never `UsePass`
of URP/Lit — that imports 65 incompatible keywords).

---

## Files

| File | Type | Purpose |
|------|------|---------|
| `CelShadingCore.hlsl` | Include | Shared `CelStep` + `BayerDither4x4` helpers |
| `CelShading.shader` | Shader | Toon shading for enemies / props / environment |
| `PlayerProximity.shader` | Shader | Toon shading + proximity dither for the player |
| `ScreenSpaceOutline.shader` | Shader | Fullscreen post-process outline (any mesh, no setup) |
| `SmoothedNormalsBaker.cs` | Editor tool | (Optional) bakes smooth normals for per-material outline |

The proximity fade is **self-contained in the shader** (measures pivot→camera
distance) — no script needed. C# drivers (all optional, in `Assets/Scripts/Combat/`):

| Script | Drives | Where it goes |
|--------|--------|---------------|
| `HitReaction.cs` | `_HitAlpha`, `_HitFlashAmount` | Player (has `HealthSystem`) |
| `HitFlash.cs` | `_HitFlashAmount`, `_HitFlashColor` | Enemies (any `HealthSystem`) |
| `CameraShake.cs` | camera transform | Camera GameObject |

---

## Shader properties (cel shaders)

| Property | Range | Meaning |
|----------|-------|---------|
| `_BaseMap` / `_BaseColor` | texture / color | Albedo |
| `_ShadowColor` | color | Tint of unlit areas (bluish = depth) |
| `_Steps` | 1–8 | Number of shading bands (2 = classic Zelda) |
| `_StepSmooth` | 0–0.49 | Band edge softness (0 = hard) |
| `_Threshold` | 0–1 | Where the light/shadow edge sits (0.5 neutral) |
| `_RimPower` | 0–16 | Rim light tightness (0 = off) |
| `_RimColor` | color | Rim tint |
| `_OutlineOn` | toggle | **Per-material** outline (default OFF — use ScreenSpaceOutline) |
| `_OutlineWidth` | 0–0.05 | Outline thickness (screen-space) |
| `_OutlineColor` | color | Outline color |
| `_HitFlashColor` | color | Hit flash tint (driven by script) |
| `_HitFlashAmount` | 0–1 | 0 = normal, 1 = full flash color (driven by script) |
| `_HitAlpha` | 0–1 | 1 = visible, 0 = dithered out (optional script override) |
| `_FadeStart` | 0–12 | *(PlayerProximity)* distance at which the player is fully visible |
| `_FadeEnd` | 0–12 | *(PlayerProximity)* distance at which the player is fully dithered out |
| `_ProximityAlpha` | 0–1 | *(PlayerProximity)* optional extra fade multiplier (default 1) |

> **PlayerProximity is self-contained**: it computes pivot→camera distance in the
> shader and fades between `_FadeStart` (visible) and `_FadeEnd` (invisible) with no
> script. `_ProximityAlpha`, `_HitAlpha`, `_HitFlashAmount` remain as optional
> overrides a script may drive via MaterialPropertyBlock. Final visibility =
> `min(AutoProximity * _ProximityAlpha, _HitAlpha)`.

---

## How the dither works

Both `_HitAlpha` and `_ProximityAlpha` feed an ordered **Bayer 4×4** screen-door clip:

```hlsl
float a = min(_ProximityAlpha, _HitAlpha);   // most transparent wins
clip(BayerDither4x4(IN.posCS.xy) - (1.0 - a));
```

- `a = 1` → every pixel passes (fully visible)
- `a = 0` → every pixel clips (fully invisible)
- `a = 0.5` → half the pixels in a 4×4 cell clip (see-through stipple)

Because the threshold comes from **screen-pixel position** (`posCS.xy`), every mesh
of the player clips at the *same* pixels when fed the *same* `a`. That's why
`PlayerProximityFade` pushes ONE uniform value to all renderers — so the holes line
up and you never see inner geometry (sword, sub-meshes, back faces) through them.

The outline pass applies the **same clip** so the outline fades through the same
holes instead of filling them.

---

## Setup

### 1. Cel shading (enemies / props)
1. Create a Material → shader `Custom/CelShading`.
2. Assign texture + tune `_Steps`, `_ShadowColor`, `_RimPower`.
3. Assign to the mesh renderers.

### 2. Player shading + proximity fade (no script)
1. Material → shader `Custom/PlayerProximity` on **all** player meshes
   (body, weapon, sub-meshes). Mixed shaders = inner geometry shows through.
2. Set `_FadeStart` (far = visible) and `_FadeEnd` (close = invisible) on the
   material. The fade works on its own from the camera distance — no component.
   - For multi-part players, all meshes should ideally share the pivot (be
     parented under one root at the same origin) so they fade together. If a
     sub-mesh has a very different pivot, give it its own matching fade values.

### 3. Outline (per-material inverted hull)
Built into CelShading / PlayerProximity (Pass 0). A second pass renders back faces
(`Cull Front`) inflated along the normal by a constant WORLD-space width, so the
line thickness is fixed and shrinks naturally with distance (toon-shader standard).
No render feature, no depth texture.

- `Enable Outline`, `Outline Width (world units)` (~0.02), `Outline Color`.
- **ZTest LEqual**: the hull is occluded by closer solid geometry, so seams between
  touching / stacked objects are hidden - only the outer silhouette of the group
  is drawn, from the camera's viewpoint.

**Smooth normals are baked automatically (no gaps), for ANY mesh:**
- `OutlineNormals.cs` - a `[RuntimeInitializeOnLoadMethod]` that, after each scene
  loads, scans every renderer using a toon shader and bakes averaged smooth normals
  into UV3 of its mesh (clones shared meshes; caches so identical meshes bake once).
  Works on Unity primitives, imported models and procedural meshes alike.
- `Editor/OutlineNormalsPostprocessor.cs` - also bakes UV3 at import time for
  imported models (redundant with the runtime baker but cheap; keeps edit-mode
  previews correct). Exclude a model with the asset label `NoOutlineBake`.

The outline reads UV3 and falls back to the vertex normal if UV3 is empty. The
baking averages normals of co-located (split) vertices, which is what closes the
wedge-shaped gaps at UV/hard-edge seams.

> A screen-space (post-process) outline was attempted for true cross-object
> intersection lines, but URP 17 RenderGraph would not reliably bind the depth
> texture into a fullscreen blit (depth read back flat) and the depth-normals
> prepass conflicted with Forward+ (ZBinningJob). Reverted to the per-material
> hull, which is robust and conflict-free.

### 4. Hit feedback
- **Enemies**: add `HitFlash` (auto-added to enemies generated by the Enemy Creator).
  Needs a `Custom/CelShading` material to show the red tint.
- **Player**: add `HitReaction`. Set `HealthSystem.invincibilityDuration > 0`
  for the red↔invisible Zelda blink (uses `_HitAlpha` + `_HitFlashAmount`).
- **Camera**: add `CameraShake` to the Camera GameObject. Auto-shakes on the
  player giving/taking hits (compares by `transform.root`).

### 5. Shadow settings (URP Asset) - IMPORTANT for cel shading
Cel shading quantises lighting, so it turns soft shadow transitions into hard
edges. The shadow-cascade boundary and the shadow-distance fade (both spheres
centred on the camera) then show up as a visible disc that follows the camera.
This is NOT a shader bug - it's inherent to shadowmaps and is solved with config.

Tested-good settings in `PC_RP Asset -> Shadows`:
- **Cascade Count**: 2  (concentrates resolution near the camera, extends range,
  only one transition boundary which sits far away)
- **Max Distance**: 90
- **Shadow Resolution**: 4096
- **Normal Bias**: 0.05  (low = no peter-panning / light-grey contact halo)
- **Depth Bias**: 1      (counters acne without reintroducing the halo)
- **Last Border**: ~10   (softens the very end of the shadow range)

The shaders fold the cast shadow into the lambert term BEFORE the cel step
(`ramp = ndl * shadowAttenuation + ...`) so cast + form shadow are one band -
multiplying shadow AFTER the step made the cascade seam a second hard edge.

---

## Gotchas / lessons learned

These are the things that broke during development — do not reintroduce them:

1. **Non-ASCII characters** (`—`, `→`, `×`, box-drawing) anywhere in a `.shader`
   or `.hlsl` → `Parse error: unexpected $undefined`. Pure ASCII only.
2. **Punctuation in `[Header(...)]`** — a `-`, `(`, or `)` inside a `Header`
   string → `$undefined` parse error. Letters and spaces only.
3. **`UsePass "Universal Render Pipeline/Lit/ShadowCaster"`** imports URP/Lit's
   65 keywords → "incompatible keyword space". Write your own ShadowCaster/DepthOnly.
4. **`static const float arr[16] = {expr,...}` inside a function** → won't compile.
   Use a closed-form formula (see `BayerDither4x4`) or file-scope literals.
5. **`smooth` as an identifier** — reserved HLSL keyword. (We use `e` / `softness`.)
6. **`Blit.hlsl` include** doesn't exist in older URP. ScreenSpaceOutline builds its
   own fullscreen triangle from `SV_VertexID` instead.
7. **Redeclaring `sampler_LinearClamp`** — already provided by `Core.hlsl`.
8. **`GetAdditionalLight(i, posCS)`** — must pass **world** position, not clip.
   And in **Forward+** a manual `for` loop is wrong; use `LIGHT_LOOP_BEGIN/END`.
9. **Per-vertex `shadowCoord`** interpolated over large triangles → shadow acne.
   Compute `TransformWorldToShadowCoord(posWS)` per-fragment.
10. **Per-fragment proximity distance** made front faces fade more than inner
    meshes → inner geometry visible. Measure from the object **pivot**
    (`TransformObjectToWorld(0)`) so the alpha is uniform across the whole mesh.
11. **Duplicate shader name** (`Custom/CelShading` in two files) → Unity picks one
    at random. Keep one file per shader name.
12. **Stray non-comment text in a `.hlsl`** (leftover notes before `#endif`) breaks
    the include → every dependent shader silently falls back → effects vanish.

---

## Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Dither does nothing | `CelShadingCore.hlsl` has a syntax error (check for stray text); or `_FadeStart` is huge so camera never gets in range |
| Player never fades near camera | `_FadeStart`/`_FadeEnd` too small (camera orbits farther than `_FadeStart`); raise `_FadeStart`. Or material isn't `PlayerProximity` |
| Inner geometry visible through holes | A player mesh uses a different shader, or a sub-mesh has a very different pivot (give it matching fade values) |
| Magenta / pink material | Shader failed to compile — check Console; usually non-ASCII or `[Header]` punctuation |
| Outline missing | ScreenSpaceOutline feature not added, or Depth Texture disabled in URP Asset |
| False outlines all over flat angled faces | Normal requirement not ticked on the feature (no normals texture); or lower `_DepthNormalThreshold` / raise `_DepthNormalThresholdScale` |
| Outline only on silhouette, no cube corners | Normal requirement not ticked, or raise `_NormalThreshold` sensitivity (lower the value) |
| Shadows have jagged edges | Enable Soft Shadows in URP Asset; raise light Normal Bias |
| Lights "from nowhere" | Forward+ with a manual light loop — must use `LIGHT_LOOP` macros (already fixed) |
| Enemy red flash invisible | Enemy material isn't `Custom/CelShading` (no `_HitFlashAmount`) |
