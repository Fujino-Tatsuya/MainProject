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
    }

    /// <summary>
    /// 생성된 폭탄을 자신의 로컬 방향 기준으로 target을 계산해 포물선 발사합니다.
    /// 투척 값들은 인스펙터에서 설정한 SerializeField를 사용합니다.
    /// </summary>
    public void BombThrow()
    {
        if (!IsServer()) return;

        // ⚠️ 여기가 조용히 return하면 "투척 애니메이션은 나오는데 폭탄만 안 나간다"는 증상만 남고
        // 아무 흔적이 없다. BT(Hold)와 Animator(투척 이벤트)는 서로의 상태를 모르기 때문에,
        // 보스가 Groggy·Break로 끊기거나 폭탄이 먼저 파괴되면 실제로 이 상태가 된다.
        // 투척 이벤트 1회당 한 줄이므로 스팸이 아니다 — 다시 주석 처리하지 말 것.
        if (_bombController == null)
        {
            Edit.LogWarning(
                "[Wells] 투척 이벤트가 왔지만 들고 있는 폭탄이 없습니다 — BombHold가 오지 않았거나 " +
                "폭탄이 먼저 파괴됐습니다(BT 주기와 애니메이션 주기가 어긋난 경우).", this);
            return;
        }

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

        // 바닥 판정은 GroundProbe로 통일한다(레이어·원점·유닛 콜라이더 제외를 한 곳에서 처리).
        // 빗나가면 target.y가 투척 높이로 남아 폭탄이 공중에 착지한다 — 그래서 성공/실패 둘 다 로그를 남긴다.
        if (GroundProbe.TryFindGround(target, groundMask.value, out RaycastHit hit, out string report))
        {
            // 바닥면과 정확히 같은 높이로 두면 폭탄·장판이 바닥과 z-fighting 한다 → 표준 간격만큼 띄운다.
            target.y = GroundProbe.SurfaceY(hit);
            Edit.Log($"[No.23] 폭탄 투척 착지 지점 확정 — {report} → 착지 y={target.y:F2}", this);
        }
        else
        {
            Edit.LogWarning($"[No.23] 폭탄 투척 착지 지점을 못 찾아 투척 높이를 그대로 씁니다({target}) — {report}", this);
        }

        _bombController.Launch(target, flyingDuration, arcHeight);

        // 던진 뒤에는 "들고 있는 폭탄 없음"이 참이어야 한다. _bombController를 남겨 두면 이미 날아간
        // (또는 폭발해 despawn된) 컨트롤러를 들고 다음 투척 이벤트에 들어가, 위의 조용한 경로로 빠진다.
        _bombInstance = null;
        _bombController = null;
    }

    /// <summary>
    /// 생성된 폭탄이 남아 있으면 네트워크에서 despawn하여 제거합니다.
    /// </summary>
    public void BombDestroy()
    {
        if (!IsServer()) return;

        if (_bombInstance != null)
        {
            NetworkObject network = _bombInstance.GetComponent<NetworkObject>();
            network.Despawn(true);
            _bombInstance = null;
        }

        // 인스턴스가 이미 사라진 경로에서도 컨트롤러 참조만 남을 수 있다 — 항상 함께 비운다.
        _bombController = null;
    }

    #endregion

    #region Helper Methods

    bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    #endregion
}
