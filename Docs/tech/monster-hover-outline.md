# 몬스터 호버 빨간 외곽선 (할 일 58)

내 마우스 커서 아래 적 한 마리와, 스킬 범위 표시가 떠 있는 동안 그 스킬에 맞을 적들(아래 "스킬 범위 외곽선")에 빨간 외곽선을 그린다. **표시만, 로컬 전용** — 네트워크·서버 권한·판정 변경 없음.
외곽선은 **림(프레넬)** 방식이다 — 월드 노멀 N 과 카메라 방향 V 의 `|dot(N, V)|` 가 0 에 가까운 곳(실루엣 가장자리)만 칠한다.

## 구성

| 파일 | 역할 |
|---|---|
| `Assets/1.Scripts/Player/PlayerEnemyHoverOutline.cs` | `Player.prefab`(base) 루트 컴포넌트. 오너(오프라인 포함 `IsInputSource`)만 돈다. **색·임계값·부드러움·강도 튜닝은 여기 한 곳.** |
| `Assets/1.Scripts/Player/Skill/Targeting/CursorUnitPick.cs` | 커서 레이 판정 공용 — 레이 → 마스크 안 첫 콜라이더 → Hurtbox 우선 Unit 해석 → 자기 자신·HP 0 제외. 스킬 조준(SingleTarget)도 이걸 쓴다. |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverOutlineOverlaySet.cs` | 대상 렌더러마다 **같은 메시를 그리는 자식 겹 렌더러**(`__HoverOutline`)를 얹었다 뗀다. |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverOutlineTracker.cs` | 대상 전환 규칙(한 번에 하나, 바뀐 프레임에만 떼기/붙이기). |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverRimSettings.cs` | 림 튜닝 값 묶음 — 값 범위 클램프(`Sanitized`), 셰이더 식의 C# 사본(`RimAlpha`, 테스트용), 머티리얼 반영. |
| `Assets/1.Scripts/Rendering/Toon/Shaders/EnemyHoverRim.shader` | `Project/Enemy Hover Rim` — 림 셰이더. |
| `Assets/3.Materials/MA_MonsterHoverOutline.mat` | 위 셰이더를 쓰는 머티리얼(이름·guid 는 1차 구현 그대로). 컴포넌트가 런타임 사본에 튜닝 값을 덮어 쓴다. |

- 대상 마스크: `GameLayers.EnemyTargetMask` = Enemy + EnemyHurtBox (단일 대상 스킬 데이터의 TargetableLayers 와 같은 조합).
- 대상: 위 마스크에서 해석된 **살아 있는 Unit** — 일반 몹·중간보스·23호·훈련 허수아비·보스 충전기(Unit 인 기믹) 포함. HP 0(사망 처리 중)·자기 자신 제외.
  **스킬 조준과 같은 기준이다 — 허수아비·충전기 포함은 사용자 확정(2026-10-20).**
- **가려진 부분은 그리지 않는다** — `ZTest LEqual` 유지, 벽 뒤 투시 패스는 추가하지 않는다(사용자 확정 (a), 2026-10-20).
- 레이는 **프레임당 1회.** 단일 대상 조준 중엔 `PlayerSkillTargeting` 이 쏜 레이의 판정을 `ReportAimedTarget` 으로 넘기고 외곽선 쪽은 쏘지 않는다(LateUpdate 에서 판정).
- 벽은 레이를 막지 않는다(스킬 조준과 같음 — 마스크에 벽이 없다).

## 림 셰이더 (`Project/Enemy Hover Rim`)

- 프래그먼트: `ndv = |dot(normalize(N_world), V)|`, `rim = 1 - smoothstep(임계값 - 부드러움/2, 임계값 + 부드러움/2, ndv)`.
  출력 = `(색.rgb × 강도, rim × 색.a)` — 림 밖은 알파 0(그 픽셀은 버린다)이라 원본이 그대로 보인다.
- 그리기: `Queue=Transparent`, `Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `ZTest LEqual`, `Offset -1,-1`, `Cull Back`.
  겹은 원본과 **같은 위치(부풀리기 없음)** 라 Offset 으로 원본 깊이 위에 덧그린다(Z-fight 없음).
- 패스: `LightMode=UniversalForward` 하나 — URP 투명 패스가 그린다. Flat Kit Per Object Outline 피처(`LightMode=Outline`)와 무관.
  그림자·깊이 패스 없음(본체 몫).
- 스키닝은 렌더러가 하므로 오브젝트 노멀을 월드로 바꾸기만 한다. 직교 카메라도 `GetWorldSpaceNormalizeViewDir` 가 처리한다.
- 🔴 `_BaseColor`/`_Color` 프로퍼티가 **없다** — HitFlash·사망 틴트 MPB 가 겹에 걸려도 읽을 색이 없어 물들지 않는다(EditMode 테스트로 고정).

### 튜닝 (`PlayerEnemyHoverOutline` 인스펙터)

| 필드 | 범위 | 기본 | 뜻 |
|---|---|---|---|
| `rimColor` | 알파 0~1 | (1, 0.08, 0.08, 1) | 림 색. 알파 = 림 불투명도. |
| `rimThreshold` | 0~1 | 0.25 | `|N·V|` 가 이보다 작은 곳만 칠한다. 클수록 림이 두껍다. |
| `rimSoftness` | 0~1 | 0.1 | 경계 번짐 폭(임계값 ± 값/2). 0 = 딱 잘림. |
| `rimIntensity` | 0~8 | 1 | 색 밝기 배수. 크면 블룸에 걸려 빛난다. |

- 값은 `HoverRimSettings.Sanitized()` 로 클램프(NaN → 최소값) 후, **값이 바뀐 프레임에만** 런타임 사본 머티리얼에 쓴다 —
  Play 중 인스펙터로 바꾸면 호버 중인 대상에 바로 보인다. 원본 `.mat` 은 안 바뀐다.
- 림은 **버텍스 노멀** 기준이라 하드 엣지(면마다 노멀이 갈라진) 메시는 가장자리 면이 넓게/좁게 갈린다 — HumanoidBot 처럼 각진 몹은 임계값을 올려 보며 맞춘다.

## 스킬 범위 외곽선 (할 일 58 후속)

스킬 **범위 표시가 떠 있는 동안** 그 스킬이 **실제로 맞힐 영역** 안의 살아 있는 적에도 같은 림을 켠다. 오너 로컬·네트워크 없음.

- **판정 = 서버 판정과 같은 기하.** 인디케이터 그림이 아니라 스킬의 피격 판정 영역(`SkillHitVolume`)을 쓴다.
  서버 판정 코드가 같은 팩토리로 영역을 만들어 겹침을 돌리므로 모양·사거리·폭·높이·앵커가 갈라질 곳이 없다.
  - `SkillHitVolume.Beam` — 거너 Q 관통 상자. `GunnerBeamAttack.OverlapBeamBox`(서버)·`GunnerChargeLaserSkill.FireVolume`(미리보기)이 쓴다.
  - `SkillHitVolume.CircleColumn` + `Accepts`(= `AssassinCircleStrikeRules.OverlapsCircle`) — 어쌔신 변신 E 원기둥.
    `AssassinCircleStrikeSkill.StrikeArea`(서버)·`TryGetGroundHitVolume`(미리보기)이 같은 `AreaCenter`/`HitVolume` 을 쓴다.
- **보고 방식**: 표시를 그리는 쪽이 매 프레임 `PlayerEnemyHoverOutline.ReportSkillHitArea(volume)` 를 부른다(호버의 `ReportAimedTarget` 과 같은 프레임 도장).
  안 부른 프레임엔 꺼진다. 표시가 꺼지는 프레임(조준 확정·취소 = `ExitStandby`, Q 발사·종료)은 `ClearSkillHitArea` 로 같은 프레임에 끈다.
  - 거너 Q: `OnOwnerTick` 이 충전 직선(`ShowCharge`)을 그린 직후 보고.
  - 지점 지정(GroundPoint) 조준: `PlayerSkillTargeting.UpdateGroundPointPreview` 가 지점 원을 그린 직후, 스킬이 `ISkillGroundHitPreview` 면 그 지점(확정 때 넘길 지점 — 고정 거리 투영·Clamp 반영)으로 보고.
- **고르기**: LateUpdate 에서 NonAlloc 상자 겹침 **1회**(마스크 = 스킬 피격 마스크 ∩ `GameLayers.EnemyTargetMask`) → 원기둥이면 `Accepts` →
  `CursorUnitPick.ResolveUnit`(Hurtbox 우선) → `IsSelectable`(HP 0·자기 자신 제외) → `SkillRangeOutlineSelection.Select`(중복 제거·가까운 순·상한).
  상자·플레이어 같은 비적은 마스크에서 빠진다.
- **합집합 규칙**: 호버 대상 ∪ 범위 대상. 같은 몬스터가 둘 다면 **호버가 이긴다**(호버 강도 하나만, 겹 중복 없음). 상한은 범위 세트에만 센다.
- **비용**: 겹 붙이기/떼기는 몬스터가 세트에 들어오거나 나갈 때(또는 호버↔범위 전환)만. 버퍼·리스트·겹 묶음은 재사용(프레임 할당 0 지향).
  런타임 사본 머티리얼은 호버용·범위용 2개.
- 서버 시점과의 차이: Q 사거리는 오너 충전 시각 기준이라 RTT 만큼 서버보다 앞서거나 뒤질 수 있다(인디케이터와 같은 차이). 위치도 오너 예측 기준.

### 스킬 적용 표

| 캐릭터 | 슬롯 | 스킬 | 범위 표시 | 판정 모양 | 판정 출처 함수 | 적용 |
|---|---|---|---|---|---|---|
| 거너 | Q | 충전 레이저 | 충전 중 직선(`ShowCharge`) | 직육면체(관통) | `GunnerBeamAttack.FirePiercing` → `OverlapBeamBox` | ✅ |
| 거너 | R | 추적 레이저 | 조준(대상 지정) + 대상 주변 원 | 생성 후 스스로 움직이며 재탐색 | `GunnerTrackingLaser` | ❌ 제외 — 경로·틱 대상이 시전 뒤 정해진다. 조준 대상은 호버(`ReportAimedTarget`)로 이미 칠해진다 |
| 거너 | E | 냉각 백스텝 | 없음(HUD 호버 화살표만) | 피해 없음(이동) | — | ❌ 해당 없음 |
| 거너 | 우클릭 | 근접 간파 | 없음(HUD 호버 상자만) | 앵커 상자 | `PlayerInterruptSkillBase` | ❌ 사용 시 범위 표시 없음 |
| 가붕이(팔라딘) | R | 최후의 심판 | 조준(대상 지정) | 단일 대상 | `FirstMeleeUltimateSkill.OnChannelCompleted` | ➖ 단일 대상 — 호버(조준 대상)로 충분 |
| 가붕이 | Q / E | 메인·서브 | 없음(즉발) | — | `FirstMeleeMainSkill`·`FirstMeleeSubSkill` | ❌ 사용 시 범위 표시 없음 |
| 가붕이 | 우클릭 | 간파 | 없음(HUD 호버 상자만) | 앵커 상자 | `PlayerInterruptSkillBase` | ❌ 사용 시 범위 표시 없음 |
| 어쌔신 | E(변신) | 원형 5타 | 조준(고정 거리 지점 원) | 원기둥(AABB-원) | `AssassinCircleStrikeSkill.StrikeArea` | ✅ |
| 어쌔신 | Q / E / R / 우클릭 | 돌진 베기·강화·변신·간파 | 없음(즉발·이동형) | — | `AssassinDashStrikeSkill` 등 | ❌ 사용 시 범위 표시 없음(돌진은 이동 경로가 판정이라 미리 알 수 없다) |

- **HUD 슬롯 호버 미리보기(`SkillLineIndicator.BeginPreview`·사거리 원 빌려 쓰기)는 넣지 않았다** — "스킬 사용 시" 표시가 아니라 툴팁용이다.
  넣으려면 `ISkillPreviewSource` 구현 스킬(거너 Q·우클릭, 가붕이 우클릭)이 같은 방식으로 `ReportSkillHitArea` 를 부르면 된다.
- 새 지점 지정 스킬은 `ISkillGroundHitPreview` 를 서버 판정과 같은 함수로 구현하면 자동으로 붙는다.

### 튜닝 (`PlayerEnemyHoverOutline` 인스펙터 — 스킬 범위)

| 필드 | 범위 | 기본 | 뜻 |
|---|---|---|---|
| `skillRangeRimIntensity` | 0~8 | 1 | 범위 대상 림 밝기 배수. 색·임계값·부드러움은 호버와 같다. |
| `skillRangeMaxTargets` | 1~ | 16 | 동시에 칠할 최대 마리 수(호버 대상 제외). 넘으면 시전자에서 가까운 순. |

- 겹침 버퍼는 64(콜라이더 수). 몬스터 하나가 콜라이더 여러 개면 한 마리로 합친다.

## 왜 림인가 (1차 구현의 부풀린 외피 대신)

1차 구현(3acd0747)은 `Zone/InteractOutline`(화면 공간 두께로 부풀린 Cull Front 헐, Flat Kit Outline 피처가 그림)을 겹에 썼다.
사용자 지시로 림으로 바꿨다 — 메시 바깥으로 튀어나오지 않아 큰 스키닝 메시(23호)에서 헐이 어긋나는 문제가 없고,
Outline 피처·기존 검정 외곽선 두께와의 겹침을 신경 쓸 필요가 없다. `Zone/InteractOutline.shader` 는 손대지 않았다(ZoneBridgeGate 외곽선 그대로).
제거된 필드: `outlineColor`·`outlineWidth`·`outlineMeshScale` → `rimColor`·`rimThreshold`·`rimSoftness`·`rimIntensity`.

## 왜 겹 렌더러인가 (머티리얼 슬롯 덧붙이기가 아니라)

몬스터 렌더러의 머티리얼 배열은 몬스터 쪽 연출이 스스로 바꾸고 복원한다 — `MonsterAttackTell`(슬롯 덧붙임 후 찍어 둔 원본으로 복원),
`DissolveDeath`(배열 교체), `MonsterCollapseDebris`, `BossChargingPylon`. 우리가 슬롯을 덧붙이면 그들이 찍은 "원본"에 외곽선이 섞여 남거나,
우리가 떼면서 그들의 교체를 되돌린다. 또 덧붙인 슬롯은 **마지막 서브메시만** 그린다 — 23호 조종사 메시는 서브메시 6개다.
겹 렌더러는 배열을 건드리지 않고(원본·공유 머티리얼 무수정, `.material` 인스턴스 생성 없음) 서브메시마다 림 머티리얼을 둔다.

## 연출과의 관계

- `HitFlash`·사망 틴트(MPB `_BaseColor`/`_Color`): 림 셰이더에 그 프로퍼티가 없어 영향 없음. HitFlash 가 겹을 캐시해도 색 프로퍼티가 없어 건너뛴다.
- 23호 체공 숨김·붕괴 사망(`enabled=false`): 겹은 매 프레임 원래 렌더러의 `enabled`(·`forceRenderingOff`)를 따라간다.
- 사망 디졸브: HP 0 이 되면 다음 LateUpdate 에 뗀다. 디졸브가 같은 프레임에 먼저 돌면 겹이 한 프레임 디졸브 사본으로 바뀔 수 있으나 곧 파괴된다(무해).
- 그로기: 렌더러·머티리얼을 건드리지 않아 무관(살아 있는 동안 계속 림).
- 23호 인터럽트 표시(`DissolveOverlay`, 같은 Transparent 큐): 떠 있는 동안엔 투명 정렬 순서에 따라 림이 그 밑에 깔릴 수 있다.
- 겹은 몬스터 하위에 런타임으로만 생기고(`HideFlags.DontSave`) 대상이 디스폰되면 함께 사라진다.
- 몬스터 코드는 반드시 몬스터 하위 렌더러를 **null 검사** 해야 한다(호버가 끝나면 겹이 파괴된다) — 현재 코드는 전부 검사한다.

## 알려진 한계 / 확인 필요

- **Play 확인 필요(사람)**: 실제 게임 카메라에서 림 두께·색감(기본 임계값 0.25 가 맞는지), 하드 엣지 몹, 23호 인터럽트 표시와 겹칠 때.
- 에디터 프리뷰 캡처(Play 없이, 23호·HumanoidBot·ChompBot 전/후): `C:/UnityProject/_lane-artifacts/MainProject-Worktree2/58-rim/`.
- 마우스가 HUD 위에 있어도 레이는 나간다(스킬 조준과 같은 동작).
