using UnityEngine;

/// <summary>
/// 적중 섬광 4단계 월드 이펙트 루트(할 일 132 단계 5) — <c>Assets/5.VFX/Interrupt/HitFlash/FX_InterruptHitFlash.prefab</c>.
/// 적중 지점에서 파랑·흰 섬광: 흰 중심 빛 + 파란 번짐 + 스타버스트 광선 + 충격파 링 + 튀는 불티. 색은 HDR(블룸).
/// 재질은 절차 생성 셰이더(<c>BeaverLobby/VFX/InterruptHitFlash</c>) — 재질의 '덮어쓸 텍스처' 칸을 채우면 그 텍스처를 쓴다.
///
/// <list type="bullet">
/// <item>모든 피어가 각자 섬광 순간(성공 Started + 섬광 1~3단계 길이)에 튼다 — 디렉터가 한 번 만들어 재사용한다(매 재생 생성 없음).</item>
/// <item>파티클은 실시간(useUnscaledTime) — 성공 슬로우(0.25) 중에도 같은 빠르기로 터진다. 프리팹에 박아 두었다.</item>
/// <item>크기 = 루트 스케일(파티클 Scaling Mode = Hierarchy).</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptHitFlashVfx : MonoBehaviour
{
    [Tooltip("재생할 루트 파티클(자식 포함). 비우면 이 오브젝트의 ParticleSystem")]
    [SerializeField] private ParticleSystem root;

    public void Play(Vector3 position, float scale)
    {
        if (root == null && !TryGetComponent(out root))
            return;

        transform.SetPositionAndRotation(position, Quaternion.identity);
        transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        root.Clear(true);
        root.Play(true);
    }

    /// <summary>강제 리셋 — 남은 입자까지 지운다.</summary>
    public void Stop()
    {
        if (root != null)
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
