# URP Decal — Use Rendering Layers 활성화

> Unity 6000.3 / URP 기준. Decal Projector에 `Rendering Layers` 항목을 노출시키려면
> Universal Renderer Data의 **Decal Renderer Feature**에서 `Use Rendering Layers`를 켜야 한다.

---

## 1. 현재 사용 중인 URP Asset 확인

먼저 어떤 URP Asset이 실제로 적용되는지 확인한다.

```
Edit → Project Settings → Quality
```

활성화된 품질 단계의 **Render Pipeline Asset**을 본다.

- 여기에 에셋이 지정돼 있으면 **이 설정이 우선**이다.
- `None`이면 아래 기본값을 사용한다.

```
Edit → Project Settings → Graphics → Default Render Pipeline
```

> ⚠️ Quality의 파이프라인 설정이 Graphics의 기본값을 **덮어쓴다.**

---

## 2. Universal Renderer Data 열기

확인한 URP Asset을 Project 창에서 선택한다.

Inspector에서 **`Renderer List`** 항목을 찾고, 사용 중인 Renderer 항목을 클릭하거나
오른쪽 **`⋮`** 버튼을 눌러 Renderer Data 에셋을 선택한다.

> Unity 6000.3 공식 문서도 URP Asset의 `Renderer List`에서 Renderer 항목 또는 `⋮`를 클릭해
> Universal Renderer 에셋을 찾도록 안내한다.

보통 파일 이름은 다음 형태다.

```
UniversalRenderer
PC_Renderer
URP_Renderer
MainRenderer
```

---

## 3. Decal Renderer Feature에서 옵션 켜기

Universal Renderer Data 에셋을 선택한 뒤 Inspector 맨 아래쪽으로 내려간다.

```
Renderer Features
└─ Decal
   ├─ Technique
   ├─ Max Draw Distance
   └─ Use Rendering Layers   ← 이 옵션 체크
```

`Use Rendering Layers`를 체크한다.

Decal 항목이 없다면 아래로 추가한다.

```
Add Renderer Feature → Decal
```

---

## 정리

- 정확한 설정 경로: **Decal Renderer Feature → Use Rendering Layers**
- 이 옵션은 `Max Draw Distance` 아래쪽에 있는 별도 체크박스다.
- 활성화하면:
  - Decal Projector에 **`Rendering Layers`** 항목이 나타난다.
  - **DepthNormal prepass**가 추가된다. (성능 비용 발생)

---

## 데칼이 바닥과 안 섞이던 문제 (할 일 202, 2026-11-04)

### 원인(코드 확인)
- 맵 바닥(존·보스방 바닥 재질 `Generic_01_A`·`Generic_01_A_Wet`)은 SVN `Assets/50.Art/MapGen/MapObj/material/Generic_Standard.shadergraph` = **URP Lit** 이다. DBuffer 데칼을 조명 **전에** 받으므로 조명·그림자·포그(화면 후처리)는 원래 데칼에도 걸린다.
- 섞이지 않게 보인 주원인은 **데칼이 바닥 노멀을 덮어쓴 것**이다.
  - 플레이어 `SkillRangeIndicator`·`AimIndicator` 그래프: NormalAlpha = 상수 1, NormalTS = 평평 → **투영 상자 전체**(투명한 부분까지)에서 바닥 노멀맵·하이라이트가 사라져 네모난 평평한 자국이 생겼다.
  - 예고 4종(`SG_EnemyC3Decal`·`SG_TelegraphDecal`·`SG_DecalFloorOnly`·`SG_ColoredDecal`): 기본 bump(평평) 노멀 × 알파 × `Normal_Blend` 0.5 → 데칼이 찍힌 곳만 바닥 요철이 절반쯤 평평해져 스티커처럼 떠 보였다.
- 색은 DBuffer 규칙상 **바닥 알베도와 lerp(덮기)** 만 된다 — 데칼 셰이더는 바닥 알베도를 읽을 수 없어서 곱·오버레이는 데칼 쪽에서 못 한다.
- 참고: Flat Kit(`Assets/FlatKit/.../Lighting_DR.hlsl`)은 데칼을 **최종 셰이딩 색**에 lerp 하고 메인 라이트만 곱한다(텍스처·밴드·추가 광원이 빠짐). 맵 바닥은 Flat Kit 이 아니고 캐릭터는 수신자 비트가 없어 지금은 영향 없다.

### 고친 것(데칼 쪽만 — 바닥·Flat Kit·SVN 셰이더 무수정)
- 데칼 6종 그래프 모두 `affectsNormal` 끔(Albedo 만). 바닥 노멀맵·하이라이트가 데칼 아래에도 그대로 남는다. 재질의 `Normal_Blend` 칸은 남아 있지만 이제 쓰이지 않는다.
- 플레이어 2종 그래프는 **Angle Fade** 를 켰다. 프로젝터 기본 180/180 이면 변화 없음 — 바닥 전용으로 쓰려면 `DecalReceivers.ConfigureFloorProjector` 와 같은 값을 넣는다.
- 가장자리: C3 는 셰이더 AA·띠 그라데이션, 플레이어는 텍스처 알파 그대로 + 거리 감쇠(telegraph-style.md §8).

### 남은 선택지(바닥 셰이더 수정 필요 — 하지 않음)
- 데칼 색을 바닥 색과 **곱/오버레이**로 섞으려면 바닥 셰이더(`Generic_Standard.shadergraph`, SVN)가 DBuffer 를 직접 읽어 곱해야 한다(URP Lit 기본 적용 대신 커스텀). 필요하면 별도 할 일로.

---

## 스킬·공격 예고 데칼 바닥 블렌딩 (할 일 221, 2026-11-05 — 1차 + 후속 외곽선 물들임)

### 무엇을 하나
- 스킬 범위 데칼과 몬스터 공격 예고가 바닥 위에 스티커처럼 떠 보이지 않게, **안쪽(채움) 알파를 공용 배율로 낮춰 바닥 무늬·명암이 비치게** 한다(1차).
- **후속("외곽선이 너무 붕 뜸")**: 외곽선(바깥 외곽선·안쪽 선)도 바닥에 **물들게** — 외곽선 알파 배율 + 물들임(0 = 일반 섞기, 1 = 완전 물들임) + 모드(밝기 보존 / 곱하기).
- 소프트는 **외곽선의 안·밖 가장자리 둘 다**에 건다(1차는 바깥 끝만이었다).

```
채움   알파 = 기존 알파 × lerp(채움 배율, 1, 가장자리 가중치) × 소프트
외곽선 (색, 알파) = 물들임(기존 색, 기존 알파 × 외곽선 알파 배율)   — 가장자리 가중치만큼 섞음
소프트 = smoothstep(0, s, t) × smoothstep(0, s, 1 − t)   (t = 외곽선 폭 안 위치 0~1, s = 소프트)
```

### 물들임 근사 — DBuffer 는 바닥 색을 못 읽는다
- 투영 데칼(DBuffer)은 바닥 알베도와 **lerp(덮기)만** 된다. 그래서 "기준 바닥 밝기(감마 0.3 = 선형 0.0732 — 맵 바닥 `Generic_01_A` 쯤)에서 목표 섞기와 같아지는 (색, 알파)" 로 근사한다. 투명 판도 **같은 근사**(같은 느낌, 판은 블렌드 상태를 안 바꾼다).
- 완전 물들임(1)에서 알파 = 원래 × 0.5 → 바닥 무늬·명암이 절반 남는다. 그 사이는 premultiplied 로 물들임 값만큼 보간.
  - **모드 0 밝기 보존(기본)** — 색조는 그대로, 밝기만 기준 바닥 쪽으로 절반 당김. 흰 선은 옅은 색 선이 되고 **어두운 바닥에서도 밝게 남는다**.
  - **모드 1 곱하기** — 기준 바닥에서 `바닥 × 색` 과 같게. 밝은 바닥에서 선이 회색으로 진해 더 잘 보이고, 어두운 바닥에서는 조금 더 탁·어둡다.
- 기본을 밝기 보존으로 둔 이유: 실제 맵 바닥이 어두운 편이고, 어두운 바닥 비교에서 예고 외곽선이 덜 묻혔다. 밝은 바닥(로비·흰 바닥)에서 선이 너무 옅으면 모드를 곱하기로.
- 비교 이미지: `C:\UnityProject\_lane-artifacts\Work2\221\compare\`(레포 밖) — `sheet_<시점>.png` = 왼쪽부터 끔 | 1차 | 밝기 보존 | 곱하기. 다시 찍기 = batchmode `-executeMethod DecalFloorBlendLabCapture.Capture`(`Dev/DecalFloorBlendLab/Editor/DecalFloorBlendLabCapture.cs`, Unity 닫고).

### 어디서 고치나 — `Assets/Resources/Rendering/DecalFloorBlendSettings.asset`
| 칸 | 뜻 | 코드 기본 | 에셋 시작값 |
|---|---|---|---|
| `skillFillAlphaScale` | 플레이어 스킬 인디케이터 안쪽 채움 배율(1 = 그대로) | 1 | 0.6 |
| `skillEdgeSoftness` | 외곽선 폭 중 안·밖 가장자리에서 이 비율만큼 흐림(0 = 그대로) | 0 | 0.5 |
| `skillPlateFillAlphaScale` | 판(투명 쿼드) 전용 채움 추가 배율 — 투영 데칼과 진하기 맞출 때 | 1 | 1 |
| `skillOutlineAlphaScale` | 외곽선·안쪽 선 알파 배율(1 = 그대로) | 1 | 0.9 |
| `skillOutlineStain` | 외곽선 물들임(0 = 일반 섞기, 1 = 완전 물들임) | 0 | 0.7 |
| `skillStainMode` | 물들임 모드 — `LuminanceKeep`(밝기 보존) / `Multiply`(곱하기) | 밝기 보존 | 밝기 보존 |
| `telegraphFillAlphaScale` | 몬스터 공격 예고 안쪽(채움·띠 그라데이션·쐐기) 배율 | 1 | 0.6 |
| `telegraphEdgeSoftness` | 예고 외곽선 안·밖 소프트 | 0 | 0.5 |
| `telegraphPlateFillAlphaScale` | 예고 판(C3 바닥 판·TelegraphQuad) 전용 채움 추가 배율 | 1 | 1 |
| `telegraphOutlineAlphaScale` | 예고 외곽선·안쪽 선 알파 배율 | 1 | 0.9 |
| `telegraphOutlineStain` | 예고 외곽선 물들임 — 회피 신호라 스킬보다 약하게 | 0 | 0.6 |
| `telegraphStainMode` | 예고 물들임 모드 | 밝기 보존 | 밝기 보존 |

- **코드 기본값(1·0·1 / 1·0·밝기 보존) = 221 이전 화면과 같다.** 에셋 값은 비교용 시작값 — 최종은 사용자 Play.
- 🔴 예고는 회피 신호 — 채움·외곽선을 너무 낮추지 말 것. 예고 외곽선 알파 ≥ 0.7·물들임 ≤ 0.8 을 테스트가 지킨다(에셋 값 검사).
- 전역 값은 4개 — `_DecalFloorBlendSkillEdge`·`_DecalFloorBlendTelegraphEdge` = (외곽선 알파, 물들임, 모드, 유효 1) 가 추가됐다.
- Play 중 에셋 값을 바꾸면 바로 보인다(Play 값이 에셋에 남는다 — 재질과 같음). 데이터 테이블(xlsx)에는 내지 않는다(렌더 룩 값).

### 적용 방식 — 전역 셰이더 값 한 곳
- `DecalFloorBlendDriver`(씬 배치 없이 스스로 생김 — `DecalDistanceFadeDriver` 와 같은 방식)가 매 프레임 설정을 전역 값 2개로 넣는다:
  `_DecalFloorBlendSkill`·`_DecalFloorBlendTelegraph` = (채움, 소프트, 판 추가, 유효 1). DecalProjector 는 속성 블록을 못 쓰고 예고는 재질 인스턴스가 많아서 전역 값이 가장 단순하다.
- 값이 없으면(w = 0 — 에디터 Play 밖·EditMode 테스트·픽셀 비교·에셋 없음) 셰이더는 221 이전 화면이다. **그래서 바닥 블렌딩은 Play 에서만 보인다.**
- 식: 셰이더 `Rendering/Telegraph/DecalFloorBlend.hlsl` = C# `DecalFloorBlend.cs`(테스트 기준).

### 대상별 — 무엇이 가장자리이고 무엇이 채움인가
| 대상 | 셰이더 | 채움(배율) | 가장자리(그대로) · 소프트 기준 |
|---|---|---|---|
| 사거리 원·대상 원·지면 마커 | `SkillRangeIndicator.shadergraph`(Custom Function `SkillRangeDecalFade`, `SkillRangeDecalAlpha.hlsl`) | 텍스처 알파 0.45 이하(안쪽 그라데이션) | 알파 0.7 이상(선·흰 테두리), 소프트 = 흰 바깥 테두리(반경 0.979~0.996) |
| 예고 C3 데칼 외곽선(부채꼴·띠·열쇠구멍) | `SG_EnemyC3Decal`(`EnemyC3Decal.hlsl`) | 띠 그라데이션·쐐기 | 외곽선·안쪽 선, 소프트 = 바깥 외곽선 |
| 예고 C3 데칼 채움(부채꼴·띠·열쇠구멍·방향 표식 호) | 〃 | 전부 | — |
| 원판(`AoeTelegraph` 코드 원판 = 23호 차징 오라) | 〃(원판 모드 6) | 안쪽 채움 | 테두리(rim) 띠, 소프트 = rim |
| C3 바닥 판(드론·박격포·전기 장판·점프 착지) | `Monster/EnemyC3Plate` | 채움·띠·쐐기(× 판 추가) | 외곽선·안쪽 선, 소프트 = 바깥 외곽선 |
| `TelegraphQuad` 판(코드 사각·원판·텍스처) | `Monster/TelegraphQuad` | 알파 0.6 이하(채움 0.55, × 판 추가) | 알파 0.95 이상(테두리 1), 소프트 = 코드 사각·원판만 |
| 거너 Q 직선·대시 화살표 | `SkillLineIndicator.shader` | 안쪽 면·쐐기(× 판 추가) | 외곽선·안쪽 선·끝선, 소프트 = 바깥 외곽선 + 양옆 흰 라인 |
| 조준 표시 | URP Lit 투명(`AimIndicator3D.mat`) — 셰이더 못 고침 | `PlayerAimIndicator` 가 `_BaseColor` 알파에 판 채움(채움 × 판 추가)만 곱함 | 그림 한 장이라 구분·소프트 없음 |

### 한계(1차)
- **건틀렛 스매시 원** = 미리 구운 sprite(`EnemyC3_Smash09.png`, SpriteRenderer 기본 셰이더)라 **적용 안 됨**. 하려면 굽기 다시(채움 알파 낮춘 sprite) 또는 전용 셰이더 — 별도 할 일.
- **23호 차징 오라** — 프리팹에 `shapeTexture` 가 없으면 C3 원판 경로라 **적용됨**. `shapeTexture` 를 넣은 `AoeTelegraph` 는 `SG_TelegraphDecal` 경로라 미적용(현재 대체 경로).
- `SG_TelegraphDecal`·`SG_DecalFloorOnly`·`SG_ColoredDecal`(옛·대체 예고 데칼)·`AimIndicator.shadergraph`(보관용 프리팹 전용)은 손대지 않았다.
- **`Mobile_Renderer` 에는 Decal 피처가 없다** — 투영 데칼은 원래 안 보이고, 판(투명 쿼드) 경로에만 적용된다(지금과 같은 상태).
- 물들임은 **근사**다 — 기준 바닥(감마 0.3)에서 맞고, 아주 밝은·아주 어두운 바닥에서는 목표 섞기와 어긋난다(데칼이 바닥 색을 못 읽음). 판도 블렌드 상태를 안 바꾸고 같은 근사를 쓴다.
- 물들임은 외곽선·안쪽 선(가장자리)만 — 원판(23호 차징 오라) **안쪽 채움**과 예고 채움에는 걸지 않는다(채움 배율만). `TelegraphQuad` 는 알파로 가장자리를 구분해 섞는다.
- 조준 표시(URP Lit)는 물들임 없음(채움 배율만).

### 확인
- 확인 씬 `Assets/0.Scenes/Debug/DebugDecalFloorBlendLab.unity`(빌드 목록 아님) — 저작 메뉴 `Tools/Debug/221 바닥 블렌딩 확인 씬 만들기`. 씬을 열고 ▶ → 밝은·어두운·무늬(`Generic_01_A`) 바닥 3장 위에 플레이어 인디케이터·예고를 나란히 만든다. **B 를 누르는 동안 블렌딩 끔**(221 이전 화면과 비교).
- 테스트: `Tools/Tests/전투 EditMode 테스트 실행` 의 `DecalFloorBlendTests`(식·1.0 = 그대로·값 없음 = 그대로·설정 → 전역 값 4개·물들임 0 = 그대로·완전 물들임 = 바닥 절반·밝기 보존은 색조 유지·곱하기는 기준 바닥에서 F×C·양쪽 소프트·예고 에셋 하한·셰이더가 공용 hlsl 사용·셰이더 컴파일). 값이 없을 때 EnemyC3 픽셀 비교가 그대로 통과 = 기본 화면 동일.
