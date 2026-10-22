# PLAN — `attackWindup` 을 공격 선딜(예비동작 길이)로 연결 (2026-10-19 · 경석)

> 상태: ✅ **승인(10-19 경석) · 구현 완료(미커밋)** — 컴파일 0 · 전투 EditMode 246/0(정책 케이스 +6). ✅ 경석 Play 확인(10-19, 달라진 것 체크) · ⏳ MPPM 2인.
> 구현 메모: 예비동작 원래 길이는 `AnimatorClipInfo` 클립 길이 ÷ 상태 속도(`info.speed × speedMultiplier`)로 컨트롤러당 1회 측정. `attackWindup` 에 Tooltip 추가 — 다음 병합 Export 때 xlsx 설명 칸이 바뀐다(값 무관).
> 출처: [PLAN-monster-data-inspector.md](PLAN-monster-data-inspector.md) §7-2(“실제 연결”) · 조사 10-19.

## 1. 지금 상태 (10-19 조사)
- 히트 = 애니 이벤트 `OnAttackHit` **전용**(`MonsterBase.HandleAttack` — 타이머 폴백 제거됨). `attackWindup` 은 옛 타이머 시절 칸이라 아무도 안 읽는다.
  → **타이머로 되살리면 이중 히트**(PLAN-monster-tells §0). 히트 시점은 계속 애니가 정한다.
- 선딜은 이미 **애니메이터 예비동작 상태 `AttackAnticipation`** 으로 있다 — Chomp(상태 속도 1.8 ≈ 0.55초, 10-13 추가) · Humanoid(≈ 0.73초).
  다른 몹(Peek·Tesla 터렛, Mortar, 중간보스 3, 23호)은 이 상태가 없다.
- 기획 r425 xlsx 값: Chomp 0.35→**0.55** · Humanoid 0.35→**0.55** · RangedMobile 0.25→0.5 · RangedTurret 0.3→0.5.
  RangedMobile·RangedTurret 데이터는 **쓰는 프리팹이 없다**(효과 0).
- 재생 속도는 `MonsterBase.ManagedAnimatorSpeed()` → `MonsterAnimSpeedPolicy.Resolve` 가 매 프레임 정한다(공격 중 = `attackSpeed`). 모든 피어에서 돈다.

## 2. 결정 (경석 10-19)
| # | 결정 |
|---|---|
| W1 | **예비동작 길이 = `attackWindup`** — `AttackAnticipation` 상태의 재생 속도를 코드가 계산해 windup 초 만에 끝나게 한다. 히트는 계속 애니 이벤트 |
| W2 | **Chomp·Humanoid 만** — `AttackAnticipation` 상태가 있는 몹만 자동 적용. 없으면 무시(지금 동작) |
| W3 | Humanoid 는 **xlsx 0.55 그대로** — 지금 0.73초보다 빨라진다(기획 의도로 본다) |
| W4 | **공격 속도를 따른다** — 실제 선딜 = `attackWindup ÷ attackSpeed` |

## 3. 설계
- `MonsterAnimSpeedPolicy.Resolve` 에 예비동작 분기 추가(순수 함수 — 테스트 가능):
  `playingAnticipation && windup > 0 && naturalLength > 0` → `attackSpeed × naturalLength ÷ windup`
  - `naturalLength` = 그 상태의 현재 길이(`AnimatorStateInfo.length` — 클립 길이 ÷ 상태 속도, animator.speed 미포함). Chomp ≈ 0.55 · Humanoid ≈ 0.73.
  - 결과: 예비동작이 windup ÷ attackSpeed 초에 끝난다. 범위 제한(0.05 ~ 10)으로 0 나눗셈·극단값 방지.
- `MonsterBase.ManagedAnimatorSpeed()` 에서 현재 상태가 `AttackAnticipation` 인지(이름 해시 캐시) 판정해 넘긴다. 상태명은 코드 상수 — 두 컨트롤러가 같은 이름을 쓴다. 새 데이터 칸 없음.
- `windup ≤ 0` = 예비동작 원래 속도(× attackSpeed) — 지금과 같다.
- 23호는 `ManagesAnimatorSpeed = false` 라 이 경로를 안 탄다(무영향). 중간보스·터렛·Mortar 는 상태가 없어 무영향.
- 인스펙터: `attackWindup` 을 “⚠️ 미사용” 묶음에서 **전투 기본**으로 옮기고 이름 “공격 선딜 (예비동작 길이)”. Tooltip 갱신(“예비동작 상태가 있는 몹만 — Chomp·Humanoid”). 필드 주석 `(base 미사용)` 정정.
- `MonsterAttackTell`(발광)은 정규화 시간 기준이라 길이가 바뀌어도 주황→빨강 순서는 그대로.

## 4. 수정 파일
- `Monster/MonsterAnimSpeedPolicy.cs` · `Monster/MonsterBase.cs`(ManagedAnimatorSpeed · `AttackWindupSeconds` 주석) · `Monster/MonsterDataSO.cs`(Tooltip·주석만 — 이름·직렬화 그대로)
- `Monster/Editor/MonsterInspectorCatalog.cs`(묶음 이동) · `MonsterAnimSpeedPolicyTests.cs`(케이스 추가)
- 에셋·컨트롤러(SVN) **무변경**.

## 5. 리스크
- **안전망 타이머**: 공격은 `attackDuration ÷ attackSpeed` 가 지나면 강제 종료된다. 선딜이 길어져 히트 전에 끝나면 **조용히 피해 0**. Chomp·Humanoid 는 1.6초 — 0.55 면 여유 있다. 큰 값을 넣을 때를 위해 `windup ≥ attackDuration` 이면 스폰 시 경고 로그.
- Humanoid 가 빨라진다(0.73 → 0.55) — 피하기 어려워질 수 있다. Play 로 확인.
- 테이블 모드에서 xlsx 0.55 가 적용되는지(인스펙터 SO 값은 0.35) — 테이블 모드 Play 로 확인. 인스펙터 모드 Play 는 SO 0.35 가 쓰인다(Chomp 가 지금보다 짧아진다 — 데이터 출처 차이일 뿐).
- 상태 판정은 레이어 0·전이 중에는 다음 상태 기준이 아님 — 전이 시간 동안 한두 프레임 원래 속도. 체감 무시 수준.

## 6. 검증
- 컴파일 0 · 전투 EditMode 전체(정책 테스트: windup 0 = 기존 · 0.55/자연 0.55 = 배속 1 · attackSpeed 2 → 절반 시간).
- Play(테이블 모드): Chomp·Humanoid 트리거→물기/타격 시간 측정(로그) ≈ 0.55 ÷ attackSpeed · 발광 순서 · 피해 정상. 중간보스·터렛·Mortar·23호 동작 무변경.
- MPPM 2인: 클라에서도 예비동작 길이 같음(속도 계산이 각 피어 로컬, 같은 데이터).
