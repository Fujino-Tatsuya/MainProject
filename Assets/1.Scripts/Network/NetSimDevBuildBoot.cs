using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 빌드 지연 테스트용(할 일 258) — 같은 오브젝트의 Multiplayer Tools <see cref="NetworkSimulator"/> 를
/// <b>Development Build 에서만</b> 켠다. 에디터(MPPM 포함)·릴리스 빌드에서는 꺼 둔다.
///
/// 프리팹(<c>NetworkManager.prefab</c>)에는 시뮬레이터가 <b>꺼진 채</b> 사용자 정의 프리셋(지연만)으로 붙어 있다.
/// 값은 그 컴포넌트 인스펙터에서 바꾼다. 지연은 이 피어의 보내기·받기 <b>양방향</b>에 걸린다
/// (UTP <c>SimulatorPipelineStage</c> 가 <c>ApplyMode.AllPackets</c>) — 한 피어만 25ms 면 RTT +50ms,
/// 호스트·클라 둘 다 Development Build 면 RTT +100ms. 상세: Docs/tech/owner-predicted-basic-attack.md §7.1.
///
/// 릴리스 빌드는 NGO <c>UnityTransport</c> 가 시뮬레이터 파이프라인 단계 자체를 만들지 않아 켜도 효과가 없다.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetSimDevBuildBoot : MonoBehaviour
{
    [SerializeField, Tooltip("Development Build 에서만 켤 시뮬레이터(같은 오브젝트). 프리셋 값은 그 컴포넌트에서 바꾼다.")]
    private NetworkSimulator simulator;

    private void Awake()
    {
        if (simulator == null)
        {
            simulator = GetComponent<NetworkSimulator>();
        }

        if (simulator == null)
        {
            return;
        }

#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        // 씬 재진입으로 생긴 중복 NetworkManager(곧 파괴됨)는 켜지 않는다 — 꺼질 때 공용 드라이버 값을 0 으로 되돌리기 때문.
        var self = GetComponent<NetworkManager>();
        if (self != null && NetworkManager.Singleton != null && NetworkManager.Singleton != self)
        {
            simulator.enabled = false;
            return;
        }

        simulator.enabled = true;
        var preset = simulator.ConnectionPreset;
        Debug.Log($"[NetSim] 지연 {(preset != null ? preset.PacketDelayMs : 0)}ms 켬");
#else
        // 에디터·릴리스 = 기본 동작 그대로(꺼짐). 에디터 Play 중 인스펙터에서 직접 체크하면 그때는 켜진다.
        if (simulator.enabled)
        {
            simulator.enabled = false;
        }
#endif
    }
}
