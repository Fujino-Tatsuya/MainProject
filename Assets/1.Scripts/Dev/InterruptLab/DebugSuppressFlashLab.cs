using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>DebugInterruptLab 의 제압 간파 섬광 전환 버튼 3개(179 후속 3).</summary>
public enum DebugSuppressFlashMode
{
    /// <summary>A6 켬 + 시퀀스 A(오늘 제작, 보라 컷).</summary>
    A,
    /// <summary>A6 켬 + 시퀀스 B(레퍼런스, 포트폴리오).</summary>
    B,
    /// <summary>A6 끔 — 제압 간파도 일반 간파 연출(②~⑥). 시퀀스 선택은 그대로 둔다.</summary>
    Off,
}

/// <summary>
/// 제압 간파 섬광 전환(179 후속 3) — 디렉터의 A6 칸·시퀀스 선택(A/B) 런타임 값만 바꾼다(프리팹·씬 저장 없음, 이 피어만).
/// </summary>
public static class DebugSuppressFlashModes
{
    public static readonly DebugSuppressFlashMode[] All = { DebugSuppressFlashMode.A, DebugSuppressFlashMode.B, DebugSuppressFlashMode.Off };

    public static DebugSuppressFlashMode Current(InterruptProductionDirector director)
    {
        if (director == null || !director.SuppressFlashEnabled)
            return DebugSuppressFlashMode.Off;
        return director.SuppressFlashVariant == SuppressFlashVariant.B ? DebugSuppressFlashMode.B : DebugSuppressFlashMode.A;
    }

    public static void Apply(InterruptProductionDirector director, DebugSuppressFlashMode mode)
    {
        if (director == null)
            return;

        if (mode == DebugSuppressFlashMode.Off)
        {
            director.SuppressFlashEnabled = false;
            return;
        }

        director.SuppressFlashEnabled = true;
        director.SuppressFlashVariant = mode == DebugSuppressFlashMode.B ? SuppressFlashVariant.B : SuppressFlashVariant.A;
    }

    public static string Label(DebugSuppressFlashMode mode) => mode switch
    {
        DebugSuppressFlashMode.A => "A: 오늘 제작(보라 컷)",
        DebugSuppressFlashMode.B => "B: 레퍼런스(포트폴리오)",
        _ => "끔(일반 간파 연출)",
    };
}

/// <summary>
/// [서버] 허수아비 간파 = 제압 간파 재현(179 후속 3) — 랩 대상(허수아비)은 제압 칸이 없어 제압 간파 표식이 나오지 않는다.
/// 켜 두면 허수아비의 서버 간파 성공(<see cref="TrainingDummyInterrupt.ServerInterruptSucceeded"/> — 같은 판정 호출 안, 성공 사건 바로 앞)에서
/// 23호 칸 0 분기와 같은 <see cref="InterruptProductionDirector.ServerMarkSuppressInterrupt"/> 를 부른다.
/// 허수아비·판정 코드 무수정(구독만). 표식은 호스트에서만 의미가 있다 — 클라 화면의 토글은 아무것도 안 한다.
/// </summary>
public sealed class DebugSuppressInterruptRepro
{
    private const float RefreshInterval = 1f;

    private readonly HashSet<TrainingDummyInterrupt> _subscribed = new HashSet<TrainingDummyInterrupt>();
    private readonly List<TrainingDummyInterrupt> _stale = new List<TrainingDummyInterrupt>();
    private float _nextRefresh;

    /// <summary>켜면 허수아비 간파 성공마다 제압 간파 표식.</summary>
    public bool Enabled;

    /// <summary>이번 Play 에서 표식을 찍은 횟수.</summary>
    public int MarkedCount { get; private set; }

    /// <summary>구독 중인 허수아비 수.</summary>
    public int DummyCount => _subscribed.Count;

    /// <summary>이 피어가 서버(호스트)인가 — 아니면 표식이 나가지 않는다.</summary>
    public static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>매 프레임 — 실제 시간 1초마다 씬의 허수아비를 다시 찾아 새로 생긴 것만 구독한다.</summary>
    public void Tick(float now)
    {
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshInterval;
        Refresh();
    }

    public void Refresh()
    {
        _stale.Clear();
        foreach (TrainingDummyInterrupt dummy in _subscribed)
            if (dummy == null)
                _stale.Add(dummy);
        foreach (TrainingDummyInterrupt dummy in _stale)
            _subscribed.Remove(dummy);

        foreach (TrainingDummyInterrupt dummy in Object.FindObjectsByType<TrainingDummyInterrupt>(FindObjectsSortMode.None))
        {
            if (_subscribed.Add(dummy))
                dummy.ServerInterruptSucceeded += HandleSucceeded;
        }
    }

    /// <summary>구독 전부 해제.</summary>
    public void Dispose()
    {
        foreach (TrainingDummyInterrupt dummy in _subscribed)
            if (dummy != null)
                dummy.ServerInterruptSucceeded -= HandleSucceeded;
        _subscribed.Clear();
    }

    private void HandleSucceeded(TrainingDummyInterruptSuccess success)
    {
        if (!Enabled)
            return;

        InterruptProductionDirector.ServerMarkSuppressInterrupt();
        MarkedCount++;
    }
}
