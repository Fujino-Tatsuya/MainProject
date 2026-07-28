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
        base.OnNetworkSpawn();
        SetFloorActive(false);
    }

    // floor 미배정 프리팹에서 NRE로 스폰이 중단되지 않게 한 곳에서만 만진다.
    void SetFloorActive(bool active)
    {
        if (floor == null)
        {
            Edit.LogWarning("[No.23] ChargeController의 floor가 비어 있습니다 — 충전 장판 표시를 건너뜁니다.", this);
            return;
        }

        floor.SetActive(active);
    }

    public override void OnNetworkDespawn()
    {
        // 기둥은 씬 상주라 보스보다 오래 산다 — 보스가 사라질 때 구독을 반드시 끊는다.
        if (IsServer)
            UnsubscribeAll();

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// ChargeController가 부착되어 있는 오브젝트 생성 시에 해당 함수를 통해 ChargingObject 리스트를 설정해야 합니다.
    /// </summary>
    /// <param name="list">등록할 ChargingObject 리스트</param>
    public void SetList(List<ChargingObject> list)
    {
        if (!IsServer) return;

        Init();

        // 재주입(보스 재스폰·리스트 교체) 시 이전 구독을 먼저 끊는다 — 안 끊으면 파괴 1회에
        // 카운트가 여러 번 올라가 _isDefeated가 조기 true가 된다.
        UnsubscribeAll();

        chargeObjects = list;
        if (chargeObjects == null) return;

        chargeObjects.RemoveAll(obj => obj == null);

        if (chargeObjects.Count != ExpectedObjectCount)
        {
            Edit.LogWarning(
                $"[No.23] ChargingObject가 {chargeObjects.Count}개입니다(기대 {ExpectedObjectCount}). " +
                "인원수별 활성 개수가 목록 범위로 잘립니다.", this);
        }

        foreach (ChargingObject obj in chargeObjects)
        {
            obj.SetMinMaxY(maxY);
            obj.DestroyEvent += CheckDestroyedObjects;
            obj.ReachEvent += CheckReachedObjects;
        }
    }

    const int ExpectedObjectCount = 4;

    void UnsubscribeAll()
    {
        if (chargeObjects == null) return;

        foreach (ChargingObject obj in chargeObjects)
        {
            if (obj == null) continue;

            obj.DestroyEvent -= CheckDestroyedObjects;
            obj.ReachEvent -= CheckReachedObjects;
        }
    }

    [ClientRpc]
    void SetFloorEnableClientRpc(bool enable)
    {
        SetFloorActive(enable);
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

        int clampedPlayers = Mathf.Clamp(playerCount, 1, 3);
        _max = (clampedPlayers == 1) ? player1 : (clampedPlayers == 2) ? player2 : player3;

        // 목록보다 많이 켜려 하면 IndexOutOfRange가 난다 — 항상 범위로 잘라 쓴다.
        _max = Mathf.Clamp(_max, 0, chargeObjects.Count);

        for (int i = 0; i < _max; i++)
        {
            chargeObjects[i].StartCharge();
        }

        Edit.Log($"[No.23] 충전 시작 — 인원 {playerCount} → 기둥 {_max}개 활성.", this);
    }

    public void EndCharge()
    {
        if (!IsServer) return;

        Init();
        SetFloorEnableClientRpc(false);

        if (chargeObjects == null) return;

        foreach (ChargingObject obj in chargeObjects)
        {
            if (obj != null)
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
