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

        if (_bombController == null)
        {
            //Edit.LogError("[Wells] BombThrow 호출 전에 BombHold가 성공하지 않았습니다.");
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

        RaycastHit hit;
        if (Physics.Raycast(target, Vector3.down, out hit, Mathf.Infinity, groundMask))
        {
            target.y = hit.point.y;
        }

        _bombController.Launch(target, flyingDuration, arcHeight);

        _bombInstance = null;
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
    }

    #endregion

    #region Helper Methods

    bool IsServer()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    #endregion
}
