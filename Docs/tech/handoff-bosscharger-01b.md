# Boss charger 01B model and effect fit

Date: 2026-10-24. Recipients: boss/prefab owner (경석), VFX owner (민경).

The four boss-room charging pylons now use the approved larger 01B model, including
the artist's added lower cylinder. Charge/discharge effects and hit boxes follow
that geometry. The whole lower section clears the socket when the pylon rises.

## Files and delivery

- Git: `Assets/0.Scenes/MainFlow/4.MapScene.unity` stores only the approved pylon/socket Y placement overrides.
- Git: `Assets/2.Prefabs/Environment/Layouts/Zones/bossroom.prefab` updates the four pylons' mesh/effect references, rise settings and existing BoxColliders.
- SVN **r512**: 26 new art files (including 14 `.meta` files) and two new folders, under `Assets/50.Art`:
  - `Environment/Models/Machinery/PowerUnits/BossCharger_01B/` and its folder `.meta`: upper/socket FBXs and importer metadata.
  - `VFX/Models/BossCharger_01B/` and its folder `.meta`: overlay source FBX plus core, coil and full-shell mesh assets.
  - `VFX/Common/Boss/Pylon/FX_PylonCharge_01B.prefab` and `FX_PylonDischarge_01B_Entry.asset`, with their `.meta` files.
  - `VFX/_Materials/Boss/M_Pylon_{CoreGlow,CoilFlow,ShellRim}_01B.mat`, with their `.meta` files.
  - `VFX/Shaders/BossCharger01B_Surface.shader` and its `.meta` file.
- `art-svn.json` pins r512; its 3,509 versioned `.meta` files were counted from the remote Art tree.

Sync the art working copy to the pinned SVN revision together with this Git change.
The new scene/prefab references require those versioned art assets and their GUIDs.

## Placement and behavior

| Setting | Value |
| --- | --- |
| Scene upper mesh local Y | `0.669` (all four) |
| Scene socket local Y | `0.67` (all four) |
| Pylon `riseHeight` | `2.020426` |
| Pylon `moveSpeed` | `4.040852` |
| Existing BoxCollider center | `(0, -1.009713, 0)` |
| Existing BoxCollider size | `(1.47, 2.019426, 1.47)` |

Rise/lower travel remains 0.5 seconds. At the raised position, the mesh and hit box
bottom meet the socket top at Y 0.67. Existing collider enable/disable behavior,
damage routing, network identity and other gameplay settings are preserved.

The main model keeps the existing Generic atlas/material. The fitted charge effect
uses three separate 01B materials (render queue 3001), with overlays approximately
1.5 mm above the model surface. Its shader copy fixes vertex/interpolated normal
handling; the original shared shader, materials and effects are unchanged.
The discharge entry uses Y offset -1.1315 and keeps the existing live duration of
2.5 seconds. The lower cylinder is included in the full-shell effect mesh.

## Verification

- User verified the updated model, rise behavior and hits in Play Mode; last acceptance: "피격확인함".
- Unity 6000.3.16f1: compilation completed with zero errors. The new effect shader was checked in the editor.
- Existing `Tools/Tests/보스 패턴 EditMode 테스트 실행`: **10 passed, 0 failed, 0 skipped** on 2026-10-24. These test electric-floor patterns; no dedicated pylon asset test currently exists.
- Direct asset checks verified the four model/effect references, mesh bounds, collider bounds, rise/socket alignment and material queues.
- Independent diff audit: removing the listed pylon edits reproduces the original scene and prefab. Runtime C# files are unchanged.
- Remote SVN r512 contains exactly the scoped 26 files and two folders. All 26 remote file SHA256 hashes match the approved local assets.
- This delivery does not claim a new MPPM session; network code and authority rules are unchanged.
