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

    private IEnumerator Start()
    {
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
        int sparkBefore = manager.PoolCountActive(sparkPrefab);
        manager.Play(composite, Vector3.zero, Quaternion.identity);
        yield return null;
        Check("컴포지트 첫 파트가 즉시 발화한다", manager.PoolCountActive(bluntPrefab) == 1);
        Check("delay가 있는 파트는 아직 안 떴다", manager.PoolCountActive(sparkPrefab) == sparkBefore);

        yield return new WaitForSeconds(composite.parts[1].delay + 0.15f);
        Check("delay 경과 후 두 번째 파트가 발화한다", manager.PoolCountActive(sparkPrefab) == sparkBefore + 1);

        yield return new WaitForSeconds(composite.ResolvedDuration + 0.3f);
        Check("컴포지트 전체가 한 번에 반납된다", manager.ActiveEffectCount == 0);

        // ── 3. 루프 핸들: Release 전에는 안 죽고, Release 후 outroDuration에 반납 ──
        EffectHandle handle = manager.PlayLooping(spark, Vector3.zero, Quaternion.identity);
        Check("PlayLooping이 핸들을 발급한다", handle.IsSet);

        yield return new WaitForSeconds(spark.ResolvedDuration + 0.3f);
        Check("루프는 duration이 지나도 살아 있다", manager.ActiveEffectCount == 1);

        manager.Release(handle);
        yield return null;
        Check("Release() 직후에는 아직 반납되지 않는다 (2단계 해제)", manager.ActiveEffectCount == 1);

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

        Finish();
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
        string summary = $"[SmokeTest] {_passed} passed / {_failed} failed\n{_report}";
        if (_failed > 0) Debug.LogError(summary); else Debug.Log(summary);
        Destroy(gameObject);
    }
}
#endif
