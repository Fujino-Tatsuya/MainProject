using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 튜토리얼 스테이지에서만 SpinnerBot 을 <b>튜토리얼 전용 Variant</b>(<c>Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab</c> —
/// SpinnerBot + <see cref="TutorialSpinnerPractice"/> + <see cref="TutorialSpinnerHealthBarRule"/> — 바는 공용 몬스터 발밑 바)로 바꿔 스폰한다(서버).
///
/// 왜 이 방법인가: <see cref="TutorialSpinnerPractice"/> 는 NetworkBehaviour 라 런타임 AddComponent 를 못 한다(NGO 인덱스가 갈린다).
/// 존3 스피너봇은 공용 존 프리팹 <c>ZoneM_typeC</c> 의 스폰 지점 Override(<c>SpinnerBot.prefab</c>)에서 나오는데, 공용 존·원본 SpinnerBot 을
/// 고치면 절차 맵에도 퍼진다. 그래서 스폰 직전 훅(<see cref="MapContentSpawner.ServerMonsterPrefabOverride"/>)에서 튜토리얼일 때만 Variant 로 바꾼다.
/// Variant 는 DefaultNetworkPrefabs 에 등록돼 있어 클라도 같은 프리팹으로 복제한다. 참조는 등록 목록에서 찾는다(씬·StageTutorial 무수정).
///
/// 판별 = <see cref="TutorialStageMarker.IsActive"/>(씬 로드 때 확정 — 맵 생성 스폰보다 먼저). 절차 맵·Debug 씬 스피너봇은 그대로.
/// </summary>
public static class TutorialSpinnerSpawnOverride
{
    private static GameObject _variant;
    private static bool _searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Install()
    {
        _variant = null;
        _searched = false;
        MapContentSpawner.ServerMonsterPrefabOverride = Resolve;
    }

    /// <summary>치환할 프리팹 — 튜토리얼 스테이지의 (연습 컴포넌트 없는) SpinnerBot 이면 Variant, 아니면 null(그대로).</summary>
    public static GameObject Resolve(GameObject prefab)
    {
        if (prefab == null || !TutorialStageMarker.IsActive) return null;
        if (prefab.GetComponent<SpinnerBot>() == null || prefab.GetComponent<TutorialSpinnerPractice>() != null) return null;

        GameObject variant = FindRegisteredVariant();
        if (variant == null)
        {
            Edit.LogError("[Tutorial] 튜토리얼 스피너봇 Variant(TutorialSpinnerPractice 가 붙은 NetworkPrefab)를 NetworkPrefabs 에서 못 찾았다 — " +
                          "원래 SpinnerBot 으로 스폰한다(연습 모드 없음). DefaultNetworkPrefabs 에 SpinnerBot_Tutorial 등록 확인.");
            return null;
        }

        Edit.Log($"[Tutorial] 스피너봇 스폰 치환 {prefab.name} → {variant.name}.");
        return variant;
    }

    private static GameObject FindRegisteredVariant()
    {
        if (_searched && _variant != null) return _variant;
        _searched = true;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.NetworkConfig?.Prefabs == null) return null;

        foreach (NetworkPrefab entry in nm.NetworkConfig.Prefabs.Prefabs)
        {
            GameObject go = entry?.Prefab;
            if (go != null && go.GetComponent<TutorialSpinnerPractice>() != null && go.GetComponent<SpinnerBot>() != null)
            {
                _variant = go;
                break;
            }
        }
        return _variant;
    }
}
