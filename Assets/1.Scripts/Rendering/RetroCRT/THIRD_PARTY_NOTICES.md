# Cyanilux Retro CRT Shader

Original project: https://github.com/Cyanilux/URP_RetroCRTShader
Original author: Cyanilux. Copyright (c) 2020 Cyanilux.
License: MIT; the complete upstream notice is preserved in `LICENSE.md`.
Pinned revision: `ccd515c616f409e7aabda4fd02a46fa97317de10` (2025-09-27).
Original graph: https://github.com/Cyanilux/URP_RetroCRTShader/blob/ccd515c616f409e7aabda4fd02a46fa97317de10/Retro/Retro.shadergraph

## Local adaptation (2026-09-14)

- Imported the original `Retro.shadergraph` as `Shaders/CyaniluxRetroCRT.shadergraph` with a fresh asset GUID.
- Following the upstream README, replaced its external render-texture Sample Texture 2D input with URP Sample Buffer / Blit Source. The node type and input/output slot serialization match the installed URP 17.3 Fullscreen Basic URP template and UniversalSampleBufferNode implementation.
- Reconnected the original distorted UV to Sample Buffer input 0 and its color output 2 to the existing effect chain. Removed only the obsolete Main Texture property, property node, unused texture-sampling slots, and their references.
- Preserved upstream CRT distortion, edge masking, scanline, RGB stripe generation, noise, brightness, color-conversion calculations, and shader-feature keywords. Added the exposed `_RGBStripeStrength` range (0 to 1, graph default 1) only between the original RGB mask and the RGB keyword On branch: `lerp(white, originalRGBMask, strength)`. Strength 1 preserves the upstream full-strength mask; strength 0 makes that mask neutral. This blends mask colors only, never the original and warped images, and adds no camera-color sample.
- Authored `Assets/99.Settings/CyaniluxRetroCRT.mat` with CRT warp and RGB stripes enabled, weak scanlines, and static, scrolling static, and image distortion disabled. After inspecting the map, reduced warp to (0.035, 0.035) and RGB stripe strength to 0.15 so the full-strength mask does not dominate gameplay. This saved material retains the two enabled keywords for build variant discovery. Runtime switching toggles the complete effect, not shader keywords.
- The Cyanilux effect reads the current full-resolution camera color; no low-resolution render texture, sample camera, sample scene, demo controller, or upstream renderer/pipeline settings were imported.

## Host-project controls (2026-09-15)

- Restored the host project's prior `PixelScanline.shader` calculation as an optional pass before the Cyanilux effect, with independent serialized pixelation and scanline controls on `RetroCRTController`. These controls default to off and are separate from the upstream graph's own scanlines. `PixelScanline.shader` and `PixelScanlinePass.cs` are host-project code, not imported Cyanilux code.
- Added `_BezelSize` (0 to 3, default 1) to scale only the Rounded Rectangle mask UV around the screen center. Size 1 preserves the original mask calculation; size 0 removes that mask. Camera-image curvature is controlled by the existing upstream warp strength. A local function clamps the Blit Source sampling UV to valid pixel centers so reducing the bezel does not reveal out-of-range buffer reads. This adds no texture samples or shader keywords. The scene controller supplies the size through a per-pass MaterialPropertyBlock without modifying the shared material asset.

Shader import, rendered appearance, and performance depend on the host project's Unity/URP configuration and are validated separately from this source adaptation.
