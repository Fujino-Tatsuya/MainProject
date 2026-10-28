# 공유 — 간파 빛 모임·적중 섬광 VFX, 덮어쓸 텍스처 칸, 소켓, 렌더링 레이어 비트 4 (할 일 132, 민경)

> 작성: 은희 레인(claude-alt), 2026-10-28. 브랜치 `feature/InterruptPresentation2`(단계3 `e634b139` · 단계4 `02e95946` · 단계5 `d04f23d4` · 단계6).
> 설계 원본: [interrupt-presentation.md §0](interrupt-presentation.md#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132). PR 없음 규칙에 따라 이 문서로 공유한다.

## 1. 새 VFX — 워커가 절차 생성으로 만든 처음 버전 (git, `Assets/5.VFX/Interrupt/`)

| 연출 | 언제·누가 보나 | 프리팹·재질 | 셰이더 |
|---|---|---|---|
| **빛 모임** — 흰 빛 기둥 + 사방에서 모여드는 입자 + 중심 빛 | 강 시도(성공 예측) 입력 순간 → 간파 애니 Hit. 오너 + 팀원 | `LightGather/FX_InterruptLightGather.prefab` · `M_InterruptLightGather_Pillar` · `M_InterruptLightGather_Mote` | `BeaverLobby/VFX/InterruptLightGather` |
| **월드 섬광**(적중 섬광 4단계) — 흰 중심·파란 번짐·광선·링·불티 | 성공 섬광 순간(성공 후 0.3초) 적중 지점. 모두 | `HitFlash/FX_InterruptHitFlash.prefab` · `M_InterruptHitFlash_Core` · `_Rays` · `_Ring` | `BeaverLobby/VFX/InterruptHitFlash` |
| **화면 섬광 오버레이**(1~3단계 + 4 걷힘) — 흰 폭발 / 빛 갈라짐+충격파 링 / 흰 반전+검은 집중선 | 강 시도 성공 때 오너 화면만, 0~0.4초 | `HitFlash/M_InterruptHitFlashOverlay.mat`(디렉터 `hitFlashOverlayMaterial` 칸) | `BeaverLobby/UI/InterruptHitFlashOverlay` |

- 파티클 셰이더 둘 다 **가산 언릿 + HDR `_Color`**(기본 (6,6,6) — 1 넘으면 블룸). 시간은 `_Time` 대신 파티클 나이로 움직여 파티클 시간 기준을 따른다.
- 빛 모임 시간 기준 = 프리팹 루트 `InterruptLightGatherVfx.timeBase`(기본 **실시간** — 강 슬로우 0.15 중에도 같은 속도). 월드 섬광은 실시간 파티클.
- 섬광 단계 길이(각 0.1초)·월드 섬광 크기(`hitFlashWorldScale`)·높이(`hitFlashHeightOffset` 1m)는 `NetworkManager.prefab › InterruptProductionDirector` 칸이다.
- `Assets/5.VFX/` 는 **git** 이다(SVN `Assets/50.Art/` 아님) — 손그림 텍스처를 넣으면 텍스처 파일 위치(SVN 아트)와 재질(git) 커밋을 맞출 것.

## 2. 덮어쓸 텍스처 칸 — 손그림으로 바꾸는 법

1. 재질(위 표)을 연다. 셰이더 GUI 맨 아래 헬프 박스에 지금 상태('절차 생성' / '덮어쓸 텍스처 사용')가 보인다.
2. 칸에 텍스처를 넣으면 GUI 가 키워드를 켜 **그 텍스처로 그린다**. 비우면 다시 절차 생성.
   - 빛 모임·월드 섬광 재질: `_OverrideTex` 하나 — 텍스처 RGBA × `_Color` × 파티클 색(파티클 UV).
   - 오버레이 재질: 단계별 `_BurstTex`(1단계 폭발) · `_GlareTex`(2단계 빛 갈라짐) · `_InvertTex`(3·4단계 흰 반전) — 화면 전체 UV, RGBA 그대로.
     색 칸 `_FlashColor`(흰 폭발·광선·반전 바탕) · `_BackColor`(1·2단계 배경, 기본 검정) · `_LineColor`(집중선) · `_RayCount` · `_LineDensity`.
3. 키워드는 셰이더 GUI(`OnGUI`·`ValidateMaterial`)가 맞춘다 — 텍스처는 재질 인스펙터에서 넣는다.
4. 집중선을 끄려면 디렉터 `hitFlashSpeedLines` 칸.

## 3. 빛 모임 소켓 — Variant 칸

- base `Player.prefab` 루트 `InterruptLightGatherSocket`: `socket` 비움(= 캐릭터 중심) · `vfxPrefab` = `FX_InterruptLightGather`. **base 는 고치지 않는다.**
- Variant 오버라이드(지금 값): 팔라딘 `SM_Wep_Shield_01` · 어쌔신 `Dagger_Weapon_R` · 거너 `LaserGun_Mount/Muzzle`.
- 위치만 따라가고 회전은 월드 기준. 캐릭터별로 다른 빛 모임이 필요하면 Variant 의 `vfxPrefab` 을 바꾼다.

## 4. 렌더링 레이어 비트 4 — 오너 암전에 '남는' 렌더러

- 강 시도 때 오너 화면은 **마스크 암전**: 렌더링 레이어 **비트 4** 를 가진 렌더러만 남고 나머지(다른 플레이어·보스 예고·투사체·HUD)는 덮인다.
- 런타임이 암전 순간 오너 캐릭터 하위 전부·간파 대상·빛 모임에 비트 4 를 **더했다가** 끝나면 지운다 — 플레이어 소유 이펙트는 따로 할 일이 없다.
  단 **암전이 켜진 뒤 새로 생긴 이펙트**는 비트가 없어 덮인다.
- 🔴 **렌더링 레이어가 Everything(4294967295)인 렌더러는 비트 4 도 가져 늘 남는다.** 그래서 SVN `Assets/50.Art` VFX 프리팹 **26파일·75곳**의 `m_RenderingLayerMask` 를 **Everything → 1**(비트 0 = 조명만)로 일괄 바꿨다
  (보스 `FX_Electricball_Shoot`·`FX_Ground_Crash`·대시/그랩/점프, 23호 `FX_Drop_Collision`, 웰즈 `FX_Bomb_Exploded`, 연기 3종, `FX_InterruptFlash_2`, `Fx_Bite`, 팔라딘 R, 거너 평타·Q·R·우클릭 등).
  목록·전후 값: `_lane-artifacts/MainProject-Worktree4/132/svn-vfx-mask-files.txt`(레포 밖). **SVN 커밋은 메인이 132 병합 직전에**(`DashAfterimage.cs` 잔상 비트 한 줄과 함께, git 보다 먼저).
- 앞으로 새 VFX 프리팹은 렌더링 레이어를 **Everything 으로 두지 말 것**(기본 `1`). 데칼(비트 1)·실루엣(비트 2·3)이 필요한 렌더러만 그 비트를 더한다.

## 5. 꺼 둔 캐릭터별 간파 연출

132 단계 6 에서 디렉터 스위치: 팔라딘 성공 충격파 B3·어쌔신 간파 성공 C2·맞은 쪽 섬광 E1 = **꺼짐**(새 적중 섬광이 대신). 시도 쪽 B1·B2·C1 은 132 이전부터 꺼짐 그대로 —
빛 모임과 같은 소켓(방패·단검)에서 '기 모으기'가 겹치므로 켤 땐 함께 보고 정한다. 공용 성공 이펙트 칸 A4(`InterruptSuccessFx` casterFx/targetFx)는 켬, 칸은 비어 있음.

## 6. 확인 부탁 (Play — 사람)

- 강 시도에서 빛 모임이 소켓(방패·단검·총구)에 붙어 보이는지, 슬로우 중 속도·밝기(블룸).
- 성공 때 오버레이 1~3단계 모양·길이, 4단계 월드 섬광 색·크기.
- 26파일 바꾼 뒤 보스·거너 이펙트가 암전 밖에서는 예전과 같이 보이는지(조명 비트 0 유지).
