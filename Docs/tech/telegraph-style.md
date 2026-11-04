# 공격 예고 색·마스크 규격 (아트 전달용)

> 2026-10-13 · 경석 · 계획 [PLAN-telegraph-style.md](../history/PLANS/PLAN-telegraph-style.md)
> 몬스터 공격 예고(바닥에 깔리는 빨간/파란 범위)의 **색과 무늬(마스크 이미지)** 를 스크립트를 열지 않고
> **에셋 인스펙터에서 바로** 고칠 수 있다. Play 중에 고쳐도 바로 반영된다.
>
> ⚠️ **2026-10-19 할 일 57(적 범위표시 C3) 이후** — 23호 훅·어퍼·잡기·돌진, 건틀렛 펀치·스매시, 스피너 채찍·돌진,
> 웰즈 드론 착탄 원, 박격포 착탄 원, 전기 장판, 23호 점프 착지 원은 **이 에셋이 아니라 `Resources/Telegraph/EnemyC3Selected.json`** 의
> 색·기하로 그린다(마스크도 적용 안 됨). 이 에셋 값이 아직 먹는 곳: 원형 장판 `AoeTelegraph`(23호 차징 오라 — 원형 판 채움 색),
> 예고 재질(`MA_TelegraphDecal`)이 없을 때의 대체 경로 채움 색. 상세 = [handoff-enemy-indicator.md](handoff-enemy-indicator.md) §8.2·§9.

---

## 1. 어디서 고치나

| 하고 싶은 것 | 고칠 곳 |
|---|---|
| **모든 몬스터** 예고를 한 번에 | `Assets/2.Prefabs/Monster/Data/TelegraphStyle_Default.asset` |
| **특정 몬스터만** 다르게 | 위 에셋을 복제(Ctrl+D) → 그 몬스터 데이터(예: `No23.asset`)의 **Telegraph Style** 칸에 끌어 넣기 |

- 몬스터 데이터 12개가 지금 전부 `TelegraphStyle_Default` 를 물고 있다.
- 칸을 비우면 코드 기본값(= 2026-10-13 화면)으로 그린다.

## 2. 스타일 4벌 — 어디에 쓰이나

| 이름 | 쓰이는 곳 | 모양 |
|---|---|---|
| **Attack** (공격) | 23호 훅·어퍼·잡기 등 · 건틀렛 펀치 · 스피너 채찍 | 부채꼴 · 경로 띠 · 둘을 합친 열쇠구멍 모양 |
| **Interrupt** (인터럽트) | **간파 가능한** 공격 — 23호 돌진 · 스피너 돌진 · 건틀렛 스매시 원(**채움 색 RGB 만**) | 위와 같음 |
| **Circle** (원형 판) | 웰즈 자폭 드론 착탄 원 | 원 |
| **Floor Tile** (사각 판) | 23호 전기 장판 경고 칸 | 사각 칸 |

🔴 **색 규약은 의미가 구분되게 유지할 것** — 빨강 = "여기 곧 맞는다", 파랑 = "간파(인터럽트)할 수 있다".
너무 쨍하면 **알파(불투명도)나 채도를 낮추되** 두 색이 서로 헷갈리게 만들지는 말 것.

## 3. 한 벌의 구조 — 윤곽선 + 채움

```
 ┌───────────── 윤곽선 (Outline) ─────────────┐   처음부터 전체 크기. "어디에" 맞는지.
 │                                            │   안쪽은 비어 있다(테두리만).
 │   ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  채움 (Fill) →→→         │   0 에서 전체로 차오른다. "언제" 맞는지.
 │   ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓                          │   다 차는 순간 = 공격.
 └────────────────────────────────────────────┘
```

| 칸 | 의미 | 단위 |
|---|---|---|
| `outline.color` · `fill.color` | 색. **알파 = 불투명도** | RGBA |
| `outlineWidth` (Attack·Interrupt) | 윤곽선 두께 | **미터(m)** — 예고 크기와 무관하게 같은 두께 |
| `rimRatio` (Circle·Floor Tile) | 판 테두리 두께 | **판 크기에 대한 비율 0~0.5** (m 아님). 테두리는 진하게, 안쪽은 0.55 배 |
| `mask` | 무늬(아래 4장) | — |

최종 불투명도 = **색 알파 × 모양 × 마스크** (한 번씩만 곱한다).

## 4. 마스크 (무늬·이미지)

각 층(윤곽선·채움)에 따로 넣는다. 비우면 무늬 없이 단색.

| 칸 | 의미 |
|---|---|
| `texture` | 마스크 이미지. **흰 = 보임 / 검·투명 = 안 보임** |
| `mode` | **Stretch(늘이기)** = 모양 전체에 이미지 한 장 · **Tile(반복)** = `tileSize` m 마다 한 장 |
| `tileSize` | 반복 모드에서 이미지 한 장이 덮는 크기(m) |
| `strength` | 0 = 마스크 없음과 같음 ~ 1 = 마스크 그대로. 은은하게 하려면 0.3~0.6 |
| `rotation` | 무늬 회전(도) |

### 4-1. 마스크 이미지 규격

| 항목 | 규격 |
|---|---|
| 형식 | PNG 또는 TGA |
| 크기 | **정사각, 2의 거듭제곱** — 256×256 권장(반복 무늬), 512×512(늘이기로 큰 그림) |
| 내용 | 아래 둘 다 받는다 — 값 = **R × A** <br>① 흑백 불투명 그림(흰 = 보임) <br>② 흰색 + 알파 채널(알파 = 보임) |
| 반복 모드용 | **이음매 없는(seamless) 타일**로 그릴 것 — 가장자리가 이어져야 줄이 안 끊긴다 |
| 방향 | 이미지 **위쪽 = 공격 방향(예고 정면)**. 원·사각 판은 위쪽 = 월드 +Z(격자 정면) |

### 4-2. 임포트 설정 (Unity 인스펙터)

| 설정 | 값 | 이유 |
|---|---|---|
| Texture Type | Default | |
| **sRGB (Color Texture)** | **끔** | 마스크는 색이 아니라 수치 — 켜면 중간 회색이 더 어둡게 먹는다 |
| **Wrap Mode** | 반복 모드 = **Repeat** · 늘이기 = Clamp | Repeat 가 아니면 반복이 안 된다(가장자리 늘어남) |
| Filter Mode | Bilinear | |
| Generate Mip Maps | 켬 | 멀리서 무늬가 지글거리지 않게 |
| Compression | Normal Quality 로 충분. 가는 줄이 뭉개지면 None | |
| Read/Write | **필요 없음** | 셰이더가 직접 읽는다 |

### 4-3. 늘이기(Stretch) 기준 크기

| 모양 | 이미지 한 장이 덮는 범위 |
|---|---|
| 부채꼴 | 꼭짓점 중심, 반경×2 정사각 |
| 경로 띠 | 띠 전체(폭 × 길이) |
| 열쇠구멍(띠+부채꼴) | 전체 바운딩 박스 |
| 드론 원 | 원 지름 정사각 |
| 전기 장판 | **칸 하나** |

- 무늬는 **예고를 따라 움직인다** — 보스가 돌면 무늬도 같이 돈다(줄무늬가 공격 방향을 가리킴).
- 채움이 차오르는 동안 무늬는 **제자리**다(커지거나 미끄러지지 않는다).

## 5. Play 중에 확인하기

1. Play → 보스 앞으로(개발 단축키 **F5** = 보스방 이동).
2. 패턴 강제(호스트 창): **F3** 훅(좌우 번갈아) · **F4** 어퍼 · **F6** 점프 · **F9** 잡기 · **F11** 돌진(인터럽트 = 파랑).
3. Project 창에서 스타일 에셋 선택 → 색·마스크를 바꾸면 **켜져 있는 예고에 바로** 반영된다(차오르던 진행은 끊기지 않음).
4. 🔴 **Play 중에 바꾼 값은 에셋에 그대로 남는다**(머티리얼과 같다). 되돌리려면 Ctrl+Z 또는 버전 관리로.
5. ⚠️ MPPM(2~3인 테스트) 중에는 내 에디터에서 바꾼 값이 다른 가상 플레이어 창에는 바로 안 보일 수 있다. 빌드는 같은 에셋이라 일치.

## 6. 범위 밖 (이 에셋으로 안 바뀌는 것)

- 보스 **방향 표식**(보스 발밑 앞/뒤 호) — 색은 전방/후방 재질의 `_BaseColor` 그대로. 모양(호)은 할 일 57 S4c1 부터 C3 데칼 셰이더가 그린다(텍스처 없음).
- **점프 착지 예고** — 할 일 57 부터 `EnemyC3JumpView`(C3 판 06·07, `EnemyC3Selected.json`). 예전 이펙트(`EffectCatalog` 의 `Drop_Charge_*`)는 쓰이지 않는다.
- **건틀렛 스매시 원** — 페이드는 아트 VFX 그대로, 그림은 할 일 57 부터 미리 구운 C3 sprite(`Resources/Telegraph/EnemyC3_Smash09.png`). 이 에셋의 인터럽트 색은 더 이상 덮지 않는다.
- 전기 장판 **발동 색**(전기 깜빡임) — `BossElectricFloorData.electricColor`(예고가 아니라 발동 표시).
- 원형 장판 컴포넌트(`AoeTelegraph`) — 2026-10-13 기준 쓰는 곳이 없다.

## 7. 개발 참고

- 셰이더: 데칼 `Assets/3.Materials/SG_TelegraphDecal.shadergraph`(커스텀 함수 1개) · 판 `Monster/TelegraphQuad` — 계산은 둘 다 `Assets/1.Scripts/Rendering/Telegraph/TelegraphMask.hlsl`.
  - `TelegraphQuad` 의 `_CodeShape`(할 일 57 S4c2): 0 = `_MainTex` 텍스처(기본), 1 사각 · 2 원판 · 3 조준 = `BossPatternVisuals` 의 코드 모양을 텍스처 없이 셰이더가 그린다(예전 런타임 64²/128² 굽기 대체, `y` = 테두리 비율).
  - C3 경로 전용: 데칼 `SG_EnemyC3Decal`(같은 `TelegraphShade` 로 색·마스크 합성) · 판 `Monster/EnemyC3Plate`. 공격 데칼의 **채움**도 할 일 57 S4a 부터 `SG_EnemyC3Decal` 이 그린다(예전 `BuildArcTexture`·`BuildRectTexture`·`BuildUnionTexture` 256² 굽기 없음 — 원본 식은 에디터 전용 `Monster/Editor/LegacyDecalTextureReference`). 구조 = handoff-enemy-indicator.md §9.
- 재질: `Assets/Resources/Telegraph/MA_TelegraphDecal.mat` · `MA_TelegraphQuad.mat` · `MA_EnemyC3Decal.mat` · `MA_EnemyC3Plate.mat`(코드가 불러 쓴다 — 지우지 말 것). `MA_TelegraphDecal` 은 C3 데칼 인스턴스의 그리기 순서(renderQueue·`_DrawOrder`) 출처로 계속 쓰인다.
- 준비/검사 메뉴: `Tools/Monster/공격 예고 스타일 준비 (멱등)` · `Tools/Monster/공격 예고 셰이더 검사`.
- 테스트: `Tools/Tests/전투 EditMode 테스트 실행` 의 `TelegraphStyleTests`(기본값 = 옛 화면 고정).

## 8. 거리 감쇠 그라데이션 (할 일 202, 2026-11-04)

로컬 플레이어(내 화면 기준)와 **가까운 곳은 원래 진하기, 멀어질수록 옅어지는** 그라데이션. 예고·표시의 모양·색은 그대로고 **알파에만** 곱한다.

```
감쇠 = 1 / (att0 + att1·d + att2·d²)      d = 픽셀 월드 위치 ↔ 로컬 플레이어, 바닥 평면(xz) 거리(m)
```

- 결과는 0~1 로 자른다(가까운 곳이 원래보다 진해지지 않음). 분모가 0·음수면 감쇠 1.
- **기본 (1, 0, 0) = 감쇠 없음 = 202 이전 화면.** 예) (1, 0.1, 0) → 10m 에서 절반 · (1, 0, 0.01) → 10m 에서 절반(먼 쪽이 더 급하게 떨어짐).
- 블룸은 보류(Emission 안 켬).

| 대상 | 계수 칸 | 비고 |
|---|---|---|
| 적 예고 C3(23호·건틀렛·스피너·드론·박격포·전기 장판·점프 착지 등) | `Resources/Telegraph/EnemyC3Selected.json` 항목(id)마다 `distanceFade` = `constant`(att0)·`linear`(att1)·`quadratic`(att2) | 데칼 외곽선·채움(같은 예고는 같은 id)·바닥 판 모두. JSON 은 Play 마다 다시 읽는다 |
| 플레이어 스킬 범위·대상 원 | 재질 `SkillRangeIndicator.mat`·`SelectedTargetCircleC.mat` 의 **Decal Fade**(x = att0, y = att1, z = att2) | 재질 칸이라 Play 중 바로 반영 |
| 조준(데칼 그래프 `AimIndicator`) | 재질 `AimIndicator.mat` 의 **Decal Fade** | ⚠️ 이 재질은 보관용 프리팹(`Paladin_VFX`·Legacy)만 쓴다. 게임 중 조준 표시는 `Player.prefab` 의 쿼드(`AimIndicator3D.mat`, URP Lit 투명)라 **감쇠가 안 걸린다** |

- **C3 띠 그라데이션과의 합성 = 곱.** 최종 알파 = (모양 × 띠 그라데이션 × 채움 × 마스크 × 색 알파) × 감쇠. 띠는 모양 안쪽 방향, 감쇠는 플레이어 거리 방향이라 둘 다 살아 있다.
- 기준점: `DecalDistanceFadeDriver`(씬 배치 없이 스스로 생김)가 매 프레임 `Player.LocalPlayer` 위치를 전역 셰이더 값 `_DecalFadePlayerPos` 하나로 넣는다. 로컬 플레이어가 없으면 감쇠 1. 텍스처·메시 생성 없음. 피어마다 자기 플레이어 기준(네트워크 값 아님).
- 코드: 식 `Rendering/Telegraph/DecalDistanceFade.hlsl`(셰이더) = `DecalDistanceFade.cs`(C#, 테스트 기준). 셰이더 쪽 재질 칸 이름은 전부 `_DecalFade`.
- 한계: 스매시 09 원(미리 구운 sprite)·23호 차징 오라(`AoeTelegraph`)·보스 방향 표식은 id 가 없어 재질 기본값(1, 0, 0) = 감쇠 없음.
- 테스트: `Tools/Tests/전투 EditMode 테스트 실행` 의 `TelegraphDistanceFadeTests`(식·기본값·분모 보호·단조 감소·JSON 기본값·재질 칸·그래프 설정).

### 8-1. 거너 Q 충전 레이저 조준 사각형 — 길이 비율 감쇠 (할 일 202 추가, 2026-11-04)

Q 충전 중 바닥에 뜨는 직사각형(`SkillLineIndicator` 의 Fill 쿼드, 재질 `3.Materials/Player/SkillLineIndicator/SkillLineRange.mat`, 셰이더 `VeyTrace/Player/Skill Line Indicator` — 데칼 아님, 투명 쿼드)은 **월드 거리 대신 사각형 길이 대비 비율 t**(시전자 쪽 끝 0 → 먼 끝 1)로 옅어진다. 모양은 **인스펙터 커브 그래프 하나**로 정한다.

| 칸 | 위치 | 뜻 | 기본 |
|---|---|---|---|
| **Length Fade Curve** `lengthFadeCurve` | `Player.prefab` → 자식 `SkillLineIndicator` 컴포넌트, 헤더 "길이 감쇠 (202 — 거너 Q)" | x = 길이 비율 t, y = 진하기 배율(0~1 로 자름) | (0,1) (0.5,0.85) (0.75,0.55) (0.9,0.2) (1,**0**) 를 매끄럽게 — 먼 끝 완전히 투명 |
| **Distance Fade** `_DecalFade` | `SkillLineRange.mat` | 위 §8 거리 감쇠 계수(att0, att1, att2) — 길이 감쇠와 **곱** | (1, 0, 0) = 감쇠 없음 |

- 커브는 머티리얼에 둘 수 없어 컴포넌트 칸이다(Fill·Ghost 가 같은 컴포넌트라 같은 값). 커브가 바뀔 때만(`OnValidate` — 인스펙터·커브 창 편집, Play 중 포함) 32개 고른 간격 샘플로 구워 속성 블록 배열 `_LineFadeCurve`(`SetFloatArray`)로 넘기고, 셰이더가 샘플 사이를 직선 보간한다. 텍스처 생성·매 프레임 할당 없음. Play 중 씬의 플레이어 인스턴스 커브를 고치면 바로 보인다(Play 끝나면 되돌아가니 확정 값은 프리팹에).
- 켜기 `_LineFadeOn`·구간 `_LineFadeSpan`(인스펙터에 숨김, 속성 블록)은 **거너 Q 만**: 충전 중 사각형(`ShowCharge`)과 HUD 슬롯 호버 미리보기(`SkillPreviewShapes.ChargeLaser` → `LengthFade`). 미리보기는 채움(최소 사거리)·고스트(최대 사거리)를 **한 사각형으로 보고 t 를 최대 사거리 끝 기준으로** 맞춘다(같은 월드 지점 = 같은 t). 다른 스킬 미리보기·이동 화살표는 기존 그대로.
- 🔴 이 셰이더의 C3 채움은 원래 **먼 끝일수록 진해지는 램프**(시전자 쪽 0.04/0.145 배 → 끝 1배)가 있다. 감쇠와 곱하면 서로 상쇄돼 균일해 보였다(11-04 사용자 "끝까지 똑같음"). 감쇠가 켜지면 램프를 끄고 채움 = Interior Opacity × 감쇠. 그래서 시전자 쪽 채움은 예전보다 진하다 — 진하기는 **Interior Opacity**(0.232)로 조절.
- 커브 값은 **최종 알파 한 곳**에 곱한다 — 채움·외곽선·안쪽선·화살표 무늬·끝선(C3 Flat End)이 모두 합쳐진 뒤라 커브 0 인 곳은 전부 완전히 투명(블렌드 SrcAlpha/OneMinusSrcAlpha, 가산 아님). 고스트도 같은 커브·같은 t.
- 커브 칸이 프리팹에 직렬화돼 있지 않으면 코드 기본 커브를 쓴다(11-04 기준 Player.prefab 은 기본 사용).
- 채움 끝의 진하기 단차(채움 알파 0.28 vs 고스트 0.09, 흰 끝선)는 최소/최대 사거리 표시 디자인이라 그대로다 — 감쇠는 그 위에 곱해진다.
- **양옆 흰 라인**(거너 Q 만): 같은 컴포넌트 헤더 "양옆 흰 라인 (202 — 거너 Q)" — **Edge Line Width**(m, 기본 0.06, 0 이면 안 그림)·**Edge Line Color**(흰색)·**Edge Line Curve**(길이 비율별 진하기, 기본 = 길이 감쇠 기본 커브, 끝 0). 텍스처는 `SkillLineRange.mat` 의 **Side Edge Line Texture** = `50.Art/TestAssets/Temp_Images/Images/white_512.png`(SVN). 같은 패스 안에서 기존 그림 **위에** 합성(별도 렌더러·패스 없음), 같은 32 샘플 굽기·속성 블록(`_EdgeLineCurve`·`_EdgeLineOn`·`_EdgeLineWidth`·`_EdgeLineColor`). 사각형 폭 안쪽에 그린다.
- 사용자 튜닝 커브는 `Player_Gunner.prefab` 의 `SkillLineIndicator.lengthFadeCurve` 오버라이드(11-04). base `Player.prefab` 은 코드 기본값.
- t 는 그려진 사각형 기준이라 벽에 잘려 짧아지면 잘린 끝이 t=1.
- 코드: 셰이더 `LineLengthFade`(샘플 직선 보간) = C# `Player/Skill/Targeting/SkillLineLengthFade.cs` `Bake`·`Sample`(테스트 기준). 테스트: `Tools/Tests/스킬 직선 인디케이터 EditMode 테스트 실행` 의 `SkillLineLengthFadeTests`.

### 8-2. 어쌔신 대시 일격 화살표 미리보기 (할 일 213, 2026-11-04)

- 어쌔신 Q 대시 일격(변신 Q 포함, `AssassinDashStrikeSkill` = `ISkillPreviewSource`)은 **HUD 슬롯 호버 미리보기**(거너 Q 미리보기와 같은 시점 — 대시는 조준 단계·충전이 없다)에 대시 거리만큼의 **화살표**를 그린다. 벽에 막히면 그 자리까지(`PlayerMotor.GetGroundedPreviewDistance`, 유닛 관통 = 실제 돌진과 같음). 그림자 회귀(복귀)는 앞 방향 대시가 아니라 미리보기 없음(기존과 같음).
- 그리기 = `SkillLineIndicator` Arrow 쿼드(`SkillLineArrow.mat`, 셰이더 `ArrowC3`) — 기존 이동 미리보기 화살표(거너 쿨 백스텝 등)와 같은 모양. `SkillPreviewShapes.Arrow(..., lengthFade: true)` 일 때만 **같은 컴포넌트의 Length Fade Curve·Edge Line Width/Color/Curve** 를 쓴다(칸 재사용). 흰 라인은 화살표 윤곽(몸통·머리) 안쪽을 따라 그리고, 화살표 면 램프(촉으로 갈수록 진함)는 감쇠가 켜지면 꺼진다(§8-1 과 같은 규칙). 텍스처 `SkillLineArrow.mat` Side Edge Line Texture = white_512.
- 커브 기본값: `Player_Assassin.prefab` 에 `Player_Gunner.prefab` 과 같은 커브 오버라이드를 복사(11-04 사용자 튜닝값). base `Player.prefab` 은 코드 기본값.
- 화살표 폭 = `SkillLineIndicator.arrowWidth`(0.9). 거리 감쇠(`_DecalFade`)는 화살표에 걸지 않는다.
