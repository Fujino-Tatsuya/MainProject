# ▶▶▶ 계획 = **할 일 83 — 플레이어 스탯 5종 종합(클라·서버 같은 계산) + 서버 2차 검증** (2026-10-21 · grill 확정 · ✅ 승인 · ✅ 1차 공격력 `fbb29438` · ✅ 2차 5종 — 컴파일 0 · EditMode 2680/0 · ⏳ 은희 Play/MPPM)

> 브랜치 `fix/TooltipValueClient`(기준 origin/development `c7057322`). 담당 은희(코어 = 본인 영역). 증상: 클라 스킬 툴팁 `{dmg}` 가 틀림.

## 2차 확장 (2026-10-21 grill, 은희) — 공격력만이 아니라 **플레이어 스탯 5종 전부**
- 범위: 공격력·이동속도·공격속도·방어력·최대 체력. 새 스탯은 표(`PlayerStatKind`)에 한 줄 추가로 늘린다.
- 모든 스탯 = 기본% · 전체% 2단계(단계 내 더하기). 가하는 피해% 는 공격력 전용 3단계로 유지.
- 상태이상: 기존 `XModifier` = 전체%, 기본% 타입 추가 — `MoveSpeedBaseModifier`(1<<16)·`AttackSpeedBaseModifier`(1<<17)·`DefenseBaseModifier`(1<<18)·`MaxHpBaseModifier`(1<<19). `BaseAttackDamageModifier`(1<<14) 는 그대로.
- 이름 정리: `PlayerAttackStats`→`PlayerStats`, `AttackStatFormula`→`PlayerStatFormula`, `IAttackStatSource`→`IPlayerStatSource`, `AttackStatReport`→`PlayerStatReport`(5종 + 가하는 피해, 요청 6곳 그대로).
- 기본값(피어마다 로컬): 공격력 `Player.attackDamage` · 이동속도 `PlayerMovement.BaseMaxSpeed` · 공격속도 `Player.attackSpeed` · 방어력 `Player.defense` · 최대 체력 `Player.maxHp`.
- 소비처 연결(사용자 무선호 → 추천안): `Unit.Final*` 5개 virtual → Player 가 종합값으로 덮어씀(HUD·회복·추락 피해 등 기존 FinalMaxHp 소비처 자동 반영) · **이동속도**: `PlayerMovement` 의 상태이상 배율 → 종합 배율 · **방어력**: 피해 경감(`Unit.ApplyMitigatedHealthDamage`)이 플레이어는 종합 방어력을 읽음(몬스터 그대로) · **공격속도**: 값만 낸다(플레이어 소비처 없음 → 후속).
- 동작 변화: 이동속도도 겹치면 곱→더하기(예: 이동 특성 1.2 + 거너 Q 감속 0.5 = 0.6 → 0.7). 방어력 상태이상이 처음으로 판정에 들어간다(현재 거는 곳 없음).

## 사실
- 원인: `Player.cs:259` `if (IsServer) Initialize(...)` → `Unit._attackDamage` 가 클라에서 0 → `FinalAttackDamage`·툴팁 `{dmg}` 가 0 기준.
- 데미지 스냅샷은 서버가 `FinalAttackDamage` 로 만든다(`PlayerSkillController:615`·`DefaultAttackController:870`·`GunnerBasicAttack:441`·`AssassinBasicAttack:619`·`FirstMeleePassive:166`·거너 레이저 데이터 3곳). 툴팁은 `SkillTooltipContracts:72`.
- 기본 공격력 원본 = `Player.attackDamage`(xlsx → 빌드 포함, 모든 피어가 같은 값). 상태이상 배율은 `StatusEffectController` 의 `NetworkList` 로 이미 복제(현재 같은 타입끼리 **곱**, 스택은 거듭제곱).
- 튜토리얼 공격력 특성 = 영구 상태이상 `AttackDamageModifier`(배율 ≥1, `TutorialTraitDirector:797`).
- `ChangeAttackDamageValue(Rpc)` 는 호출처 없음.

## 결정 (grill 2026-10-21, 은희)
1. **실제 데미지 = 서버 값 유지.** 권한 구조 불변. 클라 종합은 표시(툴팁·HUD)와 검증 보고용.
2. **같은 종합 클래스를 서버·클라가 쓴다** — 서버 판정 값도 이 클래스에서 나온다(공식 한 곳).
3. 공식(단계 내 더하기):
   `최종 공격력 = round( 기본 × (1 + Σ기본공격력%) × (1 + Σ전체공격력%) )`, 0 이상
   `피해 = round( (최종 공격력 × 스킬 계수 + 고정값) × (1 + Σ가하는피해%) )`, 0 이상 — 이후 거너 열 단계 배율 등 기존 후처리는 그대로.
4. 상태이상 스탯 타입을 3개로: `BaseAttackDamageModifier`(신규 1<<14) · `AttackDamageModifier`(기존 값 유지 = **전체 공격력**) · `DealtDamageModifier`(신규 1<<15). 인스턴스 magnitude 1.2 = +20%, 0.8 = −20%, 스택 = %×스택 수(더하기). → **기존 공격력 배율 스택이 거듭제곱→더하기로 바뀐다**(튜토리얼 특성은 스택 1이라 수치 동일).
5. 앞으로의 영구 가산(특성·성장)은 **출처 기록만 복제** — 서버는 "무엇을 얻었는지"(id)만 복제, 수치는 각 피어가 같은 SO 에서 읽어 종합. 이번엔 입력 인터페이스(`IAttackStatSource`)만 두고 구현체는 만들지 않는다(현재 특성은 상태이상 경로로 이미 들어옴).
6. **서버 2차 검증 = 공격/스킬 시작 요청마다.** 오너가 자기 종합 결과(최종 공격력, 가하는 피해%)를 요청 RPC 에 실어 보내면 서버가 자기 값과 비교, 다르면 `Debug.LogWarning`(clientId·양쪽 값·요청 종류). 서버 쪽 입력이 바뀐 뒤 **유예 시간(기본 0.5초, 인스펙터 필드)** 안의 불일치는 복제 지연으로 보고 무시. 값은 서버 것을 쓴다(경고만).
   대상 RPC: `PlayerSkillController.RequestUseSkillRpc` · `DefaultAttackController.RequestStartAttackRpc`·`RequestQueueNextAttackRpc` · 거너·어쌔신 기본 공격 시작 요청(데미지 스냅샷을 만드는 서버 지점마다).
7. 커밋 `207c24ce`(기본 공격력 NetworkVariable 복제)는 **되돌린다**(로컬, push 전).

## 구조
- 신규 `Assets/1.Scripts/Player/Stats/`:
  - `AttackStatFormula`(순수 static) — 위 공식·상태이상 % 합산·불일치 판정(유예 포함).
  - `PlayerAttackStats`(Player 프리팹 base 의 컴포넌트, 비네트워크) — 입력: `Player.attackDamage`(로컬) + `IStatusEffectFacade`(복제) + 등록된 `IAttackStatSource`. 출력: `FinalAttackDamage`·`DealtDamageMultiplier`·`ResolveDamage(계수, 고정값)`·서버용 `LastInputChangedTime`.
  - `IAttackStatSource` — 기본%/전체%/가하는피해% 기여 반환 + 변경 이벤트.
- `Unit.FinalAttackDamage` 는 `IAttackStatProvider` 가 붙어 있으면 위임(기존 `IStatusEffectFacade` 조회 패턴), 없으면 지금 식(몬스터 무변경).
- 데미지 스냅샷 지점들 → `ResolveDamage` 로 통일(가하는 피해% 적용 위치를 한 곳으로). 툴팁 `SkillTooltipDamage` 도 같은 식.
- `StatusEffectController.GetStatMultiplier` 는 이동속도 등 다른 스탯용으로 그대로. 공격 3타입은 새 합산 API.
- `UiStrings` 상태이상 이름 2개 추가, `StatusEffectType` 스탯 마스크에 신규 2개 추가.

## 위험·범위 밖
- 공격력 배율 스택 규칙 변경(4번) — 현재 스택 2 이상 쓰는 공격력 효과가 있으면 수치가 달라진다(구현 때 SO 전수 확인해 보고).
- 몬스터 공격력·이동속도/공격속도/방어력 종합은 범위 밖. `ChangeAttackDamageValue` 는 손대지 않는다.
- 플레이어 프리팹은 base(`Player.prefab`)에 컴포넌트 추가만. xlsx·SVN 무수정.

## 수용·검증
- EditMode: 공식(예: 기본 100, 기본 +10%·+20%, 전체 +50% → 195; 가하는 피해 +20%, 계수 1.8 → 421)·스택 더하기·음수 클램프·유예 판정·툴팁 = 서버 스냅샷 식 일치. 기존 툴팁·데이터 테이블·상태이상·플레이어 테스트 0 실패.
- Unity MCP Refresh → 컴파일 0 → EditMode → 로컬 커밋(push 금지). Play 진입 금지.
- 🟧 Play/MPPM(사용자): 호스트·클라 툴팁 `{dmg}` 동일 · 특성/버프 뒤 양쪽 갱신 · 정상 플레이 중 검증 경고 0 · 실제 데미지 수정 전과 동일(스택 1 기준).
- 문서: `Docs/tech/` 공격 스탯 문서 신규(공식·타입·검증), `data-table.md` §9 `{dmg}` 설명 갱신, `CONTEXT.md` 용어(기본/전체 공격력, 가하는 피해).

# ▶▶▶ 계획 = **거너 R 추적 레이저 연출 재제작 — 범위 장판 · 대상별 조준(좁혀지는 원) · 대상별 위성 레이저+땅 파문** (2026-10-21 · grill 확정 · ✅ 승인 · ✅ 1단계 MyProject 제작 · ✅ 2단계 반입·코드·배선(MyProject 저장본 그대로, dotnet 빌드 오류 0) · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-21, Q1~Q12 전부 추천안). **게임플레이 무변경** — 0.5초마다 반경 2m 안 적 전부 즉시 피해(`GunnerTrackingLaser.DamageTick`), 연출을 그 박자에 맞춘다.

## 사실
- 레이저 = 서버 소유 NetworkObject `2.Prefabs/Player/Gunner/GunnerTrackingLaser.prefab`. 서버가 이동·피해·수명 계산, 복제는 위치(NetworkTransform)·반경(NetworkVariable)뿐. 연출 = 자식 `Visual` 의 `FX_GunnerUltLaser`(반경×2 균일 배율) 하나 — 맞는 적을 모른다.
- `GunnerTrackingLaser.cs` = 은희 영역(G7). 레이저 프리팹 연출 = 민경(10-06·10-08).
- 참고: MyProject `Export/Player2/R/Effect_05_Nuke.prefab`(패키지 스크립트 `DelayActive` 등 4종 타임라인 — 초반 = `NukeMarks` 바닥·세로 표식) · `Effect_54_SateliteCannon.prefab`(스크립트 없음, 파티클 6개 원샷).

## 결정
| 항목 | 결정 |
|---|---|
| 박자(Q1-a) | 연출이 실제 피해 박자를 그대로 보여 준다 — 대상별 원이 피해 간격 동안 좁혀져 0 = 피해 순간, 그때 레이저 |
| 제작(Q2) | MyProject 빌더(`Tools/VFX/Gunner Ult (R 위성 레이저) 빌드`) → 민경 확인 → 반입. 3번 = SateliteCannon 복제 개조, 2번 = NukeMarks 파티클만 떼어 스크립트 없이 재구성 |
| 색(Q3) | 거너 청록·하늘 + 흰 코어, 예고(2번)만 따뜻한 강조색. 정확한 값은 MyProject 확인 때 |
| 1 장판(Q4) | 테두리 링 + 옅은 채움 + 테두리 도는 눈금(스캔선 없음). 루프, 레이저 따라 이동, 지름 1유닛 저작(반경×2 배율 그대로) |
| 2 조준(Q5) | 조여 드는 꺾쇠 4개 + 좁혀지는 원 + 중심 점 깜빡임 + 아주 옅은 하늘 조준선. 루프, 범위 안 적마다 하나·추종 |
| 3 레이저(Q6) | 수직으로 내리꽂기 — 흰 코어 + 글로우 + 섬광 + 파문 링 + 불꽃(그을음 없음). 원샷, 맞은 위치마다. 레이저+파문 = **한 프리팹·한 엔트리**(Q9) |
| 퇴장(Q7-b) | 레이저 제거 때 장판이 0.3초 줄어들며 사라짐(디스폰 직전 연출만 떼어 냄) |
| 동기화(Q8-a) | 서버가 피해 순간마다 **맞은 위치 목록 + 피해 간격**을 전 피어에 RPC → 이펙트 3. 조준 표식은 각 피어가 매 프레임 범위 안 적을 직접 계산(새로 들어온 적도 예고), 원 진행도 = 마지막 신호 이후 시간 ÷ 간격 |
| 코드(Q9) | 신규 `GunnerTrackingLaserView`(민경) — 장판 루프·대상별 조준 루프·신호 받으면 레이저·퇴장. `EffectManager` 엔트리 3(장판 루프 · 조준 루프 · 레이저 원샷) 카탈로그 등록. 원 진행도는 조준 프리팹 안의 작은 컴포넌트(`SetProgress`)로 — MyProject·MainProject 같은 guid. `GunnerTrackingLaser` 에는 신호 함수·호출만 **추가**(은희 handoff). 오프라인(테스트 씬)도 같은 연출 |
| 크기(Q10-b) | 조준 표식·원은 대상 몸 크기(콜라이더 반경) 따라, 상·하한. 레이저 굵기 고정 |
| 정리(Q11-a, Q12-a) | 기존 `FX_GunnerUltLaser` 는 프리팹에서 떼고 파일은 남김. 시전 총구 섬광 `FX_GunnerUltCast` 그대로 |

## 단계
1. MyProject: 빌더 + 프리팹 3종(`Export/Player2/R/FX_GunnerUlt_Zone`·`_Lock`·`_Strike`) + 미리보기(움직이는 더미 대상 · 0.5초 박자) → ⏳ 민경 확인
   - ✅ 작성: `1.Scripts/Editor/GunnerUltVfxBuilder.cs`(메뉴 `Tools/VFX/Gunner Ult (R 위성 레이저) 빌드` → 재질 사본 `Export/Player2/R/Materials/M_GUlt_*` · 프리팹 3 · 씬 `Scenes/GunnerUltPreview.unity`) · `1.Scripts/GunnerUltTester.cs`(미리보기 — MainProject View 와 같은 규칙) · `1.Scripts/TelegraphRing.cs`(guid `f4c85f3d33ed4596b0eecde3e202d475` — MainProject 에 같은 guid 로 반입)
   - 재료: 장판 = Mark_1(링)·Mark_5(톱니)·Ring_2(채움) 청록 / 조준 = Mark_7(꺾쇠)·Mark_1(원) Nuke 주황 + Particle_01(점·하늘선) / 레이저 = SateliteCannon 사본 ×0.6, 루트 한 발 버스트, 청록 재질. 셰이더 `Shader_IntegratedEffect`(MainProject 에 같은 guid 있음)
2. MainProject(🔴 Unity 끔): 반입(같은 guid) · 엔트리·카탈로그 · `GunnerTrackingLaserView` · `GunnerTrackingLaser` 신호 추가 · 프리팹 배선 · handoff
   - ✅ 10-21: 프리팹 3 → `Common/Players/Player2/R_TrackingLaser/` + 엔트리 3(Strike 1.2초·prewarm 8 / Lock prewarm 6) · 재질 12 → `_Materials/Player2/` · 팩 텍스처 7·`Cylinder_01.fbx` · `TelegraphRing.cs`(같은 guid) · 카탈로그 `Gunner_R_*` · `GunnerTrackingLaserView`(NetworkBehaviour — NetworkVariable 반경·간격·적 레이어 + Reliable RPC 맞은 위치) · `GunnerTrackingLaser` 호출 2곳 추가 · 레이저 프리팹에서 `FX_GunnerUltLaser` 제거 · `GunnerUltSetup` 가드 · [handoff-gunner-r-vfx](Docs/tech/handoff-gunner-r-vfx.md)
   - ⚠️ 반입 시점 Lock 프리팹: `Brackets` 가 `Ground` 밖(루트 직하, X 90°)이라 `TelegraphRing` 이 회전을 (0,0,각도)로 덮어써 세로로 선다 — 고칠 방법(원위치 or 스크립트가 저작값 기준으로) 민경 결정 대기
3. ⏳ 민경 Play/MPPM: 호스트·클라 모두 — 대상별 표식이 범위 출입에 맞게 붙고 떨어짐, 원 0 순간 레이저와 피해 숫자 일치, 6초 뒤 장판 줄어들며 퇴장, 늦게 들어온 클라

---

# ▶▶▶ 잠긴 계획 = **할 일 57.3 — 웰즈 드론 로컬 풀 + 로딩 예열** (2026-10-20 · 위임 승인)

> Handoff `54adb49f-b945-46bc-adff-08c1f727e3bc` · 브랜치 `feature/EnemyC3PrewarmBoss` · 선행 `3ff61ba1`/`f378714d`.

## 목표와 결정
- `WellsDroneAttack`의 클라이언트 로컬 드론 모델을 매 공격 Instantiate/Destroy 하지 않고 피어별 비활성 풀에서 재사용한다. 현재 한 보스·컴포넌트가 새 Lock 전에 Cancel 하므로 보관 상한은 **1기**다.
- 드론은 NetworkObject가 아니므로 NGO prefab handler는 쓰지 않는다. 서버 타이머·판정·RPC 중계는 바꾸지 않는다.
- 풀 인스턴스는 `DontDestroyOnLoad` 숨김 루트 아래 살아 로딩 씬→맵, Alt+0, 두 번째 판, 보스 디스폰을 건넌다. `SubsystemRegistration`의 `ResetForPlay`에서 풀·PlayableGraph를 명시 정리해 도메인 리로드 끔 재진입도 처리한다.
- Collider 비활성·Rigidbody kinematic·Animator 준비는 생성 때 한 번만 한다. DashStart/DashLoop는 한 PlayableGraph의 두 입력으로 미리 만들고 공격마다 시간·가중치만 재설정한다.
- 정본 `WellsDroneData.asset`은 Resources 밖이다. 에셋 이동/GUID 변경 없이 `Resources/Monster/WellsDronePrewarmCatalog.asset`이 정본 SO를 참조한다. 예열은 실제 모델·클립 데이터로 Mark→Lock→고정/낙하 Tick→클립 시작/루프 전환→반납을 돈다.
- 첫 그리기의 lazy `Loading.ReadObject`를 앞당기기 위해 모델의 mesh/material/texture를 순회해 접근하고, 전용 작은 RenderTexture에 CommandBuffer로 한 번 그린 뒤 `GL.Flush`한다. 게임 카메라·화면·판정에는 노출하지 않는다.
- 조준/착탄 뷰 루트는 기존 컴포넌트당 1회 재사용, 폭발은 기존 EffectManager 풀을 그대로 둔다.

## 위험·범위 밖
- URP/DX12의 PSO 준비 보장은 플랫폼별로 다르므로 빌드 GPU 첫 프레임은 Play 확인 항목으로 남긴다. 이번 결정적 테스트는 인스턴스/Animator/graph 수와 Instantiate 0 계약만 단언하고 시간은 단언하지 않는다.
- 아트·xlsx·기존 `.meta` GUID·서버 로직·`TwentyThreeBoss` RPC는 수정하지 않는다.

## 수용·검증
- 동일 인스턴스 재사용, 반복 Lock/Cancel/Impact 후 풀/Animator/PlayableGraph 수 불변, 예열 뒤 첫 Lock 생성 0, 두 번째 예열 무동작, Reset 뒤 재실행을 EditMode로 검증한다.
- Unity MCP로 Refresh→컴파일 0→전체 EditMode 0 실패→57.3 측정 메뉴 순서. Play 진입은 금지하고 문서 §9.4/9.5/9.9~9.11 및 외부 산출물 `57.3/`을 갱신한다.
# ▶▶▶ 계획 = **잡몹 5종 + 엘리트 3종 폭발 사망 — ExplodeDeath(조각이 위·바깥으로 튐) + FX_Death_Explosion_01/02 (디졸브 파티클 없음)** (2026-10-21 · grill 확정 · ✅ 승인 · ✅ 반입·코드·프리팹 — dotnet 빌드 오류 0 · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-21). 대상 = `ChompBot`·`HumanoidBot`·`MortarBot`·`PeekABot`·`TeslaBot`. ~~`GauntletBot`·`SpinnerBot`·`WallBot` 은 지금 붕괴 그대로.~~ → **2차(10-21 민경 요청): 엘리트 3종도 폭발** — 아래 「2차」.

## 사실
- 지금 사망 = `CollapseDeath`(`1.Scripts/Monster/Collapse/`, 민경 작성 10-06) — `MonsterBase` 가 사망 클립 끝 − `leadBeforeClipEnd`(0.1초)에 서버에서 `IDeathEffect.Play` → Reliable RPC 로 전 피어가 로컬 재생. 몬스터는 `GetComponent<IDeathEffect>()` 로 **하나만** 집는다.
- 조각 = `MonsterCollapseDebris.Spawn`(본 단위 조각 + Rigidbody). 처음 속도는 **수평 바깥 0.5m/s 뿐**(위로 0), 1.2초 굴러다닌 뒤 0.6초 디졸브 → 조각 루트 `Destroy`.
- 파티클 `FX_Dissolve_Death`(수명 2초, 엔트리·카탈로그 없음)는 지금 **붕괴 시작 때** 조각 루트의 자식으로 생긴다 → 디졸브 시작 때 같은 방식으로 내면 0.6초 만에 루트와 함께 잘린다.
- `FX_Death_Explosion_01`(MyProject `Export/Monsters/Death/`) = 파티클 4개, 1초, 루프 아님. 의존: `Mat_fx_HCFX_Circle_01`·`Mat_fx_HCFX_Sharp_01`·`Fbx_fx_Round_01` 은 MainProject 에 있음, **`Mat_fx_HCFX_Dust_02.mat` + `Tex_fx_HCFX_Dust_02.tga` 신규**(셰이더 `HCFX_Particle2ndColor` 는 있음).
- 5종 프리팹의 `CollapseDeath` 를 덮어쓰는 씬·존 프리팹 오버라이드 없음. `MonsterCollapseSetup`(에디터 도구)은 재실행하면 `CollapseDeath` 를 다시 붙인다.

## 결정
| 항목 | 결정 |
|---|---|
| 방식(Q1-b) | 새 컴포넌트 `1.Scripts/Monster/Collapse/ExplodeDeath.cs`(NetworkBehaviour, `IDeathEffect`) — `CollapseDeath` 와 같은 RPC·타이밍 구조. 5종은 `CollapseDeath` 를 떼고 `ExplodeDeath` 를 붙인다(공통 칸 값은 그대로 옮김). `CollapseDeath` 코드는 무변경 |
| 조각 공유(Q7-a) | `MonsterCollapseDebris.Config` 에 **추가만**: 바깥 속도 범위 · 위로 튀는 속도 범위(사라지기 시작 콜백은 3차에서 뺌. 구현 때 "퍼지는 중심 높이"는 뺌 — 바깥 방향이 수평이라 높이와 무관, 높이는 이펙트 위치에만 씀). 기본값 0/null = 지금과 똑같음(붕괴 3종 무영향) |
| 타이밍(Q2-a) | 지금처럼 사망 클립 끝 0.1초 전(`leadBeforeClipEnd`·`delayAfterClipEnd` 그대로) |
| 세기(Q3-a, Q11) | 바깥 1.5~2.5m/s · 위로 2~3m/s · 회전 180~360도/초, 사방 고르게(타격 방향 안 실음). 인스펙터 조정 |
| 폭발 이펙트(Q4-a) | `50.Art/VFX/Common/Monsters/Death/FX_Death_Explosion_01.prefab`(같은 guid) + `_Entry`(원샷) → 카탈로그 `몬스터` 묶음 `Mob_Death_Explosion`. 조각이 터지는 순간 `EffectManager.Play`(전 피어 로컬) |
| 위치·크기(Q5-a) | 몬스터마다 `burstHeight`(이펙트 위치, 루트 + 위로) · `explosionScale`. **`burstHeight` 0 = 터지는 순간 몸 렌더러 경계 중심(자동)** — 5종 콜라이더가 모두 기본값(2m 캡슐)이라 오프라인 계산 근거가 없어 런타임 자동으로 대체. 배율은 1로 두고 Play 에서 조정 |
| 디졸브 파티클(Q4·Q8-a·Q9-a) | ❌ **3차(10-21 민경)에서 뺌** — 폭발 때 디졸브 파티클 없음. 엔트리·카탈로그 `Mob_Death_Dissolve`·콜백 삭제. (원래: 조각이 디졸브를 시작할 때 조각들의 평균 위치에서 `FX_Dissolve_Death` 1회. 새 `FX_Dissolve_Death_Entry`(같은 폴더 `Common/Dissolve/`) → 카탈로그 `Mob_Death_Dissolve`, `EffectManager.Play` — 조각 루트와 따로 살아 잘리지 않음. 5종의 `particlePrefab` 직접 생성은 안 씀) |
| 정리 시간(Q6-a) | 굴러다님 1.2초 · 디졸브 0.6초 그대로 |
| 도구 가드(Q10) | `MonsterCollapseSetup` 이 `ExplodeDeath` 가 있는 프리팹은 건너뜀 |
| 신규 아트 | `Mat_fx_HCFX_Dust_02.mat` → `_Materials/`, `Tex_fx_HCFX_Dust_02.tga` → `Textures/`. 🔴 SVN add(프리팹·엔트리 포함) |
| 공유 | 몬스터 프리팹은 경석 영역 → 짧은 handoff(`Docs/tech/handoff-monster-explode-death.md`) |

## 2차 — 엘리트 3종(GauntletBot·SpinnerBot·WallBot) (10-21 민경 요청 · ✅ 구현 — dotnet 빌드 오류 0 · ⏳ Play)
| 항목 | 결정 |
|---|---|
| 컴포넌트 | 셋 다 `CollapseDeath` → `ExplodeDeath`(같은 자리, 공통 칸 그대로). 이제 `CollapseDeath` 를 쓰는 프리팹은 없다(코드는 남김) |
| 세기 | **중간**: 바깥 3~5m/s · 위로 3~5m/s · 회전 360~720도/초 |
| 폭발 이펙트 | MyProject `Export/Monsters/Death/FX_Death_Explosion_02.prefab` → `50.Art/VFX/Common/Monsters/Death/`(같은 guid) + `_Entry`(prewarm 2). 의존 신규 3(`Mat_fx_HCFX_Glow_Long_01_add`·`Mat_fx_HCFX_Impact_01_add` → `_Materials/`, `Tex_fx_HCFX_Glow_Long_01.tga` → `Textures/`). 🔴 SVN add |
| 고르기 | `ExplodeDeath.explosionEffect`(EffectEntry) 신설 — 비우면 카탈로그 `Mob_Death_Explosion`(잡몹 5종은 비움), 엘리트 3종은 `FX_Death_Explosion_02_Entry`. 카탈로그에 `Mob_Death_Explosion_Elite`(prewarm 등록) |
| 그대로 | 위치 자동(`burstHeight` 0) · 배율 1 · 정리 시간 · 타이밍 |

## 변경 파일
- 신규: `ExplodeDeath.cs` · `FX_Death_Explosion_01.prefab`·`_Entry` · Dust_02 재질·텍스처 · handoff
- `MonsterCollapseDebris.cs`(Config 추가) · `MonsterCollapseSetup.cs`(가드 1줄) · `EffectCatalog.cs`·`.asset` · 5종 몬스터 프리팹 · CONTEXT

## 검증
- dotnet 빌드 오류 0 · guid·내부 참조 누락 0 · 붕괴 3종 프리팹 무변경. ⏳ 민경 Play/MPPM: 5종 처치 → 휘청 → 조각이 위·바깥으로 튀며 폭발 이펙트 → 1.2초 뒤 디졸브(파티클 없음), 호스트·클라 둘 다 · 붕괴 3종은 기존과 같음.

---

# ▶▶▶ 계획 = **플레이어 상태이상 루프 VFX — SO 표(상태 → 이펙트) + Presenter, 첫 항목 기절 → FX_Faint** (2026-10-20 · grill 확정 · ✅ 승인 · ✅ 반입·코드·SO·프리팹 — dotnet 빌드 오류 0 · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-20). 상태이상 = `StatusEffectController`(서버 권위 `NetworkList`, 전 피어 복제). 지금 플레이어 기절이 실제로 나는 곳 = **23호 대쉬 벽꽝(1초)** 뿐(착지 보호 기절·AreaZone 은 경로만 있고 데이터상 안 남).

## 결정
| 항목 | 결정 |
|---|---|
| 반입 | MyProject `Export/Common/States/FX_Faint.prefab` → `50.Art/VFX/Common/States/FX_Faint.prefab`(같은 guid) + `FX_Faint_Entry.asset`(루프, outro 0.3초). 의존 신규 2(Klaus `Mat_fx_HCFX_Star_01.mat` → `_Materials/`, `Tex_fx_HCFX_Star_01.tga` → `Textures/`). 카탈로그 `Player_Faint`(prewarm). 🔴 SVN add |
| 변화 알림(6-a) | `StatusEffectController` 에 **추가만**: `public event Action EffectsChanged` + `OnNetworkSpawn`/`OnNetworkDespawn` 에서 `effects.OnListChanged` 구독·해제 → 전 피어에서 목록 변화(추가·갱신·해제·만료)마다 발화. 기존 함수·동작 무변경. 처음 동기화(늦은 접속)엔 안 나므로 구독자가 켜질 때 직접 읽는다. 은희 영역 → handoff·CONTEXT + 민경이 메시지로 알림 |
| SO | `1.Scripts/Effects/StatusEffectVfxSet.cs` — 행 = { 상태 종류(플래그 1개) · 엔트리(루프) · 자리(Overhead/Body/Feet) · 오프셋 · 배율 · 우선순위 }. 에셋 `9.ScriptableObject/Effects/PlayerStatusEffectVfxSet.asset` **플레이어 공용 1개**, 첫 행 = 기절 → `FX_Faint_Entry`, 자리 Overhead, 오프셋 (0, 0.5, 0)(지금 소켓 값), 배율 1, 우선순위는 머리 위 상태 글자 순서(잡힘 > 공중 > 기절 > 속박 > 침묵 > 약화 > 둔화) |
| Presenter | `1.Scripts/Effects/StatusEffectVfxPresenter.cs` — 각 Variant `VFX/States` 에 부착. 필드 = SO · 자리 트랜스폼(Overhead = `TopHead` — 이번 신설, 어쌔신·팔라딘은 Armature 인스턴스 직하 / 거너는 그 아래 `VFX/`, Body·Feet 비움 → Feet 는 플레이어 루트, Body 비면 그 행 건너뜀+경고). `EffectsChanged` + `PlayerLifeCycleController.LifeStateChanged` 구독(켜질 때 한 번 직접 읽음), **살아 있을 때만**, **자리마다 우선순위 최고 1개**(다른 자리끼리는 동시). `EffectManager.PlayLooping`(핸들 직접 보관) → 바뀐 행만 Release, OnDisable/OnDestroy 에서 반납. 추가 네트워크 없음 |
| `VFX/States`·`TopHead` | 세 Variant 에 상태이상 연출 오브젝트가 원래 없어 **둘 다 신설**(계획 단계의 '기존 Faint 소켓 삭제'는 착오 — HEAD 에 그런 오브젝트가 없다). 거너는 흩어진 추가 오브젝트를 `VFX` 컨테이너로 묶는 정리가 함께 들어감 |
| 범위 밖 | 몬스터(`MonsterStatusEffect` 별도 — SO 형식은 재사용 가능하게), `GameData.xlsx`(연출 연결이라 제외), 기절 디버그 단축키 |

## 변경 파일
- 신규: `50.Art/VFX/Common/States/FX_Faint.prefab`·`_Entry` + 의존 2 · `StatusEffectVfxSet.cs` · `StatusEffectVfxPresenter.cs` · `PlayerStatusEffectVfxSet.asset`
- `StatusEffectController.cs`(이벤트 추가) · `EffectCatalog.cs`·`.asset` · `Player_Assassin`·`Player_Gunner`·`Player_Paladin.prefab`(Presenter 부착, Faint 삭제)
- 문서: handoff(새 `Docs/tech/handoff-status-effect-vfx.md`) · CONTEXT

## 검증
- dotnet 빌드 오류 0 · guid·내부 참조 누락 0. ⏳ 민경 Play / MPPM: 23호 대쉬 벽꽝 → 머리 위 별 1초 루프 후 0.3초에 사라짐, 호스트·클라·관전 화면 모두, 기절 중 쓰러지면 즉시 꺼짐, 늦게 들어온 클라도 맞음.

---

# ▶▶▶ 계획 = **어쌔신 대쉬 잔상 — 팔라딘·거너와 같은 DashAfterimage + 속도선 (검붉은 색)** (2026-10-20 · grill 확정 · ✅ 승인 · ✅ 프리팹·재질 — 코드 무변경 · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-20). 대상 = 스페이스바 공용 대쉬(`PlayerDashController` · Dash 상태)만. Q 관통 돌진 제외.

## 사실
- 잔상 = `50.Art/VFX/Common/Players/Dash/DashAfterimage.cs`(대쉬 중 포즈 BakeMesh 스냅샷·풀링·페이드) + `DashSpeedLines.cs`(본 추종 TrailRenderer). 셰이더 `Player/DashAfterimage`(알파 블렌드, 림). EffectManager·카탈로그 미사용.
- 켜고 끄기 = `PlayerSkillVfx`(NetworkBehaviour)가 Dash 상태 엣지를 보고 Reliable RPC 로 전 피어에 — 자식의 `DashAfterimage` 를 Awake 에서 자동으로 찾는다. 팔라딘·거너 프리팹에는 있고 **어쌔신엔 없다**(base `Player.prefab` 에도 없음).
- 색은 캐릭터별 재질(팔라딘 금빛 `FX_PaladinDashAfterimage`(10-20 이름 변경, 구 `FX_DashAfterimage`) · 거너 파랑 `FX_GunnerDashAfterimage`, Solidity 0.15 / 속도선 Solidity 1).

## 결정
| 항목 | 결정 |
|---|---|
| 재질 | 신규 `FX_AssassinDashAfterimage.mat`(거너 재질 복사 → 몸 (0.35,0.03,0.04)·Solidity 0.25 + 붉은 림 HDR (2,0.16,0.1)) · `FX_AssassinDashSpeedLine.mat`(붉은색 (1.6,0.18,0.12)). 같은 폴더 `Players/Dash/`. 🔴 SVN add |
| 부착 | `Player_Assassin` 루트에 `PlayerSkillVfx`(R 칸 비움 = 잔상만, 거너와 같음) + `DashAfterimage`(modelRoot = `Armature` — 몸·머리카락·양손 단검, VFX 제외 / spawnInterval 0 · 수명 0.2(팔라딘·거너 실제 값 — 0.45 는 스크립트 기본값) · 최대 80 · safetyTimeout 2) + `DashSpeedLines`(searchRoot = `Armature`, 기본 이름 힌트 head·hand·foot, 선 0.2초·폭 0.05→0) |
| 코드 | 변경 없음(프리팹·재질만). `PlayerSkillVfx` 는 플레이어 공용 → handoff 공유 |

## 검증
- guid·내부 참조 누락 0 · cmp. ⏳ 민경 Play / MPPM: 스페이스바 대쉬마다 검붉은 잔상 + 붉은 속도선, 호스트·클라 어느 쪽이 대쉬해도 양쪽에 보임, Q 돌진엔 안 나옴.

---

# ▶▶▶ 계획 = **어쌔신 일반 E VFX 반입·배선 — 단검 날 발광(오버레이) · 오라 · 차지 · 강타 슬래쉬 (EffectSocketPlayer)** (2026-10-20 · grill 확정 · ✅ 승인 · ✅ 반입·코드·프리팹·클립 배선 — dotnet 빌드 오류 0 · ⏳ 민경 Play/MPPM · EditMode)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-20). 기획 §8(E = Buff + 강화 준비, 다음 좌클릭 = 강타 `Combo_Attack_02_01` 1회 후 소모, 변신·쓰러짐·사망에 해제). 10-16 일반 E 계획(아래)의 발광·소켓·뷰 배선은 커밋되지 않았고 v18 비주얼(`c5c80d5f`)로 단검 재질이 Flat Kit 1슬롯이 됨 → **다시 한다**.

## 사실
- 단검 `Dagger_Weapon_L/R` = 슬롯 1개 `FK_Assassin_Body`, 루트 배율 1.25(v18). 단검 FBX 10-06 이후 무변경 → `Dagger_BladeMask.png`(UV0 칼날) 그대로 유효. `Dagger_Blade_Glow.mat` 있음.
- `AssassinEnhanceReadyView` 는 코드만 있고 미부착. `Assassin_Armature/VFX/EnhancedReady` 빈 오브젝트 있음, `VFX_EnhanceAura` 소켓 없음.
- `Player_Assassin` 에 민경이 만든 `Armature/VFX/EnhancedSlash` 트랜스폼. 강타 클립 0.2167초(= Hit)에 빈 이벤트.
- 🔴 `AssassinVisualPrefabTests.BodyAndDaggerSlotsUseFlatKitBody`(은희) 가 단검 슬롯 = FK 1개만 허용 → 오버레이를 넣으면 실패.

## 결정
| 항목 | 결정 |
|---|---|
| 발광 대상 | **양손** 단검. 각 렌더러 슬롯 [1] = `Dagger_Blade_Glow`(팔라딘 패시브 방식), `EnhancedReady` 의 `MaterialFadeEffect`(`_ColorFactor`, 인 0.5초·아웃 0.3초, 대상 = 두 단검 렌더러) |
| 반입 | MyProject `Export/Player3/NormalE/BM Assassin_EnhanceAura`·`_EnhanceCharge`·`_EnhanceSlash` → `50.Art/VFX/Common/Players/Player3/NormalE/FX_Assassin_E_Aura`·`_Charge`·`_Slash`(같은 guid, 루트 이름만). 의존 신규 7(`Dagger_Blade.fbx` → `Models/`, Piloto Double 재질 3·Sword_Tex_5 재질·텍스처·Slash_Fire_Repeat → `*/PilotoStudio/`). 엔트리: 오라 루프(outro 0.7초), 차지 0.5초·강타 1.0초 원샷. 카탈로그 `Assassin_E_Aura`·`_Charge`·`_Slash`. 🔴 SVN add |
| 오라·차지 재생 | 🔸 **붙는 곳 = 단검 소켓 `Dagger_Weapon_R/L/VFX_EnhanceAura`(10-16 확정, 칼날 중간 로컬 Y 0.33 — 다시 만듦)**. `EffectSocketPlayer` 4개를 그 소켓의 자식 `Aura`·`Charge` 에 둔다(소켓 비움 = 자기 자신 = 무기를 따라감, 10-20 민경). 배율 1.25(단검 배율), 오라 safetyTimeout 0(버프 무기한). 켜기·끄기는 `AssassinEnhanceReadyView` 가 `AssassinState.IsEnhancedReady` 로(차지는 `EnhancedReadyChanged(true)` 순간만, 늦은 접속은 차지 없이) — 인스턴스 생성 코드 → 소켓 플레이어 `Play`/`Stop`/`PlayOnce` 로 교체. 추가 네트워크 없음 |
| 강타 슬래쉬 | `Armature/VFX/EnhancedSlash` 에 `EffectSocketPlayer`(id `Assassin_EnhancedSlash`, 엔트리 `Assassin_E_Slash`) · 강타 클립 0.2167초 빈 이벤트 → `PlayEffect("Assassin_EnhancedSlash")`. `AssassinSkillView` 의 `enhancedSlashPrefab`·`enhancedSlashSocket`·`PlayAttackSlash` 경로 삭제(임시 팔라딘 `FX_DoubleSlash_O`), `AssassinBasicAttack` 호출 정리, 저작 도구 해당 줄 삭제. 적중 성공 연출 없음 |
| 시전 임시 연출 | `enhanceCastPrefab` 는 이미 비어 있음(차지가 대신) — 그대로 |
| 테스트 | `AssassinVisualPrefabTests`: 단검 슬롯 [0] = FK, 그 뒤 슬롯은 `Dagger_Blade_Glow` 만 허용, 기대 슬롯 수 5 = FK 슬롯만 센다. 은희 영역 → handoff |
| 담당·공유 | `AssassinSkillView`·`AssassinBasicAttack`·`AssassinShellAuthoring`·`AssassinVisualPrefabTests` = 은희 영역 → `Docs/tech/handoff-assassin-r-vfx.md` §3-7 |

## 변경 파일
- 신규: `50.Art/VFX/Common/Players/Player3/NormalE/*`(프리팹 3·엔트리 3) + 의존 아트 7
- `Assassin_Armature.prefab`(단검 슬롯 [1]·소켓 2·EnhancedReady 뷰·페이드·소켓 플레이어 4) · `Player_Assassin.prefab`(EnhancedSlash 플레이어, 뷰 필드 정리) · `Combo_Attack_02_01.anim`(빈 이벤트 채움)
- `AssassinEnhanceReadyView.cs` · `AssassinSkillView.cs` · `AssassinBasicAttack.cs` · `AssassinShellAuthoring.cs` · `AssassinVisualPrefabTests.cs` · `EffectCatalog.cs`·`.asset`
- 문서: handoff · CONTEXT

## 검증
- dotnet 빌드 오류 0 · guid 연결(누락 0) · cmp. ⏳ 민경 Play / MPPM: E → 양손 날 붉게 + 차지 + 오라, 강타 시작에 꺼지고 0.2167초 슬래쉬, 변신·쓰러짐에 꺼짐, 다른 피어·늦은 접속 동일. EditMode `AssassinVisualPrefabTests` 통과.

---

# ▶▶▶ 계획 = **어쌔신 우클릭(간파) VFX 반입·배선 — 기 모으기 · 튀기기 · 올려치기 · 성공 (EffectSocketPlayer)** (2026-10-19 · grill 확정 · ✅ 승인 · ✅ 반입·코드·프리팹·클립 배선 — dotnet 빌드 오류 0 · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-19). 소켓 4개 = 민경이 `Player_Assassin/VFX/RightClick/WindUp · Pop · Slash · Slash_Success` 에 EffectSocketPlayer 를 넣어 둠(커밋 `23a389de`, id·effect 비어 있음. Slash·Slash_Success 소켓은 10-19 `InterruptSlash` 트랜스폼으로 옮김). 좌클릭 반입(아래 계획)과 같은 방식.

## 결정
| 항목 | 결정 |
|---|---|
| 반입 프리팹 | MyProject `Export/Player3/RClick/BM Insight_Windup`·`_Pop`·`_Slash`·`_Success`(+ 중첩 `BM Hit`) → `50.Art/VFX/Common/Players/Player3/RClick/FX_Assassin_RClick_Windup`·`_Pop`·`_Slash`·`_Success`·`_Hit`(같은 guid, 루트 이름만 변경). 엔트리 = 기 모으기 루프(outroDuration 0 → 끄는 즉시 반납), 튀기기 0.4초·올려치기 1.0초·성공 1.0초 원샷. `EffectCatalog` 에 `Assassin_RClick_*` 등록(prewarm) |
| 의존 | 신규 31개(Piloto 재질·텍스처·모델 → `*/PilotoStudio/`, Matthew Guz `Hit.mat`·텍스처 → `*/MatthewGuz/`, `SR_Success_Spark.mat` → `_Materials/Player3/`). 나머지는 같은 guid 로 이미 있음. 🔴 새 아트 SVN add |
| 소켓 id | `WindUp`=`Assassin_InsightWindup`, `Pop`=`Assassin_InsightPop`, `Slash`=`Assassin_InsightSlash`, `Slash_Success` = id 없음(코드 전용) |
| 클립 `Attack_Up_01` 이벤트 | 0초 `StartEffect(Assassin_InsightWindup)` · 13프레임(0.2167초) `StopEffect(Assassin_InsightWindup)` + `PlayEffect(Assassin_InsightPop)` · **16프레임(0.2667초, 민경이 넣어 둔 빈 이벤트) `PlayEffect(Assassin_InsightSlash)`**(10-19 민경 지정). 판정 Hit(0.6초)·End(1.275초)는 그대로. 상태 전이는 exitTime 1 이라 스킬 종료(0.6초)로 애니가 끊기지 않음 |
| "성공" 기준 | **간파 성공** = 서버 `PlayerInterruptSkillBase.ServerInterruptSucceeded`(쿨 감소와 같은 기준). 단순 적중은 아님 |
| 🔸 성공 = 올려치기와 **같이** | 서버 성공 알림 → RPC(전 피어) → "성공 대기" 표시. `EffectAnimEvents` 에 재생 알림 이벤트 `Played(string id)` 를 추가하고, `AssassinSkillView` 가 올려치기 소켓 id 가 재생되는 순간 성공 대기면 `Slash_Success.PlayOnce()`. 알림이 올려치기보다 늦게 오면(지연·나중에 판정이 0.6초로 옮겨짐) 받는 즉시 재생. 표시는 다음 시전 시작에 지운다(스킬 종료 0.6초와 올려치기 0.6초가 겹쳐도 안 잃음) |
| 기 모으기 안전 정지 | 스킬 `OnEnd`(완료·피격·사망 공통, 전 피어)에서 켜져 있으면 끈다 |
| 임시 연출 제거 | `interruptCastPrefab`(`FX_Interrupt_Flash`)·`PlayInterruptCast` 제거 → `OnClientPlay` 는 성공 대기 초기화만 |
| 담당·공유 | `AssassinInterruptSkill`·`AssassinSkillView` = 은희 영역 → `Docs/tech/handoff-assassin-r-vfx.md` 에 추가. `EffectAnimEvents` = VFX 영역(이벤트 추가만, 기존 동작 불변) |

## 변경 파일
- 신규: `50.Art/VFX/Common/Players/Player3/RClick/*`(프리팹 5·엔트리 4) + 의존 아트
- `1.Scripts/Effects/EffectAnimEvents.cs`(Played 이벤트) · `EffectCatalog.cs`·`.asset`
- `1.Scripts/Player/Assassin/AssassinSkillView.cs` · `AssassinInterruptSkill.cs`
- `2.Prefabs/Player/Assassin/Player_Assassin.prefab`(소켓 4개 id·엔트리, 뷰 필드) · `4.Animations/Player/Assassin/Attack_Up_01.anim`(이벤트 4개)
- 문서: handoff · CONTEXT

## 검증
- 컴파일 에러 0 · guid 연결(dangling 0) · cmp. ⏳ 민경 Play / MPPM(호스트·클라 모두: 기 모으기 → 0.55초 튀기며 사라짐 → 올려치기, 보스 간파 성공 때만 성공 연출이 올려치기와 함께, 시전 중 피격 시 기 모으기 남지 않음).

---

# ▶▶▶ 계획 = **어쌔신 좌클릭 VFX 반입·배선 — 일반 4타 · 일반 성공 · 변신 루프 (EffectSocketPlayer)** (2026-10-19 · grill 확정 · ✅ 승인 · ✅ 반입·코드·프리팹 배선(컴파일은 MainProject Unity 를 켜야 확인) · ⏳ 민경 Play/MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-19). 소켓 9개 = 민경이 `Player_Assassin/VFX/NormalSlash_1~4 · NormalSlash_Success_1~4 · TransformedSlash` 에 EffectSocketPlayer 를 넣어 둠(id·effect 비어 있음, 각자 Armature `BasicAttack01~04`·`TransformedAttack` 추종).

## 결정
| 항목 | 결정 |
|---|---|
| 반입 프리팹 | MyProject `LClick/BM Assassin_Slash_Arc200_BM`(원본 Arc200 은 원호 모델용 재질 V 0.625 를 링 200° 메시에 써서 어긋남 → 재질만 바로잡은 `_BM`) · `BM Assassin_Slash_Success_200`(원본, NamuFX 가산 + 그어진 섬광 중첩) · `BM Assassin_TransformedSlash`(원본, 민경 10-19 수정본 — 루프) |
| 배치 | `50.Art/VFX/Common/Players/Player3/LClick/FX_Assassin_LClick_Slash`·`_Success`·`_Transformed`(+`_Entry`). 일반·성공 = 원샷(지속시간 지정), 변신 = 루프(outroDuration 0.3). `EffectCatalog` 에 `Assassin_LClick_Slash`·`_Success`·`_Transformed` 등록(prewarm) |
| 의존 | 24개 신규(NamuFX 셰이더·서브그래프·텍스처 → `50.Art/VFX/*/NamuFX/`, Piloto BM 재질·텍스처 → 기존 `*/PilotoStudio/`, 재질 사본·링 200° 메시·그어진 섬광). 19개는 같은 guid 로 이미 있음. 🔴 새 아트 SVN add |
| 🔸 재생 방식(유지보수) | 애니 이벤트(EffectAnimEvents) 대신 **코드의 기존 Hit 이벤트**에서 — 팔라딘 `DefaultAttackController` 선례. `AssassinSkillView` 가 `EffectSocketPlayer` 배열을 든다: 일반 1~4타 = Hit(전 피어) → `normalSlash[타].PlayOnce()`, 일반 성공 = 서버 `Fire()` 반환값 true → RPC(Unreliable) → `normalSlashSuccess[타].PlayOnce()`, 변신 = 묶음 시작(전 피어) `transformedSlash.Play()` / 묶음 끝·취소·변신 해제 `Stop()`. 이유: 판정·VFX 타이밍이 Hit 이벤트 하나, `hasPendingClip`·취소·모드 필터를 그대로 탐, 변신 루프가 끊겨도 확실히 멈춤 |
| 4타 프리팹 | 4타 모두 같은 Arc200 엔트리, 방향 = 소켓 회전. 성공 4개도 같은 엔트리 |
| 코드 정리 | `AssassinSkillView` 의 `normalSlashPrefabs`·`transformedSlashPrefab`·해당 소켓 필드·Instantiate 경로를 소켓 플레이어로 교체. 강화(`enhancedSlashPrefab`) 경로는 그대로 |
| 재생 속도 | `ShurikenEffectSystem` 이 프리팹의 Simulation Speed 를 처음 본 값으로 기억해 `기본 × 배율` 로 쓰게(지금은 반납 때 1 로 덮어써 두 번째 재생부터 느려짐). 기본 1 인 기존 이펙트 동작 불변 |
| 담당·공유 | `AssassinBasicAttack`·`AssassinSkillView` = 은희 영역 → `Docs/tech/handoff-assassin-r-vfx.md` 에 추가 |

## 검증
- 컴파일 에러 0 · 프리팹·카탈로그 guid 연결 확인. ⏳ 민경 Play / MPPM(호스트·클라 모두 4타 슬래쉬 · 맞을 때만 성공 · 변신 루프 시작·정지).

---

# ▶▶▶ 계획 = **어쌔신 좌클릭 = Insight 슬래쉬 · E 강타 · 우클릭 올려치기/성공 재제작 (SlashReference 참고)** (2026-10-17 · grill 확정 · ✅ 승인 · 🛠 1단계 MyProject 제작 완료 → ⏳ 민경 컨펌)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-17). 참고 = MyProject `Export/Player3/SlashReference/`(SpecialSkillsEffectsPack EffectsSet_2 — 보이는 부분은 전부 순수 파티클, `Shader_IntegratedEffect`. 소환 스크립트 `MultipleObjectsMake` 는 MainProject EffectManager 풀링과 안 맞아 **시작 지연·버스트로 구워 순수 파티클 프리팹**으로 만든다).

## 결정
| 항목 | 결정 |
|---|---|
| 좌클릭 | `RClick/BM Insight_Slash` **사본** → `LClick/BM Assassin_Slash_Insight.prefab`. 칼끝 ✦ 3개 제거, 크기 ≈1.7배(판정 1.5~1.7m), Simulation Speed 2. 4타 방향 = 소켓 회전(기준 자세 = 소켓 XZ 면, +Z → +X 진행) |
| 기존 좌클릭 계열 | `LClick/BM Assassin_Slash`(+Arc90/150 Variant) **참고용으로 남김**, 변신 4타 `BM Assassin_TransformedSlash` 유지(범위 밖) |
| E 강타 | **25_CriticalSlash 를 참고만** — 교차 초승달 구성만 빌리고 **크기·속도·색은 새로**: 주 초승달 = 판정 방향 수평 베기(소켓 XZ 면, +Z 중심, 바깥 ≈2.0m), 보조 초승달 = 비스듬히 교차(X자). 검정·붉은색. `Export/Player3/BM Assassin_EnhancedSlash.prefab` · Simulation Speed 1.5 |
| 우클릭 올려치기 | 06_MassiveSlash 의 세로 베기 파트 **1개**(10연타 X)를 검붉게, 소켓 면에서 올려 긋기 → `RClick/BM Insight_Uppercut.prefab` · Speed 1.5 |
| 우클릭 성공 | 11_IntagibleSlash 의 좌우 대칭 초승달 2개(X자) + 불꽃 타격점을 검붉게, 같은 자리 → `RClick/BM Insight_SuccessBurst.prefab` · Speed 1.0 |
| 우클릭 범위 | 올려치기·성공만 새로. 기 모으기 `_Windup`·튀기기 `_Pop` 유지, 부착 방식(애니 이벤트 × EffectSocketPlayer, 원점 = 베기 중심) 유지. 기존 `_Slash`·`_Success` 는 컨펌 전까지 남김 |
| 색 | 팩 재질 **사본**(`Export/Player3/Materials/`)에서 `_TintColor` 붉은 HDR + 같은 메시에 **알파 블렌드 검은 층** 추가 |
| 테스트 | `EnhanceTester`(강타)·`InsightTester`(우클릭)·`BasicAttackTester`(좌클릭) 프리팹 칸만 교체 |
| 반입 의존 | `Donut_Mesh.fbx`·팩 텍스처 대부분이 MainProject 에 없음 → 반입 때 같이(SVN add). 셰이더는 같은 guid 로 있음 |

---

# ▶▶▶ 계획 = **어쌔신 강화 기본 공격(E 후 강타) 슬래쉬 — Round Slash 기반 강타판** (2026-10-16 · grill 확정 · ✅ 승인 · 🛠 1단계 MyProject 제작 완료 → ⏳ 민경 컨펌)

> 🔸 **10-16 민경 최종 결정: 강화 슬래쉬 = MyProject `Export/Player3/BM Round Slash .prefab` 원본 그대로**(아래 혼합안으로 만든 `BM Assassin_EnhancedSlash` 는 쓰지 않음). 반입 때 이 프리팹 → `enhancedSlashPrefab`(소켓 `EnhancedAttack`). 의존 = Piloto Double 재질 3종·`BM_Sword_Tex_5_alpha_soft`·Flecks 재질·`Slash_Complete_Circle_64_` 등. ⚠️ Piloto 재질 Soft Particle(1.1~1.3m)이 켜져 있어 바닥 가까이 누우면 연해질 수 있음.
>
> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). 앞 세션의 `BM Assassin_EnhancedSlash` 는 MyProject 에서 지워진 상태 → 새로 만든다. 오라·차지·칼날 발광(아래 E 계획)은 그대로.

## 사실
- 클립 **`Combo_Attack_02_01`**(1.417초) × **2배속** — Hit 실제 **0.283초**, End 0.602초(타격→끝 0.32초). 1타 · ×2.5 · 1.8m · 120°.
- Hit 애니 이벤트에서 전 피어 `PlayAttackSlash(Enhanced)` → `enhancedSlashPrefab` 를 소켓 `VFX/EnhancedAttack`(1개)에 재생. 지금 임시 = 팔라딘 `FX_DoubleSlash_O`.
- 기반 `Export/Player3/NormalQ/BM Round Slash .prefab` = Piloto 360° 링 + "Double"(가로 반복 2) 재질 → **몸 둘레 쌍 초승달**. 테두리(밝기 350)·날·채움 2·S자 쌍 초승달 칼자국 빌보드 2장(`BM_Sword_Tex_5`, 5.6/6.2m)·반짝이, 이미 검정·붉은색.

## 결정
| 항목 | 결정 |
|---|---|
| 구성 | **혼합(c)** — 주인공 = 정면 **Arc150 검기**(Round Slash 의 재질·색, Double 재질은 반복 1 + 원호 UV 보정 사본), 배경 = Round Slash **360° 링을 어두운 충격파로 옅게** |
| 크기 | 바깥 ≈ **2.1m**(Arc150 × 배율 2.9) |
| 검기 Emissive | 테두리 밝기 **2배(350 → 700)** + 칼날 띠를 따라 **흰빛에 가까운 붉은 심지 층** 추가(블룸) |
| 임팩트 요소 | ① 디스토션 2장 + 충격파 링 디스토션 ② 원호 앞으로 퍼지는 검붉은 반원 링 ③ 검은 연기 폭발 ④ 붉은 불티 20~30 ⑤ 원호 중앙 붉은 섬광 |
| 타격 성공 | 일반 좌클릭의 그어진 섬광을 **1.5배**로(같은 RPC 에 공격 종류만, 새 에셋 없음) |
| 재생 속도 | 모든 파티클 **Simulation Speed 1.5**(검기 ≈0.33초, 연기·링 0.5~0.6초). 🔴 반입 때 `ShurikenEffectSystem` 속도 덮어쓰기 수정(좌클릭 계획과 같음) |
| 파일 | `Export/Player3/BM Assassin_EnhancedSlash.prefab`(기준 자세 = 좌클릭과 같음, 소켓 `EnhancedAttack` 방향은 민경) |
| 테스트 | `EnhanceTester` 강타(F·좌클릭) — 실제 타이밍(0.283초 뒤) 슬래쉬 · 슬래쉬 소켓 칸 · 성공 체크 시 1.5배 섬광 |

---

# ▶▶▶ 계획 = **어쌔신 일반 E(단검 강화) VFX — 단검 날 붉은 발광 + 검붉은 오라 루프 + 차지 버스트 + 강화 일격 슬래쉬** (2026-10-16 · grill 확정 · ✅ 승인 · 🛠 MainProject 발광·배선 완료 / MyProject 빌드 스크립트 작성 → ⏳ 민경 실행·컨펌 → 반입)

> 🔸 **10-16 2차 grill(민경) — 아래 "결정" 표의 단검 색·발광·오라 형태 항목보다 우선.** 앞 세션 종료 확인. ✅ 승인 · ✅ 구현(아래 표대로. 발광 색은 셰이더 밝기 기준이 달라 팔라딘과 비슷한 밝기의 빨강 `_TintColor` (40, 2.7, 1.6)) · ⏳ 민경 Play
>
> | 항목 | 결정 |
> |---|---|
> | 칼날 발광 방식 | **팔라딘 방식으로 교체** — 단검 렌더러에 오버레이 슬롯 [1] `50.Art/VFX/_Materials/Dagger_Blade_Glow.mat`(셰이더 `Shader_IntegratedEffect`, `Sword_Blade_Glow` 사본) + 마스크 `50.Art/VFX/Textures/Dagger_BladeMask.png`(단검 UV0 에서 **칼날 폴리곤만** 칠함 — UV0 칼날·손잡이 섬이 안 겹쳐 MaskUV 메시 불필요). `_TintColor` 빨강 HDR (3,0.2,0.12), `MaterialFadeEffect` **float 모드 `_ColorFactor`** 0.5초 인/0.3초 아웃. 두 단검 같은 메시 = 마스크 하나 |
> | 앞 세션 변경 정리 | `ToonLit.shader`·`MaterialFadeEffect.cs`(Color 모드) **HEAD 로 되돌림**, `3.Materials/Toon/Assassin_Dagger_Toon.mat` **삭제**·양손 단검 슬롯 [0] = 원래 `Assassin_Toon.mat`. `AssassinEnhanceReadyView`·소켓·배선은 유지(발광 대상만 오버레이로, 주석 갱신) |
> | 칼날 판정 | 단검 메시 Y > 5cm(FBX 단위, = 0.05m) 폴리곤 = 칼날(손잡이 −14.5~5cm, 칼날 5~49.8cm, 등 톱니 포함) |
> | 오라 형태 | 칼날만 뽑은 메시 `Dagger_Blade`(MyProject `Export/Player3/Models/`, 반입 시 `50.Art/VFX/Models/` · SVN add)를 오라 4개 시스템의 **Shape = Mesh(표면)** 로. 소켓 `VFX_EnhanceAura`(칼날 중간 Y 0.33m)는 그대로, Shape 위치 **−0.33m 보정** |
> | MyProject 테스트 | MainProject `Dagger_Weapon_L/R.fbx`·텍스처를 **같은 guid** 로 MyProject `50.Art/Char/assassin/` 에 복사, `EnhanceTester` 가 큐브 대신 실제 단검 사용 |

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). 변신 E(Multi Slashes)·변신 상태는 건드리지 않는다. 일반 좌클릭 4타 작업과 **병행**(파일 겹침 없음 — 강화 슬래쉬만 좌클릭 슬래쉬 구조를 이어 씀).

## 사실(조사)
- 버프 = `AssassinStateSnapshot.enhancedReady` 하나(서버 쓰기 NetworkVariable). **지속시간 없음** — 다음 좌클릭이 수락돼 Enhanced 로 시작하면 소모(맞든 안 맞든), 변신·다운·사망에도 해제. 강화 일격 = ×2.5·1타·120°·1.2m 전진(`AssassinBasicAttackData` Enhanced step, AttackIndex 4).
- 훅: `AssassinState.EnhancedReadyChanged(bool)` 전 피어 발화·구독자 없음 / 빈 소켓 `Assassin_Armature/VFX/EnhancedReady` / `AssassinSkillView.enhanceCastPrefab`(임시 `Flash_star`)·`enhancedSlashPrefab`(임시 팔라딘 `FX_DoubleSlash_O`, 소켓 `VFX/EnhancedAttack`, Hit 애니 이벤트에서 전 피어 재생).
- 단검 `Dagger_Weapon_R/L` = 날·손잡이 **단일 메시·슬롯 1개**, 몸통과 같은 `Assassin_Toon.mat`(ToonLit, 발광 속성 없음). 메시 긴 축 = 로컬 Y(손잡이 −Y, 날 +Y).
- 팔라딘 패시브 = 오버레이 머티리얼 `_ColorFactor` 를 `MaterialFadeEffect` 로 페이드(오라 없음).

## 결정
| 항목 | 결정 |
|---|---|
| 작업 위치 | 파티클 에셋 = MyProject `Export/Player3/BM Assassin_Enhance*` 제작 → 컨펌 → MainProject 반입·배선. **머티리얼·셰이더·스크립트 = MainProject 직접**(MyProject 에 어쌔신 모델·ToonLit 없음) |
| Key color | Player3 팔레트 — 검정 코어(alpha 0,0,0,0.8~1) · 검붉은 연기(alpha ~0.16~0.22,0.02~0.08) · 붉은 강조(add 0.55~0.88,0~0.13) · 가장자리(1,0.12,0.08) |
| 단검 색 | **단검 전용 머티리얼 분리**: `Assassin_Toon` 의 Material Variant `Assassin_Dagger_Toon.mat` → `Assassin_Armature.prefab` 양손 단검 `m_Materials[0]` 교체 |
| 발광 | `ToonLit` 에 `[HDR]_EmissionColor`(기본 검정 = 다른 캐릭터 무변화) + **날 마스크**(오브젝트 로컬 Y 경계·부드러움 속성) 추가. 두 UnityPerMaterial CBUFFER(ForwardLit·Outline)와 다른 패스 블록 일치 유지. 붉은 HDR 발광 → 블룸 |
| 페이드 스크립트 | `MaterialFadeEffect` 에 **Color 모드** 추가(기본 = 기존 float 동작, 팔라딘 무변화). 페이드 인 0.5 · 아웃 0.3초 |
| 오라 | **양손 단검** 루프 — 검은 연기 코어(alpha) + 붉은 가장자리 글로우(add) + 붉은 불티 소량, **World 시뮬레이션**(휘두르면 꼬리). R `BladeVortex` 의 연기·글로우·불티 재활용·축소, 형태는 날 축(로컬 Y) 따라. 팀원도 알아볼 강도 |
| 차지 버스트 | E 입력 순간 검붉은 연기·불티가 양손 단검으로 **빨려 드는 차지(~0.3초)** → 오라 시작. 임시 `Flash_star` 교체 |
| 강화 일격 슬래쉬 | 좌클릭 슬래쉬(`BM Assassin_Slash`) 구조를 이어 **Arc150 기반 큰 초승달**, 검은 코어 + 붉은 가장자리 + 붉은 글로우, 일반 4타보다 두껍고 밝게 + 잔연기. `enhancedSlashPrefab` 교체 |
| 적중 섬광 | 이번엔 안 함 — 일반 4타용 적중 RPC 가 생기면 같이 씀 |
| 켜기·끄기 | `EnhancedReadyChanged`(전 피어) 구독 → true: 날 발광 FadeIn + 오라 `PlayLooping`(단검 추종) / false: FadeOut + `Release`. 차지 버스트는 기존 `OnClientPlay → PlayEnhanceCast`. **추가 네트워크 없음** |
| 타이밍 | 소모는 강화 일격 **시작** 시점(서버 수락) → 오라·발광이 먼저 꺼지기 시작하고 Hit 이벤트에서 슬래쉬가 터진다(0.3초 페이드라 자연스럽게 이어짐) |
| 늦게 들어온 피어 | 구독 시점에 `IsEnhancedReady` 가 이미 true 면 바로 켬 |

## 진행(10-16)
- ✅ MainProject: `ToonLit` 발광·날 마스크 · `MaterialFadeEffect` Color 모드 · `Assassin_Dagger_Toon.mat` · 신규 `AssassinEnhanceReadyView`(Assassin_Armature `VFX/EnhancedReady` — 단검과 같은 프리팹이라 참조 한 겹) · 단검 소켓 `VFX_EnhanceAura` · 임시 `enhanceCastPrefab` 비움. **구현 변경**: 오라·차지는 EffectManager/카탈로그가 아니라 소켓 Instantiate(평타 슬래쉬와 같은 경로) — 루프가 단검 뼈대를 따라야 해서 부모 추종이 단순하다. `AssassinSkillView` 구독 대신 Armature 쪽 새 컴포넌트.
- ✅ MyProject: 빌드 메뉴 `Tools/VFX/Assassin Enhance (일반 E) 빌드`(`1.Scripts/Editor/AssassinEnhanceVfxBuilder.cs`) → `Export/Player3/BM Assassin_EnhanceAura`·`_EnhanceCharge`·`_EnhancedSlash`(Arc150 Variant) + 씬에 `EnhanceTester`(E·F/좌클릭, 자동 반복). 재료 = `BM Blade Vortex_Fast` 시스템(MainProject 반입 완료 guid) + `BM Assassin_Slash_Arc150`.
- 🔸 10-16 민경 피드백: 강화 슬래쉬가 좌클릭처럼 약함 → `BM Insight_Slash`(간파)처럼 색·범위가 또렷해야 함. **재제작**: 간파 슬래쉬 사본을 Y −45° 로 눕혀 중심축 +Z(±45° + 칼날 무작위 회전 ≈ 120°)·×1.7, 1.25배속(간파 1·좌클릭 2), 원본 BM 재질·큰 검은 Fuzzball 배경 유지, 간파 칼끝 ✦ 줄기 제거, 좌클릭 디스토션(Arc150) + 원호 붉은 글로우·잔연기·불티. 더 이상 Arc150 Variant 가 아님 → 반입 의존 = 간파 슬래쉬 재질·`Slash_Complete_Circle_64_`·`CaracolSphere` 등(간파 반입과 공유).
- ⏳ 민경: 빌드 실행 → Play 확인·수치 조정 → 컨펌. 반입 = 오라·차지 → `Players/Player3/E_Enhance/`, `AssassinEnhanceReadyView` 의 두 칸. 강화 슬래쉬는 **좌클릭 슬래쉬 반입 때 같이**(의존 `BM Assassin_Slash`·BM 재질·`Slash_Arc*` guid 재매핑 공유) → `enhancedSlashPrefab`.
- ⚠️ 날 마스크 0.15 는 메시 Y −0.145~0.498m(단위 변환 가정)에서 고른 값 — 손잡이까지 빛나거나 날 일부가 안 빛나면 `Assassin_Dagger_Toon` 의 `Emission Mask Start` 조정.

## 변경 파일(예정)
- MainProject 코드: `1.Scripts/Rendering/Toon/Shaders/ToonLit.shader`(공용 — 은희 공유), `1.Scripts/Effects/MaterialFadeEffect.cs`(Color 모드), `1.Scripts/Player/Assassin/AssassinSkillView.cs`(구독·오라·발광 필드), `1.Scripts/Effects/EffectCatalog.cs`(엔트리 키)
- MainProject 에셋: `3.Materials/Toon/Assassin_Dagger_Toon.mat`(신규), `2.Prefabs/Player/Assassin/Assassin_Armature.prefab`(단검 머티리얼·필드), `Player_Assassin.prefab`(View 필드), `50.Art/VFX/Common/Players/Player3/E_Enhance/`(신규 프리팹·Entry — SVN add)
- MyProject: `Export/Player3/BM Assassin_EnhanceAura`·`_EnhanceCharge`·`_EnhancedSlash` + 테스터

## 리스크
- 공용 `ToonLit` 수정 — 기본값 0 이라 시각 무변화여야 함. CBUFFER 불일치 시 SRP Batcher 깨짐 → 수정 후 다른 캐릭터 화면 비교.
- 날 마스크 경계 값은 메시 bbox(Y −14.5~49.8, FBX 단위) 기반 추정 — 화면 보고 조정.
- 좌클릭 PLAN 의 🔴 `ShurikenEffectSystem` Simulation Speed 덮어쓰기 문제가 강화 슬래쉬에도 적용.
- Unity 켠 채로: `.cs`·셰이더·텍스트 에셋 소량은 OK. **Play 모드면 먼저 정지.** 바꾼 프리팹은 `cmp`·guid 확인.

## 완료 기준(MPPM 2인, 호스트·클라 양쪽)
1. E 입력 → 차지 버스트 → 양손 단검 날 붉은 발광 + 오라 루프
2. 강화 일격 → 오라·발광 페이드아웃 + 강화 슬래쉬
3. 버프 중 변신·다운·사망 → 오라·발광 페이드아웃
4. 팔라딘 패시브·다른 캐릭터 외형 무변화, 컴파일 에러 0

---

# ▶▶▶ 계획 = **어쌔신 변신 좌클릭 VFX — 4타 묶음 슬래쉬(변신판)** (2026-10-16 · grill 확정 · ✅ 승인 · 🛠 1단계 MyProject 제작 완료 → ⏳ 민경 컨펌)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). 기준 = 기획서 6장 + `AssassinBasicAttack` 변신 모드. 일반 좌클릭 슬래쉬(아래 계획, ✅ 민경 확인 — 방향 OK)의 변신판.

## 사실
- `Speed_Attack_Loop`(0.667초) × 1.25 — 입력 1회 = 4타 묶음, 유지 시 반복. Hit 실제 **0.08 / 0.187 / 0.293 / 0.40초**(간격 ≈0.107), End 0.48초. 판정 1.8m · 120°.
- Hit 이벤트마다 전 피어 `PlayAttackSlash(Transformed, …)` = 묶음당 4번. 소켓 `TransformedAttack` 1개, **몇 번째 타인지 정보 없음**(서버 `hitsFired` 만). 지금 임시 = `FX_Mob_Slash`.
- 변신 중 백어택 강제(`forceBackAttack`) → 적중마다 적 위치에 백어택 버스트(`Burst_sharp`)가 이미 난다.

## 결정
| 항목 | 결정 |
|---|---|
| 재생 단위 | **Hit 이벤트마다 슬래쉬 1개**(묶음당 4번) — 애니와 항상 맞음 |
| 모양 | 일반 슬래쉬 계열의 **변신판**: Arc120 고정, 바깥 ≈1.8m(배율 2.5). 테두리 더 뜨겁게(밝은 심지·밝기 1.3배), 날 (0.3,0,0)·채움 거의 검정, **검은 연기 꼬리 추가**(원호 따라 0.3초), **디스토션 2장 겹침**(재질 복제 없음), 반짝이·연기 개수 절반 |
| 4타 방향 | **소켓 4개 `TransformedAttack01~04`**(민경이 애니 보고 잡음) + `AssassinSkillView` 가 묶음 안 Hit 순번을 전 피어에서 직접 셈(묶음 시작에 0) — 반입 때 코드 |
| 타격 성공 | 그어진 섬광 **없음** — 기존 백어택 버스트만 |
| 파일 | 독립 프리팹 `Export/Player3/BM Assassin_TransformedSlash.prefab`(Arc120, 일반과 같은 원본·스크립트, Mesh 교체로 각도 구조 유지) |
| 재생 속도 | **모든 파티클 Simulation Speed 3.0**(검기 ≈0.17초, 다음 타와 0.06초 겹침). 🔴 반입 때 `ShurikenEffectSystem` 속도 덮어쓰기 문제는 아래 일반 계획과 같이 고친다 |
| 테스터 | `1.Scripts/TransformedAttackTester.cs` — 소켓 4·프리팹 1·실제 타이밍 묶음·유지 반복/자동 반복 |
| 기준 자세 | 일반과 같음(원점 = 원호 중심, +Z 중심축, -X → +X 진행, 반대 방향은 소켓 Z 180°) |

---

# ▶▶▶ 계획 = **어쌔신 일반 좌클릭 VFX — 원호 슬래쉬(모델 교체로 각도) + 타격 성공 그어진 섬광** (2026-10-16 · grill 확정 · ✅ 승인 · 🛠 1단계 MyProject 제작 완료 → ⏳ 민경 컨펌)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). ~~참고 = BM Flash Slash~~ → **10-16 재지시: 섬광(Impact) 빼고 전부 재제작, 참고 = `Export/Player3/BM Insight_Slash`(검정·붉은색 key color) + 검기를 따라 디스토션 + 빠른 재생**.

### 🔸 재제작(10-16 2차) — 아래 표의 슬래쉬 항목보다 우선
| 항목 | 결정 |
|---|---|
| 기반 | `BM Insight_Slash` 의 Edge(0.95,0.1,0.07)·Blade(0.55,0,0)·Fill 2장(검정)·Flecks·Fuzzball. 간파 전용 칼끝 ✦ 3개는 뺌 |
| 원호 모델 UV 보정 | `Slash_Arc*` 는 V 배치가 Piloto 링과 다르다(칼날 띠 = v 0.8~1.0, 링은 바깥 가장자리 = v 0.5) — 그래서 BM 텍스처의 밝은 중심선이 원호 안쪽에 그려져 연했다. 재질 사본 `Export/Player3/Materials/BM_AssassinSlash_Edge·_Blade·_Fill` 에서 **MainTex V 반복 0.625**(Fill 0.475)로 링 배치에 맞춤 + Soft Particle 끔 |
| 디스토션 | 팔라딘 좌클릭 `Slash Distortion` 층을 옮김 — `Slash World.mat`(MyProject·MainProject **guid 동일** `0cc31af9…`), 같은 원호 모델, 지연 0.1·수명 0.3 |
| 각도 | 그대로 — 기본 Arc120, Variant Arc90·Arc150 이 렌더러 5개(Edge·Blade·Fill 2·Distortion) Mesh 만 덮어씀 |
| 🔸 재생 속도 | **모든 파티클 시스템의 Main › Simulation Speed** 한 값(지금 2.0 = Insight 시간축 2배). 프리팹에서 시스템 9개를 함께 선택해 바꾼다 |
| 🔴 반입 때 필수 | MainProject `ShurikenEffectSystem` 은 풀 반납(`ResetForPool`)에 `SetPlayRate(1)` 로 **모든 시스템 Simulation Speed 를 1로 덮어쓰고**, 히트스톱도 절대값으로 쓴다 → 프리팹의 2.0 이 첫 재생 뒤 사라진다. 처음 본 값을 기억해 `기본값 × rate` 로 쓰도록 고쳐야 한다(이펙트 공용 코드) |

## 결정
| 항목 | 결정 |
|---|---|
| 범위 | **일반 4타**(`Combo_Attack_Wave_01~04`)만. 일반 E 강타·변신 4타 묶음은 같은 구조로 다음 차례 |
| 슬래쉬 구조 | 검기 Edge·Blade 를 **각 1개·회전 무작위 없음**으로 고정, 두 렌더러가 같은 `Slash_Arc*` 모델을 씀 → **Mesh 칸만 바꾸면 각도가 바뀐다**(모델 6종 UV 가 원호 따라 0→1 로 같음) |
| 각도 관리 | 기본 프리팹(Arc120) + **Prefab Variant**(Arc90·Arc150 — 두 렌더러 Mesh 만 덮어씀) |
| 타별 각도 | 1타 Arc90 / 2·3타 Arc120 / 4타 Arc150 (판정 90/100/100/120°보다 살짝 넓게. 100° 모델 없음) |
| 기준 자세 | 원점 = 원호 중심. 베는 면 = 소켓 XZ 면. **원호 중심축 = 소켓 +Z**, -X 쪽에서 +Z 를 지나 +X 쪽으로 진행(모델이 중심축 대칭이라 각도를 바꾸면 양 끝이 같이 늘고 준다). 반대 방향 타는 소켓을 Z 축 180° 회전(재질 양면) |
| 휘두르는 방향 | 프리팹은 기준 자세 하나, **타별 방향은 소켓 회전**(`BasicAttack01~04`, 민경이 애니 보고 잡음) |
| 크기 | 바깥 반경 ≈ 1.5m(모델 0.73m × 기본 배율 2). 이후 소켓 배율로 조절 |
| 남길 요소 | Edge·Blade 각 1, Flecks(개수 축소), FuzzAlpha(축소). **제거** = Smoke·바닥 데칼 2종·바닥 바람 링 2종 |
| 색·길이 | 검정·붉은색. 수명 0.75 → **~0.35초**(타격→타 종료 0.19초, 4타 연속에 쌓이지 않게) |
| 검기 재질(10-16 변경) | Flash Slash 재질은 너무 연함(밝기 55·꼬리 디졸브) → **BM Slash 단일 검기 재질**(Edge 밝기 350·Blade 75, Clamp 텍스처). 입자 스핀 없이 **Custom1.z UV 오프셋 0.95→0.1** 로 검기가 원호를 따라 지나간다 = 원호 각도가 곧 휘두르는 범위. 🔸 같은 날 민경: Flash Slash 참고 **취소**, BM Slash 재질 유지(b) — Soft Particle(1.1~1.3m)이 바닥 가까운 납작 원호를 지워서 **Soft Particle 끈 사본** `Export/Player3/Materials/BM_AssassinSlash_Edge·_Blade` 사용, 테두리 디졸브 고원 1.2 → 0.9 |
| 타격 성공 | **분리 방식** — 슬래쉬는 지금처럼 Hit 이벤트에서 전 피어 즉시 재생, 서버가 맞았다고 판정했을 때만 RPC 로 **그어진 섬광 원샷**을 따로 재생 |
| 그어진 섬광 | BM Flash Slash 재질로 **일자 베기 선을 새로**: 베기 선(~1.2m, 0.05초에 그어짐, 붉은 테두리·밝은 심지) + 검은 잔상 선(조금 더 남고 가늘어짐) + 가운데 섬광(0.1초) + 선 수직 붉은 불티. 전체 ~0.3초 |
| 섬광 위치 | 같은 타 소켓 기준 **원호 중앙(+Z 1.1m)**, 진행 방향(X)과 나란히, 위에서 볼 때 15° 고정 기울임. 여러 명 맞혀도 1회 |

## 코드 변경(MainProject 반입 때 · 은희 영역 → handoff)
- `AssassinBasicAttack.FireCurrentHit` 에서 `coneAttack.Fire(...)` 반환값(지금 버려짐)을 받아, 맞았으면 `AssassinSkillView` 에 전 피어 RPC(타 번호) 추가 — `BackAttackHitRpc` 와 같은 자리.
- 지금 임시로 쓰는 팔라딘 슬래쉬(1·3타가 `_O` 라 빗나가도 Impact 가 보임)를 새 슬래쉬로 교체.

| 산출물 | MyProject `Export/Player3/BM Assassin_Slash`(Arc120)·`_Slash_Arc90`·`_Slash_Arc150`(Variant)·`BM Assassin_SlashImpact` + 테스터 `1.Scripts/BasicAttackTester.cs`(소켓 4·타별 프리팹·실제 타이밍 타격 0.167/0.167/0.167/0.2초, 타 길이 0.354/0.354/0.354/0.425초로 1→4 반복·성공 체크 시 섬광) → 컨펌 → MainProject 반입 시 원호 모델은 이미 있는 `50.Art/VFX/Models/Slash_Arc*`(파일·임포트 설정 동일, **guid 만 다름 → 재매핑**) |

---

# ▶▶▶ 계획 = **어쌔신 우클릭(간파) VFX — 기 모으기 · 올려치기 · 성공 강조** (2026-10-16 · grill 확정 · 1단계 MyProject 제작 · 부착 = 민경, 애니 이벤트 × EffectSocketPlayer)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). 제작·부착(소켓 + 애니 이벤트)은 민경, **판정 타이밍 정렬·성공 알림 경로는 은희와 합의**.

## 결정
| 항목 | 결정 |
|---|---|
| 연출 단계 | 기 모으기(시전 순간) → 올려치기(판정 순간) → 성공 강조(간파 성공 시에만) |
| 기 모으기(10-16 변경) | **루프**. 오른쪽 단검 칼날 중간 소켓 `Dagger_Weapon_R/VFX_InsightCharge`(은희가 추가)에 부착, 시전부터 판정까지 재생. Gather_Core 는 1→1.2 배 맥동(0.5초 주기) |
| 기 모으기 끝 | 올려치기 재생 순간(·OnEnd·안전 타임아웃) **튀기기 원샷 `_Pop` 을 소켓에서 재생 + 루프 즉시 해제**(ReleaseImmediate) — 페이드 없이 튀기기가 덮는다. 판정 타이밍(0.15/0.6)과 무관하게 맞는다 |
| 올려치기 | 세로 초승달 베기(BM Slash 를 세움, 판정 상자 정면 0.9m·높이 1m 중심, 높이 ~1.8m) + 초승달 위 끝 붉은 ✦ 반짝임(무기 뼈대 추종 안 함) |
| 성공 강조 | 같은 자리 초승달 재번쩍 + 붉은 섬광 + 방사 불꽃 + 붉은 연기 잔광(~0.4초) |
| 일반·변신 | 같은 연출 |
| 🔸 부착 방식(10-16 민경 확정) | **애니메이션 이벤트 + `EffectSocketPlayer`** 로 재생한다(코드 타이머·RPC 로 위치를 정하지 않는다). 소켓 위치는 민경이 직접 잡는다. `Attack_Up_01` 클립에 `EffectAnimEvents` 이벤트를 심고, 각 연출은 Id 를 가진 `EffectSocketPlayer` 하나씩 — 아래 표 |
| 임시 시전 연출 | `FX_Interrupt_Flash`(interruptCastPrefab) 제거 |
| 🔴 타이밍 사실 | 판정·결과 = `hitDelay` 0.15초(타이머가 먼저). 애니 Hit 이벤트 0.6초·End 1.275초는 `hasResolvedHit`·스킬 종료(skillDuration 0.6)로 **실제 무시됨**. 정렬은 은희가 결정 |
| 성공 알림(제안) | 서버 전용 `PlayerInterruptSkillBase.ServerInterruptSucceeded` → `AssassinSkillView` 전 피어 RPC(베이스 무변경) |

### 부착 계획 — 애니메이션 이벤트 × EffectSocketPlayer (10-16)
| 연출 | EffectSocketPlayer 소켓 | 애니 이벤트(`EffectAnimEvents`) | 비고 |
|---|---|---|---|
| 기 모으기 `_Windup`(루프) | `Dagger_Weapon_R/VFX_InsightCharge`(칼날 중간, 민경이 잡음) | 시작 프레임 `StartEffect(id)` | 이펙트 매니저가 위치·회전만 따라감(뼈대 스케일 무시) |
| 튀기기 `_Pop`(원샷) | 위와 같은 소켓 | 올려치기 프레임 `PlayEffect(id)` | 같은 프레임에 기 모으기 `StopEffect(id)` — 엔트리 `outroDuration` 0(해제 파트 없음)이면 Release 즉시 반납 = "즉시 해제" |
| 올려치기 `_Slash`(원샷) | 민경이 잡는 올려치기 소켓 | 올려치기 프레임 `PlayEffect(id)` | ✅ 10-16 원점 = 베기 중심으로 옮김. 베기 면 = 소켓 XZ 면, **+Z 에서 시작해 +X 쪽으로 진행**(민경 확인 후 10-16 수정), 칼끝 ✦ = +X 0.85m(`_Success` 도 같은 원점·회전) |
| 성공 강조 `_Success`(원샷) | 올려치기와 같은 소켓 | (애니 이벤트 아님) 성공 알림을 받은 쪽이 `PlayOnce()` | 성공은 서버 판정이라 애니 이벤트로 못 낸다 — 알림 경로는 은희와 합의 |
- 애니 이벤트는 그 애니를 재생하는 **모든 피어에서 각자** 발화한다 — 이펙트 쪽에 `IsServer` 가드를 넣지 말 것(`EffectSocketPlayer` 주석).
- 판정(`hitDelay` 0.15초)과 올려치기 프레임(0.6초) 정렬은 은희 결정 사항 — 이벤트를 올려치기 프레임에 두면 동작과 연출은 늘 맞는다.
- 안전망: 판정 전 취소(피격·사망)면 `StopEffect` 이벤트가 안 올 수 있다 — `EffectSocketPlayer.safetyTimeout`(기본 5초, 10초 미만)과 OnDisable 회수가 받친다. 코어·빛 입자 수명이 10초라 그보다 짧게 둔다.

| 산출물 | MyProject `Export/Player3/BM Insight_Windup`·`_Pop`·`_Slash`·`_Success` + 테스터 `1.Scripts/InsightTester.cs` → 컨펌 → `Players/Player3/RightClick_Insight/FX_Assassin_Insight_*`, 카탈로그 `Assassin_RC_Insight*` |

---

# ▶▶▶ 계획 = **어쌔신 일반 Q VFX — 관통 일섬(PierceLine + PierceBurst)** (2026-10-16 · grill 확정 · ✅ MyProject 컨펌 · ✅ 반입·배선 — 컴파일 에러 0 · ⏳ Play·MPPM)

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-16). 변신 Q(Storm Slash — 바닥에 남는 무거운 자국)와 반대로 **허리 높이의 가늘고 날카로운 검붉은 일섬**.

## 결정
| 항목 | 결정 |
|---|---|
| 콘셉트 | 돌진 중은 가볍게(일섬 프리팹 안의 붉은 속도선), 무게는 **끝나는 순간 경로 위 일섬** |
| 위치·길이 | 시작점(OnClientPlay 위치·방향) → 실제 끝점(OnEnd 위치). 벽 조기 종료도 길이 일치 |
| 시점 | `OnEnd`(서버 종료 = 돌진 끝 + 0.1초) — "베인 자리가 늦게 드러나는" 박자. 전 피어 같은 경로 |
| 높이·색 | 허리 높이 공중, 바닥 흔적 없음. 검은 코어(알파) + 진한 피색 번짐(가산). 총 약 0.5초 |
| 두께 | 코어 ~0.2m, 번짐 ≤ 1.2m(판정 폭) |
| 구조 | **프리팹 2개**: `_Line`(+Z 1m 메시 파티클, **Z 배율만 = 이동 거리**) + `_Burst`(끝점, 배율 1, BM Thrust Slash 재채색). EffectManager 에 **축별 배율 원샷 오버로드** 추가 |
| 적중 연출 | 없음(몬스터 공용 피격으로 충분) |
| 임시 FX_Dash_Trail | 일반 Q 에서도 제거 |
| 진행 | MyProject `Export/Player3/BM Thrust Slash_PierceLine`·`_PierceBurst` + 테스터 `1.Scripts/PierceLineTester.cs` → 컨펌 → MainProject `Players/Player3/Q_Dash/FX_Assassin_PierceLine`·`_PierceBurst`, 카탈로그 `Assassin_Q_PierceLine`·`Assassin_Q_PierceBurst` |

## 리스크
- 메시 파티클(Local + Hierarchy)에 루트 비균등 배율이 길이로만 들어가는지 — 테스터로 확인.
- 클라에서는 OnEnd 가 네트워크 지연만큼 늦게 온다.

---

# ▶▶▶ 계획 = **어쌔신 R 변신 VFX — Blade Vortex 생성(0.5초)·유지·사라짐(0.5초)** (2026-10-14 · ✅ 1단계 컨펌 · ✅ 2단계 반입·배선 — 컴파일 에러 0 · ⏳ Play·MPPM)

> 🔸 **단계 분리(2026-10-14 민경)**: 1단계 = MyProject 에서 `_Fast`·`_End` 수정 + 테스터로 확인까지. **컨펌 뒤** 2단계(아래 B·C·D — MainProject 반입·코드·배선)를 진행한다.
> 1단계 산출물: `Export/Player3/BM Blade Vortex_Fast.prefab`·`BM Blade Vortex_End.prefab`, 테스터 `1.Scripts/BladeVortexTester.cs`(종료 = StopEmitting + ×outroPlayRate + End 원샷, outroDuration 뒤 루프 제거 — 2단계 EffectManager 동작과 동일). 빌드 스크립트는 세션 scratchpad `vortex_build.py`.

> 브랜치 `feature/VFX`. 결정: 민경(grill 2026-10-14). `AssassinSkillView`·`Player_Assassin.prefab` 은 은희 영역 → handoff 로 공유.

## 목표
- MyProject `Assets/Export/Player3/BM Blade Vortex.prefab` 을 어쌔신 **R 변신 루프 연출**로 쓴다:
  변신 시작에 **0.5초 만에 차오르고** → 변신 동안 유지 → 변신이 끝나면 **0.5초 만에 바깥으로 흩어지며 사라진다**.

## 결정 (grill 2026-10-14)
| 항목 | 결정 |
|---|---|
| 역할 | `transformLoopPrefab`(임시 `CharacterCirclePurple`) 대체. 시전 1회 `transformCastPrefab`(`Poof_electric`)은 **제거** |
| 사라짐 시점 | `AssassinState.TransformChanged(false)` 하나 — 시간 만료·수동 해제·쓰러짐/사망 모두 같은 연출 (쓰러짐도 `ResetForDownServer` → 같은 이벤트임을 확인) |
| 재생 경로 | **EffectManager 전환**(Instantiate/Destroy 임시 경로에서 이 연출만 빠짐). 엔트리 하나 = `parts`[Vortex] + `outroParts`[End] |
| 엔트리 위치 | **`EffectCatalog.Assassin_R_BladeVortex`**(프리워밍). `AssassinSkillView` 는 `PlayLooping` / `Release` 만 부른다 |
| EffectStagePlayer 재활용 | **안 함**(Q13 다안). 단계 교체가 `ReleaseImmediate` 라 무작위 파티클 이펙트에서는 두 번 툭 끊긴다 |
| 생성 0.5초 | 사본의 **기존 시스템을 직접 수정**: t=0 버스트(정상 상태 개체 수의 약 절반) + 알파 상승 키를 수명의 ~13% 로 + Size 시작값 ~0.8 + Flecks 지연 0 |
| 사라짐 0.5초 | **`EffectEntry.outroPlayRate`(신규, 기본 1)** — Release 시 남은 루프 파티클을 ×8 로 돌려 자기 페이드아웃을 0.5초 안에 끝냄 + 바깥으로 흩어지는 `_End` 원샷을 겹친다 |
| 저작 | MyProject **사본**에 제가 수치를 넣는다. 원본 `BM Blade Vortex` 는 그대로 |
| 위치·크기 | 플레이어 루트 추종 + 오프셋(Vortex 아랫부분이 발밑에 오게 추정값, Play 에서 조정). 배율 1 시작 |
| 이름·폴더 | `Assets/50.Art/VFX/Common/Players/Player3/R_Transform/FX_Assassin_BladeVortex.prefab` · `_End.prefab` · `_Entry.asset` |

## 만드는 것
### A. MyProject (샌드박스 `D:\김민경\유니티\My project`)
1. `Assets/Export/Player3/BM Blade Vortex_Fast.prefab` — 원본 복제(새 guid).
   - t=0 버스트: Smoke_burst 12 · Wind_Stuff 6(슬롯 신규) · Flecks 10 · Smoke_loop 3 · Wind_Radial 1. FuzzAdd/FuzzAlpha 의 기존 t=0 버스트 유지.
   - Flecks `startDelay` 1.0 → 0.
   - Color over Lifetime 알파 상승 키(0.31~0.52) → ~0.13. Size over Lifetime 시작값(0.45~0.61) → ~0.8.
   - `Burst_1` 컨테이너의 꺼진 렌더러에 걸린 **없는 머티리얼 참조**(guid b739…) → 비움.
2. `Assets/Export/Player3/BM Blade Vortex_End.prefab` — 같은 머티리얼로 만든 **원샷**(looping off, 총 ~0.5초).
   - 바람 원판·연기·바람 줄기·반짝이: t=0 버스트, 수명 0.4~0.5초, 알파 1→0, 크기 1→0.5, **radial 속도 바깥(+)**, 회전(orbital)은 유지 방향 그대로.
   - FuzzAlpha 블롭 t=0 1개(생성 때와 같은 마무리 펄스).

### B. MainProject 반입 (guid 그대로)
3. 위 두 프리팹 → `Players/Player3/R_Transform/FX_Assassin_BladeVortex(.prefab|_End.prefab)`.
4. **의존 에셋 23개** — MainProject 에 하나도 없다(guid 검사 완료). 기존 관례(`50.Art/VFX/{ShaderGraph,_Materials,Textures,Models}`)에 `PilotoStudio/` 하위 폴더로 넣는다:
   - `ShaderGraph/PilotoStudio/`: `UberFXSG.shadergraph` + 서브그래프 4개(`Functions/`)
   - `_Materials/PilotoStudio/`: 머티리얼 7개
   - `Textures/PilotoStudio/`: 텍스처 9개
   - `Models/PilotoStudio/`: `SpiralMesh.fbx`, `UV_Distorted_PolarCord.fbx`
   - **새 파일 추가만**(교체 없음)이라 Unity 를 켠 채로 가능. `50.Art` 는 SVN — **svn add 는 사람이**.

### C. Effects 코드 (VFX 영역)
5. `EffectEntry.cs` — `[Min(1f)] public float outroPlayRate = 1f;` (툴팁: Release 시 기존 루프 파트를 이 배속으로 돌려 남은 입자를 빨리 걷는다. 1 = 지금 동작).
6. `EffectManager.Release` — StopEmitting 거는 그 루프에서 `outroPlayRate > 1` 이면 `DriverOf(instance)?.SetPlayRate(instance, active.playRate * outroPlayRate)`. 기본 1 이라 **기존 엔트리(ChargeBall 등) 동작 불변**.
7. `EffectCatalog.cs` + `.asset` — `[Header("플레이어 — 어쌔신 변신(R)")] Assassin_R_BladeVortex`.
8. `FX_Assassin_BladeVortex_Entry.asset` — parts[Vortex], outroParts[End], `outroDuration` 0.6, `outroPlayRate` 8, `prewarmCount` 2.

### D. 어쌔신 (은희 영역 — handoff)
9. `AssassinSkillView.cs`
   - `transformLoopPrefab` · `transformCastPrefab` · `transformSocket` · `PlayTransformCast` 제거.
   - `EffectHandle transformLoop` + `[SerializeField] Vector3 transformEffectOffset` · `float transformEffectScale = 1`.
   - `HandleTransformChanged(true)` → (핸들 없을 때만) `PlayLooping(catalog.Assassin_R_BladeVortex, transform, offset, scale)`. `(false)` → `Release`. 늦은 접속 `SyncLoops` 는 그대로 동작.
   - `OnDestroy` 에서 핸들 회수. 엔트리가 비면 경고 후 건너뜀.
10. `AssassinTransformSkill.cs:49` — `PlayTransformCast` 호출 1줄 제거.
11. `Player_Assassin.prefab` — 제거된 3개 필드 줄 삭제, 새 오프셋·배율 값.
12. 문서: `Docs/tech/handoff-assassin-r-vfx.md`(은희용), `CONTEXT.md` 세션 기록.

## 범위 밖
- 어쌔신의 나머지 임시 연출(평타·Q 궤적·E·간파·백어택)의 EffectManager 전환.
- `EffectStagePlayer` 변경.

## 리스크
- **×8 배속 마무리**: 마지막 0.5초에 소용돌이가 확 빨라진다 — 의도("빨려 사라짐")와 맞는지 Play 에서 확인. 어색하면 배속↓ + `outroDuration`↑ 로 조정(0.5초 목표와 맞바꿈).
- **버스트 펄스**: t=0 버스트 개체가 같은 나이라 ~3초 뒤 같이 사라지며 밀도가 한 번 출렁일 수 있다 — 버스트 양으로 조절.
- 알파 키를 앞당겨 **유지 모습이 살짝 진해진다**.
- Piloto `UberFXSG` 는 샌드박스 URP 17.4 에서 저작, MainProject 는 17.3(ShaderGraph 는 둘 다 17.3) — 반입 후 셰이더 컴파일 에러 확인.
- 아웃트로 중 `SetPlayRateForTarget`(히트스톱)이 같은 대상에 오면 배속을 덮어쓴다 — 플레이어 대상 히트스톱 호출은 현재 없음.
- `AssassinSkillView`·프리팹은 은희 영역 — handoff 필수.

## 검증 (완료 조건)
- Refresh 후 컴파일 에러 0, 셰이더 에러 0. 반입 파일 `cmp` 일치 + guid 유지.
- 기존 루프 엔트리(ChargeBall 등) `outroPlayRate` = 1 확인.
- 사람 확인(Play):
  - R 시전 → ~0.5초에 소용돌이가 차고 플레이어를 따라다님. `Poof_electric` 안 나옴.
  - 시간 만료 / 2초 뒤 수동 해제 / 쓰러짐 → ~0.5초에 툭 끊김 없이 흩어져 사라짐.
  - 연속 R(쿨 끝난 뒤) 반복 시 풀 누수 경고 없음.
  - MPPM: 다른 피어에서도 보임, 변신 중 늦게 접속한 클라도 보임.

---

# ▶▶▶ 계획 = **사운드 데이터 테이블 (SoundCatalog 키 엔트리 + GameData.xlsx `Sound` 시트)** (2026-10-08 · 승인 ✅ · ✅ 구현 완료 · Play 확인(10-08))

> 브랜치 `feature/SoundAssetDataTable` (development `37f97719` 기준). 결정: 은희.

## 목표
- 사운드를 **문자열 키**로 부를 수 있게 하고, 키별 재생 수치를 **기획이 `GameData.xlsx` 에서** 고친다.
- 클립(에셋 참조)은 xlsx 에 못 넣으므로 **키 → BroAudio `SoundID` 연결은 SO(인스펙터)**, 수치는 xlsx.

## 결정 (grill 2026-10-08)
| 항목 | 결정 |
|---|---|
| 기존 구조와의 관계 | **`SoundCatalog` 확장.** 새 SO 를 따로 만들지 않는다. 기존 `UIClick`·`TitleBGM`·`LobbyBGM`·`InGameBGM` 프로퍼티와 호출부는 그대로 동작(호환 유지) |
| 엔트리 | `SoundCatalog` 에 `entries` 배열 추가. 원소 = `key`(string) + `sound`(SoundID) + 아래 수치 |
| xlsx 수치 | 볼륨·피치(랜덤 범위 포함) · 3D 여부와 최소/최대 거리 · 재생 제한(동시 재생 수, 재트리거 쿨다운) · 키 이름/설명 |
| API | `AudioManager.Play(string key)` / `Play(string key, Vector3 pos)` — 키 조회 후 엔트리 수치를 적용해 재생. 없는 키·무효 SoundID 는 경고 후 null |
| 네트워크 | **로컬 재생만.** 사운드 전용 RPC 없음. 호출 측이 이미 받는 이벤트·RPC 시점에서 로컬로 부른다 |
| 범위 | 테이블 + API + EditMode 테스트까지. **실제 재생 지점 연결은 다음 작업** |

## 접근
1. `SoundCatalog` 에 직렬화 엔트리 구조체·배열 추가, `[DataTableSheet("Sound")]` 로 xlsx `Sound` 시트에 노출. `SoundID` 필드는 `[DataTableIgnore]`.
2. 키 조회는 순수 클래스(딕셔너리 캐시, 중복 키·빈 키 검출)로 분리 → EditMode 테스트 대상.
3. 재생 제한(동시 수·쿨다운)도 순수 판정 클래스로 분리(시간을 인자로 받음) → 테스트 대상.
4. `AudioManager` 에 키 재생 메서드 추가. 볼륨·피치·3D 거리는 BroAudio 플레이어 API 로 적용(가능한 범위는 Codex 가 BroAudio 소스 확인 후 결정, 안 되는 항목은 보고).
5. `Tools/Data/Export Template`(병합)로 `Sound` 시트 생성 확인 — **xlsx 는 SVN 이라 커밋은 사람이 잠금 후** 진행. 에이전트는 xlsx 를 쓰지 않고 "필드 목록 보기"로만 확인.
6. `Docs/tech/data-table.md` 시트 표에 `Sound` 추가 + 사운드 사용법 짧게 문서화.

## 리스크
- **데이터 테이블은 배열 크기를 못 바꾼다** → 새 키(엔트리) 추가는 SO 인스펙터에서. xlsx 는 기존 엔트리의 수치·키 문자열만 바꾼다.
  - ⏩ 2026-10-14 [PLAN-sound-sync.md](PLAN-sound-sync.md) 로 대체 — 배열 크기·SoundID 연결·엔티티 생성까지 `Tools/Data/사운드 동기화` 가 xlsx 로 한다.
- 키를 xlsx 에서 바꾸면 코드에서 부르는 키와 어긋날 수 있다 → 시작 시 중복·빈 키 검증 경고.
- BroAudio 엔티티 자체에도 볼륨·피치가 있다 → 엔트리 값은 **곱(배율)** 로 적용할지 덮어쓸지 구현 시 확정해 문서화(기본안: 배율, 1 = 엔티티 값 그대로).
- 기존 `SoundCatalog.asset` 직렬화가 바뀜 — 기존 SoundID 4개가 그대로 남는지 diff 확인.

## 검증 (완료 조건)
- Refresh 후 컴파일 에러 0.
- EditMode: 키 조회(정상·없는 키·중복·빈 키), 재생 제한(동시 수·쿨다운 경계) 테스트 통과.
- `Tools/Data/Export Template 필드 목록 보기` 에 `Sound` 시트 필드가 나온다.
- `SoundCatalog.asset` 기존 4개 SoundID 유지.
- 사람 확인(Play): 기존 BGM·UIClick 정상 / 테스트 키 재생 시 소리.

---

# 몬스터 배치 적용 (2026-10-07 · 배치도 수정 후 사용자 구현 승인)

- 목표: 현재 `4.MapScene`의 전투 존에 총 **41마리**를 배치한다.
- 1존 `ZoneS_typeA`: 상하좌우 각각 ChompBot 1 + HumanoidBot 1, 중앙 MortarBot 1 = **9**.
- 2존 `ZoneM_typeC`: **2층 없음**. 진입 방향에서 가장 안쪽에 SpinnerBot 1 = **1**.
- 3존 `ZoneL_typeB`: 네 모서리 각각 PeekABot 1 + ChompBot 2 + HumanoidBot 1(16), 좌우 알람 플랫폼 각각 ChompBot 2 + MortarBot 1 + HumanoidBot 1(8), 상하 2층 각각 양 계단 HumanoidBot 1씩 + 중앙 MortarBot 1(6), 중앙 섬 GauntletBot 1 = **31**.
- 시작 및 보스방 입구는 **0**. 기존 2존 선택/회전 씬 오버라이드와 사용자 맵 수정 사항을 유지한다.
- 방식: 세 존 프리팹의 기존 `ZoneMonsterSpawnSet` / `MonsterSpawnPoint` 저작 데이터만 수정. 모든 지점은 개별 프리팹 지정, count 1, scatter 0. 이전 전투 존 마커를 교체해 중복 생성 방지.
- 권한: 기존 호스트 스폰 및 NGO 복제를 그대로 사용. 런타임 코드·스탯·몬스터 프리팹·SVN 아트 변경 없음.
- 검증: Unity 프리팹 로드/저장, 정확한 종류별 수량(Chomp 16/Humanoid 14/Mortar 5/Peek 4/Spinner 1/Gauntlet 1), 바닥 첫 충돌과 높이·장애물·이동 가능 영역, 참조/존 비네트워크 규약, 기존 수정 보존. 호스트/클라 Play 검증 가능 여부는 결과에 별도 기록.
- 범위: 해당 프리팹을 쓰는 다른 맵에도 같은 저작 배치가 적용된다. 새로운 스폰 시스템이나 아트 구조 변경은 하지 않는다.
- 결과: **적용 완료**. Unity 6000.3.16f1에서 저장 후 다시 읽은 지점/종류/회전/참조가 41개 모두 일치. 바닥 직접 검출 41/41, 바닥 가장자리 지지 검사 통과, 장애물 겹침 0. 이동형 37개 NavMesh 확인(최대 보정 거리 0.082m). 기존 존 네트워크 규약 검사 메서드 2개 통과. 작업 전 프리팹과 비교해 기존 지형·소품 직렬화 블록 동일함을 확인했다. 임시 저작/검증 도구는 제거했다. **실제 호스트·클라이언트/MPPM Play는 미실행**.

---

# ▶▶▶ 계획 = **지점 지정(GroundPoint) 확장 — 마우스 원 + 사거리 밖 자동 접근** (2026-10-07 · 그릴 Q24~Q28 · 승인 ✅ · 구현 = claude-alt)

> 브랜치 `feature/GunnerSkillIndicatorUpgrade`. 결정: 은희. 아래 "스킬 직선 인디케이터" 작업의 후속.
> 팔라딘 Q·E 미리보기는 **안 한다**(은희 결정).
>
> 🔴 **범위 확정(10-07 은희): 기능만 만들어 둔다. 거너 R 에셋은 기획서 §9 대로 `SingleTarget` 유지, 기획서도 안 고친다.**
> → 아래 1·7 은 **하지 않는다.** 2~6 은 **데이터 옵션**으로 만들어, 나중에 에셋 값만 바꾸면 거너 R 이 지점 지정으로 동작하게 한다.
> 옵션(안): `PlayerSkillData` 에 GroundPoint 전용 — 사거리 밖 처리 `Clamp`(기본, 기존 동작) / `AutoApproach`, 마우스 원 반경(0 = 안 그림), 호버 원 거리(기본 3).
> 거너 R `OnServerStart` 는 `HasAimPoint` 면 지점·대상 없음, 아니면 기존 대상 경로 — 둘 다 지원.
> 지금 게임에서 쓰는 스킬이 없으므로 검증은 EditMode + (선택) 로컬에서만 거너 R 에셋을 임시로 바꿔 Play — **에셋 변경은 커밋하지 않는다.**

## 지금 (조사)
- 거너 R `GunnerTrackingLaserSkill` = `SingleTarget`. 서버 `CanUse(direction, target)` 가 대상 사거리(12)를 보고, `OnServerStart` 가 **대상 위치**에 레이저를 띄워 `ServerInitialize(owner, target, …)`.
- `GunnerTrackingLaser` 는 **대상이 없으면 이미** `retargetRadius`(8) 안 최근접 적을 찾는다(`FindNearestEnemy`, L145~148).
- `GroundPoint` 조준은 있다(`PlayerSkillTargeting.UpdateGroundPointPreview`) — 단 **사거리 밖은 경계로 클램프해 바로 시전**, 자동 접근 없음.
  지점 마커 데칼 슬롯 `SkillRangeIndicator.groundMarkerDecal` 은 **비어 있다**.
- 자동 접근(`BeginMoveToCast`/`TickMoveToCast` + 서버 `ApplyServerAutoApproach`)은 **Unit 대상 전용**.
- 🔴 서버 `StartSkillServer` 는 `CanApproveSkill`(→`CanUse`) **뒤에** `SetAimPoint` 한다 → 지금은 `CanUse` 가 지점을 못 본다.

## 결정 (2026-10-07)
| | 결정 |
|---|---|
| Q24 시전 | R → 조준 → **클릭**. 원 중심이 사거리 안이면 즉시 시전. **밖이면 클릭 지점을 저장 → 그 지점이 사거리에 들 때까지 직선 이동 → 자동 시전** |
| Q25 레이저 | 저장 지점에 생성, **대상 없이** 시작 → 기존 재조준(반경 8 최근접 적). 없으면 제자리 |
| Q26 마우스 원 | 반경 = 레이저 `radius`(2). 조준 중엔 **사거리 무관 마우스를 따라감**(클램프 안 함). **호버에도 표시** — 캐릭터 정면(`CurrentFacing`) **3m 앞**에 + 기존 사거리 원 |
| Q27 사거리 밖 | 색 변화 **없음**(같은 모양). 커서 상태는 기존대로 |
| Q28 문서 | `Docs/design/character/character_gunner.md` §9 를 지점 지정으로 갱신 (답 없음 → 권장안으로 진행) |

## 만드는 것
1. **데이터**: `GunnerTrackingLaserData.asset` `targetingMode` 1 → **2(GroundPoint)**.
2. **조준(`PlayerSkillTargeting`)**: GroundPoint 를 **스킬별로 클램프/접근 선택** — 거너 R 은 "클램프 안 함 + 사거리 밖 클릭 = 지점 자동 접근".
   (다른 GroundPoint 스킬은 현재 없음. 기존 클램프 동작은 기본값으로 남긴다 — 데이터 플래그 or 스킬 오버라이드 중 단순한 쪽.)
3. **지점 자동 접근**: `pendingTarget`(Unit) 옆에 `pendingPoint` 경로 추가 — 오너 `TickMoveToCast` 정지식 = 기존과 동일(`castRange - RangeBuffer`),
   도달 시 `ExecuteTargetedSkill(slot, null, point, true)`. 서버 쪽 `ApplyServerAutoApproach` 에 지점 버전(RPC 인자 추가). 취소 조건 = 기존과 동일(다른 입력·대시·사망).
4. **서버 승인**: `StartSkillServer` 에서 `SetAimPoint` 를 `CanApproveSkill` **앞으로** 옮기거나 지점을 넘긴다. 거너 R `CanUse` = 지점이 사거리(+버퍼) 안.
   `OnServerStart` = `AimPoint` 에 생성, `ServerInitialize(owner, null, …)`.
5. **마우스 원 데칼**: `SkillRangeIndicator.groundMarkerDecal` 배선(저작 메뉴, 기존 사거리 원 머티리얼 재사용) + 반경 설정 API(`SetGroundMarker(show, point, radius)`).
   반경은 `GunnerTrackingLaserData.Radius` 에서.
6. **호버**: 대상 지정 스킬 호버 원(이미 있음) + 지점 스킬이면 마커 원을 `CurrentFacing * 3` 위치에.
7. **문서**: `character_gunner.md` §9.

## 리스크
- 서버 승인 순서 변경(4)이 **팔라딘 R(SingleTarget)** 에 영향 없게 — SingleTarget 은 `HasAimPoint=false` 그대로.
- 자동 접근 RPC 인자 추가 = 네트워크 계약 변경. 기존 Unit 경로 동작 동일해야 함.
- 클릭 지점이 벽 너머/절벽이면 직선 이동이 막혀 영원히 못 닿을 수 있음 → 기존 Unit 접근과 같은 정책(막히면 그대로, 다른 입력으로 취소).

## 검증
- EditMode: 지점 사거리 판정·접근 정지식(순수 함수로 빼서).
- Play(은희, MPPM): 사거리 안 클릭 즉시 시전 / 밖 클릭 → 직선 이동 후 자동 시전 / 이동 중 다른 입력·대시 취소 / 레이저가 지점에 생겨 반경 8 적 추적 / 마우스 원이 사거리 밖에서도 따라옴 / 호버 시 사거리 원 + 정면 3m 원 / **비호스트 클라에서도 동일** / 팔라딘 R 기존 동작 유지.

---

# ▶▶▶ 진행 중 = **스킬 직선 인디케이터 + HUD 호버 미리보기** (2026-10-06 · 그릴 21문항 완료 · 요약 승인 ✅ · 구현 = Codex 위임)

> 브랜치: `feature/GunnerSkillIndicatorUpgrade` (워크트리 `MainProject-Worktree2`, origin/development `373ef5a6` 기준, 아트 r392).
> 결정: **은희**. 그릴·계획: Claude. **구현: Codex.** 플레이어·UI 는 은희 영역이라 외부 승인 불필요.
> 레퍼런스: 롤 **제라스 Q(Arcanopulse)** — 폭 일정 직사각형이 차지에 따라 길어진다(700→1450, 폭 145).

## 지금 상태 (조사 결과)

- 직사각형·직선 인디케이터는 **없다.** 있는 것: `PlayerAimIndicator`(방향 데칼, 크기 고정) ·
  `SkillRangeIndicator`(원 데칼, ClickToConfirm 스킬만) · `SkillCursorView`(커서).
- 거너 Q 는 **판정은 이미 제라스 Q 와 같다** — `GunnerChargeLaserData.RangeAt(ChargeFactor(t))` = Lerp(6,16), 폭 `BeamWidth` 1.2,
  `maxChargeTime` 1.2, 벽은 `GunnerBeamAttack.CastLength(origin, dir, range, width/2, BlockingLayers)`(서버 가드 없음 → 오너가 매 프레임 호출 가능).
  보이는 건 총구 차지 파티클뿐.
- HUD 호버는 이미 있다 — `UI/Combat/SkillSlotHover.cs` 가 `OnPointerEnter/Exit/OnDisable` 로 `SkillTooltipView` 를 켜고 끈다.
  `player`·`slot` 을 이미 들고 있다. 🔴 HUD 위에서도 `PlayerAimIndicator.AimDirection` 은 계속 갱신된다(화면 아래쪽 바닥을 가리킴) → 미리보기 방향으로 못 쓴다.
- 판정 모양 실측:

| 스킬 | 판정 | 크기 원본 | 벽 |
|---|---|---|---|
| 거너 Q `GunnerChargeLaserSkill` | 총구 직선 박스, 관통 | data `minRange 6`/`maxRange 16`/`beamWidth 1.2`/`muzzleHeight 1` | `CastLength` 로 잘림 |
| 거너 RMB `GunnerInterruptSkill` | 정면 박스 + 반동 이동 `-aim` | 박스 = `Gunner_Armature.prefab` `InterruptAttack` BoxCollider(1.4×1.4, 앞 0.2~1.6m) / 반동 data `recoilDistance 1.5` | 박스 없음 / 반동은 모터 스윕(적 통과, 벽·오브젝트 정지) |
| 팔라딘 RMB `FirstMeleeInterruptSkill` | 정면 박스 | `Paladin_Armature.prefab` `InterruptAttack` BoxCollider(1.6×1.6, 앞 0.1~1.7m) | 없음 |
| 거너 E `GunnerCoolBackstepSkill` | 이동 `-aim` | data `distance 3` | 모터 스윕(적·아군·벽 정지, `BlockOtherPlayersOverride`) |

  박스 크기는 SO 가 아니라 `PlayerSkillBase.HitboxAnchor`(`ColliderInfo`) 에 있다 — 런타임에 거기서 읽는다.

## 그릴 결과 (2026-10-06)

| | 결정 |
|---|---|
| 범위 | **공용 `SkillLineIndicator`** 를 base `Player.prefab` 에. 이번 연결 = 거너 Q·거너 RMB·팔라딘 RMB·거너 E |
| 렌더링 | **Quad 메시**(데칼 아님). 발밑 +0.05, **깊이 테스트 켬**(가려진다) |
| 벽 | **판정이 잘리면 표시도 잘린다.** 판정이 벽을 안 보면 표시도 안 자른다 (Q=`CastLength`, 화살표=모터 스윕, RMB 박스=안 자름) |
| 차지 중 | **현재 길이만**. 최대 사거리 윤곽 없음 |
| 가시성 | **오너 화면만.** 네트워크 추가 없음 |
| 그림 | 임시 셰이더그래프(반투명 면 + 테두리), 색·알파·텍스처는 프로퍼티. 나중에 아트 교체 |
| 호버 미리보기 | HUD 슬롯 호버 시. 방향 = **캐릭터 정면**(`transform.forward`), 화살표는 그 반대. **미리보기도 벽 자르기 적용** |
| 프리팹 | `_forTip` 별도 프리팹 **안 만든다.** 한 인디케이터에 **진한 층(fill) · 연한 층(ghost, 알파 낮음) · 화살표** + 모드 2개 |
| 화살표 | 몸통+삼각 머리, **박스와 다른 색**(흰 계열) — "이동" vs "공격 범위" 구분 |

**스킬별 표시**

| 스킬 | 차지 중 | 호버 미리보기 |
|---|---|---|
| 거너 Q | fill = 현재 길이(벽 클립), 폭 1.2 | fill = 최소 6 · ghost = 최대 16 (둘 다 벽 클립) |
| 거너 RMB | — | fill = 정면 박스 + 화살표 뒤로 1.5(스윕 클립) |
| 팔라딘 RMB | — | fill = 정면 박스 |
| 거너 E | — | 화살표 뒤로 3(스윕 클립) |

## 만드는 것

**`Player/Skill/Targeting/SkillLineIndicator.cs`** (신규, MonoBehaviour, base `Player.prefab` 자식 `SkillLineIndicator`)
- 오너 아니면 꺼 둔다 — `PlayerAimIndicator.OnNetworkSpawn` 패턴.
- 자식 Quad 3개(fill / ghost / arrow). 피벗 = **시작점**(길이가 앞으로만 자라게).
- API (안):
  - `ShowCharge(Vector3 origin, Vector3 dir, float startOffset, float length, float width)` — 매 프레임 갱신
  - `BeginPreview(ISkillPreviewSource src)` / `EndPreview(ISkillPreviewSource src)` — 호버 중엔 `LateUpdate` 에서 매 프레임 `src` 에 다시 물어 갱신(이동·회전 추종)
  - `HideCharge()` / `HideAll()`
  - 차지가 켜지면 미리보기는 숨긴다(우선순위: 차지 > 미리보기).
- 테두리가 늘어나 보이지 않게 셰이더에 **월드 길이·폭을 프로퍼티로** 넘긴다(스케일만 쓰면 테두리가 늘어남).

**`ISkillPreviewSource`** (신규 인터페이스) + **`SkillPreviewShape`** (struct, 순수 데이터)
- `bool TryGetPreview(Vector3 origin, Vector3 forward, out SkillPreviewShape shape)`
- `SkillPreviewShape` = fill(startOffset, length, width) · ghost(length, 유무) · arrow(방향 부호, length, 유무).
- 형태 계산은 **순수 함수**로 빼서 EditMode 테스트가 물리 없이 돈다(클립 값은 인자로 받기).
- 구현: `GunnerChargeLaserSkill` · `GunnerInterruptSkill` · `FirstMeleeInterruptSkill` · `GunnerCoolBackstepSkill`. 없는 스킬은 표시 안 함.

**거너 Q 차지 연결** (`GunnerChargeLaserSkill`)
- `OnOwnerTick` 에서 `elapsed` → `RangeAt(ChargeFactor(elapsed))` → `CastLength(...)` → `ShowCharge`.
  origin 은 발사와 같게(`position + up*MuzzleHeight`), 그리기만 바닥 높이로.
- 끄기: 오너 측 `OnEnd`(발사·취소·사망·대시 전부 지남) + 비서버 오너는 `GunnerBeamView.PlayChargeLaser → EndCharge` 경로도 확인.

**RMB 박스** — `HitboxAnchor` 의 BoxCollider(center·size, Armature 로컬)를 바닥 사각형으로 변환. 미리보기는 캐릭터 정면 기준이므로 **Armature 현재 회전이 아니라 `forward` 로 다시 놓는다.**

**화살표 클립** — `PlayerMotionSweep.Resolve(capsule, desiredDelta, ...)` 를 실제 이동과 **같은 마스크·옵션**으로 호출.
RMB 반동(적 통과)과 E(적·아군 정지)의 마스크가 다르다 — `PlayerMotor` 의 `PassThroughEnemiesOverride`/`BlockOtherPlayersOverride` 처리 방식을 확인해 재현할 것.

**HUD 연결** (`SkillSlotHover`)
- `OnPointerEnter` → 로컬 플레이어 `SkillLineIndicator.BeginPreview(skillController.GetSkill(slot) as ISkillPreviewSource)`,
  `OnPointerExit` / `OnDisable` → `EndPreview`. `Bind` 시 `pointerInside` 면 시작.
- 호버 중 시전되면(슬롯 클릭·키 입력 → `PlayerSkillController.IsSkillActive`) 미리보기 숨김. Q 는 차지 표시로 넘어간다.

**셰이더·머티리얼** — `Assets/3.Materials/Player/SkillLineIndicator/` 에 URP Unlit Transparent 셰이더그래프 1개 +
머티리얼 2개(범위 = 기존 인디케이터 톤 / 화살표 = 흰 계열). 프로퍼티: `_Color`, `_Alpha`, `_BorderWidth`, `_WorldSize`.
ghost 는 같은 머티리얼에 알파만 낮춘 MaterialPropertyBlock.

## 범위 밖
- 거너 R · 팔라딘 R(사거리 원 — `SkillRangeIndicator` 재활용, 다음 작업)
- 아군에게 보여주기 · 아트 텍스처 · `SkillConfirmMode.HoldRelease`
- 🟡 문서 불일치 메모: `Docs/design/players.md` 는 팔라딘 RMB 를 "정면 패링"이라 하는데 코드는 공격 박스. 이번엔 **코드(판정) 기준**으로 그린다.

## 리스크
- **비서버 오너의 차지 끝 신호** — `OnEnd` 가 `EndSkillClientRpc` 로 늦게 올 수 있다. 발사 순간엔 `GunnerBeamView.PlayChargeLaser` 에서도 끄는지 확인.
- **`CastLength` 스크래치 버퍼 `hits`** 를 서버 `Fire` 와 공유 — 호스트에서 같은 프레임 호출 순서 문제 없는지(읽기 사이에 끼지 않는지) 확인.
- **오너 차지 경과시간** — 오너는 자기 시계로 센다(`BeginCharge` 와 같은 방식). 서버 발사 길이와 몇 프레임 어긋나는 건 허용.
- **Quad 깊이 테스트 + 경사로** — 발밑 +0.05 고정이라 큰 경사에서 묻힐 수 있다. 맵에 문제되는 경사 있으면 보고.
- base `Player.prefab` 수정 → Variant(`Player_Paladin`·`Player_Gunner`) 오버라이드 충돌 없는지 확인.

## 검증
- **EditMode(에이전트가 직접 실행)**: `SkillPreviewShape` 계산 — Q fill 6/ghost 16, 클립 값 반영, RMB 박스 사각형 변환(1.4×1.4·앞 0.2~1.6 / 1.6×1.6·앞 0.1~1.7), 화살표 방향·길이.
- **Play(은희가 직접 — MCP Play 금지, MPPM 깨짐)**:
  1. Q 차지 중 직사각형 6→16 증가, 폭 1.2 가 실제 판정과 일치
  2. 벽 앞에서 잘림(Q·화살표), RMB 박스는 안 잘림
  3. 발사·취소·사망·스턴·대시로 끊기면 즉시 사라짐
  4. HUD 호버 시 미리보기 표시 / 벗어나면 사라짐 / 호버 중 시전 → 미리보기 꺼지고 Q 는 차지 표시로
  5. MPPM 다른 플레이어 화면엔 안 보임

---

# ▶▶▶ 진행 중 = **아군 보호막 연출 = 받는 쪽이 주인** (2026-10-06 · 그릴 4문항 완료 · 승인 ✅ · 구현 완료 · 배선·Play 검증 대기)

> 작업 세션: **민경(Claude)**. 🔴 **은희 영역 파일 3개** — `PlayerShieldVfx.cs`,
> `Player_Paladin.prefab`(safetyTimeout), `Player_Gunner.prefab`(컴포넌트 추가).
> `Unit.cs`·`Player.cs`·`Health.cs` 는 **건드리지 않는다**.

거너 Q 가 아군에게 보호막을 줄 때 `FX_GunnerShieldGrant` 를 띄우던 것을,
**아군 자신의 `PlayerShieldVfx` 배리어**로 바꾼다.

## 🔴 조사: 재생시간을 넘기면 안 된다

처음 떠올린 두 안(①시전자가 지속시간을 넘긴다 ②시전자가 컴포넌트를 캐싱해 켜고 끈다)은 **둘 다 틀렸다.**

```
ShieldEndReason   Depleted(피해로 소진) / Expired(만료) / Cleared(사망·추락)
                  -> 시간보다 먼저 끝나는 경로가 둘이다. 타이머는 그걸 못 센다.
ShieldTypePolicies.Of(GunnerCharge) == Stack
                  -> 같은 아군이 Q 를 두 번 맞으면 인스턴스가 2개. 각자 다른 시각에 끝난다.
                     (팔라딘 E 는 Replace 라 1개뿐이어서 이 문제가 안 보였다)
수명 주인          거너 Q 스킬은 FireRecovery 만에 끝나는데 보호막은 5초.
                  거너가 죽거나 나가면 꺼 줄 주체가 사라진다.
```

**그리고 보호막은 이미 전 피어에 복제돼 있다.**

```csharp
Unit.cs:75   readonly NetworkList<ShieldInstance> _replicatedShields;
Unit.cs:374  public int ShieldInstanceCount                      // public
Unit.cs:377  public ShieldInstance GetShieldInstance(int index)  // public — type·sourceId 포함
Unit.cs:330  public event Action<ShieldInstance, ShieldEndReason> ServerShieldEnded;  // public
```

각 피어가 **자기 눈앞의 아군 상태를 그대로 읽으면 된다.** 늦게 들어온 클라도 맞는다.
끝난 **이유**만 서버가 아는데 그 이벤트도 public 이라 `PlayerShieldVfx` 가 직접 구독하면 된다.

➡️ **보호막 연출의 주인은 아군 자신이다.** 거너는 아무것도 넘기지 않는다.

## 그릴 결과 (2026-10-06)

| | 결정 | 근거 |
|---|---|---|
| 1 | **배리어는 언제나 하나. 색만 출처로 가른다** — `HolyShield` = 금색(기존), 그 외 = **흰색** | 두 겹을 동시에 띄우면 지저분하다. 종류별 색 구분은 HUD 가 맡기로 이미 설계돼 있다(`ShieldType` 주석) |
| 2 | 리그는 **`Player_Gunner.prefab` 에만 추가**. base `Player.prefab` 로 올리지 않는다 | `DisallowMultipleComponent` 라 팔라딘 것을 떼야 하고, `interruptWave`(단죄의 방패)는 거너에게 의미 없는 필드다. 대신 **배선 도구**로 묶어 캐릭터가 늘면 한 번 돌린다 |
| 3 | `FX_GunnerShieldGrant` 는 **일단 뺀다**(프리팹은 남긴다) | 배리어에 **0.25초 FadeIn 이 이미 들어 있다**(`HolyShieldEffect.introDuration`, 드라이버가 대출 직후 `BeginIntro`). 생성 신호가 이미 있다. 플레이에서 부족하면 0.4초 한 방으로 되살린다 |
| 4 | `HolyShield` 소켓 `safetyTimeout` 8 → **15** | 🔴 **기존 버그**: 팔라딘 `shieldDuration: 10` 인데 8초에 강제 회수 + 경고. `PlayerShieldVfx` 주석이 "보호막 지속시간보다 길어야 한다"고 못박아 둔 상황 그대로다 |

### 이번에 같이 정한 것
- **겹치면 금색이 이긴다.** 팔라딘이 자기 E 를 쓴 채 거너 Q 를 맞으면 금색만 띄운다 — 자기 핵심 스킬을 남의 연출이 덮으면 안 된다.
- **흰색은 런타임 틴트가 아니라 에셋 복제.** `HolyShieldEffect` 가 `_ColorFactor` 기준값을 1회만 캐시하므로 런타임 틴트는 재사용마다 어긋날 위험이 있다. 이 레포 관례(룩마다 프리팹)도 복제 쪽이다.

## 범위

**신규 VFX 에셋** (`50.Art/VFX/Common/Players/Common/AllyShield/`, 🔴 SVN add)
- `FX_AllyShield_Barrier.prefab` · `FX_AllyShield_Motes.prefab` · `FX_AllyShield_Entry.asset`
- `FX_AllyShield_Break.prefab` · `FX_AllyShield_Break_Entry.asset`
- 머티리얼 흰색 사본 — `Shader_IntegratedEffect` 의 색만 교체. 형태·FadeIn·파문은 금색판 그대로

**`PlayerShieldVfx.cs`** (은희 영역)
- 소켓 추가: `allyBarrierLoop` · `allyBreakBurst`
- 자기 `Unit` 을 구독/폴링해 **스스로** 켜고 끈다
  - 복제 목록(`ShieldInstanceCount` / `GetShieldInstance`)을 `LateUpdate` 에서 읽어 **어느 색을 띄울지** 판정
    (금색 = `HolyShield` 가 하나라도 있음 / 흰색 = 그 외만 있음 / 없음 = 끔)
  - `ServerShieldEnded` 를 구독해 **마지막 하나가 `Depleted` 로 끝났을 때만** Break 를 띄운다
- `HitLocal` 이 두 소켓을 모두 훑도록
- 🔴 `PlayLocal()` 은 **남긴다** — 팔라딘 `FirstMeleeSubSkill.OnClientPlay` 가 쓰고 있다.
  복제 판정과 겹쳐도 `Play()` 가 재대출이라 두 겹이 되지 않는다

**`GunnerChargeLaserSkill` / `GunnerBeamView`** (은희 영역)
- `BuildShieldPoints()` · `shieldPoints` 인자 · `shieldGrantPlayer` 재생 제거
- 🔴 `ChargeLaserFiredRpc` 의 **시그니처가 바뀐다**(`Vector3[] shieldPoints` 한 개 감소)

**프리팹**
- `Player_Gunner.prefab` — `PlayerShieldVfx` + 소켓 2개(🔴 NetworkBehaviour 목록 변경)
- `Player_Paladin.prefab` — 흰색 소켓 2개 추가 + `safetyTimeout` 8 → 15

**도구** — `Tools/VFX/보호막 연출 배선` (두 프리팹 공통, 캐릭터 추가 시 재실행)

## 구현 중 바뀐 것 (2026-10-06)

- 🔴 **`PlayLocal()` / `ServerEnd()` 를 "오프라인 전용 폴백" 으로 돌렸다.** 계획에서는 팔라딘 경로를
  그대로 두려 했는데, 그러면 **두 주인이 생긴다** — `OnClientPlay` 가 켠 직후 복제 목록이 아직
  도착하지 않은 피어에서 폴러가 "띄울 게 없다"고 판단해 **껐다 켰다 하는 깜빡임**이 난다.
  두 메서드 안에서 `if (IsNetworkActive) return;` 으로 막았으므로
  **`FirstMeleeSubSkill` 은 한 줄도 안 고쳐도 된다**(계속 불러도 안전하다).
  대가: 팔라딘 자기 배리어가 **복제 도착 뒤에** 뜬다(호스트 ~1프레임, 리모트 ~RTT). 0.25초 FadeIn 이 가린다.
- **소켓 앵커·배율은 금색 소켓에서 베낀다.** 팔라딘의 `HolyShield` 는 `socket` 이 본(Armature 안)이고
  `scale 0.5` 다 — 흰색도 같은 자리·같은 크기여야 해서 도구가 그대로 복사한다.
  거너는 베낄 것이 없어 루트 자식 `(0, 1, 0)` + `scale 0.5`.
- **`EffectCatalog` 에 2종 등록을 도구가 같이 한다.** 프리워밍은 그 목록으로만 돌아서
  빠지면 첫 재생에 한 프레임 튄다. 손으로 넣는 걸 잊기 좋은 자리다.
- 🔴 **같이 고친 기존 버그**: `PlayChargeLaser` 의 `if (shieldPoints == null) return;` 이
  **발사 애니메이션(`CrossFadeInFixedTime`) 앞**에 있었다 — 보호막 배열이 null 이면 Q 발사 모션이
  통째로 건너뛰어졌다. 그 블록이 사라지면서 같이 풀렸다.

## 리스크

- 🔴 **`ChargeLaserFiredRpc` 시그니처 변경** = 네트워크 계약 변경. 전 피어가 같은 빌드를 써야 한다.
- 🔴 **`Player_Gunner` 의 NetworkBehaviour 목록이 바뀐다.** 모든 피어가 같은 프리팹을 쓰므로 동작에는 문제없다.
- 복제 목록 폴링은 `LateUpdate` 에서 `ShieldInstanceCount` 만 읽는다(보통 0). 할당 없음.
- 흰색 배리어가 팔라딘 금색과 **같은 형태**라 2인 플레이에서 구분이 색뿐이다 — 플레이에서 확인.

## 검증
1. 솔로: 거너 Q → 아군 없음 → 아무 일도 없어야 한다
2. MPPM 2인(거너+팔라딘): 빔이 아군을 스치면 **흰** 배리어 FadeIn → 5초 유지 → 조용히 걷힘
3. 같은 아군에 Q 두 번(Stack) → 배리어는 하나, **늦게 끝나는 쪽** 기준으로 걷힘
4. 보호막 30 을 몹 공격으로 소진 → **Break 연출**
5. 팔라딘이 자기 E(금색) 중에 거너 Q 를 받음 → **금색 유지**, E 가 먼저 끝나면 흰색으로 전환
6. 아군 사망 중 보호막 → `Cleared` 로 조용히 걷힘(Break 없음)
7. 팔라딘 E 단독 10초 — **8초에 끊기지 않는지**(Q4 수정 확인)

# ▶▶ 직전 작업 = **잡몹 사망 — 디졸브 → 부위별 붕괴** (2026-10-06 · 구현 완료 · Play 검증 대기)

> 작업 세션: **민경(Claude)**. 🔴 **경석 영역 파일 1개**(`ChompBot.prefab` 의 사망 연출 컴포넌트 교체).
> `MonsterBase` 는 건드리지 않는다 — `IDeathEffect` 구멍이 이미 있다.

잡몹이 죽을 때 **디졸브로 녹는** 연출을 **원본 메시가 부위별로 무너지는** 연출로 바꾼다.
상자(`MeshFragmentSet`)의 삼각형 산산조각과는 다른 물건이다 — 여기는 **부위 덩어리**가 주저앉는다.

## 🔴 조사: 블렌더로 자를 필요가 없다

`R_ChompBot_01.fbx` — 1,661 버텍스 / 2,922 삼각 / 본 10개

```
버텍스당 영향 본 수 분포   {1: 1661}    전부 단일 본 100%. 블렌딩 0
본 경계를 가로지르는 폴리곤  0개          파츠가 서로 붙어 있지 않다
```

모델러가 **닫힌 껍데기 10개를 만들어 하나로 합친** 스킨 메시다. 본 인덱스로 삼각형을 가르면
**원본 그대로, 구멍 없이** 10조각이 나온다. 잘린 단면이 없으므로 뚜껑도 필요 없다.

```
bone[0] 317폴리 Z 67cm  몸통      bone[8] 171폴리 폭65cm Z 78cm 턱(위)
bone[1] 311폴리 Z125cm  머리      bone[9] 188폴리 폭66cm Z 51cm 턱(아래)
bone[2,5] 184/179 허벅지   bone[3,6] 182/158 정강이   bone[4,7] 68/68 발
```

같은 팩의 다른 몹도 같다 — HumanoidBot 2779/2796(99.4%) · PeekABot 1143/1150(99.4%) 단일 본.
섞인 몇 개(7~17개, 전부 이음매)는 우세 본으로 보낸다.

### 왜 블렌더 수작업보다 나은가

🔴 **미리 자른 스태틱 파츠는 바인드 포즈를 들고 있다.** 사망 애니메이션이 끝난 **그 자세**에서
무너져야 하는데, 블렌더에서 잘라 낸 조각은 T포즈 모양이다. 본 분할은 조각마다 제 본의
**사망 순간 월드 트랜스폼**을 그대로 받으므로 포즈가 공짜로 맞는다.
덤으로 몹 8종 × (절단 + 뚜껑 + 리스킨 + FBX 재익스포트 + SVN) 가 통째로 사라진다.

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 분할 방식 | **본 기준 자동 분할**. 블렌더 0. 아쉬우면 나중에 몹별 수동 보정 |
| 2 | 물리 | **해체형** — 폭발력 0, 중력 + 약한 랜덤 토크. 래그돌(`CharacterJoint`)은 같은 조각 데이터 위에 나중에 얹는다 |
| 3 | 퇴장 | 바닥에 눕고 잠시 뒤 **디졸브로 녹인다**. 디졸브를 버리는 게 아니라 **퇴장 전담**으로 내린다 |
| 4 | 범위 | ChompBot 먼저 → 잡몹 8종. **보스·중간보스 제외** (23호 `delayAfterClipEnd 2초` 는 팀장 지시 2026-09-30) |

그 밖에 합의 없이 따르는 것:
- 파편 충돌 = 상자와 **똑같은 규칙**. Effect(17) 레이어, `excludeLayers = ~(Default|Ground|Wall|Env)`,
  `ContinuousSpeculative`. 🔴 Effect 자신이 마스크에서 빠지는 것이 핵심 — 조각이 맞닿은 채
  시작하므로 서로 충돌시키면 관통을 푸는 힘으로 튀어 오른다.
- 전파 = `DissolveDeath` 와 같은 구조. 서버가 `Rpc(SendTo.ClientsAndHost, Reliable)` 한 번,
  각 피어가 자기 로컬에서 재생. 좌표는 싣지 않는다.

## 🔴 조각을 제자리에 놓는 수식

강체 가중치라 본 b 에 묶인 버텍스의 스킨 결과는 정확히

```
world = bones[b].localToWorldMatrix * bindposes[b] * v
```

그래서 굽는 쪽에서 버텍스를 `bindposes[b]` 로 미리 변환해 **본 로컬 공간**으로 옮겨 두면,
런타임에는 조각 오브젝트를 **그 본의 월드 트랜스폼에 놓기만** 하면 사망 포즈가 정확히 재현된다.
보간도 근사도 없다. (법선도 같은 행렬의 회전분으로 돌려 둔다.)

조각 메시는 자기 무게중심 기준으로 다시 중심을 잡고, 그 무게중심을 본 로컬 오프셋으로 들고 있는다
— 리지드바디의 회전 중심이 조각 한가운데여야 자연스럽게 구른다.

## 🔴 풀링을 쓰지 않는 이유

상자는 `EffectManager` 풀을 쓴다. 여기는 안 쓴다 — 드라이버(`FragmentBurstEffectSystem.Play`)가
**대출 즉시 `Burst()`** 를 부르는 구조라, 포즈를 써 넣을 틈이 없다. 포즈는 죽을 때마다 다르다.

대신 `DissolveDeath` 가 파티클을 `Instantiate` 하는 것과 같은 방식으로 디브리 루트를 하나 만든다.
🔴 **몬스터의 자식으로 두지 않는다** — 상자가 겪었던 "수명을 소유한 객체가 먼저 죽는" 구조
(`FragmentBurstEffect` 주석)를 그대로 밟게 된다. 루트는 떼어 놓고 제 수명을 제가 센다.
몹 사망은 초당 몇 번 수준이라 조각 10개 Instantiate 는 감당된다. 모자라면 나중에 풀링한다.

## 만드는 것

```
[git 신규]
1.Scripts/Monster/Collapse/MonsterPartSet.cs          부위 조각 에셋(메시 = 서브에셋)
1.Scripts/Monster/Collapse/Editor/MonsterPartSetEditor.cs   Bake — 스킨 메시를 본별로 가름
1.Scripts/Monster/Collapse/MonsterCollapseDebris.cs   떼어 놓인 디브리 루트. 수명·퇴장 디졸브
1.Scripts/Monster/Collapse/CollapseDeath.cs           IDeathEffect. DissolveDeath 자리에 들어간다

[git 수정]
2.Prefabs/Monster/ChompBot.prefab    DissolveDeath → CollapseDeath (🔴 경석 영역)

[신규 에셋]
2.Prefabs/Monster/Collapse/ChompBot_Parts.asset       Bake 결과
```

`DissolveDeath` 는 **지우지 않는다**. 보스가 계속 쓰고, 퇴장 디졸브가 같은 머티리얼 템플릿
(`M_Dissolve_Template.mat`)·같은 셰이더를 재사용한다.

## 8종 확장 — 실측과 도구 수정 (2026-10-06)

ChompBot 하나로 끝날 줄 알았는데 아니었다. 잡몹 8종의 메시를 전부 재 봤다.

| 몹 | 본 | 조각 | 경계 교차 | 걸린 것 |
|---|---|---|---|---|
| ChompBot | 10 | 10 | **0 (0.0%)** | — |
| HumanoidBot | 23 | 23 | **0 (0.0%)** | 무기가 고정 메시 |
| WallBot | 11 | 11 | 16 (0.8%) | — |
| PeekABot | 9 | 9 | 12 (1.1%) | — |
| TeslaBot | 8 | 7 | 13 (1.1%) | — |
| MortarBot | 5 | 5 | 24 (1.9%) | 조각 5개뿐 |
| SpinnerBot | 22 | 20 | **106 (4.6%)** | **스킨 렌더러 2개**(몸체·날) |
| GauntletBot | 41 | **40** | **187 (5.5%)** | 건틀릿 2짝이 고정 메시 |

🔴 **첫 도구는 세 군데서 틀렸다.**

1. `GetComponentInChildren<SkinnedMeshRenderer>` 로 **첫 렌더러만** 갈랐다 —
   SpinnerBot 의 날은 영영 안 무너진다. → 모든 스킨 렌더러를 훑는다.
2. **고정 `MeshRenderer` 를 빼먹었다** — HumanoidBot 의 총, GauntletBot 의 건틀릿 두 짝
   (각 2,886 폴리)은 스킨이 아니라 본에 매달린 평범한 메시다. 몸은 무너지는데 무기만
   공중에서 증발한다. → 통째로 한 조각이 되어 같이 떨어진다.
3. **조각 수 상한이 없었다** — GauntletBot 은 본이 41개라 그대로 가르면 40조각이고,
   그건 "무너진다"가 아니라 "부품이 쏟아진다"다. → `maxParts`(기본 12)를 넘으면
   작은 것부터 **부모 본으로 합친다**. 볼트가 그 팔뚝에 붙는 식이라 관절에서 떨어지고,
   합치면 경계를 가로지르던 삼각형도 같이 사라져 **구멍 위험까지 준다**.

데이터 구조도 바뀌었다 — `Part` 가 `followPath`/`followName`(따라갈 트랜스폼의 루트 기준 경로)과
`materials`(렌더러마다 다르므로 조각이 직접 들고 있다), `isStaticMesh` 를 갖는다.
런타임은 스킨/고정을 구분하지 않는다 — 둘 다 "제 트랜스폼의 로컬 공간에 담긴 메시"일 뿐이다.

도구에 `Tools/Monster/부위 붕괴 — 잡몹 전부 설정` 을 더했다(8종 일괄, 보스 제외).

## 개발용 즉사 키 (2026-10-06)

`1.Scripts/Utility/DevMonsterKill.cs` — **F9** 가장 가까운 몹 / **Shift+F9** 전부.
사망 연출을 반복해 보려면 필요하다(ChompBot 100HP vs 거너 평타 5 = 과열 대기 포함 9초).
🔴 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` · `RuntimeInitializeOnLoadMethod` 자가 설치라
씬·프리팹을 안 건드린다 · 피해는 서버 권한이라 **호스트에서만** 듣는다.
🔴 `ApplyDirectHealthDamage` 가 아니라 `TakeDamage(AttackInfo)` 를 쓴다 —
사망 판정이 `MonsterBase.TakeDamage` 안에 있어 전자로는 `EnterDead` 가 안 불린다.

## 검증 항목

- [ ] Bake 결과 조각 10개 · 삼각형 합계 = 원본 2,922
- [ ] 죽는 순간 조각이 **사망 포즈 그대로** 제자리에 뜬다 (한 프레임도 튀지 않아야 한다)
- [ ] 조각이 바닥을 안 뚫는다 · 플레이어를 안 민다 · 서로 안 튄다
- [ ] MPPM 2인 — 호스트·클라 **양쪽 다** 보인다 (🔴 이 레포가 반복해 밟은 함정)
- [ ] 디스폰 타이밍 — 조각이 사라지기 전에 몹이 디스폰돼도 조각은 끝까지 산다
- [ ] 히트스톱 중 사망 / 사망 직후 씬 전환에서 조각이 새지 않는다

## 리스크

- 조각이 10개뿐이라 "무너진다"가 아니라 "부품이 흩어진다"로 보일 수 있다 → 토크·대미를 낮추고
  퇴장 디졸브를 빨리 걸어 본다. 그래도 아쉬우면 **래그돌(Q2 ②)** 로 올린다.
- 발(68폴리)처럼 작은 조각은 바닥 충돌이 불안정할 수 있다 → 박스 콜라이더 + Speculative 로 시작.
- `ChompBot.prefab` 은 경석 영역이다. 컴포넌트 교체만이고 `MonsterBase` 는 안 건드리지만 공유 필요.

---

# ▶▶▶ 진행 중 = **거너 좌클릭 빔 — 임시 LineRenderer 교체** (2026-10-04 · 그릴 6문항 완료 · 구현 완료 ✅ · 배선·검증 대기)

> 작업 세션: **민경(Claude)**. 🔴 **은희 파일 2개 수정**(`GunnerBeamView.cs`·`GunnerBasicAttack.cs`).
> 🔴 `50.Art` = **SVN**. 신규 4파일 + 에디터 스크립트.

샌드박스에서 넘어온 `FX_GunnerBeam.prefab`(Hovl "Beam ray" 개조)을 실제로 붙이고 재생한다.
인수인계 문서는 `50.Art/VFX/Common/Players/Player2/README.txt`.

## 실측

```
fireInterval 0.35초 · range 14m · beamWidth 0.6 · muzzleHeight 1
현재 연출  LineRenderer 0.08초 번쩍 (GunnerBeamView.basicDuration)
프리팹     전 파티클 looping 0 · length 1초 · playOnAwake 0
붙일 곳    GunnerBeamView·GunnerBasicAttack 둘 다 Player_Gunner.prefab 에 직접
총구 소켓  없음
```

🔴 README 의 "Missing Script 제거" 는 **사실과 다르다.** `474bcb49…` 는 아직 Light 에 붙어 있고,
`m_EditorClassIdentifier` 가 `UniversalAdditionalLightData` — **URP 정식 컴포넌트**다.
패키지 안에 있어서 Assets 스캔에 안 잡혔을 뿐이다. 그대로 둔다.

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 발사 리듬 | 단발 연사. **프리팹 수명을 0.35초**(= 발사 간격)로 줄인다 |
| 2 | 바닥 전용 자식 4종 | **`FX_LaserHit.prefab` 으로 분리**, 맞았을 때 `PlayOnce()` |
| 3 | 길이 | 루트 `localScale.z = 거리 / beamUnitLength`(인스펙터) |
| 4 | 시작 위치 | 총구 소켓 추가. 코드는 `EffectSocketPlayer` 를 SerializeField 로 받는다 |
| 5 | 범위 | BasicAttack 만. Q·우클릭 LineRenderer 는 그대로, 미배선 시 폴백 |
| 6 | `hit` | 2번으로 흡수 — 맞은 발만 피격 연출 |

## 🔴 "EffectManager 는 못 쓴다" 를 뒤집은 지점

README 는 균일 float 배율 때문에 포기했지만, 코드를 읽으면 길이 있다:

```
EffectPool.Rent        대출 시점에만 localScale = originalScale × scale, 반납 때 복원
EffectManager.UpdateFollow  매 프레임 follow 의 position·rotation 만 복사 (scale 안 건드림)
EffectSocketPlayer.GetInstances  Play() 루프의 인스턴스를 꺼내 준다
```

→ `Play()` 로 켜고 `GetInstances()` 로 받아 **`localScale.z` 만 덮으면** 비균일 길이가 된다.
배율은 추종 루프가 안 건드리고 반납 때 저절로 복원된다. 조준은 **소켓을 돌려서** 한다
(추종 루프가 소켓 회전을 복사하므로, 인스턴스를 직접 돌리면 다음 프레임에 지워진다).

## 만든 것 / 고친 것

```
[SVN 신규]
Player2/FX_LaserHit.prefab          GroundGlow·ParticlesGround·Rainbow·Shockwave 를 분리
                                    중간 노드 Hit 이 Beam ray 와 같은 X+90·1.5 를 들고 있어
                                    루트의 +Z 가 빔 진행 방향이 된다
Player2/FX_GunnerBeam_Entry.asset   duration 0.35 / outro 0.35 · prewarm 1
Player2/FX_LaserHit_Entry.asset     duration 0.5 · prewarm 4
VFX/Scripts/Editor/GunnerBeamSetup.cs   Tools/VFX/거너 좌클릭 빔 배선

[SVN 수정]
Player2/FX_GunnerBeam.prefab        바닥 4종 제거 · lengthInSec 전부 0.35
                                    수명 상한 0.35 (RayDark·EndSparks·Light·LaserCore,
                                    랜덤 범위인 Particles·Sparks 는 min/max 둘 다)

[git 수정]
Effects/EffectSocketPlayer.cs       public Transform Socket 접근자 추가 (읽기 전용)
Gunner/GunnerBeamView.cs            ShowBasicShot(origin, end, hit, width) 추가
Gunner/GunnerBasicAttack.cs         ShowBeam 이 hit 를 넘기도록 (2줄)
```

🔴 **소켓은 플레이어 루트 자식**이다(본 자식 아님). `ServerFire` 가 쏘기 직전 루트를 조준 방향으로
돌리므로 방향을 공짜로 따라가고, **반동 애니메이션에 흔들리지 않는다.**

🔴 소켓이 비면 `Socket` 이 자기 트랜스폼으로 떨어진다 — 그게 플레이어 루트면
`SetPositionAndRotation` 이 **플레이어를 순간이동시킨다.** 그래서 가드를 넣고 LineRenderer 로 떨어뜨린다.

## 1차 검증에서 나온 것 (2026-10-04)

| 증상 | 원인 | 처리 |
|---|---|---|
| 빔이 총이 아니라 **몸통**에서 나감 | `ServerFire` 의 `origin` 이 `루트 + up*MuzzleHeight` | 🔴 `GunnerBasicAttack.muzzle`(Transform) 추가 — **판정선 자체**를 총구로 옮겼다 |
| 빔이 맞은 물체를 **뚫고** 지나감 → 피격 연출이 안 보임 | 늘이기가 한 번만 걸려, 지연 발화·풀 교체 시 배율 1로 남는다 | 켜 있는 동안 **매 프레임** 재적용 |

🔴 **`muzzle` 은 연출이 아니라 판정이다.** 비우면 예전 동작으로 떨어진다. 은희 공유 항목.

🔴 **빔 길이는 계산하지 말고 잴 것.** 계산으로는 24 m 가 나왔다 —
메시(`CylinderFromGround` Y 0~2) × `size3D=1`·`startSizeY 8` × `Beam ray` 스케일 1.5.
**씬에서 재니 8 m 였다.** 24/8 = 3 = 2 × 1.5 — 메시 높이도 `Beam ray` 스케일도
파티클 길이에 곱해지지 않고 `startSizeY` 가 그대로 미터였다.
`beamUnitLength` 기본값과 배선 툴의 `MeasuredBeamLength` 를 **8** 로 맞췄다.
프리팹의 `startSizeY` 를 건드리면 **다시 재야 한다.**

길이가 또 어긋나면 `Log Beam Length` 로 인스턴스 배율을 찍어 "상수 문제"인지 "늘이기가
안 걸린 것"인지 가른다(인스턴스 z 가 거리에 따라 변하면 상수 문제).

## 2차 검증 — 배선 도구 버그 (2026-10-04)

"총구로 옮겼는데 빔이 계속 몸통에서 나간다" 의 원인은 **도구가 소켓을 두 개 만든 것**이었다.

```
BeamMuzzle   GO 875638587521706581   pos (0,0,0)            ← 손으로 본(bone) 밑에 단 진짜 소켓
BeamMuzzle   GO 5809311226033411894  pos (0.25, 1, 0.35)    ← 도구가 루트에 새로 만든 중복
GunnerBasicAttack.muzzle -> 331063376735999151              ← 중복 쪽을 물고 있었다
beamUnitLength: 1                                           ← 빔이 거리의 8배로 뻗었다
```

🔴 `root.transform.Find(name)` 은 **직계 자식만** 본다. 소켓을 본 밑으로 옮기면 못 찾고
루트에 같은 이름으로 하나 더 만든다. `GetComponentsInChildren<Transform>` 로 바꾸고,
중복이 있으면 **가장 깊은 것만 남기고 지운다**(손으로 단 쪽이 깊다).

`beamUnitLength` 는 이제 도구가 **항상** 실측값 8 로 되돌린다 — 1 이나 24 가 굳어 있으면
빔 길이가 통째로 틀어지는데, 그게 눈으로는 "이펙트가 물체를 뚫는다"로만 보여 원인을 찾기 어렵다.

`Dev_Boot.unity` 의 `defaultPlayerPrefab` 을 `Player_Paladin` → **`Player_Gunner`** 로 바꿨다
(씬 경로는 `Assets/0.Scenes/Debug/Dev_Boot.unity`, 빌드 설정에는 없다).

## 🔴 길이는 트랜스폼 스케일이 아니라 startSizeY 다 (2026-10-04, 3차)

"어떤 건 스케일로 되고 어떤 건 3D start size 로 해야 한다" — 렌더 모드가 섞여 있어서다.

| 렌더 모드 | 시스템 | 스케일로 늘이면 |
|---|---|---|
| Mesh (size3D 1) | RayDark · Beam ray · AirAura | 정상 |
| Stretched (size3D 1) | LaserCore | 길이는 **속도 축**을 따라가서 두께만 찌그러진다 |
| Stretched (size3D 0) | Particles · EndSparks · Flashes · Sparks | 불꽃이라 늘일 대상이 아니다 |
| Billboard | StartFlare · Glow | 카메라를 향하므로 보는 각도마다 다르게 일그러진다 |

🔴 **`size3D` 를 켜 둔 시스템이 정확히 "빔 몸통"이다.** 작성자가 이미 표시해 둔 셈이라
코드가 목록을 따로 들 필요가 없다. 그래서 기준을 **`startSizeY` 배율 하나**로 통일했다:

```
main.startSize3D 가 true 인 것만  →  startSizeYMultiplier = 거리 / beamUnitLength
트랜스폼 스케일은 전혀 건드리지 않는다 (localScale 참조 0곳)
```

프리팹이 **1 m 기준으로 정규화**돼 있으므로 `beamUnitLength` = 1 (이전 8에서 변경).

⚠️ `startSizeYMultiplier` 는 **앞으로 방출될 입자에만** 걸린다. 그래서 `Play()` 직후,
파티클 시뮬레이션이 돌기 전 같은 프레임에 걸어야 첫 발부터 맞는다.

## 끝점 연출 — BeamEndAnchor (2026-10-04)

`EndSparks` 를 빔 끝으로 옮겼다. 길이와 달리 끝점은 **늘이는 게 아니라 옮기는** 것이라
`size3D` 같은 기존 플래그로는 구분할 수 없어 표시 컴포넌트를 하나 만들었다.

```
VFX/Scripts/BeamEndAnchor.cs   표시용 컴포넌트 + offsetAlongBeam(끝점 기준 추가 오프셋)
FX_GunnerBeam.prefab           EndSparks 에 부착
GunnerBeamView.StretchBeam     anchor.position = 루트 position + forward x (거리 + 오프셋)
```

컴포넌트를 떼면 그 자식은 총구에 남는다 — 되돌리기가 체크박스 하나다.

⚠️ `EndSparks` 는 `startSpeed 8` · Cone 이고 콘이 **빔 진행 방향**을 본다 —
벽에 맞으면 불꽃이 벽 **안쪽으로** 튄다. 쏜 사람 쪽으로 튀게 하려면 그 오브젝트를
X 로 180도 돌리면 된다. 보고 정할 문제라 그대로 두었다.

⚠️ `EndSparks` 는 Local 시뮬레이션 공간이다. 한 발 안에서 거리가 안 바뀌므로 문제없지만,
거리가 매 프레임 변하는 연출에 재사용하면 이미 떠 있는 입자가 끌려온다.

## 레이저 단면이 각져 보이던 것 (2026-10-04)

`LaserCore` 텍스처(`t_trail07.psd`, 128²)의 알파를 재 보니 원인이 명확했다:

```
V(두께 축)  양 끝  0 → 1 → 2 → 3 ...      부드럽게 빠진다
U(길이 축)  양 끝  255 ... 255            🔴 완전히 각진 단면
```

**트레일용 텍스처**라 길이 방향으로 이어 붙이는 전제다 — 양 끝을 일부러 꽉 채워 놨다.

🔴 **텍스처 페이드도 메시 캡도 빔 길이에 비례해 늘어난다.** 끝을 10% 흐리면 2m 사격에선 20cm,
14m 사격에선 1.4m 다. 메시로 둥근 끝을 모델링하면 14m 빔에 2m 짜리 원뿔 코가 달려 더 나쁘다.
그래서 **고정 크기 끝 캡**(레이저 연출의 표준 구성)으로 갔다 — 거리가 변해도 캡은 그대로다.

```
[SVN 신규]
Textures/FX_LaserCore.png     t_trail07 복제 + U 양끝 6px 페이드(알파·RGB 동시)
                              왼쪽 알파 [5, 40, 96, 159, 215, 250, 254, 255]
_Materials/FX_LaserCore.mat   AB_03 복제 — 원본은 FX_Throw_Mini 와 공유라 안 건드린다

[수정]
FX_GunnerBeam.prefab          LaserCore 머티리얼 교체 · EndGlow 추가
                              (Glow 복제 + BeamEndAnchor → 빔 끝 고정 크기 캡)
```

끝 캡이 size3D 가 꺼져 있어 `StretchBeam` 이 안 건드린다 — 그게 "거리와 무관하게 일정"의 근거다.

## 남은 일

- [ ] **Tools ▸ VFX ▸ 거너 좌클릭 빔 배선** 다시 실행 (muzzle 배선이 추가됐다)
- [ ] `BeamMuzzle` 을 실제 총구 위치로 (어림값 0.25, 1.0, 0.35)
- [ ] `Beam Unit Length` 를 씬에서 재 보고 맞춤 (메시 계산으로는 24m 쯤)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보이는지
- [ ] 🔴 SVN add 4파일(+meta) · 에디터 스크립트
- [ ] 🔴 **은희에게 공유** — `GunnerBeamView.cs`·`GunnerBasicAttack.cs`
- [ ] 🔴 `Assets/Hovl Studio/`·`Assets/GameVFX Buff Collection/` 이 **git·SVN 어느 쪽에도 없다**
      (gitignore 는 `50.Art`·`51.Audio` 만 막는다). 대용량 아트는 SVN 규약 — 어디 둘지 확인 필요

> 📁 09-22 이전 계획(23호 공격 재작업 G1~G7 · Dev Boot · 벽 투명화 1단계 · 보호막 VFX 등)과 끝난 `PLAN-*.md` 는
> [Docs/history/PLANS/](Docs/history/PLANS/) 로 옮겼다. 옛 문서의 "PLAN.md §…" 참조는 [PLAN-archive-2026-06_09-22.md](Docs/history/PLANS/PLAN-archive-2026-06_09-22.md) 를 본다.

> 🆕 2026-10-02 은희: **캐릭터 선택 화면** → [PLAN-character-select.md](PLAN-character-select.md) (구현 완료 · Play 확인 대기)

# ▶▶▶ 진행 중 = **차징 구슬 외부 전기 유입** (2026-09-30 · 그릴 15문항 완료 · **구현 완료 ✅ · Play 검증 대기**)

## 별도 수정: 몬스터 메쉬·텍스처 연결 검증 (2026-10-01 · 완료)

- 사용자 승인 범위: ChompBot 표시·텍스처 문제를 포함해 기존 제작 몬스터 8종 전체 점검·수정.
- 확인한 문제: 현재 게임 프리팹이 원본 메쉬를 사용하여 새 텍스처의 UV와 맞지 않음. 눈·안테나·CRT의 제작 UV도 원본 Unity 재질과 불일치.
- 처리: 승인된 `.asset` 메쉬와 대응 맵 연결, 기존 본체 재질을 복제한 새 재질 11개, 표시부 UV 복구, 원본 클립 전체 프레임을 사용한 rootBone 기준 표시 범위 계산.
- 보존: Animator·Avatar·본·소켓·판정·게임 컴포넌트·기존 표시부/인터럽트 재질. 게임 프리팹 저장 때 생긴 다른 컴포넌트의 직렬화 변경은 제거함.
- 검증: Unity 6000.3.16f1 실제 GPU 렌더링, 저장된 게임 프리팹 8개, 원본 클립 51개·255시점. 전 시점 표시 확인, 변형 정점의 표시 범위 이탈 0. PNG 55개 해시·임포트 설정, 패킹 맵 11묶음 확인.
- 검토: Unity `Tools > Monster Surface Textures > Open Animation And Texture Review`, `Generated/monster-remodel-20260926/surface-v1/material-review/index.html`.
- 범위 한계: 전투 FSM·동작 합성·NGO·VFX 수명·성능 검사는 포함하지 않음. 메쉬·재질과 meta는 SVN, 게임 프리팹·에디터 도구는 Git 관리.

- 2026-10-01 사용자 요청으로 이전 에셋·복제 프리팹·반복 수정본·백업·임시 도구 등 5,674개(약 5GB)를 Windows 휴지통으로 이동. 최종 에셋·meta 해시 및 게임 프리팹 의존 관계 유지, Unity 51클립·255시점 재검증 통과. 정리 목록은 Docs/tech/monster-surface-cleanup-20261001.json, 전달 안내는 SurfaceV1/README.md.

---

> 작업 세션: **민경(Claude)**. VFX 단독 — 공유 코드 변경 없음.
> 🔴 `50.Art` = **SVN**. 특히 **텍스처 바이너리 교체가 포함**되므로 그 단계만 Unity 를 닫아야 한다.

보스 차징 구슬 바깥에서 전기가 날아와 구 표면에 꽂히며 사라진다 — "에너지를 끌어모아 충전한다".

## 지금 구조 (조사 결과)

```
FX_Buff_Lightningball_Loop      빈 컨테이너 PS(렌더러 꺼짐) ← 여기에 새 시스템을 붙인다
 └ FX_Buff_Lightningball        🔴 Grow · Loop · FadeOut · Break 네 프리팹이 공유
    └ ball                      Mesh(Sphere) · startSize 10.7
       ├ electric · flare_blue · ring · glow_blue ×2
       └ lightning_center       Stretched Billboard · m_LengthScale 4.5 · UV 시트 1×4
                                mat lightning_blue → Electro03.png (256×512)
```

**스케일 체인** — 중첩 인스턴스가 내부 루트 스케일을 `0.5 → 0.4673` 으로 **덮어쓴다**.
`0.4673 × 0.4(ball) = 0.18692`, 메시 반지름 `0.5 × 10.7 = 5.35` → **구 월드 반지름 1.0 m (지름 2 m)**.
구 중심은 `ChargeBallSocket`(보스 발밑 y 0) + 오프셋 2.5 → **지면 위 2.5 m**.
🔴 `_Loop` 루트는 스케일 1 이라 **거기 붙는 새 시스템은 월드 단위로 직접 잡는다.**

**텍스처 실측** `Electro03.png` 256×512 = 256×128 프레임 4장:

| | 중앙값 | 커버리지 |
|---|---|---|
| 코어 (α>200) | **6 px** | 8.1% |
| 본체 (α>120) | 7 px | 9.2% |
| 글로우 포함 (α>40) | 11 px | 13.2% |

프레임 높이 128 기준 코어가 **4.7%**. 화면상 획 두께 = `월드 폭 × (코어px / 128)` 이므로
현재 `lightning_center`(폭 0.26~0.37 m)는 **1.2~1.7 cm** — 이게 "얇다"의 실제 숫자다.

🔴 `lightning_blue.mat` 과 `Electro03.png` 은 **각각 참조가 한 곳뿐**이다(이 이펙트 전용).

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 어디에 | **`_Loop` 에만.** 알맹이에 넣으면 Break·FadeOut 까지 나와 "아직 충전 중"이라 거짓말한다 |
| 2 | 움직임 | **날아와 꽂힌다.** "충전"은 방향을 담은 말이다. 제자리 점멸은 기존 `electric` 과 역할이 겹친다 |
| 3 | 두께 해결 | **원본 `Electro03.png` 를 팽창시켜 다시 굽는다.** 참조 1곳이라 남에게 안 번진다 |
| 4 | 팽창량 | **+3 px → 코어 6→12 px (2배).** +2 는 티가 안 나고 +5 는 번개가 아니라 리본이 된다. **글로우도 같은 반경으로** 민다 — 코어만 굵히면 납작해진다 |
| 5 | 출발 반경 | **5.0 m** (구 반지름의 5배). 차징 예고로 멀리서도 읽혀야 한다 |
| 6 | 밀도 | **rate 12/s · 수명 0.3** → 동시 3.6개 |
| 7 | 끝 처리 | **그냥 사라짐.** 크기 1→0 이면 "흡수됐다"로 읽힌다. 표면 섬광은 기존 `electric` 이 이미 준다 |
| 8 | 머티리얼 | **`lightning_blue.mat` 재사용.** 같은 텍스처라야 안팎이 한 현상으로 읽힌다. SVN 추가 0 |
| 9 | 줄기 길이 | **약 1.8 m** (이동 거리 4 m 의 45%). 🔴 길이는 `m_LengthScale` 로 잡는다 — `startSize` 로 벌면 굵기가 같이 늘어 Q4 가 무의미해진다 |
| 10 | 색 | **진한 파랑.** 구 안쪽이 이미 가장 밝다(`flare_blue`·`glow_blue ×2`·`lightning_center`). 바깥이 진해야 "모여서 가운데가 뜨겁다"는 위계가 선다 |
| 11 | 속도 | **안으로 갈수록 가속.** 등속이면 그냥 날아오는 것이고, 충전은 구가 당기는 것이다 |
| 12 | 도착 산포 | **수명 ±10%(0.27~0.33), 착지 오차 ±0.4 m.** 🔴 정확한 착지와 산포는 **동시에 안 된다** — `Velocity over Lifetime` 은 m/s 라 이동거리 = 수명 × 평균속도이고, 두 랜덤값을 파티클 단위로 역비례시키는 기능이 없다. ±20% 는 허공에서 끊기는 게 보이므로 **±10% 가 상한** |
| 14 | 방향 | **위쪽 반구만.** 🔴 구 중심이 지면 위 2.5 m 인데 반경이 5 m 라, 완전한 구로 뿌리면 **최저 y = −2.5 m = 지면 아래**에서 솟는다 |
| 15 | 줄기 폭 | **0.50 m** (획 4.7 cm). 안쪽보다 굵어야 주인공이 된다. 폭을 키우면 `m_LengthScale` 이 5 → **3.6** 으로 내려가 **텍스처 늘어남이 2.5배 → 1.8배로 줄어든다** — 획이 같이 또렷해지는 이중 이득 |

## 만드는 것

### `Electro03.png` 다시 굽기 (🔴 SVN 바이너리 교체 · **Unity 닫아야 함**)

프리멀티플라이 RGBA 에 **반경 3 다이아몬드 팽창**(3×3 십자 3회). 알파와 색을 같은 반경으로 민다.
원본은 `scratchpad/Electro03.png.bak` 에 백업하고, **배율을 바꿀 때는 항상 원본에서 다시 굽는다**(팽창은 누적되면 안 된다).

```
코어 6 px → 12 px      커버리지 8.1% → 약 18%
```

⚠️ 같은 텍스처를 쓰는 **`lightning_center` 도 같이 굵어진다** — 의도한 바다(Q8: 안팎을 한 현상으로).

### `ChargeInflow` (신규 파티클 시스템 · `_Loop` 루트 직속 · 월드 단위)

```
looping 1 · playOnAwake 1 · simulationSpace Local · lengthInSec 1 · maxNumParticles 32
startLifetime  0.27 ~ 0.33 랜덤
startSpeed     0                         ← 이동은 전부 Velocity over Lifetime 이 만든다
startSize      0.50                      ← 월드 폭
startColor     진한 파랑 {0.3, 0.7, 1.4}
Shape          Hemisphere(2) · radius 5.0 · radiusThickness 0(껍질만) · 회전 X −90(돔이 위로)
Emission       rateOverTime 12           → 동시 12 × 0.30 = 3.6개
Velocity over Lifetime  radial 커브 · scalar −13.33 · 키 0.5@0 → 1.5@1 (평균 1.0 = 가속)
                        이동거리 = 0.30 × 13.33 = 4.0 m = 5.0 − 1.0 ✔
Size over Lifetime      1 → 0.85@0.5 → 0
UV                      1×4 시트 (lightning_center 에서 복제)
Renderer       Stretched Billboard(1) · m_LengthScale 3.6 · lightning_blue.mat
                        길이 = 0.50 × 3.6 = 1.8 m
```

**구현 = `lightning_center` 의 네 문서를 `_Loop` 로 복제해 개조한다.** 필요한 것(Stretched Billboard ·
UV 시트 1×4 · 같은 머티리얼)을 이미 다 갖고 있어서, 되돌릴 것이 Shape·Emission·Velocity 뿐이다.
**신규 에셋 0 · SVN add 0** (수정 2파일: 텍스처 + `_Loop` 프리팹).

## 구현 결과 (2026-09-30)

### 🔴 텍스처 방식을 바꿨다 — max 팽창은 실패했다

계획은 "반경 3 다이아몬드 팽창"이었다. **해 봤더니 흰 심이 사라지고 균일한 청록 리본이 됐다** —
max 는 이웃의 최댓값으로 평탄화하므로 **falloff 를 통째로 지운다.** 채도 높은 파란 글로우가
넓게 퍼지면서 좁은 흰 코어를 덮어써서, "굵어진" 게 아니라 **Q4 에서 경고했던 바로 그 납작함**이 나왔다.

**블러 + 게인으로 바꿨다.** 프리멀티플라이 상태에서 박스 블러(반경 3)를 돌려 획을 번지게 한 뒤
네 채널에 같은 게인(2.5)을 곱해 클램프한다. 블러는 평균이라 **그라데이션이 살아 있고**,
같은 게인이라 언프리멀티플라이 후 **색은 그대로이며 알파만 자란다.**

| | 코어(α>200) 중앙값 | 커버리지 | 코어 중 흰 심 |
|---|---|---|---|
| 원본 | 6 px | 15.8% | 23.4% |
| max 팽창 (폐기) | 13 px | 23.4% | — 심이 뭉개짐 |
| **블러+게인 (채택)** | **12 px** | 22.8% | **31.1%** ← 심이 오히려 또렷해졌다 |

목표치(6→12 px, 2배)를 정확히 맞췄다. 🔴 **다시 구울 때는 항상 `scratchpad/Electro03.png.bak`
(원본)에서 굽는다** — 블러도 누적되면 안 된다. 인자는 `dilate.py <반경> <게인>`.

### 그 외

| | 계획 | 실제 | 왜 |
|---|---|---|---|
| `ChargeInflow` 트랜스폼 | (언급 없음) | 🔴 **회전을 항등으로 초기화** | 복제 원본 `lightning_center` 가 `ball` 밑에서 쓰던 회전을 물려받아 있었다. 그대로 두면 **반구 축이 통째로 틀어진다** — 반구 방향은 ShapeModule 의 `m_Rotation x −90` 이 단독으로 정해야 한다 |
| 프리팹 증가 | 약 9,800줄 | **9,829줄** | 예측대로 |

측정값:

```
Electro03.png               256x512 · guid 유지 확인 ✅ · 62 → 107 KB
FX_Buff_Lightningball_Loop  4,925 → 9,829줄 · 문서 10개 · 중복 0 · 탭 0
ChargeInflow (루트 직속, 월드 단위)
  수명 0.27~0.33 · 속도 0 · 크기 0.5 · 색 {0.3, 0.7, 1.4} · maxParticles 32
  Shape  Hemisphere(2) · radius 5 · radiusThickness 0 · m_Rotation x −90
  Emission  rateOverTime 12                    → 동시 3.6개
  Velocity  radial state 1 · scalar −13.333 · 커브 0.5→1.5
            이동 = 0.30 × 13.333 = 4.0 m = 5.0 − 1.0 ✔
  Size      1 → 0.85@0.5 → 0
  UV        1×4 시트 (복제로 따라옴) · prewarm 1
  Renderer  Stretched(1) · m_LengthScale 3.6 · lightning_blue.mat → 길이 1.8 m
신규 에셋 0 · SVN add 0 · SVN 수정 2파일(텍스처 + _Loop 프리팹)
```

## 검증 항목

- [ ] 🔴 **반구 축이 위를 향하나** — Unity Hemisphere 는 +Z 반구라 X −90 으로 돌린다. **정적으로 확정할 수 없다.** 틀리면 옆으로 누운 반구가 되어 **한쪽에서만** 날아온다
- [ ] 지면 아래에서 솟는 줄기가 **없다**
- [ ] 줄기가 구 표면에서 사라진다 — 허공에서 끊기거나 구를 뚫고 나오지 않는다(오차 ±0.4 m 안)
- [ ] 안으로 갈수록 빨라지는 게 보인다
- [ ] 굵기: 유입이 안쪽 `lightning_center` 보다 눈에 띄게 굵다
- [ ] 안쪽 `lightning_center` 도 같이 굵어졌는데 **뭉개지지 않았다**
- [ ] 동시 3~4개 — 구 주변이 파랗게 뭉치지 않는다
- [ ] `Grow`·`FadeOut`·`Break` 에는 **유입이 없다**(Loop 전용)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음

## 리스크

| | |
|---|---|
| 🔴 반구 회전 | 정적 검증 불가. **증상이 명확하다** — 한 방향에서만 오면 회전이 틀린 것이고, `m_Rotation` 의 x 를 ±90 로 뒤집으면 된다 |
| 🔴 SVN 바이너리 | 텍스처 교체는 **Unity 를 닫고** 해야 한다. 바꾼 뒤 `.meta` guid 가 그대로인지 확인한다 |
| 텍스처 번짐 | `lightning_center` 도 굵어진다(의도). 안쪽이 뭉개지면 팽창을 +2 로 낮춰 원본에서 다시 굽는다 |
| 프리팹 비대화 | `_Loop` 이 4,925 → 약 9,800줄(+100%). 복제 대가다 |
| 범위 밖 | `Grow`/`FadeOut`/`Break` 연출 · 표면 착지 섬광 · 유입 전용 머티리얼 |

# ⏸ Play 검증 중 = **그랩 홀드 중 전기 전이 (잡힌 대상으로)** (2026-09-30 · 구현 완료 ✅ · **MPPM 전파 확인됨** · 나머지 항목 대기)

> 작업 세션: **민경(Claude)**. 🔴 공유 파일 `TwentyThreeBoss.cs`(경석) 수정 포함.
> 🔴 신규 VFX 에셋은 `50.Art` = **SVN**.

23호가 플레이어를 붙잡고 있는 동안, 전기가 **보스 손 → 대상 가슴으로 건너가고**(다리) 동시에
**대상 몸을 뒤덮는다**(감싸기). 지금은 지짐이가 보스 손에서만 터져서 **누가 당하고 있는지 안 보인다** —
그게 이 작업이 고치는 것이다.

## 지금 상태 (조사 결과)

| | |
|---|---|
| 팔 전기 | `grabPulse` = `EffectPathPlayer`(id `ArmElectric`), 경로 **어깨→팔꿈치→손** 3점, `FX_Grab_ArmElectric_Entry`, travelTime 0.3 / interval 0.3 / pool 10 / scale 1.2 |
| 틱 지짐이 | `PlayGrabbedElectricClientRpc` 가 `grabTickInterval` 0.5초마다 `FX_Grabbed_Electricity_Entry` 를 **그랩 소켓(보스 손)** 에 원샷 |
| 홀드 길이 | `grabHoldDuration` 2초 → 지짐이 약 4회, 그 뒤 내려치기 3회 |
| 플레이어 치수 | 캡슐 반경 0.38 · 높이 1.79 · 중심 y 0.88 (발 y 0, 머리 y ≈ 1.78) |

🔴 **"잡혀 있다"는 상태는 복제되지 않는다.** `BeginRestrainedClientRpc` 가 `if (!IsOwner) return;` 로
막혀 있고 FSM 자체가 복제 대상이 아니다 — 남의 화면에서 그 플레이어는 영원히 Idle/Move 다.
`Player.IsGrabbed` 같은 프로퍼티도 없다. **각 피어가 스스로 판단할 수 없으므로 보스가 알려 줘야 한다.**

🔴 **대신 위치 추종은 공짜다.** 잡힌 플레이어는 리페어런팅되지 않는다 — `PlayerRestrainedState.FixedTick`
이 포즈만 복사하고, 루트는 여전히 씬 루트의 `NetworkTransform` 이라 **모든 피어에 위치가 복제된다.**

## 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | 무엇이 보이나 | **다리 + 감싸기 둘 다.** 다리가 주, 감싸기가 보조 — 다리만이면 시선이 손에 남고, 감싸기만이면 보스가 한 일로 안 읽힌다 |
| 2 | 리듬 | **2층.** 상시 루프를 새로 깔고, 기존 틱 원샷은 그 위에 그대로 남긴다(데미지 순간 피드백) |
| 3 | 구간 | **놓아줄 때까지 전부** — 홀드 + 내려치기 3회. 내려치기 순간의 추가 번쩍은 범위 밖(타격 연출이 이미 있다) |
| 4 | 회수 | **기존 `StopGrabPulseClientRpc` 5개 길목에 같이 태운다** → 새 누수 경로 0개 |
| 5 | 감싸기 기술 | **파티클**(셰이더 오버레이 아님) — `DissolveOverlay` 슬롯은 `FirstMeleeUltimateSkill.superArmorOverlay` 가 이미 쓴다. **궁극기 중 잡히면 싸운다.** 캐릭터마다 메시 작업이 드는 것도 이유 |
| 6 | 다리 구현 | **프록시 트랜스폼 + 두 번째 `EffectPathPlayer`**(경로 = 손 → 프록시). `EffectPathPlayer` 자체는 안 고친다 |
| 7 | 착점 | **대상 가슴 한 점**(발 기준 y 1.2). 여러 점이면 프록시·재생기·회수 길목이 배로 는다 |
| 8 | 세기 | **일정.** 램프는 `SetScale` 이 필요한데 그건 다음 펄스부터만 먹어서 굵기가 섞인다 |
| 9 | 프록시 주인 | **새 컴포넌트.** 보스 파일 증분을 "RPC 쌍 + 대상 참조"로 최소화한다 |
| 10 | 감싸기 앵커 | **프록시 하나 + `EffectSocketPlayer.offset`**(y −0.32). 추종 오프셋은 대상 회전을 탄다(`WorldPosition`) |
| 11 | 틱 원샷 | 🔴 **대상 가슴으로 옮긴다.** 타이밍·엔트리·빈도는 그대로, **위치만.** 도착점에서 터져야 인과가 완성된다 |
| 12 | 감싸기 볼륨 | **Box `0.8 × 1.9 × 0.8`, 중심 y 0.88** (캡슐 근사) |
| 13 | 감싸기 재료 | **`electronic.mat`**(`Electro.png` 번개 지그재그). 다리(리본)와 **다른 재료**여야 역할이 갈린다 |
| 14 | 에셋 | **신규 제작.** 미사용 `FX_Grabbed_Electricity.prefab`(참조 0곳) 개조는 하지 않는다 — 이름과 내용이 어긋난다 |
| 15 | 다리 리듬 | **travelTime 0.15 / interval 0.15 / pool 10.** 거리가 팔의 절반쯤이라 같은 시간이면 손에서 굼떠진다 |

## 만드는 것

### `Assets/1.Scripts/Effects/GrabbedEffectAnchor.cs` (신규 · git · ~90줄)

프록시를 대상 가슴으로 옮기고, 다리·감싸기 두 재생기를 켜고 끈다. **보스를 모른다** — 대상 트랜스폼만 받는다.

```
[SerializeField] Transform proxy;              // 따라다닐 빈 트랜스폼
[SerializeField] EffectPathPlayer bridge;      // 손 → 프록시
[SerializeField] EffectSocketPlayer envelope;  // 프록시 기준 몸 전체
[SerializeField] float chestHeight = 1.2f;     // 발 기준
[SerializeField] Transform fallback;           // 대상을 잃었을 때 떨어뜨릴 곳(보스)

public void Play(Transform target)   // 대상 물고 proxy 이동 시작 → bridge.Play() + envelope.Play()
public void Stop()                   // bridge.Stop() + envelope.Stop(), 대상 해제
public Vector3 ChestPoint            // 틱 원샷이 읽어 갈 현재 가슴 좌표
void LateUpdate()                    // proxy.position = target.position + up * chestHeight
                                     // proxy.rotation = target.rotation
```

- 🔴 **LateUpdate** 여야 한다. 잡힌 포즈는 `FixedTick`/`NetworkTransform` 이 쓰므로 그 뒤에 읽어야 한 프레임 안 밀린다.
- 🔴 **대상이 사라지면 `fallback` 위치로 떨어뜨리고 계속 재생한다.** 끄지 않는다 — 끄는 것은 보스의 종료 신호 몫이고, 여기서 임의로 끄면 종료 신호가 왔을 때 이미 없어서 상태가 갈린다. (기존 "소켓 없으면 보스 위치" 정책과 같은 판단)
- 🔴 **`IsServer` 가드를 넣지 않는다.** 모든 피어가 로컬로 돈다(`EffectPathPlayer`·`DissolveOverlay` 주석이 같은 경고를 단다).

### `FX_Grabbed_Envelope.prefab` + `_Entry.asset` (신규 · 🔴 SVN add 4파일)

`Assets/50.Art/VFX/Common/Boss/Grab/` 에 둔다.

```
looping 1 · playOnAwake 0 · simulationSpace Local
Shape   Box (0.8, 1.9, 0.8)        ← 캡슐 근사. SocketPlayer.offset 이 y 를 0.88 로 내린다
rate    25/s   수명 0.25   크기 0.25~0.6 랜덤   회전 0~2π 랜덤
색      차가운 청백 HDR (팔 전기와 같은 계열)
Size over Lifetime  0 → 1@0.3 → 0    (지지직 점멸)
Renderer  Billboard · electronic.mat
```

🔴 **속도 0 이므로 Stretched Billboard 를 쓰지 않는다.** 늘일 방향이 없어 통째로 안 보인다 —
`FX_Magnetic_Pull` 의 `Arcs` 가 정확히 그 이유로 안 보였다(2026-09-30 확인).

### `TwentyThreeBoss.cs` (수정 · git · 🔴 경석님 파일 · +~55줄)

```
[SerializeField] GrabbedEffectAnchor grabbedElectric;   + _warnedNoGrabbedElectricAnchor

[ClientRpc] StartGrabbedElectricClientRpc(NetworkObjectReference target)
              → TryGet 해서 grabbedElectric.Play(player.transform)
[ClientRpc] StopGrabbedElectricClientRpc()
              → grabbedElectric.Stop()          (Reliable — 유실되면 플레이어 몸에 영구히 붙는다)
```

호출 자리 = **기존 `grabPulse` 와 정확히 같은 6곳**:
시작 1곳(`AcquireGrab` 성공, 972행 옆) / 종료 5곳(1908 · 1986 · 2072 · 2180 · `AbortAttackChain`).

그리고 **틱 원샷의 좌표만** 바꾼다 — `PlayGrabbedElectricClientRpc` 가 `GrabSocket` 대신
`grabbedElectric.ChestPoint` 를 읽는다(앵커가 없으면 기존 소켓으로 폴백).

### `TwentyThree.prefab` (수정 · git · 문서 5개 · +~80줄)

`Dash` 옆에 `GrabbedElectric` 빈 오브젝트를 만들고 그 아래:
`GrabbedAnchor`(프록시 트랜스폼) · `GrabbedEffectAnchor` · `EffectPathPlayer`(다리) · `EffectSocketPlayer`(감싸기).
다리 경로 = `[손 본(564008308436468704), 프록시]`, 엔트리 `FX_Grab_ArmElectric_Entry`, scale 1.2.
감싸기 소켓 = 프록시, `offset (0, -0.32, 0)`, 엔트리 `FX_Grabbed_Envelope_Entry`.
보스 컴포넌트에 `grabbedElectric` 배선.

## 구현 결과 (2026-09-30)

계획대로 들어갔다. **계획에서 바뀐 것은 아래 두 줄뿐이다.**

| | 계획 | 실제 | 왜 |
|---|---|---|---|
| 감싸기 `safetyTimeout` | (미지정, 기본 5) | **12** | 🔴 그랩 체인 최장이 catch 1.1 + hold 1.13 + throw 0.65×3 + end 1.37 = **5.55초**다. 5 였으면 **정상 재생을 잘라낸다** |
| 종료 길목 | "5곳" | **4곳** | 실제로 `StopGrabPulseClientRpc` 가 불리는 자리는 넷이다(헛잡기 · 대상 소멸 · 놓아줌 · AbortAttackChain) |

측정값:

```
GrabbedEffectAnchor.cs      140줄 · 중괄호 9/9
TwentyThreeBoss.cs          4,772 → 4,841줄 (+69) · 중괄호 304/304
  시작 1곳(AcquireGrab 1966) / 종료 4곳(1955 · 2038 · 2125 · 2235)
  틱 원샷 좌표(2859) → grabbedElectric.ChestPoint, 폴백 소켓 → 보스
TwentyThree.prefab          문서 162개 · 중복 0 · 탭 0
  VFX/Grab/GrabbedElectric [Anchor][PathPlayer 다리][SocketPlayer 감싸기]
                           └ GrabbedAnchor (프록시)
FX_Grabbed_Envelope.prefab  4,890줄 — FX_Dissolve_Death 복제 후 개조
  Box(0.8,1.9,0.8) · rate 25/s · 수명 0.25 · 크기 0.25~0.6 · 회전 0~2π
  색 {1.2, 2, 3.2} · Size 0→1@0.3→0 · Billboard · electronic.mat
  Color·Velocity 모듈 OFF · looping 1 · simulationSpeed 1 · Local
FX_Grabbed_Envelope_Entry   duration 0 / outro 0.6 / prewarm 2
다리                        FX_Grab_ArmElectric_Entry 재사용 · travelTime·interval 0.15 · pool 10 · scale 1.2
```

🔴 **복제 원본을 `FX_Dissolve_Death` 로 골랐다.** 프로젝트에서 **단일 시스템 + Billboard + Size 모듈**을
모두 갖춘 프리팹이 그것뿐이다(`Grab` 폴더의 것들은 전부 트레일이 달린 다계층이다).
`DissolveDeath` 는 Shape 를 런타임에 스킨드 메시로 갈아끼우므로 프리팹 쪽 Shape 는 폴백이고,
복제본을 Box 로 바꾸는 데 걸림돌이 없었다.

🔴 **신규 에셋 4파일 = SVN add 필요** (`FX_Grabbed_Envelope.prefab`·`.meta`, `_Entry.asset`·`.meta`).
`GrabbedEffectAnchor.cs`·`.meta` 는 `1.Scripts` = git 이다.

## 검증 항목

- [ ] 붙잡는 순간 다리가 생기고, 놓는 순간 사라진다
- [ ] 다리가 **손에서 가슴까지** 이어진다 — 팔 구간과 속도가 이어져 보인다
- [ ] 감싸기가 몸을 덮는다 — 발밑·머리 위로 새지 않는다
- [ ] 틱 번쩍이 **대상에서** 터진다(0.5초 간격 유지)
- [ ] 내려치기 3회 동안에도 유지되고, 놓는 순간 함께 걷힌다
- [ ] 🔴 돌진 중 **카운터·그로기·보스 사망**으로 끊었을 때 플레이어 몸에 안 남는다
- [ ] 🔴 잡힌 **플레이어가 사망**해도 안 남는다
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다 — 특히 *잡히지 않은* 쪽 화면에서 보이는지(복제 안 되는 상태라 여기가 핵심)
- [ ] 궁극기(슈퍼아머 오버레이) 중에 잡혀도 둘이 안 싸운다
- [ ] 콘솔 경고 없음

## 리스크

| | |
|---|---|
| 🔴 공유 파일 | `TwentyThreeBoss.cs` 를 이 세션에서만 **네 번째** 수정(궤적·보호막·벽충돌·이번). 경석님께 **한 덩어리로** 정리해 넘긴다 |
| 대상 참조 | 클라에서 `NetworkObjectReference.TryGet` 이 실패할 수 있다(디스폰 타이밍) → 폴백 위치로 떨어뜨리고 종료 신호를 기다린다 |
| 한 프레임 밀림 | `LateUpdate` 로 읽지만 `NetworkTransform` 보간과 어긋나면 다리 끝이 살짝 뜰 수 있다 — Play 에서 확인 |
| SVN | 🔴 **신규 4파일**(`FX_Grabbed_Envelope` 프리팹·엔트리 + meta). 기존 백로그와 함께 add 필요 |
| 범위 밖 | 미사용 `FX_Grabbed_Electricity.prefab`(참조 0곳) 정리 · 내려치기 순간 추가 번쩍 · 세기 램프 · 징크스 대응 |

# ⏸ Play 검증 대기 = **성검 메쉬 (R스킬 최후의 심판 · 낙하 착지)** (2026-09-29 · 그릴 18문항 완료 · **2026-09-29 기성 에셋으로 선회**)

> 작업 세션: **민경(Claude)**. VFX 단독 영역이라 공유 대상 없음 —
> 🔴 단, `FX_Lightening_Strike.prefab` 은 **`50.Art` = SVN** 이다.

## 목표

`FX_Lightening_Strike` 의 **빛 기둥(`LightPillar`)을 3D 성검 메쉬로 교체**한다.
칼은 하늘에서 떨어져 지목된 좌표에 꽂히고, 포인트가 빛난다.

## 🔴 2026-09-29 방향 전환 — 직접 모델링을 버리고 기성 에셋을 쓴다

Blender 로 직접 뽑던 메쉬(실루엣 6회 반복)를 접고, 사용자가 임포트한
**`Assets/JC_StylizedWeapons_Lite/`** (JC Stylized Weapons Lite) 의 `SM_Sword_01` 로 간다.

**없어진 단계 = 1(모델링) · 2(UV) · 3(문양 베이크) · 4(FBX 내보내기).**
에셋이 메쉬와 2048² 텍스처 4장(Base / Normal / Metallic / Roughness)을 이미 들고 온다.

**따라서 아래 그릴 결정은 에셋이 대신한다 — 다시 논의하지 않는다:**
`1`(가드 형태) `2`(음각 구현) `3`(음각/양각) `4`(칼날 단면) `6`(음/양 배분)
`9`(폴리·뒷면) `11`(텍스처 구성) `12`(해상도·UV).
**살아 있는 결정 = `5`(크기·피벗) `7`(발광 포인트) `8`(커스텀 셰이더) `10`(요 회전)
`13`(가짜 광원) `14`(발광 연출) `15`~`18`(낙하 연출).**

### 에셋 실측치 (FBX 직접 파싱 · Blender 안 거침)

| | |
|---|---|
| 메쉬 | `SM_Sword_01.fbx` guid `a3f745712a8a6d34ba2d5c78c073ce8d`, 서브메쉬 fileID `7905712616854619025` |
| 삼각형 | **2,984 tri / 1,560 vert** — 그릴 9의 "3~5k" 안에 정확히 들어온다 |
| 단위 | `UnitScaleFactor 1` = **cm 파일**, `useFileUnits: 1` → Unity 에서 **1.200 m** |
| 🔴 축 | 칼날이 **+Y**(칼끝 위), 폭 X, 두께 Z. 길이 `-0.152 .. +1.048`, 폭 `±0.200`, 두께 `±0.043` |
| 🔴 피벗 | **칼끝이 아니라 그립 속**(손잡이 끝에서 +0.152 m). 그릴 5의 "피벗=칼끝"이 안 맞는다 |
| 머티리얼 | `M_StylizedWeaponsLite_Sword_01` = URP **AutodeskInteractive**(PBR). HDRP 프로퍼티는 잔재 |
| 룩 | 금 장식 + 강철 날 + **보라 가죽 그립** + **청록 보석** 인레이 → 그릴 7의 "발광 3곳"에 그대로 맞는다 |
| 위치 | `Assets/JC_StylizedWeapons_Lite/` = **git 추적 중**(6.3 MB). `50.Art`(SVN)가 아니다 ← 아래 리스크 |

### 피벗·회전·크기를 어떻게 맞출 것인가

에셋 피벗이 칼끝이 아니므로 **메쉬를 다시 굽지 않고 파티클에서 해결한다**
(그릴 10 에서 회전을 파티클에 두기로 한 것과 같은 이유 — 인스펙터에서 보이고 싸다).

| 필요한 것 | 쓰는 노브 |
|---|---|
| 칼끝을 아래로 | `startRotationX = 180°` (에셋은 칼끝이 +Y) |
| 60° 카메라 정면 각 | `startRotationY` **시작 135°, ±20° 튜닝** (그릴 10 그대로) |
| 6 m 로 키우기 | `startSize = 5` (1.200 m × 5 = **6.0 m**) |
| 🔴 칼끝을 바닥에 정확히 | `ParticleSystemRenderer.m_Pivot` — **파티클 크기 단위**의 오프셋이다. 칼끝은 원점에서 +1.048/1.200 = **+0.873** 지점 → 뒤집혔으므로 `pivot.y` 로 상쇄한다. 실측으로 확정할 것 |

## 조사로 확정된 사실

| | |
|---|---|
| 현재 `LightPillar` | 유니티 내장 Cylinder, 크기 `2 × 15 × 2`, 수명 1초, 길이 0.05초, 이미터 local y=30 |
| `Renderer.m_RenderAlignment` | **2 (Local)** — 메쉬가 카메라를 안 따라간다. 월드 고정 ✅ |
| `PS.startRotation3D` | 미설정 → 켜고 X/Y 를 준다 |
| `PS.VelocityModule` | 이미 `enabled: 1` — 낙하에 그대로 쓴다 |
| 엔트리 | `FX_Lightening_Strike_Entry`, `computedDuration` **2.15초**, prewarm 3 |
| 재생 배율 | `targetStrikeScale: 1` (코드가 안 키움) |
| 카메라 시선 | `(-0.36, -0.86, 0.36)` → 피치 60°, **요 -45°** |
| 씬 라이트 | 조사 방향 `(-0.17,-0.94,-0.29)`, 고도 **70°**(거의 수직) |
| 🔴 맵 조명 | 디렉셔널이 **Baked 모드**인데 라이트맵도 프로브도 없다 → **동적 오브젝트는 직접광을 못 받는다.** URP Lit 도 AutodeskInteractive 도 여기서 죽는다 → **에셋 머티리얼을 그대로 쓸 수 없는 이유** |
| 🔴 제약 | `ShurikenEffectSystem.CanDrive` 가 `ParticleSystem` 을 요구한다 → 평범한 MeshRenderer 는 EffectManager 가 못 켠다. **반드시 파티클의 Mesh 렌더 모드** |
| 머티리얼 1장 | 파티클 Mesh 모드는 머티리얼을 하나만 받는다 → 에셋이 1장만 쓰므로 문제 없다 ✅ |

## 남은 설계 결정 (그릴 합의 중 유효한 것)

| # | 항목 | 결정 |
|---|---|---|
| 5 | 크기 | **6 m** (`startSize 5`). 피벗은 위 표대로 렌더러에서 보정 |
| 7 | 발광 포인트 | **3곳** — 가드 보석 · 폼멜 보석 · 날 중앙. 에셋의 **청록 보석**이 그대로 후보다 |
| 8 | 셰이더 | **커스텀 이미시브.** 씬 조명을 안 쓴다 |
| 10 | 정면 각도 | 파티클 `startRotationY`(135° ±20°). 메쉬에 굽지 않는다 |
| 13 | 가짜 광원 | **히어로 라이팅** — 키 ≈ `(0.5, 0.7, -0.5)` 월드, 뒤에 림 하나 |
| 14 | 발광 연출 | **등장 플래시 후 안정**(`_Time` 기반) |
| 15 | 등장 | **하늘에서 낙하** |
| 16 | 시간 배분 | 낙하 **0.3초(약 12m)** → 체류 1.45초 → 소멸 0.4초 |
| 17 | 낙하 곡선 | **가속 후 급정지** |
| 18 | 궤적 | **새 스트레치 빌보드** 추가. `LightPillar` 은 삭제 |

### 설계에서 특별히 짚은 것

**① 회전을 코드가 아니라 파티클에 둔 이유.** 필요한 각도는 "카메라가 요 -45°일 때만 맞는 값"이다.
코드(`Quaternion.identity` 자리)에 숨기면 **왜 그 숫자인지 아무도 모르게** 되고, 은희 님 파일을 건드리게 된다.
메쉬에 구우면 리익스포트 없이는 못 고친다. 파티클 인스펙터가 셋 중 가장 눈에 보이고 가장 싸다.

**② 낙하를 넣으면 타격감이 밀릴 수 있다 — 그래서 0.3초다.** 이 이펙트는 `OnChannelCompleted()`,
즉 채널링이 **이미 끝난** 시점에 재생된다. 낙하 시간만큼 "심판"과 "도착" 사이에 빈 틈이 생긴다.
0.3초는 눈이 낙하를 인지하는 하한이면서 틈이 안 느껴지는 상한이다. 늘리려면 이 트레이드오프를 다시 봐야 한다.

**③ 🔴 낙하 정지는 수동으로 맞춘 커브다.** Velocity over Lifetime 은 "바닥에서 정확히 0"을 모른다.
높이·시간·커브가 서로 물려 있어 **하나를 바꾸면 셋을 다시 맞춰야 한다.** 프리팹에 주석을 남길 수 없으니
이 문단이 그 기록이다.

**④ 🔴 에셋 머티리얼을 왜 버리는가.** `AutodeskInteractive` 는 PBR 이라 씬 직접광이 필요한데
이 맵은 디렉셔널이 Baked 이고 프로브가 없다. 그대로 쓰면 **앰비언트만 받아 납작한 회색 덩어리**가 된다.
커스텀 셰이더는 에셋의 Base·Normal 을 그대로 샘플링하되 **광원을 상수로 들고 있어서**
맵 조명이 고쳐지든 말든 똑같이 나온다. 색 튜닝도 인스펙터 한 번이다.

## 구현 단계

1. ~~Blender 모델링~~ · ~~UV~~ · ~~문양 베이크~~ · ~~FBX 내보내기~~ → **에셋으로 대체 ✅**
2. **에셋 정리 ✅** — `Assets/50.Art/VFX/Models/JC_StylizedWeapons_Lite/` 로 이동(guid 보존, git → SVN).
   데모 씬·배경 머티리얼·컨버터 `.unitypackage` 2개는 사용자가 삭제
3. **셰이더 ✅** — `Assets/50.Art/VFX/Shaders/HolySword.shader` (`VFX/HolySword`)
4. **텍스처 ✅** — 둘 다 `Assets/50.Art/VFX/Textures/`
   - `HolySword_Base.png` (2048²) — **에셋 베이스컬러를 대체**. 아래 「베이스맵을 갈아끼운 이유」
   - `HolySword_Emissive.png` (1024², R=보석 G=금)
5. **머티리얼 ✅** — `Assets/50.Art/VFX/_Materials/HolySword.mat`
6. **프리팹 ✅** — 아래 「적용 수치」
7. **검증 — 미완**(Play 필요)

### 베이스맵을 갈아끼운 이유 (2026-09-29)

에셋 원본 `T_StylizedWeapons_Sword_Base`(금·보라 가죽·청록 보석으로 칠해진 것)는 **프롭 룩**이다.
바닥에 꽂히는 심판의 검으로는 안 읽힌다. 그래서 초기 요구였던
**"백색 외곽선 + 안쪽으로 갈수록 희미한 그라데이션"** 을 **에셋의 실제 UV 위에** 다시 구웠다.

`scratchpad/make_basemap.py` — 순수 파이썬(zlib+struct), PIL/numpy 없음.

| | |
|---|---|
| 외곽선 씨앗 | ① **UV 섬 경계**(FBX `LayerElementUV` 를 직접 파싱해 1,509 폴리곤을 2048² 로 래스터화) ② **금 장식 영역 경계** ③ **보석 영역 경계** |
| 왜 셋인가 | 섬 경계만 쓰면 조각 실루엣만 나온다. 금·보석 경계를 넣어야 **덩굴 문양이 선으로 드러난다** |
| 거리장 | 챔퍼 3-4, 섬 안쪽으로만 번진다 |
| 램프 | `RIM 3px` 순백 → `FALLOFF 22px` smoothstep → `FLOOR 0.10` |
| 패딩 | 섬 밖 `10px` 번지게 — 밉맵에서 검은 테두리가 생기는 걸 막는다 |
| 색 | **없다.** RGB 는 회색조뿐이고 색은 전부 머티리얼이 칠한다 |

🔴 **첫 시도는 실패했다** — `RIM 5 / FALLOFF 58` 로 구웠더니 섬 폭보다 폴오프가 넓어
**덮인 면적의 70%가 거의 흰색**이 됐다. 그라데이션이 안 읽히고 통짜 백색 덩어리가 된다.
섬(특히 금 리본)이 2048² 에서 80~200px 밖에 안 된다는 걸 계산에 안 넣은 탓이다.

**셰이더도 같이 바꿨다 — 색을 전부 머티리얼로 뺐다:**

| 전 | 후 |
|---|---|
| `_GoldBoost` (float, 밝기만) | **`_GoldTint`** (HDR Color) |
| `_GemColor` 로 **덮어쓰기** | **곱하기** — 덮으면 외곽선 그라데이션이 평평해진다 |
| (없음) | **`_EdgeEmission`** — 베이스맵 밝기에 곱하므로 **선만 빛나고 안쪽은 안 빛난다** |

머티리얼 기본값: 칼날 `(0.78,0.86,1.0)` 차가운 백 / 금 `(1.0,0.82,0.45)` / 보석 `(0.35,0.95,1.1)`.
🔴 에셋 **노멀맵은 그대로 쓴다.** 면이 흰 라인아트로 평평해진 만큼 오히려 더 중요해졌다.

### 적용 수치 — `FX_Lightening_Strike.prefab`

🔴 `LightPillar` 을 **지우지 않고 개조**했다. 그 슬롯이 이미 `m_RenderMode: 4`(Mesh) +
`m_RenderAlignment: 2`(Local) 였고, 5천 줄짜리 ParticleSystem 문서를 손으로 쓰는 것보다
비교할 수 없이 안전하다. 그래서 그릴 Q18의 "새 스트레치 빌보드 추가"도
**같은 파티클의 TrailModule** 로 대체했다(GameObject 증가 0).

| | 전 | 후 | 근거 |
|---|---|---|---|
| 이름 | `LightPillar` | `HolySword` | |
| 이미터 Y | 30 | **15** | 낙하 12.0 + 칼끝 오프셋(사용자 튜닝) |
| `startLifetime` | 1 | **2.15** | 0.3 낙하 + 1.45 체류 + 0.4 소멸 (그릴 Q16) |
| `startSize` / `size3D` | 2 / 1 (2×15×2 원기둥) | **5 / 0** | 1.200 m × 5 = **6.0 m** (그릴 Q5) |
| `rotation3D` | 0 | **1** | X/Y 회전을 쓰려면 필수 |
| `startRotationX` | 0 | **3.1415927** (=180°) | 에셋은 칼날이 **+Y** → 뒤집어 꽂는다 |
| `startRotationY` | 0 | **2.3561945** (=135°) | 그릴 Q10 시작값. ±20° 굴려 튜닝 |
| ShapeModule | on | **off** | 칼이 정확히 원점 위에 뜨게 |
| SizeModule | on | **off** | 칼은 커지거나 작아지지 않는다 |
| `maxNumParticles` | 1000 | **4** | 칼은 하나 |

**🔴 낙하 산술 — 하나 바꾸면 셋 다 다시 맞춰야 한다** (위 ③의 실제 숫자)

```
칼끝 오프셋 = 1.048 m (메쉬 원점→칼끝) × startSize 5           = 5.24 m
낙하 거리   = 12.0 m                                           (고정)
이미터 Y    = 15        ← 🔴 사용자가 눈으로 튜닝한 값. 여기서 12 내려가 3.0 에 선다
렌더러 pivot.y = -0.16  ← 🔴 사용자 튜닝. 칼끝을 바닥 아래로 묻어 "꽂힌" 느낌을 낸다
속도 y 커브 = 0 → -1 선형(t 0→0.0682) → 0 급정지(t 0.0698)
속도 scalar = 160  (최고속 160 m/s)
이동거리    = 160 × 0.5 × 0.0698 × 2.15s                       = 12.0 m 정확히
착지 시각   = 0.0698 × 2.15s                                   = 0.150 s
```

**낙하 시간을 다시 바꿀 때의 공식** (`LIFE` = startLifetime 2.15):

```
t2(급정지 시각, 정규화) = 원하는 낙하시간 / LIFE
속도 scalar            = 낙하거리 / (0.5 × t2 × LIFE)
t1(램프 끝)            = t2 - 0.0016
키 기울기              = -1 / t1
→ 낙하거리를 유지하면 **착지 지점이 안 움직이므로** 이미터 Y 와 pivot 은 건드릴 필요가 없다.
→ 대신 **충돌 자식 6개의 startDelay 를 착지 시각에 맞춰 다시 밀어야 한다.**
```

🔴 **`m_Pivot` 과 이미터 높이는 사용자가 Play 로 맞춘 값이다.** 에셋 원점이 칼끝이 아니라
그립 속이라 둘을 같이 써야 칼끝 위치가 잡힌다. `m_Pivot` 은 "파티클 크기 단위"라
부호·배율을 정적으로 확정할 수 없어 눈으로 맞췄다 — **숫자를 근거 없이 되돌리지 말 것.**

**🔴 충돌 자식 6개는 착지 시각을 따라다닌다.** 자식들의 `startDelay` 는 "칼이 땅에 닿는 순간"에
맞춰져 있다. 낙하 시간을 바꾸면 **반드시 같이 밀어야 한다** — 안 밀면 칼이 닿기 전에 바닥이 터진다.

| | FlashCore | FlashGold | ShockRing | GroundDisc | RockChunks | ImpactDust |
|---|---|---|---|---|---|---|
| 착지 0.148초(원래 빛기둥) | 0.15 | 0.15 | 0.15 | 0.17 | 0.16 | 0.19 |
| 착지 0.30초 | 0.30 | 0.30 | 0.30 | 0.32 | 0.31 | 0.34 |
| **착지 0.15초 (현재)** | **0.15** | **0.15** | **0.15** | **0.17** | **0.16** | **0.19** |

`MotesSpiral` / `MotesRise` 는 **안 민다** — 낙하 전부터 바닥이 기운다는 읽힘이 오히려 좋다.

`FX_Lightening_Strike_Entry.computedDuration` = **2.2**
(최장은 `HolySword` 자신 = `lengthInSec 0.05 + startDelay 0 + startLifetime 2.15`).
`EffectLifetime.SystemLifetime` 의 식(`duration + startDelay + startLifetime`)과 동일하게 계산했다.
🔴 낙하 시간을 줄여도 **엔트리 수명은 거의 안 변한다** — 칼의 `startLifetime` 이 지배하기 때문이다.

**🔴 트레일 Y 위치 — Unity 에 오프셋 필드가 없다** (2026-09-30)

Trails 모듈에는 위치 항목이 **아예 없다**(mode/ratio/lifetime/minVertexDistance/textureMode/
textureScale/ribbonCount/shadowBias/worldSpace/dieWithParticles/sizeAffects*/inheritParticleColor/
generateLightingData/split*/attach*/colorOverLifetime/widthOverTrail/colorOverTrail 가 전부).
**트레일은 파티클 중심에 고정**이다.

그런데 이 에셋의 메쉬 원점은 **그립 속**(손잡이 끝에서 +0.152 m)이고 `startRotationX 180°` 로
뒤집혀 있다. 그래서 파티클 중심은 **폼멜 꼭대기에서 0.76 m 아래**, 칼날은 거기서 5.24 m 더
아래로 뻗는다 — 즉 트레일이 **손잡이 근처에서** 뿜어져 나온다.

내리려면 **두 노브를 같이** 움직여야 한다:

```
D = 트레일을 칼끝 쪽으로 내릴 거리(m)
이미터 Y        -= D          ← 파티클(=트레일)이 D 내려간다
렌더러 pivot.y   = D / startSize   ← 메쉬만 D 다시 올려 칼은 제자리

현재(사용자 튜닝 후): 이미터 Y **15**,  pivot.y **-0.16**   (startSize 5)
```

⚠️ 제안값은 이미터 13.8 / pivot +0.44 였으나 **사용자가 Play 로 보고 위 값으로 맞췄다.**
pivot 부호는 180° 뒤집힘과 pivot 규약이 겹쳐 정적으로 확정할 수 없었던 부분이다 —
**지금 값이 정답이다. 산술로 되돌리지 말 것.**

**셰이더 구동 입력** — ColorModule 이 셰이더를 먹인다.
`color.a` = 존재량(1 온전 → 0 완전히 흩어짐), `color.rgb` = 등장 플래시 틴트.
알파 키 3개: 1@0 → 1@0.814 → 0@1.0 (= 마지막 0.4초에 디졸브).

**트레일** — TrailModule on, `lifetime` 0.12(×2.15s = 0.26초), `sizeAffectsWidth` off,
`widthOverTrail` 0.9 m(= 날 폭), `colorOverTrail` 꼬리로 갈수록 투명.
머티리얼은 `m_Materials[1]` 에 **`HolySword_Trail.mat`**
(`Eric/URP Particles/Additive` + `t_trail01.png`, 틴트 `(0.62,0.78,1)`, Emission 3).

🔴 **처음엔 `Mat_fx_HCFX_Twinkle_01_add` 를 물렸다가 "트레일이 아예 안 보인다"로 반려됐다 (2026-09-30).**
"같은 프리팹의 Motes 가 이미 쓰니 안전하다"는 이유로 골랐는데, **텍스처를 안 봤다.**
그 머티리얼의 그림은 `Tex_fx_HCFX_Twinkle_01.tga` = **256×128 에 네 갈래 반짝이 별 2개**,
알파>8 인 픽셀이 **18.9%** 뿐이다. `textureMode: 0`(Stretch)은 이 아틀라스를 12 m 트레일에
**딱 한 번** 늘려 입히므로 길이 방향엔 별 두 개의 흐릿한 자국만 남고, 폭 방향은 별의
투명한 위아래에 걸린다. 애디티브라 검정은 그대로 안 보임이다.
**교훈: 트레일 머티리얼은 "가로로 긴 스트릭" 텍스처여야 한다.** 빌보드에서 예쁜 스프라이트는
트레일에서 정확히 반대로 동작한다. `m_Materials[1]` 이 트레일 슬롯인 것은 프로젝트 내
트레일 켜진 프리팹 8개로 확인했다.

### 착지 후 연출 2종 — `SwordGlow` · `SwordTwinkle` (2026-09-30 · 그릴 13문항 완료 · **구현 완료 ✅ · Play 검증 대기**)

칼이 꽂힌 뒤 「칼 중심의 은은한 원형 glow」 + 「칼 주위에서 제자리 반짝임」을 얹는다.
둘 다 **루트 직속**이다 — `Impact` 밑은 전부 착지 *순간* 연출이고 이 둘은 *그 뒤로 지속*이라 성격이 다르다.

**기준 타임라인** (칼 착지 0.150초 · 칼 수명 2.15초 · 디졸브 1.75~2.15초 · 엔트리 2.2초)

| | |
|---|---|
| 칼 지상 높이 | **4.56 m** (폼멜 꼭대기). ⚠️ pivot 부호에 따라 2.96 m 일 수도 있다 — 치수는 이 값 기준이며 어긋나면 배율만 줄이면 된다 |
| 가드 스팬 / 날 폭 | 2.0 m / 0.75 m |

#### 그릴에서 확정된 것

| # | 항목 | 결정 |
|---|---|---|
| 1 | glow 형태 | **빌보드 헤일로**(카메라를 보는 정원). 바닥 판은 `GroundDisc`·`ShockRing` 이 이미 1.7초까지 깔린다 |
| 2 | glow 리듬 | **등장 플래시 → 안정**. 셰이더 발광(그릴 Q14)과 같은 리듬이라 따로 놀지 않는다 |
| 3 | twinkle 볼륨 | **세로 실린더**. 구로 잡으면 6 m 칼의 위아래가 빈다 |
| 4 | twinkle 밀도 | **보통** — 14/s × 수명 0.3초 = 항상 4개 남짓 |
| 5 | 색 | glow = **차가운 백**(칼날과 통일) / twinkle = **금빛**(금 장식과 호응, 백색 위에서 점으로 뜬다) |
| 6 | glow 치수 | **지름 5 m, 중심 높이 2.3 m** — 칼 높이와 엇비슷해야 "칼이 머금은 빛"으로 읽힌다 |
| 7 | twinkle 볼륨 치수 | **반경 1.5 m × 높이 5.0 m**, 바닥에서 0.2 m 띄움 |
| 8 | twinkle 개당 | **0.15~0.4 m 랜덤**, **회전 없음(축 정렬)** — 렌즈 반짝임은 화면 축에 정렬된다 |
| 9 | twinkle 곡선 | **크기만** 0→최대→0. 애디티브라 크기가 곧 밝기다. 알파는 안 얹는다(뭉근해진다) |
| 10 | 시작 | glow **0.15초**(착지) / twinkle **0.4초** — 0.15~0.35초는 Flash·RockChunks·ImpactDust 가 동시에 터져 0.15 m 반짝임이 묻힌다 |
| 11 | 머티리얼 | **신규 0개.** glow = `Common_Glow3.mat`, twinkle = `twink_01.mat` 재사용 + **파티클 Start Color 로 틴트** |
| 12 | 밝기 기준 | glow 는 **칼보다 확실히 어둡게**. 형태는 칼이 책임지고 glow 는 "거기 있다"만 알린다 |
| 13 | 가려짐 | **보정 안 한다.** 15~20% 가려지는데, 가려졌다 나타나는 것이 칼의 두께를 알려 준다 |

#### 적용 수치

**`SwordGlow`** — 루트 직속, local `(0, 2.3, 0)`

```
lengthInSec 0.05 · looping 0 · playOnAwake 0
startDelay 0.15   startLifetime 2.0   startSpeed 0   startSize 5.0
startColor  차가운 백 HDR — 칼날 색조(0.62, 0.78, 1.0) × 2
Emission    rate 0, 버스트 1발 @ t=0        Shape  끔(원점에 1개)
Color over Lifetime 알파 = 플래시 후 안정 후 페이드
   1.00 @ 0      (착지 0.15초, 플래시)
   0.45 @ 0.10   (0.35초, 안정 수준으로 내려앉음)
   0.45 @ 0.80   (1.75초, 칼 디졸브 시작)
   0.00 @ 1.00   (2.15초)
Renderer  Billboard(0) · View(0) · Common_Glow3.mat
```

**`SwordTwinkle`** — 루트 직속, local `(0, 2.7, 0)` (= 0.2 + 5.0/2)

```
lengthInSec 1.35 (0.4 → 1.75초 방출) · looping 0 · playOnAwake 0
startDelay 0.4    startLifetime 0.3   startSpeed 0
startSize  0.15 ~ 0.4 랜덤            startRotation 0 (축 정렬)
startColor 금빛 HDR — 금 장식 색조(1.0, 0.82, 0.45) × 3
Emission   rate 14/s   →  라이브 14 × 0.3 = 4.2개
Shape      Box, scale (3.0, 5.0, 3.0)
Size over Lifetime  0 @ 0  →  1.0 @ 0.30  →  0 @ 1.0   (빠른 어택, 느린 감쇠)
Renderer   Billboard(0) · View(0) · twink_01.mat
```

🔴 **Shape 를 실린더가 아니라 Box 로 쓰는 이유.** 유니티 ShapeModule 에 **실린더가 없다.**
`ConeVolume` 의 angle 을 0 으로 두면 실린더가 되지만 원뿔 축을 +Y 로 돌리는 회전을
정적으로 검증할 수 없다(잘못되면 옆으로 누운 실린더가 된다). Box 3×5×3 은 회전이 필요 없고,
모서리가 반경 1.5 대신 2.12 까지 나가지만 **초당 14개 랜덤 배치에서는 사각형으로 안 읽힌다.**

#### 엔트리 수명 — 안 고쳤다 ✅

```
SwordGlow     0.15 + 0.05 + 2.00 = 2.200   ← HolySword 와 동률로 최장
HolySword     0.00 + 0.05 + 2.15 = 2.200
ShockRing     0.15 + 1.50 + 0.50 = 2.150
SwordTwinkle  0.40 + 1.35 + 0.30 = 2.050
```
`FX_Lightening_Strike_Entry.computedDuration` **2.2 유지** — 실측으로 확인했다.

#### 구현 후 실측값

| | SwordGlow | SwordTwinkle |
|---|---|---|
| Transform(local) | `(0, 2.3, 0)` | `(0, 2.7, 0)` |
| lengthInSec / startDelay | 0.05 / **0.15** | **1.35** / **0.4** |
| startLifetime / startSize | 2.0 / 5 | 0.3 / **0.15~0.4 랜덤** |
| startColor (HDR) | `(1.24, 1.56, 2)` 차가운 백 | `(3, 2.46, 1.35)` 금빛 |
| Emission | rate 0 + **버스트 1발** | **rate 14/s** + 버스트 0발 |
| 켜진 모듈 | Initial · Emission · **Color** | Initial · **Shape** · Emission · **Size** |
| Shape | 꺼짐 | **Box (3, 5, 3)** |
| 렌더러 | Billboard · View · `Common_Glow3` | Billboard · View · `twink_01` |
| maxNumParticles | 4 | 64 |

알파 곡선(glow): `1.0 @0.15초 → 0.45 @0.35초 → 0.45 @1.75초 → 0 @2.15초`
크기 곡선(twinkle): `0 → 1.0 @0.3 → 0`

🔴 버스트를 지우는 대신 **개수를 0으로** 뒀다(`countCurve.scalar: 0`).
`m_Bursts` 시퀀스를 통째로 들어내면 YAML 구조를 건드려야 하는데, 0발 버스트는 완전한 no-op 이다.

#### 구현 방식

🔴 **복제 원본을 `MotesSpiral` 에서 `HolySword` 로 바꿨다**(구현 중 판단).
`MotesSpiral` 은 **UVModule(스프라이트 시트)과 CustomDataModule 이 켜져 있어** 그대로 복제하면
텍스처가 잘려 나오고, 렌더러 버텍스 스트림에도 커스텀 데이터(`00010304052226`)가 물려 있었다.
반면 `HolySword` 는 **버스트 1발 · Shape 꺼짐 · UV 꺼짐 · ColorModule 켜짐** 이라 두 목표에 훨씬 가깝다.
되돌려야 했던 것은 `VelocityModule` / `TrailModule` / `rotation3D` 뿐이다.

실제 결과: 프리팹 **49,904 → 59,838줄**(+20%), 문서 44 → **52개**, fileID 중복 0.

#### 검증 항목

- [ ] glow 가 칼보다 **어둡다** — 칼 라인아트가 흰 원에 안 먹힌다
- [ ] 착지 순간 한 번 밝았다가 은은한 수준으로 내려앉는다
- [ ] twinkle 이 0.4초부터 보이고, **이동하지 않는다**(제자리에서 반짝였다 사라짐)
- [ ] twinkle 별 모양이 기울지 않는다(축 정렬)
- [ ] 동시에 4개 남짓 — 성기지도 촘촘하지도 않다
- [ ] 1.75초부터 둘 다 걷히고 2.2초에 깔끔히 끝난다(잔상·풀 고갈 없음)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음

#### 리스크

| | |
|---|---|
| 칼 지상 높이 | pivot 부호 미확정이라 4.56 m 가정. 2.96 m 였다면 glow 지름·실린더 높이를 약 0.65배로 줄여야 한다 |
| 프리팹 비대화 | +20%. 머지 충돌 시 손으로 풀기 어려워진다 |
| SVN | **신규 에셋 0개** ✅ — 프리팹 하나만 수정된다 |
| 재사용 머티리얼 | `Common_Glow3` · `twink_01` 은 다른 이펙트도 쓴다. **머티리얼 자체는 절대 수정하지 않는다** — 색·밝기는 전부 파티클 Start Color 로만 조절 |


## 검증 항목

- [ ] 지목한 좌표에 **칼끝이 정확히** 꽂힌다 (🔴 에셋 피벗이 그립 속이라 `pivot` 보정이 맞는지)
- [ ] 60° 카메라에서 **가드 윗면과 날 두께**가 보인다 — 이게 이 작업의 목적이다
- [ ] `startRotationY` 를 굴려 정면 각도를 맞출 수 있다
- [ ] 낙하 0.3초 뒤 **바닥에서 정확히 멈춘다**(뚫고 내려가거나 공중에 서지 않는다)
- [ ] 트레일이 낙하 구간에만 보이고 착지와 함께 걷힌다
- [ ] 발광 포인트가 멀리서도 읽힌다
- [ ] 2.15초에 깔끔히 사라진다(엔트리 수명과 어긋나지 않는다)
- [ ] 🔴 **MPPM 클라 창**에서 같게 보인다
- [ ] 콘솔 경고 없음 · 풀 고갈 없음

## 리스크

| | |
|---|---|
| 🔴 배치 | 에셋이 **git 에 6.3 MB** 로 들어간다. 팀 규칙은 "대용량 아트 = SVN"(`50.Art` 는 gitignore). **결정 필요** |
| 🔴 SVN | 셰이더·머티리얼·마스크 = **신규 3파일**이 `50.Art` 밑. 미등록 에셋이 머지로 날아간 전례 있음 → `svn add` 필수 |
| 피벗 | 에셋 피벗이 칼끝이 아니다. 렌더러 `pivot` 은 **파티클 크기 단위**라 `startSize` 를 바꾸면 같이 흔들린다 |
| 낙하 커브 | 위 ③. 높이·시간·커브가 물려 있다 |
| 데모 씬 | 에셋 데모 씬이 **Baked 라이팅 데이터**를 들고 온다. 빌드 씬 목록에 안 들어가게 할 것 |
| 맵 조명 | 커스텀 셰이더로 우회했지만 **근본 원인(프로브 없음·Baked 라이트)은 그대로**다. 경석 님 영역 |

---

# ⏸ 보류 = **23호 팔다리 전기 (돌진·레이지)** (2026-09-28 · 1~6단계 구현 완료 · **7단계(도구 실행)와 Play 검증 대기**)

> 작업 세션: **민경(Claude)**. 🔴 `TwentyThreeBoss.cs` 를 건드리므로 **경석 님 공유 필요**(AGENTS.md §3).
> 선례 = `GrabArmVFX`(잡기 팔 전기). 같은 `EffectPathPlayer` 를 팔다리 네 갈래로 늘린 것이다.

## 목표

보스 **몸통에서 손·발까지 이어지는 전기 흐름**을 붙이고, **일반 대쉬 공격**과 **레이지 돌진**에서 재생한다.

## 조사로 확정된 사실

| | |
|---|---|
| 도구 | `EffectPathPlayer` — "트랜스폼 배열을 따라 흐르는 펄스". 🔴 **경로는 하나만 받는다. 분기 불가** → 네 갈래 = 컴포넌트 4개 |
| 선례 | `GrabArmVFX` = `EffectPathPlayer`(`id: ArmElectric`, 3점, 0.3/0.3/pool 10). **애니 이벤트가 아니라 코드**가 켠다(`grabPulse` 필드) |
| 애니메이터 상태 | 일반 대쉬 = `DashAttack` / 레이지 = `Rage` (별개 클립) |
| 기존 훅 | 레이지는 `BeginRageDash` → `StartRageSmashClientRpc()` / `StopRageDash` → `Stop…` 쌍이 있다. **일반 대쉬는 연출 RPC 가 없다** |
| 리그 | 팔 `c_shoulder → forearm → hand` · 다리 `c_thigh_b → c_thigh_fk → c_leg_fk → leg_fk → foot` · 몸통 `c_spine_02.x` / `c_root_master.x` |
| 🔴 본 참조 | 중첩 FBX 인스턴스라 `--- !u!4 &<id> stripped` 스텁으로 들어간다. fileID 가 **이름 해시**라 텍스트로 지어낼 수 없다 |
| `EffectPathPlayer` API | `Id · IsEmitting · Play() · PlayOnce() · Stop()`. 🔴 **`SetScale` 이 없다**(`EffectSocketPlayer` 에는 있다) |
| `Stop()` | `!_emitting` 이면 즉시 반환 — 길목에서 무조건 불러도 안전 |
| `Play()` | `!_ready` 면 조용히 반환 — **경로가 2점 미만이면 아무 일도 안 일어난다** |

## 확정된 설계 (그릴 12문항 전부 합의)

| 항목 | 결정 |
|---|---|
| **경로** | 4갈래. 팔 `c_spine_02.x → c_shoulder.{l,r} → forearm.{l,r} → hand.{l,r}` / 다리 `c_root_master.x → c_thigh_b.{l,r} → c_leg_fk.{l,r} → foot.{l,r}` |
| **왜 시작점이 다른가** | 가슴에서 다리로 내려가면 몸통을 가로지르는 긴 구간이 생겨 관절 보간이 튄다 |
| **컴포넌트** | `TwentyThree/Effects/LimbElectricVFX_{ArmL,ArmR,LegL,LegR}`, `id` 비움(코드 전용) |
| **수치** | travelTime `0.25` / interval `0.1` / pulsePoolSize `6` / scale `1` |
| **왜 interval < travelTime** | 펄스를 **겹치게** 해야 "띄엄띄엄 지나간다"가 아니라 "계속 흐른다"로 읽힌다 |
| **엔트리** | `FX_Limb_Electric_Entry` 신규. 파트 프리팹은 그랩과 동일(`b0551a23…f02`), outroDuration `0.6`, **prewarmCount `24`**(4갈래×6), maxActiveWarn `40` |
| **왜 엔트리를 나누나** | 보기엔 같아도 그랩은 1갈래·이번은 4갈래라 prewarm 수요가 다르다. 공유하면 튜닝이 서로를 흔든다 |
| **카탈로그** | 새 헤더 `보스 — 돌진 전기` 밑에 `Dash_LimbElectric` |
| **보스 필드** | `[SerializeField] EffectPathPlayer[] limbPulses` **1개** (경석 님 파일의 표면 최소화) |
| **켜기** | `StartAttack()` switch — `case Dash:` · `case RageDash:` 각 한 줄. **선딜부터** |
| **끄기** | `FinishChain()` + `AbortAttackChain()` **두 길목** |
| **레이지 배율** | `EffectPathPlayer.SetScale(float)` 을 추가하고 레이지만 `1.6` |
| **본 배선** | `Effects/Editor/EffectSystemSetup.cs` 에 "팔다리 경로 채우기" 메뉴 추가 |

### 설계에서 특별히 짚은 것

**① 끄는 자리를 열거하지 않는다.** 그랩은 `StopGrabPulseClientRpc()` 를 네 군데에 흩뿌려 놓고
주석이 "끄는 곳은 넷이다 — 헛잡기 경로가 특히 중요하다"고 경고한다. 돌진은 그럴 필요가 없다:
정상 종료 `FinishChain()` · 비정상 `AbortAttackChain()`(카운터·그로기·사망·타임아웃) 둘뿐이다.
**덤으로 일반 대쉬와 레이지가 같은 두 곳으로 끝나므로 Stop 한 쌍이 둘 다 커버한다.**

**② 레이지 진입점이 둘인데 저절로 풀린다.** 일반 선택과 차징 완료(`StartRageAfterCharge`) 두 경로가
있지만, 후자도 결국 `StartAttack()` 을 다시 부른다. **switch 한 곳이면 둘 다 커버된다.**

**③ 선딜부터 켜는 근거는 그랩 주석에 이미 있다.**
> 팔 전기는 **잡기 판정보다 먼저** 흐른다 — 판정(AcquireGrab)은 히트 프레임이라 거기서 켜면
> 팔을 뻗는 동안 아무 예고가 없다.

돌진도 같다. `BeginDash()` 는 클립 0.57(실제 돌진 시작)에 불리므로 거기서 켜면 선딜이 비어 있다.
⚠️ 다만 **선딜 정보가 하나 늘어나는 건 난이도 변경**이다 — 경석 님 확인 대상.

**④ 애니메이션 이벤트를 쓰지 않는 이유.** 갈래가 4개면 id 도 4개라 클립마다 이벤트가 8개가 된다.
게다가 이 클립들은 SVN 아트 FBX 이고, 같은 세션에서 `JumpLanding` 이벤트가 `time: 0` 에 박혀
**조용히 안 뜨던** 사고를 이미 겪었다.

**⑤ 본 참조를 손으로 끌지 않는 이유.** 4갈래 × 4점 = 16개다. 레포에 이미 같은 패턴이 있고
(`"Tools 의 잡기소켓 저작 도구가 채워 준다"`), 이름 기반이라 **리깅이 바뀌어도 다시 돌리면 그만**이다.

## 구현 단계

1. **`EffectPathPlayer.SetScale(float)`** 추가 — `EffectSocketPlayer` 와 같은 3줄.
   ⚠️ 주석에 "재생 중에 부르면 이번 재생에 반영되지 않는다(배율은 대출 시점 확정)"를 남긴다.
2. **`FX_Limb_Electric_Entry.asset`** 신규 + `.meta`(guid 고정).
3. **`EffectCatalog`** — `Dash_LimbElectric` 프로퍼티 + 에셋 등록.
4. **`EffectSystemSetup.cs`** — "23호 팔다리 전기 경로 채우기" 메뉴. 본 이름으로 4갈래 자동 배선.
5. **`TwentyThree.prefab`** — `LimbElectricVFX_*` 4개 생성, `EffectPathPlayer` 붙이고 수치 저작.
   🔴 Unity 닫고. 경로는 비워 두고 4단계 도구로 채운다.
6. **`TwentyThreeBoss.cs`** 🔴 경석 — 필드 1 + switch 2줄 + RPC 2개 + 길목 2줄 (총 7줄쯤).
7. 검증.

## 구현 결과 (2026-09-28)

모두 완료. 계획에서 **한 가지 벗어났다**: 4단계를 `EffectSystemSetup.cs` 에 얹지 않고
**`Effects/Editor/LimbElectricPathAuthoring.cs` 새 파일**로 뺐다 — `EffectSystemSetup` 은 자기 주석에
"Effect System v1의 에디터 진입점 **두 가지**(기본 에셋 생성 · 스모크 테스트)"라고 적혀 있어서
보스 본 배선을 섞으면 그 계약이 깨진다. 레포의 `*Authoring.cs` 관례(`CrateAuthoringTool`,
`BossRoomAuthoring`, `PlayerInterruptSkillAuthoring`)와도 이쪽이 맞는다.

| 파일 | 내용 |
|---|---|
| `Effects/EffectPathPlayer.cs` | `SetScale(float)` 추가 (Play 전 호출 경고 주석 포함) |
| `50.Art/VFX/Common/Boss/FX_Limb_Electric_Entry.asset` 🆕 | guid `450273ae…`, prewarm 24 / maxActiveWarn 40 / outro 0.6 |
| `Effects/EffectCatalog.cs` + `.asset` | `보스 — 돌진 전기` 헤더 + `Dash_LimbElectric` |
| `Effects/Editor/LimbElectricPathAuthoring.cs` 🆕 | `Tools/Effects/23호 팔다리 전기 경로 채우기` |
| `2.Prefabs/Monster/Boss/TwentyThree.prefab` | `LimbElectricVFX_{ArmL,ArmR,LegL,LegR}` 4개 (0.25/0.1/pool 6), `limbPulses` 배선 |
| `Monster/Boss/TwentyThreeBoss.cs` 🔴경석 | 필드 1 + switch 2곳 + RPC 2개 + 길목 2곳 |

검증: 앵커 105 / dangling 0 · 카탈로그 프로퍼티 43 = 필드 43 · 줄바꿈·인코딩 유지.

🔴 **경로는 아직 비어 있다(`path: []`).** `Play()` 는 `_ready` 가 아니면 조용히 반환하므로
7단계(도구 1클릭) 전에는 **아무 일도 일어나지 않는다** — "안 나온다"로 오진하지 말 것.

## 검증 항목

- [ ] 일반 대쉬: **선딜부터** 네 갈래가 흐르고, 돌진이 끝나면 걷힌다
- [ ] 레이지: 차징 완료 경로로 들어가도 켜지고, **연타 사이에 끊기지 않는다**
- [ ] 레이지가 일반 대쉬보다 **굵다**(scale 1.6)
- [ ] 돌진 중 카운터/그로기/사망 → `AbortAttackChain` 으로 전기가 남지 않는다
- [ ] 체인 타임아웃(안전망) 경로에서도 안 남는다
- [ ] 🔴 **MPPM 클라 창**에서 보스가 이동 중일 때 펄스가 몸에 붙어 있다
- [ ] 그랩 팔 전기(`GrabArmVFX`)가 **회귀하지 않았다** — 엔트리를 나눴으므로 영향 없어야 한다
- [ ] 콘솔에 `EffectPathPlayer` 경고 없음 · 풀 고갈 경고 없음

## 리스크

| | |
|---|---|
| 🔴 경석 님 파일 | `TwentyThreeBoss.cs` 7줄. 선딜 노출은 **난이도 변경**이라 별도 확인 |
| 본 참조 유실 | 리깅에서 본 이름이 바뀌면 끊긴다 → 4단계 도구를 다시 돌리면 복구 |
| 펄스 과다 | 4갈래 × pool 6 = 동시 24. 프레임 문제가 보이면 interval 을 먼저 올린다 |
| SVN | 새 엔트리 2파일(`50.Art`)은 `svn add` 필요 — 이번 세션에 미등록 에셋이 머지로 날아간 전례 있음 |

---

