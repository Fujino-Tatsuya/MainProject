# 상호작용 정책

> 이 문서는 플레이어, 보스, 투사체, 오브젝트 사이에서 발생하는 모든 상호작용의 공통 책임 경계를 정의한다.
> 대표 예시는 보스의 `Grab`과 플레이어의 `Grabbed` 상태다.

## 핵심 용어

| 용어 | 의미 | 예시 |
|------|------|------|
| `Interaction` | 둘 이상의 게임 오브젝트 사이에서 발생하는 상호작용 사건 | `Grab`, `Damage`, `Knockback`, `Stun` |
| `Instigator` | 상호작용을 발생시키는 주체 | 플레이어 공격, 보스 패턴, 투사체, 함정 |
| `Receiver` | 상호작용의 결과를 받아 자신의 상태를 바꾸는 주체 | 공격당한 플레이어, 잡힌 플레이어, 맞은 보스 |
| `Target` | 판정 또는 선택 단계에서 후보로 잡힌 대상 | grab 사거리 안에 들어온 플레이어 |

`Target`은 아직 효과를 받은 주체가 아니다. 판정이 성공하고 상호작용이 수락되면 그 대상은 `Receiver`가 된다.

## 기본 원칙

1. `Instigator`는 상호작용을 시도하고, 판정하고, 요청한다.
2. `Receiver`는 상호작용을 수락 또는 거부하고, 자신의 상태 변화를 적용한다.
3. `Instigator`는 `Receiver`의 내부 상태를 직접 변경하지 않는다.
4. `Receiver`는 `Instigator`의 패턴 진행, 쿨타임, 애니메이션 상태를 직접 변경하지 않는다.
5. 서버 권한이 필요한 상호작용은 서버가 최종 판정하고 결과를 복제한다.

이 원칙의 목적은 같은 사건을 양쪽 FSM에서 중복 구현하지 않는 것이다. 양쪽에 상태가 동시에 존재할 수는 있지만, 각 상태는 자기 역할만 표현해야 한다.

## 책임 분리

### Instigator 책임

- 상호작용을 시작할 조건 판단
- 사거리, 방향, 충돌체, 타이밍 같은 판정 수행
- 후보 `Target` 선택
- `InteractionContext` 생성
- `Receiver`에게 상호작용 요청
- 자기 자신의 패턴 상태, 애니메이션, 쿨타임, 후속 행동 처리

### Receiver 책임

- 현재 상태 기준으로 상호작용 수락 가능 여부 판단
- 입력 잠금, 이동 잠금, 회전 잠금 같은 자기 제어권 처리
- 피격, 잡힘, 넉백, 기절 등 결과 상태 진입
- 자기 체력, 상태이상, 로컬 연출, 피격 반응 처리
- 상호작용 종료 시 자기 상태 복구

### 공통 데이터

상호작용 요청은 가능한 한 명시적인 컨텍스트로 전달한다.

```csharp
public readonly struct InteractionContext
{
    public InteractionType Type { get; }
    public GameObject Instigator { get; }
    public GameObject Receiver { get; }
    public Vector3 HitPoint { get; }
    public Vector3 Direction { get; }
    public float Duration { get; }
    public float DamageRatio { get; }
}
```

실제 필드는 상호작용 종류에 따라 줄이거나 늘릴 수 있다. 중요한 것은 `Instigator`와 `Receiver`가 암묵적으로 서로의 내부 구현을 만지지 않고, 같은 사건을 같은 데이터로 이해하는 것이다.

## Grab 기준 정책

`Grab`은 `Instigator` 쪽 액션이고, `Grabbed`는 `Receiver` 쪽 결과 상태다.

| 구분 | 위치 | 책임 |
|------|------|------|
| `BossGrabState` 또는 `GrabAttackState` | 보스 FSM | 잡기 패턴 시작, 사거리 판정, grab socket, 애니메이션 타이밍, 던지기 요청 |
| `PlayerGrabbedState` | 플레이어 FSM | 잡힘 수락 여부, 입력 기반 이동/공격/회전/인터럽트 잠금, 잡힘 애니메이션 재생 |

보스가 플레이어를 잡는 흐름은 다음처럼 본다.

1. 보스가 `Grab` 상태에 진입한다.
2. 보스가 사거리 안의 플레이어를 `Target`으로 찾는다.
3. 서버가 판정에 성공하면 `GrabInteractionContext`를 만든다.
4. 플레이어 쪽 `Receiver`가 grab을 수락할 수 있는지 판단한다.
5. 수락되면 플레이어는 `Grabbed` 상태에 진입한다.
6. 보스는 자기 애니메이션과 grab socket 기준으로 위치 고정, hold damage, throw/release 타이밍을 계속 진행한다.
7. throw 또는 release 타이밍에 보스가 종료 상호작용을 요청한다.
8. 플레이어는 `Grabbed`를 종료하고 입력 기반 행동 잠금을 해제한다.

보스 쪽 `Grab`은 "내가 잡기 패턴을 수행 중이다"를 의미한다. 플레이어 쪽 `Grabbed`는 "내가 잡힌 결과 상태에 있다"를 의미한다. 두 이름은 비슷하지만 같은 책임이 아니다.

## 네이밍 규칙

행동을 시작하는 쪽은 능동형 이름을 쓴다.

- `Grab`
- `Throw`
- `DashAttack`
- `ApplyDamage`
- `RequestInteraction`

결과를 받는 쪽은 수동형 또는 상태형 이름을 쓴다.

- `Grabbed`
- `Stunned`
- `KnockedBack`
- `Damaged`
- `ReceiveInteraction`

판정 후보에는 `Target`을 쓴다.

- `CurrentTarget`
- `FindTarget`
- `TrySelectTarget`
- `TargetDistance`

상호작용을 실제로 처리하는 API에서는 `Receiver`를 우선한다.

- `IInteractionReceiver`
- `TryReceiveInteraction`
- `ReceiveGrab`
- `CanReceiveInteraction`

## 중복 구현 금지선

다음 구현은 피한다.

- 보스 grab 코드가 플레이어 입력 컴포넌트를 직접 끄는 것
- 보스 grab 코드가 플레이어 FSM을 강제로 특정 내부 상태로 밀어 넣는 것
- 플레이어 grabbed 코드가 보스 쿨타임이나 보스 FSM 전이를 직접 제어하는 것
- 같은 피해량, 지속시간, 해제 조건을 보스와 플레이어 양쪽에 따로 하드코딩하는 것
- `Target`이라는 이름으로 이미 효과를 받은 상태까지 표현하는 것

대신 상호작용 요청과 수락 API를 통해 경계를 둔다.

```csharp
if (target.TryGetComponent(out IInteractionReceiver receiver))
{
    receiver.TryReceiveInteraction(context);
}
```

## 수락과 거부

`Receiver`는 항상 상호작용을 거부할 수 있어야 한다. 예시는 다음과 같다.

- 이미 사망함
- 무적 상태
- 이미 더 높은 우선순위의 잡힘 또는 컷신 상태
- 네트워크 소유권 또는 서버 권한 조건 불일치
- 같은 상호작용의 중복 요청

거부되었을 때 `Instigator`는 자기 패턴 실패, 후딜, 쿨타임, 재시도 여부를 스스로 결정한다.

## 우선순위와 충돌

여러 상호작용이 동시에 들어올 수 있으므로, `Receiver`는 상태 우선순위를 가져야 한다.

예시 우선순위:

1. `Dead`
2. `Grabbed` 또는 컷신성 강제 제어
3. `Airborne`
4. `Stun`
5. `Knockback`
6. `Slow`, `Root`, `Silence` 같은 일반 상태이상

우선순위는 최종 구현에서 별도 정책으로 확정한다. 여기서는 "상호작용 수락 여부는 Receiver가 판단한다"는 원칙만 확정한다.

## 문서화 규칙

새 상호작용을 추가할 때는 다음을 문서 또는 코드 주석으로 남긴다.

- `Instigator`가 누구인가
- `Receiver`가 누구인가
- `Target` 판정 기준은 무엇인가
- 서버 권한인지 클라이언트 예측인지
- `Receiver`가 거부할 수 있는 조건은 무엇인가
- 종료 조건은 누가 요청하고 누가 적용하는가

## 현재 결정

- 공통 용어는 `Instigator`, `Receiver`, `Target`을 사용한다.
- `Target`은 판정 후보, `Receiver`는 결과 수신자로 구분한다.
- `Grab`과 `Grabbed`는 중복 기능이 아니라 능동 액션과 수동 결과 상태로 분리한다.
- 보스는 grab을 직접 적용하는 것이 아니라 플레이어 receiver에게 grab 수신을 요청한다.
- 플레이어는 grabbed 상태에서 입력 기반 이동, 공격, 회전, 인터럽트 잠금과 잡힘 애니메이션만 책임진다.
