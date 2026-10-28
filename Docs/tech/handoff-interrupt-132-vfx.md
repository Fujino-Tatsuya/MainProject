# 공유 — 간파 빛 모임·적중 섬광 VFX, 덮어쓸 텍스처 칸, 소켓, 렌더링 레이어 비트 4 (할 일 132, 민경)

> 작성: 은희 레인(claude-alt), 2026-10-28. 브랜치 `feature/InterruptPresentation2`(단계3 `e634b139` · 단계4 `02e95946` · 단계5 `d04f23d4` · 단계6 · §15 섬광 5단계 재구성).
> 설계 원본: [interrupt-presentation.md §0](interrupt-presentation.md#0-132-체계--예측-3상태강약오너-암전빛-모임적중-섬광-할-일-132). PR 없음 규칙에 따라 이 문서로 공유한다.

## 1. 새 VFX — 워커가 절차 생성으로 만든 처음 버전 (git, `Assets/5.VFX/Interrupt/`)

| 연출 | 언제·누가 보나 | 프리팹·재질 | 셰이더 |
|---|---|---|---|
| **빛 모임** — 흰 빛 기둥 + 사방에서 모여드는 입자 + 중심 빛 | 강 시도(성공 예측) 입력 순간 → 간파 애니 Hit. 오너 + 팀원 | `LightGather/FX_InterruptLightGather.prefab` · `M_InterruptLightGather_Pillar` · `M_InterruptLightGather_Mote` | `BeaverLobby/VFX/InterruptLightGather` |
| **월드 섬광** — 흰 중심·파란 번짐·광선·링·불티 | 성공 섬광 순간(성공 후 0.133초 = ⑤ 시작) 적중 지점. 모두 | `HitFlash/FX_InterruptHitFlash.prefab` · `M_InterruptHitFlash_Core` · `_Rays` · `_Ring` | `BeaverLobby/VFX/InterruptHitFlash` |
| **화면 섬광 오버레이**(②③④⑥) — ② 플레어 층 + 푸른 고리 / ③ 회청색 화이트아웃 / ④ 암전 남김 영역 흰 스미어 / ⑥ 무지개 고리 | 강 시도 성공 때 오너 화면만, 0~0.433초 | `HitFlash/M_InterruptHitFlashOverlay.mat`(디렉터 `hitFlashOverlayMaterial` 칸) | `BeaverLobby/UI/InterruptHitFlashOverlay` |
| **② 렌즈 플레어** — 베이지 별 모양, 가로(아나모픽) 빛살이 가장 김 | ② 동안 오너 화면만 | `HitFlash/LF_InterruptHitFlashStarburst.asset`(URP Lens Flare (SRP) 데이터, 디렉터 `hitFlashLensFlare`) | URP LensFlareDataDriven |
| **⑤⑥ 색수차·블룸** — 무지개 색수차, 블룸 상승 후 가라앉음 | ⑤⑥ 동안 오너 화면만 | `HitFlash/VP_InterruptHitFlash_Aberration.asset` · `VP_InterruptHitFlash_Bloom.asset`(Volume 프로필 — 런타임은 가중치만 움직인다) | URP 후처리 |

- 파티클 셰이더 둘 다 **가산 언릿 + HDR `_Color`**(기본 (6,6,6) — 1 넘으면 블룸). 시간은 `_Time` 대신 파티클 나이로 움직여 파티클 시간 기준을 따른다.
- 시간 기준 = 프리팹 루트 `InterruptLightGatherVfx.timeBase` · `InterruptHitFlashVfx.timeBase`(둘 다 기본 **스케일** — 슬로우를 따라 느려진다, 할 일 132 §14).
  재생할 때마다 모든 파티클 `useUnscaledTime` 에 칸 값을 넣으므로 자식 파티클을 더할 때 따로 맞출 필요 없다. 실시간으로 되돌리려면 그 칸만 바꾼다.
- 섬광 단계(할 일 132 §15 재구성 — 레퍼런스 영상 30fps): ② 스타버스트 0.067 → ③ 화이트아웃 0.033 → ④ 스미어 0.033 → **섬광 순간 0.133**(오너 암전 걷힘·월드 섬광·팀원 암순응)
  → ⑤ 색수차 블러 0.1 → ⑥ 가라앉음 0.2 → 끝 0.433. 길이는 디렉터 칸(`hitFlashStarburstDuration` 등 5칸), 시계는 `hitFlashTimeBase`(기본 **실시간**) — 월드 섬광 파티클 자체만 스케일 시간으로 느리게 터진다.
  표·누가 보나·렌더 순서: [interrupt-presentation.md §0.4·§0.7](interrupt-presentation.md).
  월드 섬광 크기(`hitFlashWorldScale`)·높이(`hitFlashHeightOffset` 1m)는 `NetworkManager.prefab › InterruptProductionDirector` 칸이다.
- `Assets/5.VFX/` 는 **git** 이다(SVN `Assets/50.Art/` 아님) — 손그림 텍스처를 넣으면 텍스처 파일 위치(SVN 아트)와 재질(git) 커밋을 맞출 것.

## 2. 덮어쓸 텍스처 칸 — 손그림으로 바꾸는 법

1. 재질(위 표)을 연다. 셰이더 GUI 맨 아래 헬프 박스에 지금 상태('절차 생성' / '덮어쓸 텍스처 사용')가 보인다.
2. 칸에 텍스처를 넣으면 GUI 가 키워드를 켜 **그 텍스처로 그린다**. 비우면 다시 절차 생성.
   - 빛 모임·월드 섬광 재질: `_OverrideTex` 하나 — 텍스처 RGBA × `_Color` × 파티클 색(파티클 UV).
   - 오버레이 재질: 단계별 `_StarburstTex`(② — **가산**, 채우면 플레어 층·푸른 고리 대신) · `_WhiteoutTex`(③) · `_SmearTex`(④) — 화면 전체 UV, RGBA(③④ 는 알파만큼 덮음).
     ⑤ 는 그림이 없고(실제 화면 효과), ⑥ 무지개 고리는 절차 생성만.
     색·모양 칸 `_FlareGain`(② 플레어 층 배율) · `_RingColor`(② 푸른 고리, HDR) · `_WhiteoutColor`·`_WhiteoutOpacity`·`_StreakCount`(③) ·
     `_SmearColor`·`_SmearBackColor`·`_SmearDirection`·`_SmearLength`(④) · `_RainbowIntensity`(⑥).
   - 렌즈 플레어 에셋(`LF_InterruptHitFlashStarburst`): URP Lens Flare (SRP) 인스펙터에서 요소(빛살 13개 — 가로 1·세로 1·대각 4·잔 빛살 6·코어 1)의 크기·회전·색·세기를 고친다.
     요소 종류를 Image 로 바꾸면 손그림 텍스처를 쓸 수 있다. 세기·크기 전체 배율은 디렉터 `hitFlashFlareIntensity`·`hitFlashFlareScale`.
   - 색수차·블룸 프로필: Volume 프로필 인스펙터에서 값만 고친다(런타임은 가중치만 0 → 1 → 0). 방사형 블러 세기는 디렉터 `hitFlashBlurStrength`.
3. 키워드는 셰이더 GUI(`OnGUI`·`ValidateMaterial`)가 맞춘다 — 텍스처는 재질 인스펙터에서 넣는다.
4. 레퍼런스 영상 프레임을 텍스처로 쓰지 않는다(참고만 — 계획서 §15).

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
- 성공 때 ② 플레어 모양(가로 빛살 최장·베이지)·위치(적중 지점과 맞는지, 상하 뒤집힘 없는지) · ③ 화이트아웃 · ④ 스미어(오너·대상이 흰 덩어리로) ·
  ⑤⑥ 블러·색수차·블룸 세기, 섬광 순간 월드 섬광 색·크기. 팀원 화면엔 ②~⑥ 화면 효과가 없고 월드 섬광·암순응만인지.
- 26파일 바꾼 뒤 보스·거너 이펙트가 암전 밖에서는 예전과 같이 보이는지(조명 비트 0 유지).
