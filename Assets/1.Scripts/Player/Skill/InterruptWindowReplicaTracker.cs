using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [클라] 간파 대상 카운터 창 복제값의 열림/닫힘 기록(할 일 132) — 오너 클라의 간파 시도 예측이 '열린 뒤 경과·추정 길이'를 얻는다.
/// <list type="bullet">
/// <item>대상(<see cref="IInterruptWindowReplica"/>)이 복제값 OnValueChanged 에서 <see cref="Record"/> 를 부른다. 새 전송 없음 — 이미 오는 복제값의 도착 시각만 적는다.</item>
/// <item>기록·추정 시각 = 이 피어의 게임초(<c>Time.time</c>, 할 일 139-6) — 창 타이머·판정까지 남은 시간과 같은 시계라 슬로우 중 경과가 실시간만큼 늘지 않는다.
///       기준은 복제값이 <b>도착한</b> 순간이라 경과는 서버보다 한 방향 지연만큼 짧게(늦게) 잡힌다 — 남은 창을 그만큼 길게 보는 쪽.
///       (예전 ServerTime 기록 + LocalTime 추정은 왕복 지연을 맞췄지만 실시간이라 슬로우 중 경과가 부풀었다 — 139-5 보고.)</item>
/// <item>창 길이는 로컬로 아는 값(<see cref="InterruptWindowLengthResolver"/>) 우선, 모르면 이 기록의 관측 중앙값.</item>
/// <item>서버(호스트)는 기록하지 않고 서버 기록(<see cref="InterruptLagMonitor.EstimateWindow"/>)을 그대로 쓴다.</item>
/// </list>
/// 예측(연출)에만 쓰고 판정에는 쓰지 않는다.
/// </summary>
public static class InterruptWindowReplicaTracker
{
    private const int PruneThreshold = 32;

    private static readonly Dictionary<GameObject, InterruptWindowHistory> Histories =
        new Dictionary<GameObject, InterruptWindowHistory>();
    private static readonly List<GameObject> PruneBuffer = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Histories.Clear();

    /// <summary>
    /// [클라] 복제된 창 전이 기록. 서버·오프라인이면 아무것도 안 한다(서버는 InterruptLagMonitor 가 기록).
    /// 열림은 호스트 포함 모든 피어에서 로컬 선착순 장부(<see cref="InterruptLocalWindowClaims"/>, 할 일 143-2)를 초기화한다 — 서버 장부와 같은 복제값 전이.
    /// </summary>
    public static void Record(GameObject owner, bool open)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (owner == null || nm == null || !nm.IsListening)
            return;

        if (open)
            InterruptLocalWindowClaims.Local.WindowOpened(TargetIdOf(owner));

        if (nm.IsServer)
            return;

        if (!Histories.TryGetValue(owner, out InterruptWindowHistory history))
        {
            if (Histories.Count >= PruneThreshold)
                PruneDestroyed();
            history = new InterruptWindowHistory();
            Histories.Add(owner, history);
        }

        history.Record(Time.timeAsDouble, open);
    }

    /// <summary>[클라] 대상이 디스폰된다 — 기록을 지운다.</summary>
    public static void Forget(GameObject owner)
    {
        if (owner == null)
            return;

        Histories.Remove(owner);
        InterruptLocalWindowClaims.Local.Forget(TargetIdOf(owner));
    }

    /// <summary>로컬 선착순 장부 키 — 창 소유자의 NetworkObjectId(NetworkBehaviour.NetworkObject 와 같은 찾기). 없으면 0.</summary>
    public static ulong TargetIdOf(GameObject owner)
    {
        NetworkObject networkObject = owner != null ? owner.GetComponentInParent<NetworkObject>() : null;
        return networkObject != null ? networkObject.NetworkObjectId : 0;
    }

    /// <summary>
    /// 간파 시도 예측이 쓰는 창 관측 기록(추정 길이 중앙값의 재료)을 비운다 — 할 일 139 디버그 랩의 단일 진입점.
    /// 클라 복제값 기록과 서버 기록(<see cref="InterruptLagMonitor.ResetWindowObservations"/>)을 함께 비운다.
    /// 마지막 전이 하나는 남긴다 — 지금 열림·열린 뒤 경과는 유지되고, 추정 길이는 다음 열림→닫힘 관측 전까지 모름(-1).
    /// </summary>
    public static void ResetObservations()
    {
        foreach (InterruptWindowHistory history in Histories.Values)
            history.ClearKeepingLatest();
        InterruptLagMonitor.ResetWindowObservations();
    }

    /// <summary>
    /// [오너] 간파 시도 예측용 창 추정 — 열린 뒤 경과·추정 길이(게임초). 모르면 -1, 무기한 길이 = +∞.
    /// 서버(호스트)는 서버 기록, 클라는 복제값 기록에서 구한다. 길이 해석은 둘 다 <see cref="InterruptWindowLengthResolver"/>.
    /// </summary>
    public static void Estimate(GameObject owner, bool openNow, out float openElapsed, out float expectedDuration)
    {
        openElapsed = -1f;
        expectedDuration = -1f;
        NetworkManager nm = NetworkManager.Singleton;
        if (owner == null || nm == null || !nm.IsListening)
            return;

        if (nm.IsServer)
        {
            InterruptLagMonitor.EstimateWindow(owner, openNow, out openElapsed, out expectedDuration);
            return;
        }

        // 경과를 먼저 구한다 — 해석기(23호)가 '열릴 때의 공개 상태'를 경과로 되짚는다.
        if (Histories.TryGetValue(owner, out InterruptWindowHistory history))
            history.Estimate(Time.timeAsDouble, openNow, out openElapsed, out expectedDuration);
        expectedDuration = InterruptWindowLengthResolver.PreferLocal(
            InterruptWindowLengthResolver.Resolve(owner, false, openElapsed), expectedDuration);
    }

    private static void PruneDestroyed()
    {
        PruneBuffer.Clear();
        foreach (GameObject key in Histories.Keys)
        {
            if (key == null)
                PruneBuffer.Add(key);
        }

        foreach (GameObject key in PruneBuffer)
            Histories.Remove(key);
        PruneBuffer.Clear();
    }
}
