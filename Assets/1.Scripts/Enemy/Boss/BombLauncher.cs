using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawn/Hold/Launch는 서버 전용이므로 서버에서만 동작합니다.
/// </summary>
public class BombLauncher : MonoBehaviour
{
    #region Inspector Variables

    [Header("폭탄 생성")]
    [SerializeField] GameObject bombPrefab;
    [SerializeField] Transform bombSocket;

    [Header("투척 설정")]
    [SerializeField] Vector3 throwLocalDirection = Vector3.forward;
    [SerializeField] float throwDistance;
    [SerializeField] float flyingDuration;
    [SerializeField] float arcHeight;
    [SerializeField] float spreadAngle;   // 좌우 랜덤 살포 각도 (forward 기준 ± 도)
    [SerializeField] LayerMask groundMask;

    #endregion

    #region Config Injection

    /// <summary>
    /// 투척 관련 수치를 외부에서 주입한다. (SO 종속 없이 값만 받음)
    /// </summary>
    public void SetThrowFigures(Vector3 localDirection, float distance, float duration, float arc, float spread)
    {
        throwLocalDirection = localDirection;
        throwDistance = distance;
        flyingDuration = duration;
        arcHeight = arc;
        spreadAngle = spread;
    }

    #endregion

    #region State Variables

    GameObject _bombInstance;
    BombController _bombController;

    #endregion

    #region Public Methods

    /// <summary>
    /// 폭탄 프리팹을 생성해 네트워크 스폰하고, 지정된 소켓을 따라가도록 고정합니다.
    /// </summary>
    public void BombHold()
    {
        if (!IsServer()) return;

        if (bombPrefab == null)
        {
            Edit.LogError("[Wells] bombPrefab이 연결되어 있지 않습니다.");
            return;
        }

        if (bombSocket == null)
        {
            Edit.LogError("[Wells] bombSocket이 연결되어 있지 않습니다.");
            return;
        }

        if (_bombInstance != null)
            return;

        _bombInstance = Instantiate(bombPrefab, bombSocket.position, bombSocket.rotation);

        NetworkObject networkObject = _bombInstance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Edit.LogError("[Wells] bombPrefab에 NetworkObject 컴포넌트가 없습니다.");
            Destroy(_bombInstance);
            _bombInstance = null;
            return;
        }
        networkObject.Spawn();

        _bombController = _bombInstance.GetComponent<BombController>();
        if (_bombController == null)
        {
            Edit.LogError("[Wells] bombPrefab에 BombController 컴포넌트가 없습니다.");
            return;
        }

        _bombController.Hold(bombSocket);

        // [진단] 생성은 되는데 투척이 0회인 상태를 가르기 위한 기준점.
        // 이 로그만 반복되고 아래 BombThrow 로그가 없으면 = 던지기 경로가 아예 안 불린다.
        Edit.Log("[진단][Wells] BombHold 성공 — 폭탄 생성·소켓 장착");
    }

    /// <summary>
    /// 생성된 폭탄을 자신의 로컬 방향 기준으로 target을 계산해 포물선 발사합니다.
    /// 투척 값들은 인스펙터에서 설정한 SerializeField를 사용합니다.
    /// </summary>
    public void BombThrow()
    {
        if (!IsServer()) return;

        // [진단] BombThrow 가 **불리기는 했다**는 사실 자체가 핵심 신호다.
        // 이 로그가 보이면  → 던지기 경로는 도는데 그 시점에 폭탄이 이미 없다
        //                     (jump/die/groggy 의 time-0 BombDestroyEvent 가 먼저 먹었다는 뜻)
        // 이 로그가 없으면  → ThrowBombEvent 자체가 발화하지 않았다
        //                     (throwing 클립이 51% 지점에 도달 못 함 / 상태 진입 실패)
        //
        // ⚠️ 예전엔 이 로그가 주석 처리돼 있어, 폭탄이 안 나가는데 콘솔은 완전히 조용했다.
        //    실제로 이번 증상(생성 4회 / 투척 0회 / 에러 0건)이 정확히 이 경로였고,
        //    무증상이라 원인을 좁히지 못한 채 시간을 썼다. 다시 주석 처리하지 말 것.
        if (_bombController == null)
        {
            Edit.Log("[진단][Wells] BombThrow 호출됨 — 그러나 들고 있는 폭탄이 없다(투척 전 파괴됨)");
            return;
        }

        Edit.Log("[진단][Wells] BombThrow 호출됨 — 폭탄 보유 확인, 투척 진행");

        if (throwDistance <= 0f)
        {
            Edit.LogError("[Wells] throwDistance는 0보다 커야 합니다.");
            return;
        }

        if (flyingDuration <= 0f)
        {
            Edit.LogError("[Wells] flyingDuration은 0보다 커야 합니다.");
            return;
        }

        if (arcHeight <= 0f)
        {
            Edit.LogError("[Wells] arcHeight는 0보다 커야 합니다.");
            return;
        }

        if (throwLocalDirection == Vector3.zero)
        {
            Edit.LogError("[Wells] throwLocalDirection이 0입니다.");
            return;
        }

        // 좌우 ±spreadAngle 범위 내 랜덤 각도로 로컬 방향을 회전 (몸통은 고정, 방향만 랜덤 살포)
        float randomAngle = Random.Range(-spreadAngle, spreadAngle);
        Vector3 spreadLocalDir = Quaternion.AngleAxis(randomAngle, Vector3.up) * throwLocalDirection;

        // ThrowBombAction과 동일한 target 계산 (agent = this.transform)
        Vector3 dir = transform.TransformDirection(spreadLocalDir).normalized;
        Vector3 throwVector = dir * throwDistance;
        Vector3 target = transform.position + throwVector;

        // [임시 진단] groundMask 실제 런타임 값 확인 (Ground만이면 8, Ground+Default면 9)
        Edit.Log($"[진단][Wells] groundMask.value = {groundMask.value} (Ground만=8, +Default=9)");

        RaycastHit hit;
        if (Physics.Raycast(target, Vector3.down, out hit, Mathf.Infinity, groundMask))
        {
            target.y = hit.point.y;
        }

        _bombController.Launch(target, flyingDuration, arcHeight);

        _bombInstance = null;
        _bombController = null;
    }

    /// <summary>
    /// 생성된 폭탄이 남아 있으면 네트워크에서 despawn하여 제거합니다.
    /// </summary>
    public void BombDestroy()
    {
        if (!IsServer()) return;

        if (_bombInstance == null)
        {
            // 들고 있는 폭탄이 없는 정상 경로. 다만 낡은 컨트롤러 참조가 남아 있으면
            // 다음 BombThrow 가 조용히 실패하므로 여기서 함께 끊는다.
            _bombController = null;
            return;
        }

        NetworkObject network = _bombInstance.GetComponent<NetworkObject>();
        if (network != null && network.IsSpawned)
            network.Despawn(true);

        _bombInstance = null;

        // ⚠️ 예전에는 _bombInstance 만 지우고 _bombController 는 남겨 뒀다.
        //    BombThrow 는 _bombController 로 판단하므로, 파괴된 폭탄의 참조가 남으면
        //    "Hold 는 새로 되는데 Throw 는 조용히 실패"하는 비대칭 상태가 만들어진다.
        //    BombThrow 가 둘 다 지우는 것과 대칭을 맞춘다.
        _bombController = null;

        Edit.Log("[진단][Wells] BombDestroy — 들고 있던 폭탄을 파괴(jump/die/groggy 의 time-0 이벤트)");
    }

    #endregion

    #region Helper Methods

    bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    #endregion
}
