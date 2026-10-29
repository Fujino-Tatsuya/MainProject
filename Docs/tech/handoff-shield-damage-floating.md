# 인수인계 — 보스 쉴드 피해 숫자 표시 (할 일 141)

> 2026-10-29 · 작성 claude-alt · 브랜치 `feature/ShieldDamageFloating` · 보스 영역(경석) 공유용(PR 없음 규칙).

## 문제
웰즈·23호가 쉴드(자폭 실드)를 가진 상태에서 플레이어가 때리면 데미지 숫자(FloatingDamage)가 안 떴다.

## 확정 원인
- **23호** — 레이드 ④ 폐기 처분/0줄 자폭의 **자폭 실드**는 `Health` 보호막이 아니라 `TwentyThreeBoss._selfDestructShieldLeft` 다.
  `TwentyThreeBoss.ReceiveAttack` 이 `base.ReceiveAttack` **전에** 흡수 몫을 `attackInfo.damage` 에서 뺀다.
  그래서 Unit 의 `ApplyHealthDamage` 에는 남은 피해만 오고, 전부 흡수되면 피해 0 → 조기 반환 → `ClientDamageDealtClientRpc` 가 안 나간다.
  넘친 몫이 있으면 체력 숫자만 뜨고 실드 몫은 어디에도 실리지 않았다.
- **웰즈** — 별도 경로 없음. `BossWells` 는 23호 NetworkObject 자식의 **MonoBehaviour**(체력·`ReceiveAttack`·쉴드 없음)라
  웰즈를 때린 피해도 23호 유닛 하나로 들어간다. 원인은 위와 같다.
- 참고: Unit 일반 보호막(`Health.TakeShieldDamage`, 거너 차지 실드)은 원래 Shield 채널로 RPC 를 낸다. 보스에 `AddShield` 하는 코드는 없다.

## 바꾼 것
| 파일 | 내용 |
|---|---|
| `Assets/1.Scripts/Unit/Unit.cs` | `protected void ReportAbsorbedShieldDamage(amount, attackInfo, hitContext)` 추가 — 기존 `ClientDamageDealtClientRpc(0, amount, …)` 를 Shield 채널로 보낸다. **새 RPC 없음.** |
| `Assets/1.Scripts/Monster/Boss/TwentyThreeBoss.cs` | `ReceiveAttack` 에서 흡수량을 지역 변수 `absorbed` 로 남기고, `base.ReceiveAttack` 이 받아들였으면(`resolved`) 위 메서드를 한 번 부른다. 판정·실드 차감·통계·위협은 그대로. |
| `FloatingDamageSettings.cs` | `FloatingPopupStyle.offset`(유형별 고정 화면 오프셋 px) 추가. ShieldDamage 기본색 #7FD8FF · 오프셋 (0, 64). |
| `FloatingDamagePopup.cs` | 위치 계산에 스타일 오프셋을 더한다. |
| `Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset` | ShieldDamage 색 #7FD8FF · offset (0,64), 나머지 유형 offset (0,0). |
| `Assets/Tests/EditMode/FloatingDamage/Editor/FloatingDamageShieldStyleTests.cs` | 색·세로 간격·누적 키 분리 테스트(러너: Tools/Tests/데미지 숫자 EditMode 테스트 실행). |

## 규칙
- 실드를 깨고 체력까지 들어간 한 타격 = 체력 숫자(기존 색) + 실드 숫자(하늘색) 두 개. 누적 키는 `PopupKind` 를 포함해 섞이지 않는다.
- 겹침 방지 = ShieldDamage 스타일 오프셋. 필요 간격 = (체력 높이 + 실드 높이)/2 × 최대 holdScale + 분산 y × 2 = 61px(현재 값 기준) ≤ 64.
  실드 숫자만 뜨는 경우도 같은 오프셋(체력 숫자 자리보다 64px 위)이다. 색·오프셋은 에셋 인스펙터 "유형별 스타일"에서 바꾼다.
- 실드 숫자는 **방어 경감 전** 값이다(자폭 실드가 원래 경감 전 피해로 깎인다). 체력 숫자는 경감 뒤 값.
- 0줄 자폭 보호 중엔 넘친 몫이 HP 로 안 가므로(ZeroGuard) 실드 숫자만 뜬다.
- 공격이 거절되면(사망·휴면) 숫자도 없다 — 실드 차감은 기존대로 일어난다(변경 없음).

## 보스 쪽에서 새 흡수 지점을 만들 때
`base.ReceiveAttack` 전에 `attackInfo.damage` 를 깎는 흡수는 숫자에 안 실린다. 받아들여진 뒤 `ReportAbsorbedShieldDamage` 를 불러 준다.
