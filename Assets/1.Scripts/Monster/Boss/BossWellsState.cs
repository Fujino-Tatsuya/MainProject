// Wells(웰즈) 상태 — **4개**. 정본 boss-fsm-detailed-spec.md §10.
//
// 🔴 기존 `WellsState`(5개)에서 **`Jump` 를 뺐다**. 애니메이터의 Jump 스테이트는 미사용이 된다.
//
// ⚠️ 이름이 `BossWellsState` 인 이유: 레거시 `Enemy/Boss/Wells&No.23/WellsState.cs` 의
//    `WellsState` 는 `[BlackboardEnum]`(Unity.Behavior) 이라 BT 결합이 있고, 전환 검증 전까지
//    공존하므로 이름을 갈랐다.
//
// 🔴 **이 상태는 Wells 가 스스로 복제하지 못한다.** Wells 는 스폰되지 않는 중첩 NetworkObject 라
//    자기 `NetworkVariable` 을 가질 수 없다(§10.1) → **23호의 NetworkObject 에 실어 복제**한다.
public enum BossWellsState
{
    Idle = 0,   // 대기. 폭탄 쿨 만료 → Throw
    Throw,      // 투척 클립(hasExitTime 이라 끝나면 스스로 Idle 로 돌아온다 — 23호와 반대)
    Groggy,     // 폭탄 주기 정지. 23호가 그로기/브레이크를 풀면 Idle 로 복귀
    Dead,
}
