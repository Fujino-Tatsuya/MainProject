using UnityEngine;

/// <summary>
/// 몬스터가 대상을 <b>인지할 수 있는 높이</b> 판정.
///
/// 문제 — 인지는 `OverlapSphere(detectionRadius)` 순수 구체였다. 높이를 보지 않으니 위층 몹이
/// 아래를 때리고 아래층 몹이 위를 때렸다. 전투가 층으로 갈리지 않고 사방에서 얻어맞는다
/// (2026-09-08 팀장 관찰: "너무 많은 곳에서 위에서도 아래에서도 공격한다").
///
/// 해법 — 수직 차이에 상한을 둔다. 경사·계단을 올라오면 Δy 가 줄어 <b>자연히</b> 인지되고,
/// 아래에 있으면 Δy 가 커서 모른다. 별도의 "층" 개념이나 트리거 볼륨을 만들지 않아도 된다.
///
/// 🔴 <b>인지(획득) 단계에만 쓴다.</b> 이미 물고 있는 대상은 <c>leashRadius</c> 로만 풀린다
///    (팀장 확정) — 추격 중 플레이어가 단차 하나 내려갔다고 증발하면 몹이 오가라 한다.
///
/// ⚠️ 한계는 명시해 둔다: 높이만 보므로 <b>같은 층의 벽 뒤</b>는 여전히 인지한다.
///    NavMesh 경로 검사는 마리마다 <c>CalculatePath</c> 비용이 붙어 기각했다.
/// </summary>
public static class MonsterPerceptionPolicy
{
    /// <summary>
    /// 높이 차가 인지 가능한 범위인가.
    /// </summary>
    /// <param name="selfY">몬스터의 y.</param>
    /// <param name="targetY">대상의 y.</param>
    /// <param name="tolerance">
    /// 허용 수직 차(m). <b>0 이하면 제한을 끈다</b> — 예전 동작(높이 무시)으로 되돌리는 값이다.
    /// </param>
    public static bool WithinHeight(float selfY, float targetY, float tolerance)
    {
        if (tolerance <= 0f) return true;
        return Mathf.Abs(targetY - selfY) <= tolerance;
    }
}
