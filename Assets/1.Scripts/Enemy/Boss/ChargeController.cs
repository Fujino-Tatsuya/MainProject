using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class ChargeController : NetworkBehaviour, IDamageSettable
{
    List<ChargingObject> chargeObjects;
    [SerializeField] float maxY = 1f;
    [Header("플레이어 인원 수에 따른 오브젝트 갯수")]
    [SerializeField] int player1 = 1;
    [SerializeField] int player2 = 2;
    [SerializeField] int player3 = 3;

    [SerializeField] GameObject floor;
    ColliderBasicAttack _floorColliderAttack;

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

        if (!IsServer) return;

        // 머지(2026-07-29): floor 장판 공격 참조 취득은 feature/Boss 쪽 신규 로직.
        // floor가 비어 있는 프리팹이 있어 SetFloorActive와 같은 이유로 가드한다.
        if (floor != null)
            _floorColliderAttack = floor.GetComponent<ColliderBasicAttack>();
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

    public void SetDamage(int value)
    {
        _floorColliderAttack.SetDamage(value);
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

        // 진단 (2026-07-30) — 판정이 `==` 라서 카운트가 _max 를 지나치면 두 플래그 모두
        // 영원히 false 로 남고, BT는 IsDefeated/IsReached 를 기다리며 Idle 에 park 된다.
        // 파괴와 도달이 섞이는 경우(_destroyCount + _reachedCount == _max)도 같은 교착이다.
        Edit.Log(
            $"[No.23/진단] 기둥 파괴 — 파괴 {_destroyCount}/{_max}, 도달 {_reachedCount}/{_max}, " +
            $"IsDefeated={_isDefeated}, IsReached={_isReached}", this);

        if (_destroyCount > _max)
        {
            Edit.LogError(
                $"[No.23/진단] 파괴 카운트가 _max({_max})를 넘었다({_destroyCount}) — " +
                "`==` 판정이라 IsDefeated 가 절대 true 가 되지 않는다(BT 교착).", this);
        }
    }

    void CheckReachedObjects(object sender, EventArgs eventArgs)
    {
        _reachedCount++;
        if (_reachedCount == _max)
        {
            _isReached = true;
        }

        Edit.Log(
            $"[No.23/진단] 기둥 도달 — 도달 {_reachedCount}/{_max}, 파괴 {_destroyCount}/{_max}, " +
            $"IsDefeated={_isDefeated}, IsReached={_isReached}", this);

        // 파괴 + 도달이 _max 를 채웠는데 어느 플래그도 안 섰다면 그게 바로 교착이다.
        if (!_isDefeated && !_isReached && _destroyCount + _reachedCount >= _max)
        {
            Edit.LogError(
                $"[No.23/진단] 교착 — 파괴 {_destroyCount} + 도달 {_reachedCount} 가 _max({_max}) 를 채웠지만 " +
                "`==` 판정이라 IsDefeated/IsReached 둘 다 false 다. BT가 차징에서 빠져나오지 못한다.", this);
        }
    }

    void Init()
    {
        // 진단 — 중단된 차징이 남긴 카운트를 여기서 지운다. 무엇을 지웠는지 보이면
        // "부활 후 차징이 재시작됐는지 / 이전 상태를 물고 있는지"를 가를 수 있다.
        if (_destroyCount != 0 || _reachedCount != 0 || _isDefeated || _isReached)
        {
            Edit.Log(
                $"[No.23/진단] 차징 카운터 초기화 — 이전 상태 파괴 {_destroyCount}, 도달 {_reachedCount}, " +
                $"IsDefeated={_isDefeated}, IsReached={_isReached} (_max={_max})", this);
        }

        _isDefeated = false;
        _destroyCount = 0;

        _isReached = false;
        _reachedCount = 0;
    }
}
