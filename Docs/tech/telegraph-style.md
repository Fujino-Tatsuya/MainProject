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
