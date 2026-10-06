using UnityEngine;

/// <summary>
/// 떠오르는 대미지 숫자의 생성 기준점. 붙어 있지 않으면 몸 콜라이더 중심을 쓴다(<see cref="FloatingDamageSpawner.ResolveAnchor"/>).
/// 기준점 = <see cref="anchorTransform"/>(본·소켓) 위치, 없으면 이 오브젝트 기준 <see cref="localOffset"/>.
/// 그 위에 <see cref="worldOffset"/> 을 더한다 — 소켓이 본 아래 깊이 있어 옮기기 불편할 때 인스펙터에서 바로 조절하려고 둔 칸(2026-10-06, 보스 머리 위 표시 조정).
/// 떠 있는 숫자도 매 프레임 이 값을 다시 읽으므로 Play 중 수정이 즉시 보인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FloatingDamageAnchor : MonoBehaviour
{
    [Tooltip("기준 Transform(본·소켓). 지정하면 아래 Local Offset 은 무시된다.")]
    [SerializeField] Transform anchorTransform;
    [Tooltip("Anchor Transform 이 비었을 때만 — 이 오브젝트 로컬 좌표 기준 위치.")]
    [SerializeField] Vector3 localOffset;
    [Tooltip("기준점에 더하는 월드 축 오프셋(m). 숫자가 너무 높으면 Y 를 음수로. Play 중 바꾸면 바로 반영된다.")]
    [SerializeField] Vector3 worldOffset;

    public Vector3 WorldPosition =>
        (anchorTransform != null ? anchorTransform.position : transform.TransformPoint(localOffset)) + worldOffset;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(WorldPosition, 0.15f);
    }
}
