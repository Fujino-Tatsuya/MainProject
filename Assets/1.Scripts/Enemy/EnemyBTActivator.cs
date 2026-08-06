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

    // 부활 시 플레이어 명부를 다시 세우기 위한 변수들.
    // 그래프의 On Start 분기는 FindGroupsByTag → GetPlayerCount 로 이 둘을 채우는데,
    // 그 분기는 **시작할 때 한 번만** 돈다. 그래서 도중에 죽었다 살아나면 TargetGroup 에
    // 파괴된(또는 낡은) 참조가 남고 TotalPlayerNumber 도 옛 값 그대로다.
    // 재시작 이벤트를 받는 분기를 그래프에 따로 만드는 대신, 같은 효과를 여기서 낸다
    // (그래프를 건드리면 런타임 그래프 RID 가 통째로 재직렬화돼 diff 가 수천 줄이 된다).
    [SerializeField] string targetGroupVariableName = "TargetGroup";
    [SerializeField] string totalPlayerNumberVariableName = "TotalPlayerNumber";
    [SerializeField] string playerTag = "Player";

    [SerializeField] private ReStart restartChannel;

    readonly List<BlackboardVariable<bool>> isOpenVariables = new();
    readonly List<BlackboardVariable<bool>> isOverVariables = new();
    readonly List<BlackboardVariable<List<GameObject>>> targetGroupVariables = new();
    readonly List<BlackboardVariable<int>> totalPlayerNumberVariables = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        CacheIsOpenVariables();
    }

    void CacheIsOpenVariables()
    {
        isOpenVariables.Clear();
        isOverVariables.Clear();
        targetGroupVariables.Clear();
        totalPlayerNumberVariables.Clear();

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

            // 플레이어 명부도 그래프마다 없을 수 있다 — 같은 규칙으로 조용히 건너뛴다.
            if (bt.BlackboardReference.GetVariable<List<GameObject>>(targetGroupVariableName, out var group))
                targetGroupVariables.Add(group);

            if (bt.BlackboardReference.GetVariable<int>(totalPlayerNumberVariableName, out var total))
                totalPlayerNumberVariables.Add(total);
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
        // 플레이어 명부를 먼저 다시 세운다. IsOver 를 내려 전투를 재개시켜도 TargetGroup 이
        // 파괴된 참조를 들고 있으면 추격 대상이 없어 그 자리에 서 있게 된다.
        RefreshPlayerGroup();
        ClearIsOver();
        OpenBT();
    }

    /// <summary>
    /// TargetGroup / TotalPlayerNumber 를 현재 살아 있는 플레이어로 다시 채운다.
    /// 그래프의 FindGroupsByTag + GetPlayerCount 조합과 같은 일을 한다
    /// (FindGroupsByTag 의 onlyCountRoot=true 경로와 동일하게 root 기준으로 중복 제거).
    /// </summary>
    void RefreshPlayerGroup()
    {
        if (targetGroupVariables.Count == 0 && totalPlayerNumberVariables.Count == 0)
        {
            Debug.LogWarning(
                $"[Enemy] BT에 {targetGroupVariableName}/{totalPlayerNumberVariableName} 이 없어 " +
                "플레이어 명부를 갱신하지 못했습니다 — 부활해도 추격 대상이 비어 있을 수 있습니다.", this);
            return;
        }

        GameObject[] tagged = GameObject.FindGameObjectsWithTag(playerTag);

        // 태그가 자식에 붙는 프리팹도 있으므로 root 로 접어 중복을 제거한다.
        var roots = new List<GameObject>();
        for (int i = 0; i < tagged.Length; i++)
        {
            GameObject root = tagged[i].transform.root.gameObject;
            if (!roots.Contains(root))
                roots.Add(root);
        }

        for (int i = 0; i < targetGroupVariables.Count; i++)
        {
            // 리스트 인스턴스를 통째로 바꾸지 않고 내용만 교체한다 —
            // 그래프의 다른 노드가 같은 리스트 참조를 들고 있을 수 있다.
            List<GameObject> list = targetGroupVariables[i].Value;
            if (list == null)
            {
                list = new List<GameObject>();
                targetGroupVariables[i].Value = list;
            }

            list.Clear();
            list.AddRange(roots);
        }

        for (int i = 0; i < totalPlayerNumberVariables.Count; i++)
        {
            int before = totalPlayerNumberVariables[i].Value;
            totalPlayerNumberVariables[i].Value = roots.Count;
            Debug.Log(
                $"[Enemy/진단] {totalPlayerNumberVariableName}[{i}] {before} → {roots.Count} (명부 갱신)", this);
        }
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
