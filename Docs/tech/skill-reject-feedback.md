# 스킬 쿨타임 거부 피드백 — 이벤트와 사운드

> 할 일 44 2차(2026-10-15). 스킬을 쿨타임 중에 눌러 거부됐을 때 오너 화면에서만 효과음·UI 를 낼 수 있게 한 연결 지점.
> 선입력 버퍼 규칙(자원 부족 입력은 바로 버림)과 함께 들어왔다.

## 1. 이벤트

- **`PlayerSkillController.CooldownRejected`** — `event Action<PlayerSkillSlot>`, 인자 = 거부된 슬롯(`Main`·`Sub`·`Interrupt`·`Ultimate`).
- **오너 로컬 전용**: 그 플레이어를 조작하는 피어(오프라인·호스트 자기 캐릭터·원격 클라 자기 캐릭터)에서만 난다. 서버·다른 피어에는 없다. 사운드 전용 RPC 도 없다.
- **입력 1회 = 1회**: Idle/Move 에서 스킬 입력(이번 프레임 눌림 또는 선입력 버퍼)을 처리할 때 쿨타임이면 그 입력을 버퍼에서 버리고 한 번 낸다.
  버렸으므로 다음 틱에 같은 입력으로 다시 나지 않는다(테스트 `PlayerInputBufferTests.CooldownReject_*`).
- **안 나는 경우**: 스킬 실행·요청 응답 대기 중(바쁨 — 입력은 0.2초 들고 있다가 풀린 첫 틱에 판정), 슬롯 미배정, CC·차단 효과·`CanUse` 거부, 조준 중 억제 입력.
- 판정 = 이 피어의 쿨타임 장부(`IsBlockedByCooldown` — 원격 오너는 서버가 미러한 HUD 장부). HUD 가 쿨타임으로 보이면 시전 요청을 보내지 않는다(조준 진입 게이트와 같은 조기 차단, 최종 검증은 그대로 서버).
- 구독: 같은 플레이어의 `PlayerSkillController` 에 `CooldownRejected += ...`. 쓰는 곳: `PlayerStateController.cs` 의 `PlayerStateBase.TryStartSkill`.

## 2. 사운드 지정

- `Player.prefab`(base) 의 **`PlayerSkillController` › `Cooldown Reject Sound Key`** 에 `Sound` 시트 `key` 를 적는다(슬롯 공용 하나). Variant 는 base 를 따른다.
- **비우면 무음**(기본값). 키가 있으면 `AudioManager.Instance.Play(key, 플레이어 위치)` — 2D 엔트리는 위치를 무시한다.
- 사운드 추가 순서는 [data-table.md §6 사운드 키](data-table.md#사운드-키--사운드-동기화): `Sound` 시트에 행 작성(예: key `SFX_SkillCooldownReject`, audioType `UI` 또는 `SFX`, is3D `0`) → `Tools/Data/사운드 동기화` → 위 인스펙터 칸에 같은 키.
- 슬롯마다 다른 소리가 필요하면 이벤트 인자(슬롯)로 구독하는 쪽에서 나눈다.
