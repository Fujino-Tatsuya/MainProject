# PLAN-monster-tells.md — 일반 몹 공격 예고(Chomp 선딜·입 발광 / Humanoid 도끼 발광 / Mortar 수직 낙하 포격)

> 상태: **✅ 완료(팀장 확인 10-13)** · 2026-10-13 · 작업자: 경석(Claude) · 브랜치 `feature/Boss23`
> 요청(팀장 10-13): Chomp 는 붙자마자 물어 피할 수 없다 → 선딜 + 입 색 주황→빨강→공격. Humanoid 는 도끼 주황→빨강→공격.
> Mortar 는 포물선이 벽에 부딪힌다 → 코그모 궁처럼 원형 예고 + 하늘에서 낙하. 조사 = Explore 에이전트, 제한사항 = Codex 교차검증(10-13).

## 0. 현재 상태 (10-13 조사)
- 세 몹 모두 **전용 클래스 없음** — `MonsterBase` + 데이터(`Assets/2.Prefabs/Monster/Data/*Data.asset`) + 프리팹.
- 🔴 `attackWindup`(Chomp·Humanoid 0.35, Mortar 3.5)은 **어디서도 안 쓰인다**. 타격 = 애니 이벤트 `OnAttackHit`(서버만 판정). 타이머로 되살리면 이중 히트.
- 트리거→히트: **Chomp ≈ 0.20초**(공격 클립 하나, 이벤트 @0.357) · **Humanoid ≈ 0.85초**(예비동작 0.73초 + 타격). `engageAttackDelay` 둘 다 0.
  Chomp FBX 에 **안 쓰는 `Anticipation Attack` 테이크**가 있다(`A_Chompbot_Attack.fbx.meta`).
- 메시: **Humanoid 도끼 = 별도 MeshRenderer `G_SlendertBot_Weapon`**(중첩 아트 프리팹 안). **Chomp = 스킨드 1개·서브메시 1개**, 턱은 본(`UpperJaw`·`LowerJaw`)뿐, 정점당 본 1개(강체).
  셰이더 = FlatKit `StylizedSurface`(`_EmissionColor` 있음).
- 색 장치: `HitFlash` 가 렌더러 MPB 를 `SetPropertyBlock(null)` 로 통째로 지운다 → 다른 MPB 색은 피격 한 번에 날아간다.
- 모든 피어 신호: 공격 상태 복제(`_state` NetworkVariable → `OnStateChanged`), 애니 진행(피어 로컬). 선딜 전용 RPC 없음.
- Mortar: Cast→AttackStart→Anticipation(루프, 커밋)→Strike(`OnAttackHit` 에서 투사체 발사, 진입~발사 ≈ 1.45초). 포물선(arcHeight 4, 비행 ≈ 1.8초) → 벽·바닥에 닿으면 그 자리 폭발.
  `MonsterRangedAttack`·`MonsterProjectile`·`P_MonsterProjectile` 는 **PeekABot·TeslaBot 과 공유**. 시야(LOS) 검사 없음. 맵에 층(y 0 / 2.94)·다리 있음.
  🐞 직격 후 스플래시가 같은 대상에 한 번 더 들어간다(`MonsterProjectile.cs:104-107,165`).

## 1. 결정 (팀장 10-13)
| # | 결정 |
|---|---|
| M1 | Chomp 선딜 = **예비동작 테이크를 먼저 확인**해서 쓸 만하면 Humanoid 처럼 예비동작→공격 구조로. 애매하면 **코드로 멈춤 구간**(공격 애니 시작 전 N 초 정지·조준) |
| M2 | 부위 표시 = **발광(에미션) 색** 주황 → 빨강 → 공격(타격 순간 끔) |
| M3 | Chomp 입 = **턱 메시 분리**(본 기준, 스피너 분할 선례) |
| M4 | Mortar = **수직 낙하 + 고정 원**(Codex 권장 ①) |
| M5 | 사격은 **시야 있을 때만** — 박격포→타깃 사이 벽이 있으면 안 쏘고 자리를 옮긴다(벽 = 엄폐물 유지). 인지·추격은 지금 그대로 |
| M6 | 원 = **발사 순간 타깃 발밑에 고정**, **1.5초** 동안 차오르고 다 차는 순간 착탄(낙하는 마지막 구간에 보임). 시간은 데이터 칸 |
| M7 | 예고가 뜬 뒤엔 박격포가 죽거나 맞아도 **포격은 떨어진다**(지금 투사체도 발사 후엔 안 사라짐) |
| M8 | 피해 = **같은 바닥만** — 착탄 바닥 높이 ± 허용 높이 안, 원 반경은 평면(XZ) 거리. 위층·다리 아래는 안 맞음 |

## 2. 구현
### 2-1. 부위 발광(공용) — `MonsterAttackTell` 컴포넌트
- 프리팹에 붙이는 표시 전용 컴포넌트(모든 피어, IsServer 가드 없음). 칸: 발광 대상 렌더러 · 주황/빨강 색 · 주황→빨강 전환 시점 · 세기.
- 시작 = `OnStateChanged` 에서 Attack 진입. 진행 = 애니 정규화 시간(또는 M1 의 정지 타이머) — 타격 직전 빨강. 끝 = 타격 이벤트 또는 Attack 이탈(한 곳에서 일괄 해제 — Humanoid 쿨 0 연타 깜빡임 방지).
- 🔴 `HitFlash` 충돌: 발광은 **재질 인덱스별 MPB 가 아니라** 해당 렌더러의 `_EmissionColor` 를 쓰되, `HitFlash` 가 MPB 를 지운 뒤 다음 프레임에 다시 넣는다(매 프레임 적용 — 렌더러 1~2개라 비용 무시). 또는 `HitFlash` 에 "이 렌더러 제외" 마커(`NoHitFlash` 선례). 구현 때 둘 중 화면 확인으로 결정.
- 발광이 켜지려면 재질에 에미션 키워드가 필요 — 런타임 재질 인스턴스에서 켠다(공유 재질 오염 금지).

### 2-2. Chomp
- M1: `A_Chompbot_Attack.fbx` 의 `Anticipation Attack` 테이크 길이·모양 확인(Unity 에서 미리보기). 쓸 만하면 클립 정의 추가(FBX .meta = **SVN** → Unity 닫고 작업) + `Controller_ChompBot` 에 예비동작 상태 추가. 아니면 `MonsterBase` 에 **공격 전 대기**(데이터 `preAttackHold` 초, 기본 0 — 다른 몹 무영향): Attack 진입 후 그 시간 동안 트리거를 미루고 정지·조준만.
- M3: 본 가중치로 턱(UpperJaw·LowerJaw 계열) 정점을 떼어 **턱 렌더러**를 형제로 만든다(본·rootBone 공유 — 스키닝 총량 불변). 새 메시 사본은 FBX 옆(50.Art, **SVN 커밋 필요**). 원본 FBX·아바타·본 경로 무변경(스피너 선례 — Blender 왕복 금지).
  확인: 사망 분해 파츠셋(`ChompBot_Parts.asset`)·`DissolveDeath`·피격 콜라이더·`HitFlash`(턱도 깜빡여야 함)·실루엣.
- 선딜 값: 0.5~0.6초에서 시작(Humanoid 0.85 보다 짧게 — 물기 몹 정체성 유지). Play 로 팀장 조정.

### 2-3. Humanoid
- 도끼 렌더러 `G_SlendertBot_Weapon` 에 `MonsterAttackTell`. 중첩 아트 프리팹이라 **런타임에 이름으로 찾는다**(외부 프리팹 오버라이드가 해석 안 된 전례 `MonsterBase.cs:166-172`).
- 예비동작 0.73초 동안 주황 → 타격 직전 빨강. 선딜 길이는 그대로.

### 2-4. Mortar — 전용 컴포넌트 `MortarStrike`(공유 투사체 코드 무변경)
- 분기: 데이터에 **명시적 공격 방식**(`rangedStyle = Projectile | SkyStrike`) — `PerformAttackHit` 의 원거리 경로에서 SkyStrike 면 이 컴포넌트로. Peek·Tesla 는 그대로(Codex: `arcHeight` 를 재해석하지 말 것).
- 흐름(서버): 발사 이벤트 순간 타깃 **발 위치** 스냅샷(지금의 `+0.8m` 조준 보정 안 씀) → 바닥 높이 확정 → 공격 ID·지점·반경·**서버 착탄 시각** 을 모든 피어에 RPC → 서버 타이머 만료 시 **한 번** 판정 → 착탄 이벤트 RPC(폭발 VFX·사운드 각 피어 재생).
  포격은 몹 FSM 과 분리된 독립 수명(몹 사망·피격·재배치와 무관 — M7). 동시 포격 여러 개 허용(리스트).
- 표시(모든 피어): `BossPatternVisuals` 판(원 윤곽 + 채움, 스타일 = 몬스터 데이터의 `TelegraphStyleSO.circle`)을 서버 착탄 시각 기준 진행률로 채움 — 지연만큼 늦게 시작해도 끝은 맞춘다. 낙하체는 마지막 구간 로컬 Lerp(드론 선례).
- LOS(M5): 공격 시작 시 박격포 머즐 → 타깃 몸통 레이캐스트(정적 지형 레이어). 막히면 공격 안 하고 재배치. 발사 순간 다시 검사해 막혔으면 쏘지 않는다.
- 판정(M8): 착탄 지점 기준 OverlapSphere(넉넉히) → Unit 별 1회(중복 제거) → **XZ 거리 ≤ 반경 && |발 높이 − 착탄 바닥| ≤ 허용 높이**. 피해 경로는 기존 `Hurtbox → ReceiveAttack`·타깃 마스크 유지. 버퍼는 넉넉히(16칸 포화 주의).
- 데이터 신설: `skyStrikeTelegraphTime`(1.5) · `skyStrikeFloorTolerance`(예: 1.5m) · 반경은 기존 `splashRadius` 재사용. xlsx 노출 여부(`[DataTableIgnore]`)는 구현 때 결정 — 기획 수치면 테이블.
- 폭발 VFX = 기존 `FX_Mob_Bomb_Explode` 엔트리를 `EffectManager` 로 직접 재생(투사체 Despawn 경로 대신). 사운드는 엔트리가 비어 있음 — 빈 채로 둔다(사운드 담당).
- 🐞 직격 이중 피해(Peek·Tesla 도 해당) — **팀장 10-13: 고친다**(직격 대상은 스플래시에서 제외).

## 3. 리스크
- Chomp 턱 분리 = SVN 메시 신규 + 프리팹 구조 변경 → 사망 분해·피격 VFX·실루엣 회귀. 스피너 분할 도구를 본 가중치 기준으로 바꿔야 함(서브메시 기준이 아님).
- 발광이 FlatKit 셰이더에서 기대만큼 보이는지(에미션 키워드·Bloom 꺼짐 상태) — 화면 확인 후 세기 조정.
- Mortar LOS 레이어 선택(정적 지형만 막아야 하고 몹·플레이어·트리거는 통과) — 존 프리팹 레이어 확인.
- Mortar 동시 포격 가독성(Squad) — 원 겹침. 외곽선 유지, 필요하면 발사 시차.
- 데이터 테이블 모드(xlsx)에서 새 칸이 덮이는지.

## 4. 검증
- 컴파일 0 · EditMode(Mortar 판정 필터: 같은 바닥/다른 층/XZ 경계, LOS 판정 규칙, `preAttackHold` 0 이면 기존과 동일).
- Play: Chomp 선딜·입 발광 순서(주황→빨강→물기) · Humanoid 도끼 발광 · 피격 중에도 발광 유지 · Mortar 원 → 낙하 → 같은 바닥만 피해 · 벽 뒤에선 안 쏨 · 몹 사망 후에도 떨어짐.
- MPPM 2인: 클라에서도 발광·예고·착탄 시각 일치.

## 진행
### 10-13 구현 ✅ · 컴파일 0 · 전투 EditMode 123/2(기존 실패 2 = BossCounterData Jump/Dash — 무관) · MonsterSkyStrikeTests 6/6 · ✅ 팀장 Play 확인("다 문제없음")
- **Chomp 선딜**: FBX 파싱으로 `Armature|Attack` 테이크 0~30 프레임 = 예비동작(몸 들썩·턱 반 벌림) 확인 → `A_Chompbot_Attack.fbx.meta` 에 클립 `Anticipation Attack`(0~30) 추가(옛 이름표 ID 재사용) + `Controller_ChompBot` 에 `AttackAnticipation` 상태(속도 1.8 ≈ 0.55초, Attack 트리거 전이를 여기로) — 저작 메뉴 `Tools/Monster/몬스터 공격 예고 준비 (멱등)`.
- **발광**: M2 의 "FlatKit 에미션" 대신 **가산 발광 슬롯**(`Monster/AttackGlow`, `MonsterAttackTell`)으로 변경 — FlatKit `_EMISSION` 은 shader_feature 라 쓰는 재질이 없으면 빌드에서 빠지고, HitFlash 의 MPB 초기화와 부딪힌다. 원래 재질 무변경 → 피격 섬광과 충돌 없음(팀장 "보고 정하자" 건 해소). 타이밍 = 복제 상태(Attack) + 애니 상태 이름·정규화 시간(Chomp 히트 0.357 · Humanoid 0.102).
- **Chomp 턱 분리**: 본(UpperJaw·LowerJaw) 기준 삼각형 분리 → `ChompBot_FormReview_v5_Unity_Body/_Jaw.asset`(SVN 신규, 몸 23081·턱 6794 정점) + 형제 렌더러 `ChompBot_Jaw`(본·rootBone 공유). 사망 분해는 모든 렌더러를 숨기고 구운 조각을 띄우는 방식이라 영향 없음.
- **Humanoid**: 도끼 `G_SlendertBot_Weapon` 을 이름으로 찾는 `MonsterAttackTell`.
- **Mortar**: `MonsterDataSO.rangedStyle = SkyStrike` + `skyStrike*` 칸([DataTableIgnore] — xlsx 반영은 나중). `MonsterSkyStrike`(피어별 독립 관리자 — 서버 착탄 시각 RPC 1회, 클라는 서버 시각으로 스스로 폭발, 서버 1회 판정). 시야 = 사격 시작 전 + 발사 순간 레이캐스트(Default, Unit 무시) → 막히면 재배치. 같은 바닥 판정(평면 반경 + 발 높이 ± 1.5m). 폭탄 모양·폭발 = 투사체 프리팹에서 빌림.
- **직격 이중 피해 수정**(`MonsterProjectile.Detonate(directHit)`).
- SVN 커밋 대상: FBX .meta · Controller_ChompBot · 분리 메시 2개(+.meta).
