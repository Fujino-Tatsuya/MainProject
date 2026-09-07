using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 상자 파괴를 전 피어에 알리는 단일 진입점. <see cref="MapNetworkSync"/>와 <b>같은 오브젝트</b>에 붙인다.
///
/// <b>왜 여기 사는가.</b> 새 매니저를 씬에 배치하면 "이 씬에만 빠뜨려서 여기서만 안 된다"를 겪는다
/// (EffectManager로 이미 당한 버그). 맵 오브젝트에 얹으면 <b>수명이 정확히 일치</b>하고 —
/// 맵이 없으면 상자도 없다 — 빠뜨릴 수가 없다. 없으면 맵 자체가 안 생겨 즉시 드러난다.
///
/// <b>왜 상자마다 NetworkObject를 두지 않는가.</b> 상자 100개면 NetworkObject 100개 +
/// NetworkVariable 400개인데, 상자가 동기화할 상태는 없다. 사건 하나를 위해 상태 저장소를
/// 100벌 사는 셈이다. 여기 하나로 모으면 오브젝트 1개 + 파괴당 작은 메시지 1개다.
///
/// <b>왜 상자 ID를 싣는가.</b> 맵은 시드 기반 결정적 생성이라(<see cref="MapNetworkSync"/>) 모든 피어가
/// 같은 순서로 같은 상자를 만든다. 그 스폰 순번이 곧 공용 식별자다 — 좌표를 실을 필요가 없다.
/// </summary>
[DisallowMultipleComponent]
public class CrateBreakBroadcaster : NetworkBehaviour
{
    static CrateBreakBroadcaster _instance;

    // 폴백 경고는 씬당 1회. 원인이 "컴포넌트 하나 없음"이라 상자마다 반복해도 정보가 늘지 않는다.
    static bool _warnedMissing;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _instance = this;
        _warnedMissing = false;   // 씬이 바뀌면 다시 판정한다
    }

    public override void OnNetworkDespawn()
    {
        if (_instance == this) _instance = null;
        _warnedMissing = false;
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// [서버] 상자 파괴를 전 피어에 알린다. 브로드캐스터가 없으면 로컬로만 처리하고 경고한다.
    /// </summary>
    public static void ServerBreak(int crateId)
    {
        if (_instance != null && _instance.IsSpawned)
        {
            _instance.BreakCrateRpc(crateId);
            return;
        }

        // 폴백. 조용히 넘기면 상자가 아예 안 부서지므로 로컬로는 처리한다.
        WarnFallbackOnce();
        CrateRegistry.BreakLocal(crateId);
    }

    /// <summary>
    /// 폴백 진입을 알린다.
    ///
    /// <b>이 폴백이 위험한 이유는 조용해서다.</b> 호스트 혼자 테스트하면 브로드캐스터가 있든 없든
    /// 똑같이 잘 부서져서 구분이 안 되고, MPPM 2인을 띄워야 "클라에서만 상자가 안 부서진다"로
    /// 뒤늦게 드러난다. 그래서 네트워크가 도는데 브로드캐스터가 없으면 <b>에러</b>로 올린다.
    /// </summary>
    static void WarnFallbackOnce()
    {
        if (_warnedMissing) return;
        _warnedMissing = true;

        NetworkManager nm = NetworkManager.Singleton;
        bool networked = nm != null && nm.IsListening;

        if (!networked)
        {
            // 네트워크 없이 도는 테스트 씬. 의도된 경로라 정보성으로만 남긴다.
            Edit.Log("[Crate] 네트워크가 없어 상자 파괴를 로컬로만 처리한다(테스트 씬 정상 경로).");
            return;
        }

        Edit.LogError(
            "[Crate] 이 씬에 CrateBreakBroadcaster가 없다 — 상자가 서버에서만 부서지고 " +
            "클라이언트에는 멀쩡히 남는다.\n" +
            "MapNetworkSync 오브젝트(NetworkObject가 이미 붙어 있다)에 " +
            "CrateBreakBroadcaster를 추가할 것.");
    }

    /// <summary>
    /// 전 피어가 각자 로컬로 부순다.
    ///
    /// ⚠️ <b>Reliable(기본)이다.</b> <c>DissolveDeath</c>의 파괴 연출이 Unreliable인 것과 다르다 —
    /// 디졸브는 유실돼도 오브젝트가 어차피 디스폰되지만, 상자는 <b>때린 플레이어 눈앞에서
    /// 이펙트 없이 증발</b>한다. 파괴당 작은 메시지 하나니까 보장받는 쪽이 맞다.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    void BreakCrateRpc(int crateId) => CrateRegistry.BreakLocal(crateId);
}
