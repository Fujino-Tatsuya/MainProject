using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [클라] 간파 대상 카운터 창 복제값의 열림/닫힘 기록(할 일 132) — 오너 클라의 간파 시도 예측이 '열린 뒤 경과·추정 길이'를 얻는다.
/// <list type="bullet">
/// <item>대상(<see cref="IInterruptWindowReplica"/>)이 복제값 OnValueChanged 에서 <see cref="Record"/> 를 부른다. 새 전송 없음 — 이미 오는 복제값의 도착 시각만 적는다.</item>
/// <item>기록 시각 = 받은 순간의 <c>NetworkManager.ServerTime</c>(≈ 서버가 바꾼 시각), 추정 시각 = <c>LocalTime</c>(≈ 지금 보낸 요청이 서버에 닿는 시각)
///       — 왕복 지연만큼 경과를 서버 승인 시각 쪽으로 맞춘다.</item>
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

    /// <summary>[클라] 복제된 창 전이 기록. 서버·오프라인이면 아무것도 안 한다(서버는 InterruptLagMonitor 가 기록).</summary>
    public static void Record(GameObject owner, bool open)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (owner == null || nm == null || !nm.IsListening || nm.IsServer)
            return;

        if (!Histories.TryGetValue(owner, out InterruptWindowHistory history))
        {
            if (Histories.Count >= PruneThreshold)
                PruneDestroyed();
            history = new InterruptWindowHistory();
            Histories.Add(owner, history);
        }

        history.Record(nm.ServerTime.Time, open);
    }

    /// <summary>[클라] 대상이 디스폰된다 — 기록을 지운다.</summary>
    public static void Forget(GameObject owner)
    {
        if (owner != null)
            Histories.Remove(owner);
    }

    /// <summary>
    /// [오너] 간파 시도 예측용 창 추정 — 열린 뒤 경과·추정 길이(초). 모르면 -1.
    /// 서버(호스트)는 서버 기록, 클라는 복제값 기록에서 구한다.
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

        if (Histories.TryGetValue(owner, out InterruptWindowHistory history))
            history.Estimate(nm.LocalTime.Time, openNow, out openElapsed, out expectedDuration);
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
