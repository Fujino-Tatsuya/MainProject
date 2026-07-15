# HitBox/HurtBox 판정 최적화 — Collider InstanceID 전역 레지스트리

> 상태: 제안(미적용) — 외부 리뷰에서 받은 최적화 팁 기록 (2026-07-09)
> 관련: `Assets/1.Scripts/Unit/Hurtbox.cs`, `Docs/tech/hurtbox-attack-resolution-decisions.md`

## 문제

HurtBox 내부의 `GetComponent`보다 더 비용이 큰 것은 **공격하는 쪽(HitBox/투사체)** 에서
충돌할 때마다 호출되는 `other.TryGetComponent<HurtBox>()`다.
칼·투사체가 적을 때릴 때마다 발생하므로 실전에서 1초에 수십~수백 번 호출되는 주범.

## 해결안

HurtBox를 컴포넌트 탐색으로 찾지 말고, **`Collider.GetInstanceID()`를 Key로 하는
전역 딕셔너리 매니저**를 두는 것이 정석.

### 1. 전역 캐싱 딕셔너리 (CombatSystem)

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class CombatSystem
{
    // 콜라이더 ID를 키로 하여 HurtBox를 바로 찾는 초고속 딕셔너리
    private static readonly Dictionary<int, HurtBox> _hurtBoxRegistry = new Dictionary<int, HurtBox>();

    public static void Register(int colliderId, HurtBox hurtBox) => _hurtBoxRegistry[colliderId] = hurtBox;
    public static void Unregister(int colliderId) => _hurtBoxRegistry.Remove(colliderId);

    public static bool TryGetHurtBox(int colliderId, out HurtBox hurtBox)
    {
        return _hurtBoxRegistry.TryGetValue(colliderId, out hurtBox);
    }
}
```

### 2. HurtBox — 켜질 때 등록, 꺼질 때 해제

```csharp
public class HurtBox : MonoBehaviour
{
    private Collider _collider;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        CombatSystem.Register(_collider.GetInstanceID(), this);
    }

    private void OnDestroy()
    {
        if (_collider != null) CombatSystem.Unregister(_collider.GetInstanceID());
    }
}
```

### 3. 공격 측(HitBox/투사체) — GetComponent 완전 제거

```csharp
// 공격용 툴이나 투사체 내부
private void OnTriggerEnter(Collider other)
{
    // GetComponent 없이 딕셔너리 look-up으로 0.00001초만에 HurtBox를 찾아옴
    if (CombatSystem.TryGetHurtBox(other.GetInstanceID(), out var hurtBox))
    {
        hurtBox.OnHurt(attackInfo);
    }
}
```

## 기대 효과

- 인터페이스의 구조적 유연함(`Unit`, `Bomb` 분리 가능)을 100% 유지
- 실시간 전투 연산 비용 최소화 — 가상 함수 호출 오버헤드만 남고 메모리/가비지 압박에서 해방
- 공격 판정 경로에서 `GetComponent`/`TryGetComponent` 호출 완전 제거
