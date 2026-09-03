using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class TwentyThreeArenaContext : NetworkBehaviour
{
    [SerializeField] GameObject bossPrefab;
    [SerializeField] List<ChargingObject> ChargingObjects;
    [SerializeField] Vector3 bossPos;

    EnemyBTActivator _btActivator;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;


       NetworkObject boss = Instantiate(bossPrefab, bossPos, Quaternion.identity).GetComponent<NetworkObject>();
       if (boss == null)
       {
           Edit.LogError("[No.23] 해당 프리펩에 NetworkObject 컴포넌트가 없습니다. 추가해주세요.");
           return;
       }
       boss.Spawn();


        ChargeController controller = boss.GetComponentInChildren<ChargeController>();
        if (controller == null)
        {
            Edit.LogError("[No.23] 해당 보스에 ChargeController 컴포넌트가 없습니다. 추가해주세요.");
            return;
        }
        controller.SetList(ChargingObjects);

        _btActivator = boss.GetComponent<EnemyBTActivator>();
        if (_btActivator == null)
        {
            Edit.LogError("[No.23] 해당 보스에 EnemyBTActivator 컴포넌트가 없습니다. 추가해주세요.");
            return;
        }
        //_btActivator.OpenBT();
    }

    private void Update()
    {
        if (!IsServer) return;
        if (Input.GetKeyDown(KeyCode.F11))
        {
            _btActivator.OpenBT();
        }
    }
}
