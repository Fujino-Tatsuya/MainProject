using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Wells 애니메이션 클립의 이벤트 수신부(폭탄 투척/파괴).
///
/// 🔴 <b>NetworkBehaviour 로 되돌리지 말 것.</b>
///
/// Wells 는 TwentyThree 프리팹 안에 **중첩된 NetworkObject** 다. NGO 는 프리팹의 중첩
/// NetworkObject 를 스폰하지 않는다(씬 오브젝트만 지원). 실제 런타임 경고:
///   "[Netcode] Spawning NetworkObjects with nested NetworkObjects is only supported
///    for scene objects. Child NetworkObjects will not be spawned over the network!"
///
/// NetworkBehaviour.IsServer 는 계산 프로퍼티가 아니라 **스폰 시 대입되는 필드**
/// (public bool IsServer { get; private set;}) 라, 미스폰 상태에서는 영원히 false 다.
/// 그래서 이 클래스가 NetworkBehaviour 였을 때 `if (IsServer)` 가 **모든 애니메이션 이벤트를
/// 조용히 삼켰다** — 폭탄이 생성만 되고 던져지지 않았고, jump/die/groggy 의 BombDestroyEvent
/// 까지 전부 무시됐다. 콘솔에는 아무 흔적도 남지 않았다.
///
/// 이 컴포넌트는 네트워크 복제가 필요 없다. 서버에서만 폭탄을 다루면 되고, 폭탄 자체가
/// NetworkObject 라 복제된다. 그래서 BombLauncher 와 동일하게 MonoBehaviour + NetworkManager
/// 직접 조회로 서버를 판정한다.
/// </summary>
public class WellsAnimEvents : MonoBehaviour
{
    [SerializeField] BombLauncher bombLauncher;

    public void ThrowBombEvent()
    {
        if (!IsServer()) return;

        if (bombLauncher == null)
        {
            Edit.LogError("[Wells] bombLauncher 가 배선되지 않았습니다.", this);
            return;
        }

        bombLauncher.BombThrow();
    }

    public void BombDestroyEvent()
    {
        if (!IsServer()) return;

        if (bombLauncher == null)
        {
            Edit.LogError("[Wells] bombLauncher 가 배선되지 않았습니다.", this);
            return;
        }

        bombLauncher.BombDestroy();
    }

    // BombLauncher.IsServer() 와 같은 판정. 중첩 미스폰 문제를 우회한다.
    static bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }
}
