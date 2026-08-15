using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="EffectEntry.duration"/> <b>실측 도구</b>. 플레이 모드에서 엔트리의 파트를 직접 띄우고
/// 마지막 입자가 죽는 시각을 재서 콘솔에 찍는다. 등록된 duration이 실측보다 짧으면 경고한다.
///
/// 왜 필요한가: 이 시스템은 종료를 프리팹에서 추론하지 않고 데이터의 duration으로 회수한다.
/// 감으로 적으면 짧을 때 파티클이 잘리고 길면 풀이 낭비되는데, <b>둘 다 조용히 일어난다.</b>
/// 조기 반납(StopAction.Callback)을 생략한 대가를 이 도구로 덮는다.
///
/// 풀과 매니저를 거치지 않고 직접 Instantiate한다 — 매니저의 타이머가 개입하면 측정 대상이 사라진다.
/// v1에서 살아 있는지 판정할 수 있는 기술은 Shuriken뿐이라, 다른 기술의 파트는 측정에서 빠진다.
/// </summary>
public class EffectDurationProbe : MonoBehaviour
{
    [Tooltip("실측할 엔트리")]
    [SerializeField] private EffectEntry entry;

    [Tooltip("파트를 띄울 기준 위치. 비우면 이 오브젝트")]
    [SerializeField] private Transform origin;

    [Tooltip("이 시간(초)을 넘으면 측정을 포기한다. 루프가 섞여 있을 때의 무한 대기 방지")]
    [SerializeField, Min(1f)] private float timeout = 30f;

    private readonly ShurikenEffectSystem _shuriken = new ShurikenEffectSystem();
    private Coroutine _running;

    public EffectEntry Entry => entry;

    /// <summary>실측을 시작한다. 플레이 모드에서만 동작한다.</summary>
    public void Measure()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EffectDurationProbe] 플레이 모드에서만 실측할 수 있다.", this);
            return;
        }

        if (entry == null)
        {
            Debug.LogWarning("[EffectDurationProbe] 측정할 EffectEntry가 비어 있다.", this);
            return;
        }

        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(MeasureRoutine());
    }

    private IEnumerator MeasureRoutine()
    {
        // 루프가 섞여 있으면 parts는 영원히 안 죽는다 → 측정 대상은 outroParts가 된다.
        bool looping = ContainsLoopingSystem(entry.parts);
        EffectPart[] targets = looping ? entry.outroParts : entry.parts;
        float registered = looping ? entry.ResolvedOutroDuration : entry.ResolvedDuration;
        string label = looping ? "outroDuration" : "duration";

        if (targets == null || targets.Length == 0)
        {
            Debug.LogWarning($"[EffectDurationProbe] '{entry.name}'에 측정할 {(looping ? "outroParts" : "parts")}가 없다.", this);
            _running = null;
            yield break;
        }

        Transform anchor = origin != null ? origin : transform;
        var probeRoot = new GameObject($"[Probe] {entry.name}");
        probeRoot.transform.SetPositionAndRotation(anchor.position, anchor.rotation);

        var pending = new List<EffectPart>(targets);
        var spawned = new List<GameObject>(targets.Length);
        int driven = 0;

        float elapsed = 0f;
        float lastAliveAt = 0f;

        while (elapsed < timeout)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                EffectPart part = pending[i];
                if (part == null || part.prefab == null) { pending.RemoveAt(i); continue; }
                if (part.delay > elapsed) continue;

                pending.RemoveAt(i);

                GameObject instance = Instantiate(part.prefab, probeRoot.transform);
                instance.transform.localPosition = part.offset;
                spawned.Add(instance);

                if (_shuriken.CanDrive(instance))
                {
                    _shuriken.Play(instance);
                    driven++;
                }
            }

            bool anyAlive = false;
            for (int i = 0; i < spawned.Count; i++)
            {
                if (ShurikenEffectSystem.IsAlive(spawned[i])) { anyAlive = true; break; }
            }

            if (anyAlive) lastAliveAt = elapsed;
            else if (pending.Count == 0 && spawned.Count > 0 && elapsed > 0f) break;

            yield return null;
            elapsed += Time.deltaTime;
        }

        // 마지막으로 살아 있던 프레임 + 그 프레임의 여유를 실측값으로 본다.
        float measured = Mathf.Min(elapsed, lastAliveAt + Time.deltaTime);

        Destroy(probeRoot);
        _running = null;

        if (driven == 0)
        {
            Debug.LogWarning($"[EffectDurationProbe] '{entry.name}': Shuriken 파트가 하나도 없어 측정하지 못했다. " +
                             "v1에서 수명을 잴 수 있는 기술은 ParticleSystem뿐이다.", this);
            yield break;
        }

        int skipped = spawned.Count - driven;
        string skippedNote = skipped > 0 ? $" (Shuriken이 아닌 파트 {skipped}개는 측정 제외)" : string.Empty;

        if (elapsed >= timeout)
        {
            Debug.LogWarning($"[EffectDurationProbe] '{entry.name}': {timeout:F0}초 안에 입자가 죽지 않았다. " +
                             "루프 시스템이 섞여 있는지 확인할 것.", this);
        }
        else if (registered < measured)
        {
            Debug.LogWarning($"[EffectDurationProbe] '{entry.name}' 실측 {measured:F2}s > 등록 {label} " +
                             $"{registered:F2}s — 이펙트가 끝나기 전에 반납된다(파티클이 잘린다).{skippedNote}", this);
        }
        else
        {
            Debug.Log($"[EffectDurationProbe] '{entry.name}' 실측 {measured:F2}s / 등록 {label} " +
                      $"{registered:F2}s — 여유 {registered - measured:F2}s.{skippedNote}", this);
        }
    }

    private static bool ContainsLoopingSystem(EffectPart[] parts)
    {
        if (parts == null) return false;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == null || parts[i].prefab == null) continue;

            var systems = parts[i].prefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int s = 0; s < systems.Length; s++)
            {
                if (systems[s].main.loop) return true;
            }
        }

        return false;
    }
}
