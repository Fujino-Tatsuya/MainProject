# handoff — 간파 선착순 한 명 (할 일 143, 은희 → 경석)

> 원본 규칙: [interrupt-presentation.md §0.10](interrupt-presentation.md#010-선착순-한-명-할-일-143). 브랜치 `feature/InterruptFirstComeOnly`.

## 무엇이 바뀌나 (보스 쪽 체감)

- 보스 간파 창 하나에 두 사람이 간파를 넣으면 **서버에서 먼저 성공한 1명만** 지금처럼 처리된다.
- 두 번째 사람의 간파 히트는 **`AttackInfo.isInterruptAttack = false`** 로 들어간다 — 보스 입장에선 일반 피해 한 대.
  - 23호·스파링 봇: 첫 성공 직후 취약 상태라 예전엔 두 번째 간파가 **취약 넉백**(`StartVulnerableKnockback`)을 한 번 더 냈다 → 이제 안 난다.
  - 23호 진단 로그 `인터럽트가 카운터로 성립하지 않았다` 도 두 번째 사람 몫은 안 찍힌다(표식이 없으니).
  - 중간보스·허수아비: 이미 `TryConsumeInterrupt` 로 창당 1회라 결과는 같다(피해만).
- 피해 자체(제압 ×1.2·자폭 실드 흡수·위협·막타 기록)는 그대로 들어간다.

## 보스 코드 변경

**없다.** 판정 지점은 플레이어 `PlayerInterruptSkillBase.ResolveHit` 한 곳이다.
창 열림·닫힘은 이미 보스가 부르는 `InterruptLagMonitor.RecordWindow`(23호 `SetCounterWindow`, `MonsterCounterWindow.RecordTransition`, 허수아비)가
선착순 장부(`InterruptWindowClaims`)에도 넘긴다.

## 보스 쪽에서 지켜 줄 것

- 새 간파 대상(또는 새 창 경로)을 만들 때도 창 개폐는 **`InterruptLagMonitor.RecordWindow` 를 지나게** 한다(23호는 `SetCounterWindow`, 중간보스는 `MonsterCounterWindow`).
  안 지나면 장부가 '새 창'을 몰라, 앞 창에서 밀린 시전이 그 첫 성공 시각 이전에 시작했으면 새 창에서도 피해만 될 수 있다.
- 간파 반응(카운터·취약 넉백·기믹 간파 처리)은 지금처럼 **`isInterruptAttack` 플래그로만** 판단한다. 다른 근거(예: 공격 타입)로 간파 반응을 붙이면 선착순이 새는 길이 된다.

## 같은 창 판정(요약)

- 새 창이 열리면 초기화. 첫 성공 수신 안에서 보스가 창을 닫아도 기록은 남는다.
- 다른 사람이 성공시킨 창이 열려 있거나, 닫혔어도 **그 첫 성공 이전(서버 시작 시각)** 에 시작한 시전 = 두 번째(피해만).
- 첫 성공 **뒤에** 새로 시작한 간파는 지금처럼 취약 중 간파 넉백이 난다(설계된 취약 규칙).

## Play 확인 (MPPM 2인)

1. 23호 돌진 창에서 A 성공 → B 가 같은 창에 연달아 간파: B 는 숫자만, 보스 넉백·두 번째 연출 없음.
2. 거의 동시: 둘 다 강 시도 암전 → 늦은 쪽은 섬광 없이 암전이 걷히고 슬로우가 실패 복귀.
3. 다음 창: B 가 먼저 성공하면 B 가 정상 처리(초기화 확인).
