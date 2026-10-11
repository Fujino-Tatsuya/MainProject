using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 존1 평타 연습 허수아비 자리(S7 Z1-3, 논의 D10). <c>StageTutorial.prefab</c> 의 자식으로 저작한다
/// (저작 메뉴 <c>Tools/Map/Authoring/Tutorial/8. Place Zone1 Dummies</c> · <c>2. Build StageTutorial Prefab</c>).
/// 🔴 허수아비는 NetworkObject 라 프리팹·씬에 넣지 않는다 — 자식 Transform 은 <b>자리</b>뿐이고,
///    서버의 <see cref="TutorialZone1Flow"/> 가 튜토리얼이 켜질 때 <see cref="DummyPrefab"/> 를 그 자리에 스폰한다.
/// 공용 존 프리팹(ZoneS_typeStart)은 절차 맵도 쓰므로 건드리지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialZone1Dummies : MonoBehaviour
{
    [Tooltip("간파 사이클이 없는 허수아비 변형(TrainingDummy_Tutorial). NetworkPrefabs 에 등록돼 있어야 한다.")]
    [SerializeField] private GameObject dummyPrefab;

    public GameObject DummyPrefab => dummyPrefab;

    /// <summary>자리 = 직속 자식 Transform 전부(저작 순서).</summary>
    public void GetSpots(List<Transform> result)
    {
        result.Clear();
        foreach (Transform child in transform) result.Add(child);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        foreach (Transform child in transform)
        {
            Gizmos.DrawWireCube(child.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
            Gizmos.DrawLine(child.position + Vector3.up, child.position + Vector3.up + child.forward);
        }
    }
#endif
}
