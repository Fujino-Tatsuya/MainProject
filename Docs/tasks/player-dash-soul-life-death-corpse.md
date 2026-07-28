# 기능 요청서 — Player Soul · LifeCount · 최종 사망 · 시체

> 목적: Player Dash/Fall 작업과 병렬 구현  
> 기준 계약: [`PLAN.md`](../../PLAN.md)  
> 최소 기반: `feature/FallView` @ `f3c390a`의 Unit 피해/사망 API, PlayerGroundingSensor, Float Camera 코어  
> 우선순위: Soul/Life/최종 사망은 필수, 래그돌은 최하위 아이디어이며 구현 금지

## 1. 요청 목표

Player가 사망했을 때 남은 부활 횟수에 따라 Soul 또는 PermanentDead로 전환한다.
Soul은 같은 Player Root와 네트워크 소유권을 유지하면서 이동만 가능해야 한다.
최종 전투 사망은 시체를 남기고, 최종 추락 사망은 시체를 남기지 않는다.

## 2. 책임 범위

이 작업이 담당한다.

- Player 생명주기 상태
- 임시 멀티플레이 LifeCount
- Soul Visual과 Soul 이동/충돌 전환
- Alive/Soul/Dead/PermanentDead 입력·UI 정책
- 최종 전투 사망 시체
- 최종 로컬 사망 관전 전환
- Fall 작업의 `FallDeathContext` 소비

이 작업이 담당하지 않는다.

- 추락 Threshold 감지
- 추락 피해 계산
- 생존 안전지점 복귀
- Float Camera 코어 구현
- 대시 이동·충전·RTT 검증
- 래그돌

## 3. 공용 상태

권장 생명주기:

```text
Alive
→ DeadPresentation
→ Soul
→ Alive

Alive
→ DeadPresentation
→ PermanentDead
```

### Alive

- 일반 이동·공격·스킬·대시 가능 여부를 각 시스템이 판단.
- 단일 Hurtbox 활성.
- CombatUI 표시.

### DeadPresentation

- 사망 직후 특정 연출시간 동안 유지.
- 모든 게임플레이 입력 차단.
- Hurtbox Collider 비활성.
- CombatUI 숨김.
- 임시 상태/무적 Token/Blink 제거.
- 전투 사망은 원위치, 추락 사망은 Float Camera 상태에서 계속 낙하 가능.

### Soul

- 이동만 허용.
- 공격·기본공격·스킬·대시 입력 차단.
- CombatUI 다시 표시.
- HP/Shield는 0 표시.
- 스킬 쿨다운과 Dash 충전 표시는 계속 갱신하되 슬롯은 사용 불가 색상.
- Player Root가 네트워크·이동·상태를 계속 담당.
- Hurtbox 비활성으로 전투 Target에서 제외.

### PermanentDead

- CombatUI 숨김.
- 모든 게임플레이 입력 차단.
- `CameraTargetSwitcher`의 `[`/`]` 관전 입력 활성화.
- Alive/Soul Player만 관전 후보.
- 후보가 있으면 다음 대상 자동 선택.
- 후보가 없으면 마지막 Float/Camera 위치 유지.

## 4. LifeCount

v1은 `Temp_MultiGameRule`을 만든다.

- 서버가 Client별 LifeCount를 관리.
- 기본값: 실제 부활 가능 횟수 3회.
- 사망 시 `lifeCount > 0`이면 Soul 진입 가능.
- LifeCount는 Soul 진입 때가 아니라 실제 Alive 부활 성공 때 1 감소.
- `lifeCount == 0`인 사망은 PermanentDead.
- 클라이언트가 값을 변경할 수 없음.
- UI 표시는 Debug 전용. 정식 UI는 후속.
- 추후 `PlayerGameRuleData` SO의 값으로 교체할 수 있게 초기값 공급원을 분리.

연결 해제/재접속은 현재 InGame 재접속 미지원 정책을 따른다.

## 5. Soul Visual

- `CharacterDefinition` 또는 후속 Player 조립 데이터에 `SoulVisualPrefab` 참조.
- Alive Player 생성 때 Soul Visual을 미리 생성해 비활성화.
- NetworkObject가 아닌 Player Root 자식.
- Soul 전환 때 Alive Visual을 끄고 Soul Visual을 켠다.
- Soul Prefab은 상체 Mesh 중심이고 하체는 유령 효과로 표현 예정.
- Animator/VFX Socket 계약 없음.
- Soul 전용 능력 데이터 없음.
- 별도 Soul NetworkObject를 스폰하지 않음.
- Visual 누락 시 상태는 정상 유지하고 `[SoulAlert]` Warning.

## 6. Soul 물리·충돌

- 기존 Player Rigidbody와 CapsuleCollider를 재사용.
- Root Layer를 `Soul`로 전환.
- `PlayerGroundingSensor`를 Soul 지면 마스크로 전환.
- Soul은 일반 Ground/Wall/Env와 미리 배치된 공용 `SoulPlane`에만 충돌.
- Alive Player, Enemy, Projectile, Hazard와 물리 충돌하지 않음.
- 전투 Target/Hurtbox 검색에서 제외.
- Soul은 낭떠러지 위를 항상 이동할 수 있다.
- 맵 전체의 SoulPlane은 사전 배치하며 Runtime 생성하지 않는다.
- 중력은 유지하고 Visual만 부유 표현 가능.
- 이동속도는 향후 `PlayerGameRuleData.soulSpeed`, v1은 `Temp_MultiGameRule` 또는 직렬화 기본값.
- 상태이상 MoveSpeed Modifier를 적용하지 않는 고정 Soul 속도.

## 7. 사망 원인별 위치

### 일반 전투 사망

- DeadPresentation과 Soul 시작 위치는 사망 위치.
- 부활 가능하면 Soul로 전환.
- LifeCount 0이면 같은 위치에 시체를 남기고 PermanentDead.

### 추락 사망

Fall 작업이 다음 Context를 제공한다.

```text
FallDeathContext
- playerNetworkObjectId
- deathWorldPosition
- fallPoint
- soulStartXZ
- sourceSceneHandle
```

- `soulStartXZ`는 추락 피해가 실제로 사망시킨 순간의 X/Z.
- 부활 가능하면 해당 X/Z에서 SoulPlane으로 Y를 투영해 Soul 시작.
- SoulPlane 투영 실패 시 명시적 Fallback과 `[SoulAlert]`.
- LifeCount 0이면 시체를 남기지 않는다.
- 최종 추락 사망 Player Root Visual은 영구 비활성화한다.

## 8. Soul → Alive 부활

상세 부활 조건과 특수 Soul Rule은 후속 확장점으로 남긴다.
v1 연결 계약:

- 서버만 부활 승인.
- 실제 Alive 전환 성공 때 LifeCount 1 감소.
- Alive Visual 복구, Soul Visual 비활성.
- Root Layer/Collision/Grounding을 Alive 규칙으로 복구.
- Hurtbox는 다른 보호 Token을 확인한 뒤 활성화.
- 생명주기와 무관한 쿨다운은 유지.
- Dash 충전은 `1 / MaxCharge`로 강제 설정.
- Dash 다음 충전 진행도는 0부터 시작.
- 부활 보호시간·Blink가 필요하면 공용 보호 Token API 사용.

## 9. 시체

별도 `PlayerCorpseController`로 분리한다.

### 생성 조건

- `lifeCount == 0`인 일반 전투 사망만 시체 유지.
- 최종 추락 사망은 시체 없음.
- v1 래그돌 없음.
- 시체 표현과 전용 Collider/Rigidbody는 Player 조립 시 미리 생성해 비활성화하고, 조건을 만족할 때 활성화한다.

### 권한과 네트워크

- 기존 Player NetworkObject를 유지한다.
- 서버만 시체 Rigidbody와 Collider를 시뮬레이션.
- Client는 Visual만 표시.
- NetworkTransform은 Position XYZ만 동기화.
- Rotation/Scale 동기화 없음.
- Interpolation 활성.
- 시체 전용 Collider는 Prefab 직렬화, 기본 BoxCollider.
- Rigidbody Rotation Freeze.

### 이동 플랫폼

- 시체 위치는 이동 플랫폼 Delta를 따라간다.
- 플랫폼 회전은 반영하지 않는다.
- 플랫폼이 소멸하면 서버 Rigidbody 중력을 받아 낙하한다.
- Ground/Env에 착지할 수 있다.
- Corpse Layer와 충돌 매트릭스를 사용한다.
- Scene의 FallBoundary Y 이하로 내려가면 시체 Visual을 영구 제거.
- 시체 낙하는 Player 추락 피해·Soul 전환을 재발생시키지 않는다.

## 10. UI와 Camera

| 상태 | CombatUI | 관전 입력 |
|---|---|---|
| Alive | 표시 | 비활성 |
| DeadPresentation | 숨김 | 비활성 |
| Soul | 표시 | 비활성 |
| PermanentDead | 숨김 | `[`/`]` 활성 |

- Soul의 스킬/Dash 슬롯은 Blocked 색상.
- 쿨다운·충전 Fill은 계속 갱신.
- 추락 사망 DeadPresentation은 Float Camera 유지.
- Soul 전환 시 정상 Follow Camera로 복귀해 같은 Player Root를 추적.
- PermanentDead는 다음 Alive/Soul Player로 자동 전환.
- 비활성화된 Player GameObject는 관전 후보에서 제거.
- `PlayerCameraTarget` 같은 신규 Marker는 만들지 않고 LifeState/활성 상태를 사용.

## 11. Hurtbox와 전투 제외

- Soul/Dead/PermanentDead는 유일한 Hurtbox Collider 비활성.
- `SetActive(false)`로 전체 Root를 끄는 대신 필요한 Visual/Combat Collider를 분리 제어.
- 기존 직접 Unit 공격 경로가 Soul을 맞히면 `[HurtboxAlert][SoulAlert]` Warning.
- 전체 전투 경로의 Hurtbox 통일은 이 브랜치 범위 밖.
- 추락용 SoulPlane은 Soul Layer에만 충돌하고 피해를 주지 않는다.

## 12. 권장 파일

실제 프로젝트 구조에 맞춰 이름은 조정 가능하다.

- `Assets/1.Scripts/Player/Life/PlayerLifeState.cs`
- `Assets/1.Scripts/Player/Life/PlayerLifeCycleController.cs`
- `Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs`
- `Assets/1.Scripts/Player/Soul/PlayerSoulController.cs`
- `Assets/1.Scripts/Player/Corpse/PlayerCorpseController.cs`
- Soul/Corpse Prefab 및 `.meta`
- `CharacterDefinition.cs`
- `Player.cs`
- `PlayerMovement.cs`
- `PlayerStateController.cs`
- `PlayerGroundingSensor.cs`
- `CameraTargetSwitcher.cs`
- CombatHUD 관련 파일
- Player Prefab

## 13. 예상 충돌 파일

Dash/Fall 브랜치도 다음 파일을 만질 수 있다.

- `Player.cs`
- `PlayerMovement.cs`
- `PlayerStateController.cs`
- `CharacterDefinition.cs`
- `CameraTargetSwitcher.cs`
- Player Prefab
- CombatHUD Prefab

브랜치는 분리하되 병합 시 다음 계약을 우선한다.

1. 이동 Transform 쓰기는 Owner만.
2. LifeCount·사망·부활 승인과 Corpse 물리는 Server만.
3. Fall은 `FallDeathContext`를 발행하고 생명주기 내부를 직접 조작하지 않는다.
4. Soul은 Dash 내부 장부를 직접 수정하지 않고 공개 Reset API를 호출한다.
5. Visual 상태가 게임플레이 판정을 결정하지 않는다.

## 14. 구현 순서

1. `PlayerLifeState`와 생명주기 Controller
2. `Temp_MultiGameRule` 서버 LifeCount
3. DeadPresentation/Soul/PermanentDead 전환
4. Soul Visual·Layer·이동
5. CombatUI 정책
6. Camera 관전 정책
7. Corpse Controller와 플랫폼/낙하
8. FallDeathContext 연결
9. 빌드와 통합 체크리스트

## 15. 완료 조건

- 컴파일 오류 0.
- 서버만 LifeCount 감소·부활·PermanentDead 확정.
- 일반 사망: LifeCount 있으면 Soul, 0이면 시체 + PermanentDead.
- 추락 사망: LifeCount 있으면 SoulPlane Soul, 0이면 시체 없이 PermanentDead.
- Soul은 이동만 가능하고 전투/물리 Target에서 제외.
- Soul CombatUI 표시, Dead/PermanentDead 숨김.
- 부활 때 Dash 충전 1개와 회복 타이머 초기화 API 호출.
- 최종 전투 시체가 이동 플랫폼을 따라가고 플랫폼 소멸 후 낙하.
- Corpse가 FallBoundary 아래로 내려가면 영구 제거.
- PermanentDead 로컬 플레이어가 `[`/`]`로 Alive/Soul 대상 관전.
- 래그돌 없음.

## 16. Agent 완료 보고 형식

- Branch와 최종 Commit SHA
- 변경/신규 파일
- 공개 API와 `FallDeathContext` 소비 지점
- 서버/오너 권한 경계
- 빌드·테스트 결과
- Prefab/Layer/Collision Matrix에서 사용자가 직접 확인할 항목
- 병합 충돌 예상 파일
- 남은 통합 작업
