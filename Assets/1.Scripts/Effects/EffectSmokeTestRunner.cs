#if UNITY_EDITOR
using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// Effect System v1 배관 스모크 테스트. <c>Tools/Effects/스모크 테스트</c>로 실행한다.
///
/// 눈으로 봐야 하는 항목(연출이 자연스러운가, 뚝 끊기지 않는가)은 못 본다 — 그건
/// <see cref="EffectSceneTester"/>로 VFXScene에서 직접 확인한다.
/// 여기서는 코드로 판정 가능한 것만 본다: 자동 반납 · delay 발화 · 핸들 세대 · 대상 소멸 회수 ·
/// 풀 재사용 · 감속 전달. 드라이버를 새로 추가한 뒤 이걸 다시 돌리면 회귀를 잡을 수 있다.
/// </summary>
public class EffectSmokeTestRunner : MonoBehaviour
{
    private readonly StringBuilder _report = new StringBuilder();
    private int _passed;
    private int _failed;

    public static void Launch()
    {
        var go = new GameObject("[EffectSmokeTest]");
        go.AddComponent<EffectSmokeTestRunner>();
    }

    private float _maxDeltaTimeBackup;

    private IEnumerator Start()
    {
        // 포커스를 잃은 에디터는 프레임이 수백 ms까지 늘어난다. 그러면 "Release() 직후"처럼
        // 한 프레임 뒤를 보는 검사가 큰 dt 한 방에 수명을 다 태워버려 가짜 실패가 난다.
        // 시뮬레이션이 한 프레임에 진행할 수 있는 시간을 묶어 프레임 페이싱과 무관하게 만든다.
        _maxDeltaTimeBackup = Time.maximumDeltaTime;
        Time.maximumDeltaTime = 0.05f;

        yield return null;   // 매니저의 Awake가 먼저 돌게 한다

        EffectManager manager = EffectManager.Instance;
        if (manager == null)
        {
            Debug.LogError("[SmokeTest] EffectManager.Instance가 없다. 중단.");
            Destroy(gameObject);
            yield break;
        }

        EffectEntry spark = manager.Catalog != null ? manager.Catalog.HitSpark : null;
        EffectEntry composite = manager.Catalog != null ? manager.Catalog.HitBlunt : null;

        Check("카탈로그에 HitSpark가 있다", spark != null);
        Check("카탈로그에 HitBlunt가 있다", composite != null);

        if (spark == null || composite == null) { Finish(); yield break; }

        GameObject sparkPrefab = spark.parts[0].prefab;
        GameObject bluntPrefab = composite.parts[0].prefab;

        Check("프리워밍이 인스턴스를 미리 만들었다", manager.PoolCountAll(sparkPrefab) >= spark.prewarmCount);

        // ── 1. 원샷: 재생 → duration 후 자동 반납 ──
        manager.Play(spark, Vector3.zero, Quaternion.identity);
        yield return null;
        Check("원샷 재생 직후 인스턴스가 대출된다", manager.PoolCountActive(sparkPrefab) == 1);

        yield return new WaitForSeconds(spark.ResolvedDuration + 0.3f);
        Check("duration 후 자동 반납된다", manager.PoolCountActive(sparkPrefab) == 0);
        Check("반납 후 활성 이펙트가 0이다", manager.ActiveEffectCount == 0);

        // ── 2. 컴포지트: delay가 지나야 두 번째 파트가 뜬다 ──
        // 지연 파트의 프리팹은 composite에서 직접 꺼낸다. spark 엔트리와 같은 프리팹을 쓴다고
        // 가정하면 카탈로그를 갈아끼우는 순간 엉뚱한 풀을 세게 된다.
        GameObject delayedPrefab = composite.parts.Length > 1 ? composite.parts[1].prefab : null;

        int delayedBefore = manager.PoolCountActive(delayedPrefab);
        manager.Play(composite, Vector3.zero, Quaternion.identity);
        yield return null;
        Check("컴포지트 첫 파트가 즉시 발화한다", manager.PoolCountActive(bluntPrefab) == 1);
        Check("delay가 있는 파트는 아직 안 떴다", manager.PoolCountActive(delayedPrefab) == delayedBefore);

        yield return new WaitForSeconds(composite.parts[1].delay + 0.15f);
        Check($"delay 경과 후 두 번째 파트가 발화한다 " +
              $"(기대 {delayedBefore + 1}, 실제 {manager.PoolCountActive(delayedPrefab)}, 프리팹 {delayedPrefab?.name})",
            manager.PoolCountActive(delayedPrefab) == delayedBefore + 1);

        yield return new WaitForSeconds(composite.ResolvedDuration + 0.3f);
        Check("컴포지트 전체가 한 번에 반납된다", manager.ActiveEffectCount == 0);

        // ── 3. 루프 핸들: Release 전에는 안 죽고, Release 후 outroDuration에 반납 ──
        EffectHandle handle = manager.PlayLooping(spark, Vector3.zero, Quaternion.identity);
        Check("PlayLooping이 핸들을 발급한다", handle.IsSet);

        yield return new WaitForSeconds(spark.ResolvedDuration + 0.3f);
        Check("루프는 duration이 지나도 살아 있다", manager.ActiveEffectCount == 1);

        float outro = spark.ResolvedOutroDuration;
        manager.Release(handle);
        yield return null;

        // outro가 0인 엔트리는 Release 즉시 반납되는 게 정상이다 — 2단계 해제를 검사할 대상이 아니다.
        if (outro > 0f)
            Check($"Release() 직후에는 아직 반납되지 않는다 — 2단계 해제 (outro {outro:F2}s)",
                manager.ActiveEffectCount == 1);
        else
            Check($"outroDuration이 0이라 Release가 즉시 반납한다 ('{spark.name}' — 2단계 해제 검사 생략)",
                manager.ActiveEffectCount == 0);

        yield return new WaitForSeconds(spark.ResolvedOutroDuration + 0.3f);
        Check("outroDuration 후 반납된다", manager.ActiveEffectCount == 0);

        // ── 4. stale 핸들 방어 ──
        manager.Release(handle);          // 이미 반납된 핸들
        manager.Release(EffectHandle.None);
        Check("stale/None 핸들 Release가 조용한 no-op이다", manager.ActiveEffectCount == 0);

        // ── 5. 추종 대상 소멸 → 정상 회수, 풀에 null이 남지 않음 ──
        var target = new GameObject("[SmokeTarget]");
        target.transform.position = new Vector3(5f, 0f, 0f);

        EffectHandle attached = manager.PlayLooping(spark, target.transform);
        yield return null;
        Check("부착 루프가 대상 위치로 간다",
            Vector3.Distance(FirstActiveInstancePosition(sparkPrefab), target.transform.position) < 0.01f);

        Destroy(target);
        yield return null;
        yield return null;
        Check("대상이 파괴되면 이펙트가 회수된다", manager.ActiveEffectCount == 0);
        Check("대상 파괴 후 풀 대출이 0이다", manager.PoolCountActive(sparkPrefab) == 0);

        manager.Release(attached);   // 이미 회수된 핸들 — 던지지 않아야 한다

        // ── 6. 풀 재사용: 연속 발화해도 인스턴스 총수가 계속 늘지 않는다 ──
        int before = manager.PoolCountAll(sparkPrefab);
        for (int i = 0; i < 20; i++)
        {
            manager.Play(spark, Vector3.zero, Quaternion.identity);
            yield return new WaitForSeconds(0.03f);
        }
        int peak = manager.PoolCountAll(sparkPrefab);

        yield return new WaitForSeconds(spark.ResolvedDuration + 0.5f);
        Check("연속 발화 후 전부 반납된다", manager.PoolCountActive(sparkPrefab) == 0);

        for (int i = 0; i < 20; i++)
        {
            manager.Play(spark, Vector3.zero, Quaternion.identity);
            yield return new WaitForSeconds(0.03f);
        }
        Check($"2회차 발화는 새 인스턴스를 만들지 않는다 (1회차 {before}→{peak}, 2회차 {manager.PoolCountAll(sparkPrefab)})",
            manager.PoolCountAll(sparkPrefab) <= peak);

        yield return new WaitForSeconds(spark.ResolvedDuration + 0.5f);

        // ── 7. 히트스톱: 대상에 붙은 이펙트만 감속 ──
        var freezeTarget = new GameObject("[SmokeFreezeTarget]");
        manager.PlayLooping(spark, freezeTarget.transform);
        yield return null;

        manager.SetPlayRateForTarget(freezeTarget.transform, 0f);
        yield return null;
        Check("SetPlayRateForTarget(0)이 simulationSpeed를 0으로 만든다",
            Mathf.Approximately(FirstActiveSimulationSpeed(sparkPrefab), 0f));

        manager.SetPlayRateForTarget(freezeTarget.transform, 1f);
        yield return null;
        Check("rate 1로 되돌아온다", Mathf.Approximately(FirstActiveSimulationSpeed(sparkPrefab), 1f));

        Destroy(freezeTarget);
        yield return null;
        yield return null;

        Check("정리 후 활성 이펙트가 0이다", manager.ActiveEffectCount == 0);

        // ── 8. 타격점 계산 (EffectHitPoint) — 구를 쓰면 정답을 손으로 알 수 있다 ──
        CheckHitPoint();

        Finish();
    }

    /// <summary>
    /// 반지름 0.5 구에 대해서는 정답이 자명하다 — 어디서 때리든 표면 점은 중심에서 정확히 0.5만큼 떨어져 있고,
    /// 방향은 중심→공격자 방향이다. 그래서 계산이 맞는지 손으로 검산할 수 있다.
    /// </summary>
    private void CheckHitPoint()
    {
        var host = new GameObject("[SmokeHitTarget]");
        host.transform.position = new Vector3(10f, 0f, 0f);
        var sphere = host.AddComponent<SphereCollider>();   // 기본 radius 0.5

        Vector3 center = sphere.bounds.center;
        const float radius = 0.5f;

        // (1) 공격자가 콜라이더 밖 — 정상 경로
        Vector3 outside = center + new Vector3(2f, 0f, 0f);
        Pose fromOutside = EffectHitPoint.Resolve(new AttackHitContext(outside, host.transform, sphere));
        Check($"밖에서 때리면 표면에 붙는다 (중심거리 {Vector3.Distance(fromOutside.position, center):F3} ≈ {radius})",
            Mathf.Abs(Vector3.Distance(fromOutside.position, center) - radius) < 0.02f);
        Check("회전의 +Z가 공격자를 향한다",
            Vector3.Dot(fromOutside.rotation * Vector3.forward, Vector3.right) > 0.99f);

        // (2) 공격자가 콜라이더 '안' — 근접 무기가 파고든 경우. 밀어내기 경로를 탄다
        Vector3 inside = center + new Vector3(0.1f, 0f, 0f);
        Pose fromInside = EffectHitPoint.Resolve(new AttackHitContext(inside, host.transform, sphere));
        Check($"안에서 때려도 몸 속이 아니라 표면에 뜬다 (중심거리 {Vector3.Distance(fromInside.position, center):F3})",
            Mathf.Abs(Vector3.Distance(fromInside.position, center) - radius) < 0.02f);

        // (3) 공격자가 정확히 중심 — 방향을 정할 수 없다. Vector3.up으로 도망쳐야 한다
        Pose fromCenter = EffectHitPoint.Resolve(new AttackHitContext(center, host.transform, sphere));
        Check("중심에서 때리면 위쪽 표면으로 도망친다",
            Mathf.Abs(Vector3.Distance(fromCenter.position, center) - radius) < 0.02f &&
            Vector3.Dot((fromCenter.position - center).normalized, Vector3.up) > 0.9f);

        // (4) 투사체 — sourcePosition이 곧 접촉점이라 "공격자 방향"이 0이 된다 → 바깥 방향으로 대체
        Vector3 contact = center + new Vector3(0f, 0f, radius);
        Pose projectile = EffectHitPoint.Resolve(new AttackHitContext(contact, host.transform, sphere));
        Check("접촉점이 곧 공격자 위치여도 회전이 무너지지 않는다",
            Vector3.Dot(projectile.rotation * Vector3.forward, Vector3.forward) > 0.9f);

        // (5) 콜라이더가 없으면 anchor로 퇴화한다
        var anchor = new GameObject("[SmokeAnchor]");
        anchor.transform.position = new Vector3(3f, 1.2f, 4f);
        Pose noCollider = EffectHitPoint.Resolve(new AttackHitContext(Vector3.zero, null, null), anchor.transform);
        Check("hitCollider가 없으면 anchor 자리에 뜬다",
            Vector3.Distance(noCollider.position, anchor.transform.position) < 0.001f);

        Destroy(anchor);
        Destroy(host);
    }

    private static Vector3 FirstActiveInstancePosition(GameObject prefab)
    {
        var instances = FindObjectsByType<EffectInstance>(FindObjectsSortMode.None);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].sourcePrefab == prefab && instances[i].gameObject.activeSelf)
                return instances[i].transform.position;
        }
        return Vector3.negativeInfinity;
    }

    private static float FirstActiveSimulationSpeed(GameObject prefab)
    {
        var instances = FindObjectsByType<EffectInstance>(FindObjectsSortMode.None);
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].sourcePrefab != prefab || !instances[i].gameObject.activeSelf) continue;

            var ps = instances[i].GetComponentInChildren<ParticleSystem>(true);
            if (ps != null) return ps.main.simulationSpeed;
        }
        return float.NaN;
    }

    private void Check(string label, bool condition)
    {
        if (condition) _passed++; else _failed++;
        _report.AppendLine($"  {(condition ? "PASS" : "FAIL")}  {label}");
    }

    private void Finish()
    {
        Time.maximumDeltaTime = _maxDeltaTimeBackup;

        string summary = $"[SmokeTest] {_passed} passed / {_failed} failed\n{_report}";
        if (_failed > 0) Debug.LogError(summary); else Debug.Log(summary);
        Destroy(gameObject);
    }
}
#endif
