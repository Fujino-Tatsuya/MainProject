using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀링 전제 프리팹 규칙 중 <b>어기면 풀이 깨지는 두 가지</b>를 인스턴스 생성 시점에 검사한다.
///
/// 문서 체크리스트만으로는 "조용히 깨지는" 사고를 못 막는다 — 프리팹이 자기 GameObject를
/// 파괴해 버리면 풀은 null을 들고 있게 되고, 원인(프리팹 설정)과 증상(엉뚱한 시점의 NRE)이 멀어진다.
/// 그래서 경고를 남기고 <b>인스턴스에 한해</b> 무해한 값으로 되돌린다(원본 에셋은 건드리지 않는다).
///
/// Play On Awake는 검사하지 않는다 — 루프 이펙트의 L_Intro는 켜져 있는 게 정상이라 오탐이 난다.
/// </summary>
public static class EffectPrefabRules
{
    // 경고는 프리팹당 1회만. 풀은 같은 프리팹으로 인스턴스를 수십 개 만들기 때문에
    // 억제하지 않으면 위반 하나가 콘솔을 마비시킨다.
    private static readonly HashSet<int> Warned = new HashSet<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWarnings() => Warned.Clear();

    /// <summary>새로 만든 풀 인스턴스를 검사·교정한다. 위반이 하나라도 있으면 true.</summary>
    public static bool ValidateAndFix(GameObject instance, GameObject sourcePrefab)
    {
        bool violated = false;
        bool quiet = !Warned.Add(sourcePrefab.GetInstanceID());

        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            if (main.stopAction != ParticleSystemStopAction.Destroy) continue;

            if (!quiet)
            {
                Edit.LogWarning(
                    $"[Effect] 프리팹 규칙 위반 — '{sourcePrefab.name}'의 ParticleSystem '{particles[i].name}'에 " +
                    "Stop Action = Destroy가 걸려 있다. 풀 인스턴스가 사라진다. 프리팹에서 None으로 고칠 것. " +
                    "(지금은 런타임 인스턴스에만 None을 적용해 넘어간다)", sourcePrefab);
            }

            main.stopAction = ParticleSystemStopAction.None;
            violated = true;
        }

        var trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            if (!trails[i].autodestruct) continue;

            if (!quiet)
            {
                Edit.LogWarning(
                    $"[Effect] 프리팹 규칙 위반 — '{sourcePrefab.name}'의 TrailRenderer '{trails[i].name}'에 " +
                    "AutoDestruct가 켜져 있다. GameObject를 파괴해 풀과 충돌한다. 프리팹에서 끌 것. " +
                    "(지금은 런타임 인스턴스에만 꺼서 넘어간다)", sourcePrefab);
            }

            trails[i].autodestruct = false;
            violated = true;
        }

        return violated;
    }
}
