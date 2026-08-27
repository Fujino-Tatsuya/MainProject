using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shuriken(<see cref="ParticleSystem"/>) 파트 드라이버. v1의 유일한 구현체.
///
/// ⚠️ <b>MainModule은 struct다.</b> <c>ps.main.simulationSpeed = x</c>는 컴파일되지 않는다.
/// <c>var main = ps.main; main.simulationSpeed = x;</c> 패턴을 써야 한다 (<see cref="SetPlayRate"/> 참고).
/// </summary>
public class ShurikenEffectSystem : IEffectSystem
{
    public bool CanDrive(GameObject instance)
    {
        // 여기서는 캐시를 만들지 않는다 — 손을 들지 않은 프리팹에 남의 캐시 컴포넌트가 붙는다.
        var cache = instance.GetComponent<ShurikenPartCache>();
        if (cache != null && cache.all != null) return cache.all.Length > 0;

        return instance.GetComponentInChildren<ParticleSystem>(true) != null;
    }

    /// <summary>
    /// <paramref name="duration"/>는 <b>의도적으로 무시한다.</b> 파티클의 시간축은 프리팹의 Start Lifetime이
    /// 진실이고, 그걸 런타임에 덮어쓰면 저작자가 에디터에서 본 것과 다른 이펙트가 재생된다.
    /// 수명은 매니저의 타이머가 따로 재고 있으므로 여기서 맞출 필요도 없다.
    /// </summary>
    public void Play(GameObject instance, float duration)
    {
        ShurikenPartCache cache = Cache(instance);

        // Clear를 먼저 하는 이유: 반납 경로를 타지 않고 돌아온 인스턴스(강제 회수 등)가
        // 이전 입자를 들고 다시 나가는 것을 막는다.
        for (int i = 0; i < cache.roots.Length; i++)
        {
            cache.roots[i].Clear(true);
            cache.roots[i].Play(true);
        }

        // 트레일도 이력을 먼저 지운다. 안 지우면 이전 대출자가 있던 자리에서 지금 위치까지 줄이 한 번 그어진다.
        for (int i = 0; i < cache.trails.Length; i++)
        {
            cache.trails[i].Clear();
            cache.trails[i].emitting = true;
        }
    }

    public void Stop(GameObject instance, bool immediate)
    {
        ShurikenPartCache cache = Cache(instance);

        if (immediate)
        {
            for (int i = 0; i < cache.roots.Length; i++)
                cache.roots[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            for (int i = 0; i < cache.trails.Length; i++)
            {
                cache.trails[i].emitting = false;
                cache.trails[i].Clear();
            }
            return;
        }

        // 트레일의 "자연스러운 해제" = 발생만 끄고 남은 궤적은 TrailRenderer.time 동안 알아서 사라진다.
        // 여기서 Clear()를 부르면 휘두르던 궤적이 뚝 끊긴다 — 파티클의 StopEmitting과 같은 의도다.
        for (int i = 0; i < cache.trails.Length; i++)
            cache.trails[i].emitting = false;

        // 루프 시스템만 "발생 정지". StopEmittingAndClear를 쓰면 뚝 끊긴다 —
        // 이 인자 하나가 "자연스러운 해제"의 정체다.
        // 원샷 파트는 건드리지 않는다(아직 뿜는 중일 수 있고, 어차피 제 수명대로 죽는다).
        for (int i = 0; i < cache.all.Length; i++)
        {
            if (cache.all[i].main.loop)
                cache.all[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    public void SetPlayRate(GameObject instance, float rate)
    {
        ShurikenPartCache cache = Cache(instance);

        for (int i = 0; i < cache.all.Length; i++)
        {
            // MainModule은 struct — 지역 변수에 받아서 설정해야 한다.
            ParticleSystem.MainModule main = cache.all[i].main;
            main.simulationSpeed = rate;
        }

        // 트레일에는 배율 개념이 없다(재생 속도 속성이 없다). 대신 궤적은 트랜스폼 이동으로 그려지므로,
        // 히트스톱으로 대상이 멈추면 궤적도 자연히 자라지 않는다 — 여기서 할 일이 없다.
    }

    public void ResetForPool(GameObject instance)
    {
        ShurikenPartCache cache = Cache(instance);

        for (int i = 0; i < cache.roots.Length; i++)
            cache.roots[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // 트레일은 반드시 여기서 지운다. 정점 이력은 Stop으로 사라지지 않아서, 남겨두면
        // 다음 대출 때 그 자리에서 새 위치까지 줄이 이어진다.
        for (int i = 0; i < cache.trails.Length; i++)
        {
            cache.trails[i].emitting = false;
            cache.trails[i].Clear();
        }

        // 감속이 걸린 채 반납되면 다음 대출자가 얼어붙은 이펙트를 받는다.
        SetPlayRate(instance, 1f);
    }

    /// <summary>이 인스턴스의 입자가 아직 살아 있는가. <see cref="EffectDurationProbe"/>의 실측에 쓴다.</summary>
    public static bool IsAlive(GameObject instance)
    {
        ShurikenPartCache cache = Cache(instance);

        for (int i = 0; i < cache.roots.Length; i++)
        {
            if (cache.roots[i].IsAlive(true)) return true;
        }
        return false;
    }

    /// <summary>인스턴스에 캐시 컴포넌트를 붙이고(최초 1회) 컴포넌트 배열을 돌려준다.</summary>
    private static ShurikenPartCache Cache(GameObject instance)
    {
        var cache = instance.GetComponent<ShurikenPartCache>();
        if (cache != null && cache.all != null && cache.trails != null) return cache;

        if (cache == null) cache = instance.AddComponent<ShurikenPartCache>();

        cache.all = instance.GetComponentsInChildren<ParticleSystem>(true);
        cache.roots = FindRoots(cache.all, instance.transform);
        cache.trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        return cache;
    }

    /// <summary>
    /// 조상 중에 ParticleSystem이 없는 시스템만 고른다.
    /// 하위 시스템(서브 이미터·자식 파티클)은 부모가 withChildren=true로 함께 몰기 때문에
    /// 여기서 직접 Play/Stop하면 이중 제어가 된다.
    /// </summary>
    private static ParticleSystem[] FindRoots(ParticleSystem[] all, Transform instanceRoot)
    {
        var roots = new List<ParticleSystem>(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            bool nested = false;

            Transform parent = all[i].transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<ParticleSystem>() != null) { nested = true; break; }
                if (parent == instanceRoot) break;   // 인스턴스 밖(풀 루트)까지 올라가지 않는다
                parent = parent.parent;
            }

            if (!nested) roots.Add(all[i]);
        }

        return roots.ToArray();
    }
}
