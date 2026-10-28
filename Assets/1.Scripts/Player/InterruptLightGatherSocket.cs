using UnityEngine;

/// <summary>
/// 빛 모임 소켓 칸(할 일 132 단계 4) — 간파 강 시도(성공 예측) 동안 <see cref="vfxPrefab"/> 을 <see cref="socket"/> 위치에서 튼다.
/// <c>Player.prefab</c>(base) 루트에 붙고 소켓 칸은 비워 둔다(= 캐릭터 중심, 이 오브젝트) — 캐릭터 Variant 가 방패·칼·총구를 연결한다.
///
/// <list type="bullet">
/// <item>수동 실행자다 — 오너 화면은 <see cref="InterruptLightGatherOwnerOutput"/>, 팀원 화면은 디렉터의 <see cref="InterruptLightGatherPeers"/> 가 부른다.</item>
/// <item>VFX 는 첫 재생 때 한 번 만들어 이 플레이어 아래 두고 재사용한다(매 재생 생성 없음). 플레이어와 함께 사라진다.</item>
/// <item>VFX 는 소켓 위치만 따라가고 회전은 월드 기준(빛 기둥이 늘 위로 선다) — <see cref="InterruptLightGatherVfx"/>.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptLightGatherSocket : MonoBehaviour, IInterruptLightGatherPlayer
{
    [Tooltip("빛 모임이 붙을 소켓(팔라딘 방패·어쌔신 칼·거너 총구). base 는 비워 둔다 = 캐릭터 중심(이 오브젝트). 캐릭터 Variant 가 연결한다")]
    [SerializeField] private Transform socket;

    [Tooltip("빛 모임 VFX 프리팹(흰 빛 기둥 + 모여드는 입자). 첫 재생 때 한 번 만들어 재사용한다")]
    [SerializeField] private InterruptLightGatherVfx vfxPrefab;

    private InterruptLightGatherVfx _instance;

    /// <summary>지금 붙일 곳 — 소켓, 비어 있으면 캐릭터 중심.</summary>
    public Transform Anchor => InterruptLightGatherRules.ResolveAnchor(socket, transform);

    /// <summary>만들어 둔 VFX(첫 재생 전 null).</summary>
    public InterruptLightGatherVfx Instance => _instance;

    public GameObject Play()
    {
        if (vfxPrefab == null)
            return null;

        if (_instance == null)
        {
            _instance = Instantiate(vfxPrefab, transform);
            _instance.name = vfxPrefab.name;
        }

        _instance.Play(Anchor);
        return _instance.gameObject;
    }

    public void Stop()
    {
        if (_instance != null)
            _instance.Stop();
    }
}
