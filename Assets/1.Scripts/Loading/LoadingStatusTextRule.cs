using UnityEngine;

/// <summary>
/// 로딩 화면 상태 문구(<see cref="NetworkLoadingScreenView"/> statusText)를 언제 바꿀지 — 275 후속("상태 문구가 너무 자주 바뀐다").
/// 단계 문구는 최소 표시 시간 동안 유지하고(그 사이 지나간 단계는 건너뜀), 준비(Ready)·완료는 기다리지 않는다.
/// 준비 문구 순환은 준비 시작부터 <c>interval</c> 마다 다음 칸(예전 = 0.5초 고정, 시작 칸도 시계에 따라 아무 칸).
/// </summary>
public static class LoadingStatusTextRule
{
    /// <summary>지금 다음 단계 문구로 바꿔도 되는지. 준비·완료는 늘 즉시, 그 밖은 지금 문구를 <paramref name="minSeconds"/> 이상 보였을 때만.</summary>
    public static bool CanSwitch(NetworkLoadingPhase next, float shownSeconds, float minSeconds)
    {
        if (next == NetworkLoadingPhase.Ready || next == NetworkLoadingPhase.Completed)
        {
            return true;
        }

        return shownSeconds >= minSeconds;
    }

    /// <summary>준비 문구 칸 — 준비 시작부터 <paramref name="interval"/> 초마다 다음 칸, 끝나면 처음으로. 칸이 없으면 0.</summary>
    public static int ReadyIndex(float sinceReadySeconds, float interval, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int step = Mathf.FloorToInt(Mathf.Max(0f, sinceReadySeconds) / Mathf.Max(0.1f, interval));
        return step % count;
    }
}
