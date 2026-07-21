using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using System;

public class ChargeController : NetworkBehaviour
{
    List<ChargingObject> chargeObjects;
    [SerializeField] float maxY = 1f;
    [Header("플레이어 인원 수에 따른 오브젝트 갯수")]
    [SerializeField] int player1 = 1;
    [SerializeField] int player2 = 2;
    [SerializeField] int player3 = 3;

    [SerializeField] GameObject floor;

    int _max = 0;
    int _destroyCount = 0;
    // 테스트를 위해 잠시 public 처리.
    public bool _isDefeated = false;
    public bool IsDefeated { get { return _isDefeated; } }
    int _reachedCount = 0;
    bool _isReached = false;
    public bool IsReached { get { return _isReached; } }

    public override void OnNetworkSpawn()
    {
        floor.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        if (chargeObjects != null)
        {
            foreach (ChargingObject obj in chargeObjects)
            {
                obj.DestroyEvent -= CheckDestroyedObjects;
                obj.ReachEvent -= CheckReachedObjects;
            }
        }
    }

    /// <summary>
    /// ChargeController가 부착되어 있는 오브젝트 생성 시에 해당 함수를 통해 ChargingObject 리스트를 설정해야 합니다.
    /// </summary>
    /// <param name="list">등록할 ChargingObject 리스트</param>
    public void SetList(List<ChargingObject> list)
    {
        if (!IsServer) return;

        Init();

        chargeObjects = list;

        foreach (ChargingObject obj in chargeObjects)
        {
            obj.SetMinMaxY(maxY);
            obj.DestroyEvent += CheckDestroyedObjects;
            obj.ReachEvent += CheckReachedObjects;
        }
    }

    [ClientRpc]
    void SetFloorEnableClientRpc(bool enable)
    {
        floor.SetActive(enable);
    }

    public void StartCharge(int playerCount)
    {
        if (!IsServer) return;

        if (chargeObjects == null || chargeObjects.Count == 0)
        {
            Edit.LogError("[No.23] ChargeController에 ChargingObject 리스트가 설정되지 않았습니다." +
                "\nChargeController가 부착되어 있는 오브젝트 생성 시에 SetList()함수를 통해 리스트를 설정해야 합니다.");
            return;
        }
        Init();
        SetFloorEnableClientRpc(true);

        _max = (playerCount == 1) ? player1 : (playerCount == 2) ? player2 : player3;

        for (int i = 0; i < _max; i++)
        {
            chargeObjects[i].StartCharge();
        }
    }

    public void EndCharge()
    {
        if (!IsServer) return;

        Init();
        SetFloorEnableClientRpc(false);

        foreach (ChargingObject obj in chargeObjects)
        {
            obj.EndCharge();
        }
    }


    void CheckDestroyedObjects(object sender, EventArgs eventArgs)
    {
        _destroyCount++;
        if (_destroyCount == _max)
        {
            _isDefeated = true;
        }
    }

    void CheckReachedObjects(object sender, EventArgs eventArgs)
    {
        _reachedCount++;
        if (_reachedCount == _max)
        {
            _isReached = true;
        }
    }

    void Init()
    {
        _isDefeated = false;
        _destroyCount = 0;

        _isReached = false;
        _reachedCount = 0;
    }
}
