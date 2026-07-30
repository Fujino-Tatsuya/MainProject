using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;

public class EnemyBTActivator : NetworkBehaviour
{
    [SerializeField] BehaviorGraphAgent[] targetBTs;
    [SerializeField] string isOpenVariableName = "IsOpen";

    // 전투 종료 플래그. No.23 그래프는 이 값을 **true 로만** 쓴다(쓰는 노드 1개, 값 1).
    // false 로 되돌리는 노드가 없어서, 한 번 서면 Idle 상태 분기가
    // `IsOver == true → IsOpen = false` 를 매번 실행해 BT가 영구히 닫혔다.
    // 부활로 전투를 이어갈 때는 이 플래그도 함께 내려야 한다.
    [SerializeField] string isOverVariableName = "IsOver";

    [SerializeField] private ReStart restartChannel;

    readonly List<BlackboardVariable<bool>> isOpenVariables = new();
    readonly List<BlackboardVariable<bool>> isOverVariables = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        CacheIsOpenVariables();
    }

    void CacheIsOpenVariables()
    {
        isOpenVariables.Clear();
        isOverVariables.Clear();

        foreach (BehaviorGraphAgent bt in targetBTs)
        {
            if (bt == null)
                continue;

            if (bt.BlackboardReference.GetVariable<bool>(isOpenVariableName, out var isOpen))
            {
                isOpenVariables.Add(isOpen);
            }
            else
            {
                Debug.LogError($"[Enemy] {bt.name} BT에서 {isOpenVariableName} 변수를 찾지 못했습니다.", bt);
            }

            // IsOver 는 그래프마다 없을 수 있다(Wells 등) — 없으면 조용히 건너뛴다.
            if (bt.BlackboardReference.GetVariable<bool>(isOverVariableName, out var isOver))
                isOverVariables.Add(isOver);
        }

        // 외부 변경 감시 기준값 — 초기화하지 않으면 첫 Update가 허위 변화를 보고한다.
        _lastObserved = isOpenVariables.Count > 0 && isOpenVariables[0].Value;
    }

    public void OpenBT()
    {
        if (!IsServer) return;

        SetAll(true, "OpenBT");
    }

    public void CloseBT()
    {
        if (!IsServer) return;

        SetAll(false, "CloseBT");
    }


    public void RaiseRestart()
    {
        if (!IsServer) return;

        // 이 채널을 구독(On Start 등)하는 모든 BehaviorGraph의 노드가 트리거됨
        //
        // ⚠️ 채널이 비어 있으면 여기서 NRE 가 난다 — 프리팹 배선 누락을 예외 대신 에러로 드러낸다.
        // 채널이 없어도 OpenBT 는 해 줘야 최소한 IsOpen 은 복구된다.
        if (restartChannel == null)
        {
            Debug.LogError(
                "[Enemy] restartChannel(ReStart)이 배선되지 않아 BT 재시작 이벤트를 보내지 못했습니다 — " +
                "IsOpen 만 복구합니다.", this);
        }
        else
        {
            restartChannel.SendEventMessage();
            Debug.Log("[Enemy/진단] ReStart 이벤트 발행 — BT의 On Start 구독 노드가 트리거된다.", this);
        }

        // ⚠️ IsOpen 만 켜면 되돌아간다. Idle 상태 분기가 `IsOver == true → IsOpen = false` 를
        // 실행하고, 그래프에는 IsOver 를 false 로 쓰는 노드가 없기 때문이다(쓰기 1곳, 값 true 뿐).
        // 그래서 전투를 이어가려면 IsOver 를 여기서 내려 줘야 한다. OpenBT 보다 먼저 내린다 —
        // 순서가 뒤바뀌면 같은 프레임에 다시 닫힐 수 있다.
        ClearIsOver();
        OpenBT();
    }

    void ClearIsOver()
    {
        if (isOverVariables.Count == 0)
        {
            Debug.LogWarning(
                $"[Enemy] BT에 {isOverVariableName} 변수가 없어 전투 종료 플래그를 내리지 못했습니다 — " +
                "Idle 분기가 BT를 다시 닫을 수 있습니다.", this);
            return;
        }

        for (int i = 0; i < isOverVariables.Count; i++)
        {
            bool before = isOverVariables[i].Value;
            isOverVariables[i].Value = false;
            Debug.Log($"[Enemy/진단] {isOverVariableName}[{i}] {before} → False (전투 재개)", this);
        }
    }

    // ── 진단 (2026-07-30) ──────────────────────────────────────────────────
    // 이전에는 대입만 하고 흔적을 남기지 않아 ① 캐시가 비어 조용한 no-op 이었는지
    // ② 값이 실제로 바뀌었는지를 구분할 수 없었다. 부활 후 재개가 "무시되는지" 판별에 필요하다.
    void SetAll(bool value, string reason)
    {
        if (isOpenVariables.Count == 0)
        {
            Debug.LogError(
                $"[Enemy] {reason} 이 아무 일도 하지 않았다 — {isOpenVariableName} 캐시가 비어 있다. " +
                "targetBTs 배선 또는 블랙보드 변수 이름을 확인할 것.", this);
            return;
        }

        for (int i = 0; i < isOpenVariables.Count; i++)
        {
            bool before = isOpenVariables[i].Value;
            isOpenVariables[i].Value = value;
            Debug.Log($"[Enemy/진단] {reason} — {isOpenVariableName}[{i}] {before} → {value}", this);
        }

        _lastObserved = value;
    }

    // BT 그래프 자신도 SetVariableValueAction 으로 IsOpen 을 쓴다(No.23 = 2곳).
    // 재개 직후 그래프가 곧바로 되돌리면 "로그는 찍히는데 아무 일도 안 난다"로 보이므로 되돌림을 잡는다.
    bool _lastObserved;

    void Update()
    {
        if (!IsServer || isOpenVariables.Count == 0) return;

        bool current = isOpenVariables[0].Value;
        if (current == _lastObserved) return;

        _lastObserved = current;
        Debug.Log(
            $"[Enemy/진단] {isOpenVariableName} 이 외부(BT 그래프)에서 {!current} → {current} 로 바뀌었다.", this);
    }
}
