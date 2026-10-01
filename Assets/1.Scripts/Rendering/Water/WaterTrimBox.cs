using UnityEngine;

// 존 물 잘라내기 상자 (PLAN-flatkit.md 9-b · 2026-10-01).
//
// 이 상자(Transform 위치·회전·크기, XZ 만 본다) 안에 들어가는 물 칸은 저작 도구가 물을 만들 때 지운다.
// 벽이 두 면뿐인 존 모서리처럼 물 가장자리가 허공에 드러나거나, 벽 투명화 때 벽 뒤 물이 비치는 곳을
// 팀장이 직접 덮어서 다듬는 용도다(팀장 10-01 — 큰 존 입구 직전 모서리).
//
// 쓰는 법: 존 프리팹에서 빈 오브젝트에 이 컴포넌트 → 위치·크기(Scale X/Z)를 맞춤 →
//         Tools/Rendering/Flat Kit/Water/2. Zone Patches 실행.
// 🔴 존 루트 밑 `Water` 오브젝트 **밖에** 둘 것 — `Water` 는 도구가 매번 지우고 새로 만든다.
// 런타임 동작 없음(기즈모만).
[DisallowMultipleComponent]
public sealed class WaterTrimBox : MonoBehaviour
{
    // 존 루트 기준 XZ 점이 상자 안인가. toLocal = 존 루트 worldToLocal.
    public bool Contains(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint);
        return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.15f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0.05f, 1f));
    }
#endif
}
