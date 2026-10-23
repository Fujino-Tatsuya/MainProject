using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로비 훈련 표적 자리(할 일 86 T3 — TRAIN_01 표적 3개). 3.LobbyScene 훈련실 입구 쪽에 둔다 — 좌표는 임시, 최종 위치는 사용자 배치.
/// 🔴 표적(TrainingDummy)은 NetworkObject 라 씬에 넣지 않는다(로비 상주 허수아비 없음, PLAN §7-1) — 자식 Transform 은 <b>자리</b>뿐이고,
///    서버의 <see cref="LobbyTrainingWeaponCheck"/> 가 TRAIN_01 진행 중에만 그 자리에 스폰·단계를 떠나면 디스폰한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingTargetSpots : MonoBehaviour
{
    /// <summary>자리 = 직속 자식 Transform 전부(배치 순서).</summary>
    public void GetSpots(List<Transform> result)
    {
        result.Clear();
        foreach (Transform child in transform)
            result.Add(child);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
        foreach (Transform child in transform)
        {
            Gizmos.DrawWireCube(child.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
            Gizmos.DrawLine(child.position + Vector3.up, child.position + Vector3.up + child.forward);
        }
    }
#endif
}
