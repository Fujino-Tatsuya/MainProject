using System.Collections.Generic;
using UnityEngine;
using static EffectCatalog;
using static EffectHitPoint;

/// <summary>
/// 피격 이펙트를 <b>로컬에서</b> 재생하는 공용 경로. 서버 판정과 무관한 순수 연출이다.
///
/// <b>왜 별도 클래스인가.</b> 이 코드가 필요한 두 클래스(<c>MonsterBase</c>·<c>Enemy</c>)는 형제라
/// 상속으로 공유할 수 없다 — <c>MonsterBase</c>는 코드 FSM, <c>Enemy</c>는 BT 결합이고 둘의 분리는
/// 의도된 것이다(<c>MonsterBase.cs</c> 상단 주석). 그렇다고 한쪽에 두면 다른 쪽이 그쪽에 의존하게
/// 되므로, 어느 편도 아닌 이 자리에 둔다. <see cref="EffectHitPoint"/>와 같은 층이다.
///
/// <b>RPC는 여기 없다.</b> RPC는 <c>NetworkBehaviour</c>에만 선언할 수 있으므로 각 클래스가 자기
/// 것을 갖고, 그 수신부가 이 함수를 부른다. 서버가 보내는 것은 공격자 위치 하나뿐이다 —
/// 이유는 각 호출부의 RPC 주석 참조.
/// </summary>
public static class HitVFXPlayback
{
    // hitVFXCollider 미배선 경고는 대상당 1회. 매 피격마다 찍으면 콘솔이 마비된다.
    private static readonly HashSet<int> Warned = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWarnings() => Warned.Clear();

    /// <summary>
    /// 타격점을 <b>수신측 로컬 콜라이더로</b> 계산해 이펙트를 재생한다.
    /// </summary>
    /// <param name="context">로그 대상 및 경고 중복 제거 키. 피격당한 유닛.</param>
    /// <param name="hitVFXCollider">피격자 표면. 비어 있으면 경고 1회 후 조용히 건너뛴다.</param>
    /// <param name="sourcePosition">공격자 위치(서버가 RPC로 넘겨준 값).</param>
    /// <remarks>
    /// <c>hitPointMode</c>는 인자로 받지 않는다 — 전 유닛 공통이라 <see cref="EffectManager"/>가
    /// 들고 있고 여기서 직접 읽는다. 반면 <paramref name="hitVFXType"/>은 유닛마다 다를 수 있어
    /// 호출자가 넘긴다(디버그 오버라이드는 <c>GetHitEffect</c> 안에서 적용된다).
    /// </remarks>
    public static void Play(
        Component context, Collider hitVFXCollider,
        HitVFXType hitVFXType, Vector3 sourcePosition)
    {
        if (EffectManager.Instance == null) return;

        // 콜라이더가 없으면 EffectHitPoint가 bounds/transform을 역참조하다 터진다. 프리팹 배선을
        // 잊은 몹이 맞을 때마다 예외를 뿜는 대신, 이펙트만 빼고 게임은 계속 굴린다.
        if (hitVFXCollider == null)
        {
            if (context != null && Warned.Add(context.GetInstanceID()))
            {
                Edit.LogWarning(
                    $"[HitVFX] {context.name}: hitVFXCollider가 비어 있어 피격 이펙트를 건너뜁니다. " +
                    "프리팹 인스펙터의 '피격 이펙트 제어'에 콜라이더를 연결하세요.", context);
            }
            return;
        }

        // ⚠️ 엔트리를 캐시하지 말 것 — 매번 물어봐야 디버그 HUD의 런타임 교체가 다음 피격부터 반영된다.
        EffectEntry entry = EffectManager.Instance.GetHitEffect(hitVFXType);
        if (entry == null) return;

        HitPointInfo hitPointInfo = new HitPointInfo(
            sourcePosition, hitVFXCollider, hitVFXCollider.transform);

        // ⚠️ 모드도 캐시 금지 — 매번 물어봐야 F2 런타임 교체가 다음 피격부터 반영된다.
        Pose pose = EffectHitPoint.Resolve(EffectManager.Instance.HitPointMode, hitPointInfo);
        EffectManager.Instance.Play(entry, pose.position, pose.rotation);
    }
}
