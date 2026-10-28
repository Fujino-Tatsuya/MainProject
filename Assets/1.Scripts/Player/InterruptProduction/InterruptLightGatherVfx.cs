using UnityEngine;

/// <summary>
/// 빛 모임 VFX 루트(할 일 132 단계 4) — <c>Assets/5.VFX/Interrupt/LightGather/FX_InterruptLightGather.prefab</c>.
/// 흰 빛 기둥(루트 파티클) + 사방에서 모여드는 입자 + 중심 빛(자식 파티클). 재질은 절차 생성 셰이더
/// (<c>BeaverLobby/VFX/InterruptLightGather</c>) — 재질의 '덮어쓸 텍스처' 칸을 채우면 그 텍스처를 쓴다.
///
/// <list type="bullet">
/// <item>재생 = 붙일 곳 위치로 옮겨 처음부터 튼다. 정지 = 방출만 멈춘다(남은 입자는 자기 수명대로 사라진다).</item>
/// <item>매 프레임(LateUpdate) 붙일 곳 위치만 따라가고 회전은 월드 기준이다 — 소켓 본이 돌아도 기둥은 위로 선다.
///       입자는 로컬 시뮬레이션이라 움직이는 소켓 쪽으로 모인다.</item>
/// <item>시간 기준 칸(<see cref="timeBase"/>)은 재생할 때마다 모든 파티클에 넣는다(Play 중 바꾸면 다음 재생부터).</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptLightGatherVfx : MonoBehaviour
{
    [Tooltip("빛 모임 재생 시간 기준 — 실시간(기본): 강 슬로우 0.15·히트스톱 중에도 같은 빠르기 / 스케일: 슬로우를 따라 느려진다")]
    [SerializeField] private InterruptLightGatherTimeBase timeBase = InterruptLightGatherTimeBase.Unscaled;

    [Tooltip("재생할 루트 파티클(자식 포함). 비우면 이 오브젝트의 ParticleSystem")]
    [SerializeField] private ParticleSystem root;

    private ParticleSystem[] _systems;
    private Transform _anchor;

    public InterruptLightGatherTimeBase TimeBase => timeBase;

    /// <summary>방출 중(정지 전).</summary>
    public bool IsEmitting => root != null && root.isEmitting;

    public void Play(Transform anchor)
    {
        if (root == null && !TryGetComponent(out root))
            return;

        _systems ??= GetComponentsInChildren<ParticleSystem>(true);
        bool unscaled = InterruptLightGatherRules.UsesUnscaledTime(timeBase);
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem.MainModule main = _systems[i].main;
            main.useUnscaledTime = unscaled;
        }

        _anchor = anchor;
        Follow();
        root.Clear(true);
        root.Play(true);
    }

    public void Stop()
    {
        if (root != null)
            root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void LateUpdate() => Follow();

    private void Follow()
    {
        if (_anchor != null)
            transform.SetPositionAndRotation(_anchor.position, Quaternion.identity);
    }
}
