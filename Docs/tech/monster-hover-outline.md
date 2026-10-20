# 몬스터 외곽선 — 호버·스킬 범위(빨강) / 나를 노림(주황) (할 일 58)

내 마우스 커서 아래 적 한 마리와, 스킬 범위 표시가 떠 있는 동안 그 스킬에 맞을 적들(아래 "스킬 범위 외곽선")에 **빨간** 외곽선,
나(로컬 플레이어)를 추적·공격 중인 몬스터에 **주황** 외곽선을 그린다. **표시만, 오너 로컬 전용** — 판정·서버 권한 변경 없음
(주황 판정용으로 몬스터 대상 id 복제 값 하나만 추가, 아래 "나를 노리는 몬스터").
외곽선은 **화면 공간 마스크** 방식이다 — 대상 렌더러만 마스크 RT 에 그리고, 마스크를 N px 넓힌 것 − 원래 마스크 = 바깥 윤곽만 칠한다.

## 구성

| 파일 | 역할 |
|---|---|
| `Assets/1.Scripts/Player/PlayerEnemyHoverOutline.cs` | `Player.prefab`(base) 루트 컴포넌트. 오너(오프라인 포함 `IsInputSource`)만 돈다. 대상 세트 3개를 고르고 렌더러 목록을 `EnemyOutlineDrawList` 에 넘긴다. **색·두께·상한 튜닝은 여기 한 곳.** |
| `Assets/1.Scripts/Player/Skill/Targeting/CursorUnitPick.cs` | 커서 레이 판정 공용 — 레이 → 마스크 안 첫 콜라이더 → Hurtbox 우선 Unit 해석 → 자기 자신·HP 0 제외. 스킬 조준(SingleTarget)도 이걸 쓴다. |
| `Assets/1.Scripts/Rendering/HoverOutline/EnemyOutlineFeature.cs` | URP 렌더러 피처(RenderGraph). 마스크 패스 + 합성 패스. `PlayerSilhouetteFeature` 구조를 본떴다. |
| `Assets/1.Scripts/Rendering/HoverOutline/Shaders/EnemyOutlineMask.shader` | `Hidden/Rendering/EnemyOutlineMask` — 마스크(R 커버리지·G 색 인덱스·B eye 깊이). |
| `Assets/1.Scripts/Rendering/HoverOutline/Shaders/EnemyOutlineComposite.shader` | `Hidden/Rendering/EnemyOutlineComposite` — 원판 팽창 − 원래 마스크, 가림 판정, 알파 블렌딩. |
| `Assets/1.Scripts/Rendering/HoverOutline/EnemyOutlineDrawList.cs` | 이번 프레임 그릴 렌더러·색 인덱스·색·두께(정적 — 컴포넌트가 쓰고 피처가 읽는다). |
| `Assets/1.Scripts/Rendering/HoverOutline/EnemyOutlineRenderers.cs` | 마스크에 그릴 렌더러 고르기(모델 메시만, 연출·반투명 제외)·켜짐 판정·서브메시 수. |
| `Assets/1.Scripts/Rendering/HoverOutline/HoverOutlineTracker.cs` | 호버 대상 전환 규칙(한 번에 하나). |
| `Assets/1.Scripts/Rendering/HoverOutline/SkillRangeOutlineSelection.cs` | 스킬 범위 대상 고르기(중복·상한·가까운 순·사망·호버 제외). |
| `Assets/1.Scripts/Rendering/HoverOutline/MonsterAggroTargetRule.cs` | 나를 노림 — 서버 복제 값 규칙·클라 판정·고르기(빨강 우선·상한). |
| `Assets/99.Settings/PC_Renderer.asset` | `EnemyOutlineFeature` 가 렌더러 피처 목록 **맨 끝**에 들어 있다(다른 렌더러·파이프라인 에셋은 무수정). |

- 대상 마스크: `GameLayers.EnemyTargetMask` = Enemy + EnemyHurtBox (단일 대상 스킬 데이터의 TargetableLayers 와 같은 조합).
- 대상: 위 마스크에서 해석된 **살아 있는 Unit** — 일반 몹·중간보스·23호·훈련 허수아비·보스 충전기(Unit 인 기믹) 포함. HP 0(사망 처리 중)·자기 자신 제외.
  **스킬 조준과 같은 기준이다 — 허수아비·충전기 포함은 사용자 확정(2026-10-20).**
- **가려진 부분은 그리지 않는다**(사용자 확정 (a), 2026-10-20) — 아래 "가림".
- 레이는 **프레임당 1회.** 단일 대상 조준 중엔 `PlayerSkillTargeting` 이 쏜 레이의 판정을 `ReportAimedTarget` 으로 넘기고 외곽선 쪽은 쏘지 않는다(LateUpdate 에서 판정).
- 벽은 레이를 막지 않는다(스킬 조준과 같음 — 마스크에 벽이 없다).

## 화면 공간 마스크 외곽선 (`EnemyOutlineFeature`)

| 단계 | 내용 |
|---|---|
| 데이터 | `PlayerEnemyHoverOutline.LateUpdate` 가 매 프레임 `EnemyOutlineDrawList.Begin(색, 색, 두께)` + 대상 렌더러마다 `Add(렌더러, 색 인덱스)`. 꺼진 렌더러(`enabled=false`·`forceRenderingOff`·비활성)는 넣지 않는다. 목록이 비면 피처는 패스를 아예 넣지 않는다. |
| 마스크 패스 `EnemyOutline Mask` | 풀해상도 `R16G16B16A16_SFloat` 컬러 + 32bit 전용 깊이(매 프레임 클리어, MSAA 1, Point). 대상 렌더러를 `DrawRenderer(렌더러, 마스크 머티리얼, 서브메시 i)` 로 **서브메시 전부** 다시 그린다. 출력 R=1, G=색 인덱스(0 빨강/1 주황 — 머티리얼 2개), B=eye 깊이(m), ZWrite On·LEqual 로 대상끼리 앞뒤를 가린다. |
| 합성 패스 `EnemyOutline Composite` | `Blitter` 전체 화면. 마스크 R=1(대상 안쪽)이면 버린다 → **안쪽 선 없음**(23호처럼 렌더러·서브메시가 여러 개여도 바깥 윤곽 하나). 반경 N px **원판**(dx²+dy² ≤ N²+N) 안 대상 픽셀 중 가장 앞(B 최소)의 색·깊이를 고른다 → 없으면 버림. 그 픽셀의 씬 깊이(`_CameraDepthTexture`)가 대상 깊이 − 여유보다 앞이면 버림(가림). 카메라 컬러에 `Blend SrcAlpha OneMinusSrcAlpha`. |
| 주입 | `BeforeRenderingPostProcessing`(550). 같은 550 인 `PlayerSilhouetteFeature` 보다 렌더러 목록에서 뒤라 **실루엣 합성 다음**에 얹힌다(투명·포그(450) 뒤, 포스트프로세스·`RetroCRT`(AfterRenderingPostProcessing) 앞). `MaskBlurFeature`(550)는 비활성. |
| 카메라 | Preview·Reflection·오버레이·`targetTexture` 카메라(미니맵)에는 걸지 않는다(`PlayerSilhouetteFeature` 와 같은 규칙). 예외: `EnemyOutlineFeature.DevCaptureCamera`(에디터 캡처 전용 정적 필드, 기본 null). |

- 🔴 **원본 무수정**: 레이어·렌더링 레이어·머티리얼 배열·계층을 건드리지 않는다. 덮어쓰기 머티리얼로 같은 렌더러를 한 번 더 그릴 뿐이다
  (몬스터 쪽 연출 `MonsterAttackTell`·`DissolveDeath`·`MonsterCollapseDebris`·`BossChargingPylon` 이 배열을 스스로 바꾸고 복원하므로).
- 🔴 마스크 셰이더에 `_BaseColor`/`_Color` 가 없다 — HitFlash·사망 틴트 MPB 와 무관(EditMode 테스트로 고정).
- 두께는 **마스크 픽셀 단위**(= 카메라 타깃 해상도의 픽셀) — 해상도·거리와 무관하게 같은 픽셀 수. 원판이라 대각선도 같은 굵기. 범위 1~8 클램프.
- 마스크에 그릴 렌더러: `SkinnedMeshRenderer`/`MeshRenderer` 이고 메인 머티리얼이 불투명 큐(≤ GeometryLast), `AoeTelegraph`·`NoHitFlash` 아래가 아님(HitFlash 와 같은 제외 규칙).
  대상이 세트에 들어올 때 한 번 모아 두고, 켜짐은 매 프레임 본다.
- 알파 클립(컷아웃) 재질은 덮어쓰기 머티리얼이 클립을 하지 않아 메시 전체 모양으로 윤곽이 잡힌다(현재 몬스터 메시는 해당 없음으로 판단 — Play 확인 항목).

### 가림

- **윤곽 픽셀에서** 판정한다: 그 픽셀의 씬 eye 깊이 < (반경 안 가장 앞 대상의 eye 깊이 − `_occludeBias`) 면 그 픽셀에 대상보다 앞선 물체(벽·기둥·다른 물체)가 있으므로 안 그린다(ZTest LEqual 대응).
- 마스크 단계에서 가려진 픽셀을 지우지 않는 이유: 지우면 "보이는 부분"의 테두리가 되어 **벽이 몸을 자르는 경계에 가짜 선**이 생긴다(`PlayerSilhouetteMask.shader` 설계 2판과 같은 이유).
  지금 방식은 몸이 벽 뒤로 이어지는 자리엔 선이 없고, 벽 뒤로 숨은 윤곽 부분만 빠진다.
- `_occludeBias` 기본 0.2m(피처 설정). 너무 작으면 발밑 바닥이 윤곽을 잘라 먹고, 너무 크면 얇은 벽 너머로 윤곽이 샌다 — Play 확인 항목.
- 원근·직교 카메라 모두 맞다(마스크는 뷰 공간 z, 씬 깊이는 `unity_OrthoParams` 로 분기).

### 튜닝 (`PlayerEnemyHoverOutline` 인스펙터)

| 필드 | 범위 | 기본 | 뜻 |
|---|---|---|---|
| `outlineColor` | 알파 0~1 | (1, 0.08, 0.08, 1) | 호버·스킬 범위(빨강). 알파 = 불투명도. |
| `aggroOutlineColor` | 알파 0~1 | (1, 0.55, 0.1, 1) | 나를 노림(주황). |
| `outlineWidthPixels` | 1~8 | 3 | 외곽선 굵기(화면 픽셀). |
| `skillRangeMaxTargets` | 1~ | 16 | 스킬 범위 동시 최대 마리(호버 제외). 넘으면 시전자에서 가까운 순. |
| `aggroMaxTargets` | 0~ | 8 | 나를 노림 동시 최대 마리(빨강 대상 제외). 넘으면 가까운 순. 0 = 끔. |

- 값은 매 프레임 `EnemyOutlineDrawList` 로 넘기므로 Play 중 인스펙터로 바꾸면 바로 보인다.
- 피처 쪽(`PC_Renderer` → `EnemyOutlineFeature`): `_occludeBias`(0.2m), `_injectionPoint`(550).

## 스킬 범위 외곽선 (할 일 58 후속)

스킬 **범위 표시가 떠 있는 동안** 그 스킬이 **실제로 맞힐 영역** 안의 살아 있는 적에도 빨간 외곽선을 켠다. 오너 로컬·네트워크 없음.

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
- **합집합 규칙**: 호버 대상 ∪ 범위 대상(둘 다 빨강). 상한은 범위 세트에만 센다.
- **비용**: 대상 렌더러 수집은 몬스터가 세트에 들어올 때만. 버퍼·리스트·렌더러 묶음은 재사용(프레임 할당 0 지향).
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
- 겹침 버퍼는 64(콜라이더 수). 몬스터 하나가 콜라이더 여러 개면 한 마리로 합친다.

## 나를 노리는 몬스터 (할 일 58 후속 3, 주황)

서버의 몬스터 현재 대상(`MonsterBase._target`, Transform)은 원래 **클라에 복제되지 않았다**(서버 전용 필드, 복제 RPC 도 없음). 그래서 표시용 복제 값 하나를 추가했다(경석 영역, 사용자 승인 2026-10-20).

| 항목 | 내용 |
|---|---|
| 추가한 필드 | `MonsterBase._aggroTargetId` — `NetworkVariable<ulong>`, ReadPermission Everyone, WritePermission Server, 기본 0. 읽기 프로퍼티 `MonsterBase.AggroTargetId`. 대상 Transform → id 캐시 `MonsterAggroTargetRule.TargetIdCache`(대상이 바뀐 틱에만 `GetComponentInParent<NetworkObject>`). |
| 쓰는 시점 | 서버, `MonsterBase.Update` 매 프레임 FSM 틱 **앞**(직전 틱 결과 — 한 프레임 늦음, 표시엔 무관). 값이 바뀔 때만 쓴다. |
| 쓰는 값 | `MonsterAggroTargetRule.ReplicatedTargetId(state, dead, 서버로직정지, 대상id)` — **Chase·Attack** 이고 살아 있고 휴면(`SetDormantServer`)·연출 정지(`SetServerLogicSuspended`)가 아닐 때만 대상의 NetworkObjectId, 그 밖(Idle·Hit·Groggy·Return·Knockback·Dead·대상 없음) 0. |
| 읽는 곳 | `PlayerEnemyHoverOutline.ResolveAggroTargets`(오너 로컬). NGO `SpawnManager.SpawnedObjectsList` 를 0.25초마다 훑어 `MonsterBase` 목록을 만들고, 매 프레임 `AggroTargetId == 내 Player.NetworkObjectId` && 살아 있음(`CursorUnitPick.IsSelectable`)인 몬스터를 후보로. |
| 동작 영향 | 없음 — FSM·판정·어그로 규칙은 이 값을 읽지 않는다. |

- 대상: `MonsterBase` 를 상속하는 전부 — 일반 몹·중간보스(GauntletBot·SpinnerBot·WallBot)·**23호(TwentyThreeBoss) 포함**(모두 base `Update` 를 그대로 쓴다).
  **웰즈(BossWells)는 제외** — `MonoBehaviour` 인 23호 탑승 장식이라 자기 대상이 없다(23호 본체가 주황이 된다).
- 고르기(`MonsterAggroTargetRule.SelectAggro`): 빨강 세트(호버·스킬 범위)에 든 몬스터는 빼고(**빨강 > 주황**, 한 몬스터는 한 색), 가까운 순, 상한 `aggroMaxTargets`(8).
- 켜짐/꺼짐: 몬스터가 나를 물고 Chase/Attack 이면 켜지고, 추적 해제(대상 잃음·리쉬 복귀 Return)·피격 경직(Hit)·그로기·넉백 중·사망(HP 0 → 즉시)·휴면·연출 정지면 꺼진다.
  피격 경직·그로기 동안 잠깐 꺼졌다 다시 켜지는 것은 "추적 또는 공격 중" 정의 그대로다.
- MPPM: 피어마다 자기 Player 의 id 로 판정한다(같은 몬스터가 A 화면에선 주황, B 화면에선 무색).
- 네트워크 비용: 몬스터당 ulong 하나, 대상이 바뀔 때만 전송.

## 연출과의 관계

- `HitFlash`·사망 틴트(MPB `_BaseColor`/`_Color`): 마스크는 덮어쓰기 머티리얼로 그리므로 무관. 원본 렌더러 MPB 도 안 건드린다.
- 23호 체공 숨김·붕괴 사망(`enabled=false`): 매 프레임 렌더러 켜짐을 보고 목록에서 뺀다.
- 사망 디졸브: HP 0 이 되면 다음 LateUpdate 에 세트에서 빠진다.
- 그로기: 렌더러·머티리얼을 건드리지 않아 무관(빨강은 살아 있는 동안 계속, 주황은 위 규칙대로 꺼짐).
- **23호 인터럽트 표시(`DissolveOverlay`, Transparent 큐)**: 림 방식에선 투명 정렬에 따라 림이 그 밑에 깔릴 수 있었다.
  지금은 합성이 **투명 패스 뒤(550)** 카메라 컬러에 얹히므로 깔리지 않는다 — 기존 한계 해소. 대신 외곽선은 바깥 윤곽이라 몸 안쪽의 투명 표시와는 겹치지 않는다.

## 이전 방식 (기록)

- 1차(3acd0747): 같은 메시 자식 겹 렌더러(`__HoverOutline`) + `Zone/InteractOutline`(부풀린 Cull Front 헐).
- 2차(47cda62e): 겹 렌더러 + 림(프레넬, `Project/Enemy Hover Rim`) — `|N·V|` 작은 곳만 칠함. 안쪽 접힘에도 선이 생기고, 하드 엣지 메시에서 두께가 갈리고, 투명 큐에 깔렸다.
- 3차(현재): 화면 공간 마스크. 겹 렌더러(`HoverOutlineOverlaySet`)·림 셰이더(`EnemyHoverRim.shader`)·`HoverRimSettings`·`MA_MonsterHoverOutline.mat` 는 삭제했다.

## 알려진 한계 / 확인 필요

- **Play 확인 필요(사람)**: 3px 균일 두께, 안쪽 선 없음, 23호 하나의 윤곽, 겹친 두 몬스터·벽 가림·발밑 바닥(occludeBias 0.2), 주황 켜짐/꺼짐(어그로 해제·사망), MPPM 각자 기준.
- 에디터 캡처(Play 없이, 전/후): `C:/UnityProject/_lane-artifacts/MainProject-Worktree2/58-mask/`.
- 마우스가 HUD 위에 있어도 레이는 나간다(스킬 조준과 같은 동작).
- `PixelScanline`(RetroCRT) 포스트가 블록 재샘플을 하므로 실제 화면 굵기는 블록 크기에 따라 달라 보일 수 있다(실루엣 윤곽선과 같은 조건).
