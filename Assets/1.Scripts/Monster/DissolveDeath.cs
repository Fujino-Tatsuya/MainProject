using System;
using System.Collections;
using UnityEngine;

// 사망 연출 훅. MonsterBase는 사망 단일 지점에서 IDeathEffect가 있으면 Play()를 호출하고,
// 없으면 despawnDelay 후 즉시 디스폰한다.
public interface IDeathEffect
{
    // 연출 재생. 끝나면 onComplete를 반드시 호출(디스폰 트리거). 서버에서 호출된다.
    void Play(Action onComplete);
}

// 디졸브 사망 연출(플레이스홀더).
// 디졸브 셰이더/머티리얼이 아직 없으므로, 지정한 머티리얼 float 프로퍼티가 존재할 때만 보간한다.
// 프로퍼티/렌더러가 없으면 예외 없이 즉시 onComplete(폴백).
[DisallowMultipleComponent]
public class DissolveDeath : MonoBehaviour, IDeathEffect
{
    [SerializeField] private Renderer[] renderers;      // 비우면 자식에서 자동 수집
    [SerializeField] private string dissolveProperty = "_DissolveAmount";
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float from = 0f;
    [SerializeField] private float to = 1f;

    private MaterialPropertyBlock _mpb;

    public void Play(Action onComplete)
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (!HasDissolveProperty())
        {
            // 디졸브 미지원(셰이더/프로퍼티 없음) — MonsterBase의 임시 사망 표시(축소/틴트)가
            // 재생될 시간을 준 뒤 디스폰(그래야 "죽는 게 보인다"). 셰이더 도입 시 이 폴백은 실제 디졸브로 대체.
            StartCoroutine(DelayThenComplete(onComplete));
            return;
        }

        StartCoroutine(DissolveRoutine(onComplete));
    }

    // 디졸브 미지원 시: 임시 사망 표시 시간을 준 뒤 디스폰.
    private IEnumerator DelayThenComplete(Action onComplete)
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, duration));
        onComplete?.Invoke();
    }

    private bool HasDissolveProperty()
    {
        if (renderers == null || string.IsNullOrEmpty(dissolveProperty))
            return false;

        foreach (Renderer r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            if (r.sharedMaterial.HasProperty(dissolveProperty))
                return true;
        }
        return false;
    }

    private IEnumerator DissolveRoutine(Action onComplete)
    {
        _mpb ??= new MaterialPropertyBlock();
        float t = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);

        while (t < safeDuration)
        {
            t += Time.deltaTime;
            float v = Mathf.Lerp(from, to, t / safeDuration);
            ApplyValue(v);
            yield return null;
        }

        ApplyValue(to);
        onComplete?.Invoke();
    }

    private void ApplyValue(float v)
    {
        foreach (Renderer r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            if (!r.sharedMaterial.HasProperty(dissolveProperty)) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(dissolveProperty, v);
            r.SetPropertyBlock(_mpb);
        }
    }
}
