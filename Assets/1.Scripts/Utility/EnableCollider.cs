using UnityEngine;
using Unity.Netcode;

public class EnableCollider : NetworkBehaviour
{
    [SerializeField] bool _isServerAuthority = true;
    [SerializeField] bool _initValue = false;

    BoxCollider _boxCollider;
    CapsuleCollider _capsuleCollider;
    SphereCollider _sphereCollider;
    MeshCollider _meshCollider;

    bool _hasBox = false;
    bool _hasCapsule = false;
    bool _hasSphere = false;
    bool _hasMesh = false;

    void Awake()
    {
        _boxCollider = GetComponent<BoxCollider>();
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _sphereCollider = GetComponent<SphereCollider>();
        _meshCollider = GetComponent<MeshCollider>();

        _hasBox = (_boxCollider) ? true : false;
        _hasCapsule = (_capsuleCollider) ? true : false;
        _hasSphere = (_sphereCollider) ? true : false;
        _hasMesh = (_meshCollider) ? true : false;

        // 초기화 값
        ApplyColliderEnabled(_initValue);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 서버권위인데 서버가 아닐 경우
        if (_isServerAuthority && !IsServer)
        {
            ApplyColliderEnabled(false);
        }
    }

    /// <summary>
    /// 해당 컴포넌트가 부착되어 있는 오브젝트의 콜라이더 활성화 여부를 셋팅하는 함수
    /// </summary>
    /// <param name="enable">true : 활성화, false : 비활성화 </param>
    public void SetEnableCollider(bool enable)
    {
        if (_isServerAuthority && !IsServer)
            return;

        ApplyColliderEnabled(enable);
    }

    void ApplyColliderEnabled(bool enable)
    {
        if(_hasBox)
            _boxCollider.enabled = enable;

        if(_hasCapsule)
            _capsuleCollider.enabled = enable;

        if (_hasSphere)
            _sphereCollider.enabled = enable;

        if(_hasMesh)
            _meshCollider.enabled = enable;
    }
}
