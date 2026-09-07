# 요청 — 플레이어의 `GrabController` 구체 타입 의존 제거 (경석 → 은희)

> 작성 2026-08-10 · 요청자 경석(몬스터 전담) · 대상 은희(플레이어·코어)
> 상태: **요청 전** — 팀장이 직접 전달 예정

## 한 줄

플레이어 구속 상태가 보스의 **레거시 클래스 `GrabController` 를 구체 타입으로** 붙잡고 있어서,
보스 재작성으로 죽은 레거시 폴더(`Assets/1.Scripts/Enemy/Boss/`)를 지울 수 없습니다.
**`Transform` 하나만 얻는 의존**이라 인터페이스로 끊어 주시면 됩니다.

## 정확한 지점

[`Assets/1.Scripts/Player/PlayerStateController.cs:760`](../../Assets/1.Scripts/Player/PlayerStateController.cs#L760)
— `PlayerRestrainedState.Enter()`

```csharp
if (mode == RestraintMode.Carry)
{
    GrabController grabController = instigator != null
        ? instigator.GetComponentInChildren<GrabController>()
        : null;
    followTarget = grabController != null ? grabController.GrabSocket : null;
}
```

플레이어가 `GrabController` 에서 실제로 쓰는 것은 **`public Transform GrabSocket` 하나뿐**입니다
(`GrabController.cs:232`). 나머지(`Detect`·`Throw`·`SetGrabFigures`·BT 참조)는 플레이어가 건드리지 않습니다.

## 왜 지금 문제가 되나

- 보스 FSM 재작성이 끝나면서 `Enemy/Boss/` 의 레거시 스택은 전부 대체됐습니다.
  실측상 `GrabController`·`JumpController`·`ChargeController` 는 **어느 프리팹·씬에도 붙어 있지 않습니다(부착 0곳)**.
- 그런데 `GrabController` 만은 **소스를 지울 수 없습니다** — 위 코드가 컴파일 의존을 만들기 때문입니다.
- 즉 아무도 쓰지 않는 클래스가 폴더 정리를 막고 있는 상태입니다.

## 요청 내용

`GrabSocket` 을 **인터페이스로 노출**해 주세요. 이름·위치는 은희 님 판단에 맡깁니다. 예시:

```csharp
public interface IGrabSocketProvider
{
    Transform GrabSocket { get; }
}
```

그리고 위 `Enter()` 를 이렇게:

```csharp
if (mode == RestraintMode.Carry)
{
    var provider = instigator != null
        ? instigator.GetComponentInChildren<IGrabSocketProvider>()
        : null;
    followTarget = provider?.GrabSocket;   // null 허용은 그대로
}
```

제공자 쪽(보스 소켓)은 **제가 붙입니다** — 인터페이스만 정해 주시면 신형 보스 프리팹에 구현체를 답니다.

## 🔴 깨지면 안 되는 것

**`followTarget == null` 널 허용을 유지해 주세요.**
지금 신형 보스에는 잡기 소켓이 아직 없어서, 널일 때 "위치 추종만 건너뛰고 물리 위임·입력 차단은 유지"되는
성질로 **"제자리에 붙잡힘"이 성립 중**입니다. 여기가 깨지면 잡기가 통째로 무력화됩니다.
(이 계약은 `CONTEXT.md` 의 2026-08-07 인수인계에 이미 명시돼 있습니다.)

## 참고 — 예전에 폐기됐던 건과의 관계

2026-08-07 작업에서 원 요청이던 캐리 소켓 일반화(`ICarrySocketProvider`)는 **폐기**됐고,
대신 `RestraintMode{Carry, Push}` 로 정리되면서 `GrabController` 는 **무수정**으로 남기는 것이
계약이었습니다. 그 판단은 당시 맞았습니다 — 그때는 레거시 폴더를 지울 계획이 없었습니다.

지금 다시 올리는 이유는 **범위가 달라서**입니다. 그때는 "보스가 소켓을 일반화해서 넘겨 달라"는
기능 요청이었고, 지금은 **"죽은 클래스 하나를 지우기 위한 컴파일 의존 제거"** 입니다.
동작 변경은 0이고, 바뀌는 줄은 위 4줄뿐입니다.

## 완료 판정

1. `PlayerStateController` 가 `GrabController` 를 이름으로 참조하지 않는다
2. `Restrained.Carry` 동작이 그대로다 (소켓 있으면 추종, 없으면 제자리 — 둘 다)
3. 그 뒤 제가 `Assets/1.Scripts/Enemy/Boss/` 정리를 이어받습니다

## 이 요청에 포함되지 않는 것

- `ChargeController`·`JumpController` — 플레이어와 무관합니다. BT 액션·조건이 참조하므로
  `8.BehaviorTreeGraph` 폴더 삭제와 함께 **제가** 처리합니다.
- 보스 쪽 잡기 소켓 실제 배치 — 제 몫입니다.
