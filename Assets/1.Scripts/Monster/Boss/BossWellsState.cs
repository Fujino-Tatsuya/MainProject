using System;
using Unity.Netcode;

// Wells(웰즈) 상태. 정본 boss-fsm-detailed-spec.md §10 · §10.3(할 일 240 — 순간 클립).
//
// 🔴 기존 `WellsState`(5개)에서 **`Jump` 를 뺐다**. 애니메이터의 Jump 스테이트는 미사용이 된다.
//
// ⚠️ 이름이 `BossWellsState` 인 이유: 레거시 `Enemy/Boss/Wells&No.23/WellsState.cs` 의
//    `WellsState` 는 `[BlackboardEnum]`(Unity.Behavior) 이라 BT 결합이 있고, 전환 검증 전까지
//    공존하므로 이름을 갈랐다.
//
// 🔴 **이 상태는 Wells 가 스스로 복제하지 못한다.** Wells 는 스폰되지 않는 중첩 NetworkObject 라
//    자기 `NetworkVariable` 을 가질 수 없다(§10.1) → **23호의 NetworkObject 에 실어 복제**한다(<see cref="BossWellsCue"/>).
//
// 🔴 값 추가는 끝에만(로그·복제 바이트가 정수로 읽힌다).
public enum BossWellsState
{
    Idle = 0,   // 대기. 폭탄 쿨 만료 → Throw
    Throw,      // 투척 클립(hasExitTime 이라 끝나면 스스로 Idle 로 돌아온다 — 23호와 반대)
    Groggy,     // 폭탄 주기 정지. 23호가 그로기/브레이크를 풀면 Idle 로 복귀

    Dead,

    // ── 할 일 240 순간 클립(끝에 추가) ──────────────────────────────
    Rush,       // 돌진(평소·레이지 매 회)·점프 이륙(송전기 진입 포함) → yaho!  (한 번 재생 후 Idle)
    Windup,     // 23호 돌진 예고 시작(DashPrep 자세) → Charge!(Throw 상태)  (한 번 재생 후 Idle)
    ParryHit,   // 간파 당함(일반·대쉬·기믹 중 간파·릴레이/연쇄 간파 경직) → getownedV1  (한 번 재생 후 Idle)
    Fear,       // 제압 당해 가만히 있는 동안 → Fear (마지막 프레임 유지)
    GroggyEnd,  // 제압이 거의 끝날 때(남은 시간 ≤ 앞당김 초) → groggy(Groggy 상태). 제압이 끝나면 Idle
}

/// <summary>
/// 23호가 복제하는 웰즈 상태 + 순번(할 일 240). 같은 순간이 연달아 와도(레이지 돌진 3회 = Rush×3) 순번이 바뀌어
/// OnValueChanged 가 매번 떠 모든 피어가 클립을 다시 튼다. 새 RPC 없이 기존 복제 한 칸(<c>_wellsState</c>)을 그대로 쓴다.
/// </summary>
public struct BossWellsCue : INetworkSerializeByMemcpy, IEquatable<BossWellsCue>
{
    public BossWellsState State;
    public byte Seq;

    public BossWellsCue(BossWellsState state, byte seq)
    {
        State = state;
        Seq = seq;
    }

    public bool Equals(BossWellsCue other) => State == other.State && Seq == other.Seq;
    public override bool Equals(object obj) => obj is BossWellsCue o && Equals(o);
    public override int GetHashCode() => ((int)State << 8) | Seq;
}
