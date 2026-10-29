# 보스 무적 HP 바 잠김 연출 (할 일 147, 2026-10-29)

> 보스 코드(TwentyThreeBoss)는 경석 영역이라 PR 대신 이 문서로 공유한다. **피해 판정은 한 줄도 바꾸지 않았다** — 서버가 이미 하던
> "피해 0 처리" 상태를 bool 하나로 복제만 한다.

## 무엇이 보이나
- 보스가 무적인 동안 화면 상단 보스 HP 바 채움이 **아주 밝은 회색(#E6E6E6)** 으로 덮이고, 바 가운데에 **자물쇠** 아이콘이 뜬다.
- 무적이 풀리면 0.15초(인스펙터, 최대 0.2초) 동안 회색이 걷히고 자물쇠가 1.3배로 커지며 사라진다. 해제 중 다시 무적이 되면 바로 잠김으로.

## 무적 판정 — 무엇을 넣고 뺐나
보스 쪽 새 복제값: `TwentyThreeBoss.HpLocked`(`NetworkVariable<bool>`, 서버 write) — `TwentyThreeBoss.HpLock.cs`.
서버가 매 프레임(`TwentyThreeBoss.JumpRise.cs` 의 `LateUpdate` 첫 줄) 계산해 바뀔 때만 쓴다.

| 원인 | 포함 | 근거 |
|---|---|---|
| 레이드 기믹 무적 `RaidGimmickDamageImmune`(간파 릴레이·북진 압박) | ✅ | `ReceiveAttack` 이 플레이어 피해를 0 으로 만든다 |
| 0줄 자폭 보호 `ZeroGuard` Reserved·Active·Exploding(폭발 대기 시간 안) | ✅ | `ZeroGuardFilter` 가 피해 0, `CanApplyHealthDamage` 가 직접 피해 거부. 자폭 실드 바(SelfDestructShield)는 그대로 위에 그려진다 |
| 휴면 `MonsterBase.IsDormant` | ✅ | `ReceiveAttack` 거절 + `CanApplyHealthDamage` false. 23호엔 보통 안 걸리지만 걸리면 무적이 맞다 |
| 0줄 치명 피해를 HP 1 에서 멈추는 첫 타(ZeroGuard Off → Reserved 로 넘어가는 순간) | ❌ | 무적이 아니라 피해를 깎는 것 — 그 다음 프레임부터 Reserved 로 잠김이 켜진다 |
| ZeroGuard Done·Killing | ❌ | 다음 피해에 죽는다(무적 아님) |
| 웰즈(BossWells) | — | 23호 등에 탄 장식 MonoBehaviour 라 자기 HP·무적이 없다. 보스 바는 23호 하나 |
| 튜토리얼 스피너(SpinnerBot `BlocksHealthDamage`) | ❌ | 23호·웰즈만 대상(사양). 스피너는 머리 위 바를 따로 쓴다 |

🔴 `ZeroGuardProtecting` getter 는 Exploding → Done 을 넘기는 부작용이 있어 쓰지 않았다 — 같은 조건을 읽기만 한다.
새 무적 원인을 추가하면 `ComputeHpLockedServer()` 에 한 줄 넣으면 HUD 가 따라간다.

## UI
- 규칙(무적 여부 → 덮개 세기·자물쇠 표시·해제 진행): `Assets/1.Scripts/UI/Combat/BossHpLockDisplay.cs`(순수 C#),
  테스트 `Assets/1.Scripts/UI/Combat/Editor/BossHpLockDisplayTests.cs`.
- `BossHealthHUD`: HP_Fill(그라데이션 Mask) 안 맨 위에 단색 덮개 `HP_LockTint` 를 **런타임 생성**한다 — 스프라이트가 색이 칠해져 있어
  `Image.color` 곱하기로는 회색이 안 나온다. 그라데이션을 끈 경우도 덮개가 바 모양 밖으로 안 나가게 Mask(원본 그리기 유지)를 붙인다.
- 프리팹: `Assets/2.Prefabs/UI/BossHealthHUD.prefab`(CombatHUD.prefab 안 중첩 원본) `BossInfoSlot/HP_LockIcon` 추가(40×40, Pos 0,12 = HP 바 가운데, 꺼 둔 채).
  CombatHUD.prefab 은 무수정.

| 인스펙터(BossHealthHUD) | 기본 |
|---|---|
| Hp Lock Icon | `BossInfoSlot/HP_LockIcon` |
| Hp Lock Color | #E6E6E6 |
| Hp Lock Release Seconds | 0.15 (0~0.2) |
| Hp Lock Release Pop | 0.3 |

- 아이콘: `Assets/50.Art/UI/HUD/lock.png`(SVN) — Sprite(2D and UI)·Single·밉맵 끔·Alpha Is Transparency(같은 폴더 `gauge_boss_HP.png` 와 같게).

## Play 확인 항목(사람)
- 23호 레이드 간파 릴레이·북진 압박 동안 바가 밝은 회색 + 자물쇠, 끝나면 짧게 걷힘.
- 레이드 0줄(자폭) — HP 1 에서 멈춘 뒤 자폭·폭발 대기 동안 잠김, 실드 바가 회색 위에 보이는지.
- 호스트·클라(MPPM) 둘 다 같은 타이밍, 늦게 들어온 클라도 현재 잠김을 본다.
- 등장 차오름(흰 발광) 중에는 잠김이 없어야 정상.
