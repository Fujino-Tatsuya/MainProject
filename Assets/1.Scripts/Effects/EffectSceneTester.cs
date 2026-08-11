using System.Collections;
using UnityEngine;

/// <summary>
/// VFXScene에서 v1 완료 체크리스트 6케이스를 손으로 돌려보는 검증 하네스.
/// "읽었다"는 완료가 아니다 — 돌아가는 것으로 끝낸다.
///
/// 인스펙터 버튼(<c>EffectSceneTesterEditor</c>)이나 컨텍스트 메뉴에서 각 케이스를 실행한다.
/// 프로덕션 코드가 아니라 검증 도구다 — 씬에만 두고 프리팹에는 넣지 않는다.
/// </summary>
public class EffectSceneTester : MonoBehaviour
{
    [Header("위치")]
    [Tooltip("원샷 이펙트를 띄울 자리. 비우면 이 오브젝트")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("케이스 5·6에서 이펙트가 따라다닐 대상 (1.8 유닛 캡슐 프록시)")]
    [SerializeField] private Transform followTarget;

    [Header("엔트리")]
    [Tooltip("케이스 1 — 원샷 타격")]
    [SerializeField] private EffectEntry oneShot;

    [Tooltip("케이스 2·3 — 컴포지트 3막 + 사운드 파트")]
    [SerializeField] private EffectEntry composite;

    [Tooltip("케이스 4·5 — 루프 3분할 + 핸들")]
    [SerializeField] private EffectEntry loop;

    [Header("케이스 6 — 풀 재사용")]
    [SerializeField, Min(1)] private int burstCount = 20;
    [SerializeField, Min(0f)] private float burstInterval = 0.05f;

    [Header("케이스 7 — 타격점 계산")]
    [Tooltip("타격을 받을 콜라이더. 비우면 followTarget에서 찾는다")]
    [SerializeField] private Collider hitTarget;

    private EffectHandle _loopHandle;

    private Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;
    private Quaternion SpawnRotation => spawnPoint != null ? spawnPoint.rotation : transform.rotation;

    #region 케이스 1~3 — 원샷 / 컴포지트 / 사운드

    /// <summary>케이스 1 — 재생되고 duration 후 자동 반납되는가.</summary>
    [ContextMenu("케이스 1 — 원샷 타격")]
    public void Case1OneShot()
    {
        if (!Ready(oneShot, "oneShot")) return;
        EffectManager.Instance.Play(oneShot, SpawnPosition, SpawnRotation);
    }

    /// <summary>케이스 2·3 — 파트가 delay 간격으로 순차 발화하고 사운드가 같이 터지는가.</summary>
    [ContextMenu("케이스 2·3 — 컴포지트 3막 + 사운드")]
    public void Case2Composite()
    {
        if (!Ready(composite, "composite")) return;
        EffectManager.Instance.Play(composite, SpawnPosition, SpawnRotation);
    }

    #endregion

    #region 케이스 4·5 — 루프 / 부착 추종

    /// <summary>케이스 4 — 월드 고정 루프를 켠다. Intro → Loop.</summary>
    [ContextMenu("케이스 4 — 루프 시작 (월드 고정)")]
    public void Case4PlayLoop()
    {
        if (!Ready(loop, "loop")) return;
        if (_loopHandle.IsSet) Debug.Log("[EffectSceneTester] 이전 루프 핸들을 덮어쓴다.", this);

        _loopHandle = EffectManager.Instance.PlayLooping(loop, SpawnPosition, SpawnRotation);
    }

    /// <summary>케이스 5 — 추종 대상에 붙여 루프를 켠다.</summary>
    [ContextMenu("케이스 5 — 루프 시작 (대상 추종)")]
    public void Case5PlayAttachedLoop()
    {
        if (!Ready(loop, "loop")) return;

        if (followTarget == null)
        {
            Debug.LogWarning("[EffectSceneTester] followTarget이 비어 있다 (파괴됐거나 미할당).", this);
            return;
        }

        _loopHandle = EffectManager.Instance.PlayLooping(loop, followTarget);
    }

    /// <summary>케이스 4 — outroParts 재생 + Loop StopEmitting → outroDuration 후 반납.</summary>
    [ContextMenu("케이스 4·5 — Release()")]
    public void CaseReleaseLoop()
    {
        if (EffectManager.Instance == null) return;

        EffectManager.Instance.Release(_loopHandle);
        _loopHandle = EffectHandle.None;
    }

    /// <summary>
    /// 케이스 5 — 추종 대상을 파괴한다. 이펙트가 정상 회수되고 풀에 null이 남지 않아야 한다.
    /// (핸들은 일부러 버려 둔다 — stale 핸들에 Release()를 걸어도 조용한 no-op인지 같이 본다)
    /// </summary>
    [ContextMenu("케이스 5 — 추종 대상 파괴")]
    public void Case5DestroyTarget()
    {
        if (followTarget == null) return;

        Destroy(followTarget.gameObject);
        Debug.Log("[EffectSceneTester] 추종 대상을 파괴했다. 다음 프레임에 이펙트가 회수돼야 한다.", this);
    }

    #endregion

    #region 케이스 6 — 풀 재사용 / 히트스톱

    /// <summary>케이스 6 — 연속 발화 후에도 인스턴스 총수가 늘지 않는가.</summary>
    [ContextMenu("케이스 6 — 연속 발화")]
    public void Case6Burst()
    {
        if (!Ready(oneShot, "oneShot")) return;
        StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        for (int i = 0; i < burstCount; i++)
        {
            EffectManager.Instance.Play(oneShot, SpawnPosition, SpawnRotation);
            if (burstInterval > 0f) yield return new WaitForSeconds(burstInterval);
        }

        LogPoolStats();
    }

    /// <summary>케이스 6 — 추종 대상에 붙은 이펙트만 정지시킨다(히트스톱 대역).</summary>
    [ContextMenu("케이스 6 — 대상 이펙트 정지 (rate 0)")]
    public void Case6FreezeTarget() => SetTargetRate(0f);

    /// <summary>케이스 6 — 정지를 푼다.</summary>
    [ContextMenu("케이스 6 — 대상 이펙트 재개 (rate 1)")]
    public void Case6ResumeTarget() => SetTargetRate(1f);

    private void SetTargetRate(float rate)
    {
        if (EffectManager.Instance == null || followTarget == null) return;
        EffectManager.Instance.SetPlayRateForTarget(followTarget, rate);
    }

    /// <summary>풀 통계를 콘솔에 찍는다. 연속 발화 전후로 비교한다.</summary>
    [ContextMenu("풀 통계 출력")]
    public void LogPoolStats()
    {
        if (EffectManager.Instance == null) return;

        Debug.Log($"[EffectSceneTester] 활성 이펙트 {EffectManager.Instance.ActiveEffectCount}개 / " +
                  $"{PoolLine(oneShot)}{PoolLine(composite)}{PoolLine(loop)}", this);
    }

    private static string PoolLine(EffectEntry entry)
    {
        if (entry == null || entry.parts == null) return string.Empty;

        string line = string.Empty;
        for (int i = 0; i < entry.parts.Length; i++)
        {
            GameObject prefab = entry.parts[i]?.prefab;
            if (prefab == null) continue;

            line += $"[{prefab.name}] 총 {EffectManager.Instance.PoolCountAll(prefab)}개 " +
                    $"(대출 {EffectManager.Instance.PoolCountActive(prefab)}개) ";
        }
        return line;
    }

    #endregion

    #region 케이스 7 — 타격점 계산 (EffectHitPoint)

    /// <summary>
    /// spawnPoint를 "공격자", hitTarget을 "피격자"로 삼아 <see cref="EffectHitPoint"/>가 계산한
    /// 자리에 이펙트를 띄운다. spawnPoint를 캡슐 주위로 옮겨가며 눌러보면
    /// 점이 표면을 따라 미끄러지고 방향이 늘 공격자 쪽을 향하는지 확인할 수 있다.
    /// </summary>
    [ContextMenu("케이스 7 — 타격점에 재생")]
    public void Case7HitPoint()
    {
        if (!Ready(oneShot, "oneShot")) return;
        if (!TryBuildHitContext(out AttackHitContext context)) return;

        //Pose pose = EffectHitPoint.Resolve(context, followTarget);
        //EffectManager.Instance.Play(oneShot, pose.position, pose.rotation);
    }

    private bool TryBuildHitContext(out AttackHitContext context)
    {
        context = default;

        Collider target = ResolveHitTarget();
        if (target == null)
        {
            Debug.LogWarning("[EffectSceneTester] hitTarget이 비어 있고 followTarget에서도 콜라이더를 찾지 못했다.", this);
            return false;
        }

        Transform source = spawnPoint != null ? spawnPoint : transform;
        context = new AttackHitContext(source.position, source, target);
        return true;
    }

    private Collider ResolveHitTarget()
    {
        if (hitTarget != null) return hitTarget;
        return followTarget != null ? followTarget.GetComponentInChildren<Collider>() : null;
    }

    /// <summary>
    /// 플레이 모드 없이도 계산 결과를 눈으로 본다.
    /// 노랑 = 공격자(spawnPoint) · 하늘 = 계산된 타격점 · 초록 = 이펙트가 바라볼 방향(+Z).
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Collider target = ResolveHitTarget();
        if (target == null) return;

        Transform source = spawnPoint != null ? spawnPoint : transform;
        //Pose pose = EffectHitPoint.Resolve(new AttackHitContext(source.position, source, target), followTarget);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(source.position, 0.08f);
        //Gizmos.DrawLine(source.position, pose.position);

        Gizmos.color = Color.cyan;
        //Gizmos.DrawSphere(pose.position, 0.06f);

        Gizmos.color = Color.green;
        //Gizmos.DrawRay(pose.position, pose.rotation * Vector3.forward * 0.5f);
    }

    #endregion

    private bool Ready(EffectEntry entry, string fieldName)
    {
        if (EffectManager.Instance == null)
        {
            Debug.LogWarning("[EffectSceneTester] EffectManager가 씬에 없다. EffectManager 프리팹을 배치할 것.", this);
            return false;
        }

        if (entry == null)
        {
            Debug.LogWarning($"[EffectSceneTester] '{fieldName}' 엔트리가 비어 있다.", this);
            return false;
        }

        return true;
    }
}
