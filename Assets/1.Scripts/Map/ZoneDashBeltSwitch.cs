using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 존 대시 벨트 켜고 끄기의 순수 규칙(할 일 257 — Unity·NGO 비의존, EditMode <c>ZoneDashBeltRuleTests</c>).
/// <list type="bullet">
/// <item>패널이 <b>전부</b> 활성이면 켠다. 하나라도 꺼져 있으면(0~3개) 꺼짐.</item>
/// <item>핵심 몬스터(건틀렛 봇) 처치를 한 번이라도 보면 그 뒤로는 패널과 무관하게 영원히 끈다(래치 — 다시 켜지지 않음).</item>
/// </list>
/// </summary>
public sealed class ZoneDashBeltRule
{
    /// <summary>처치를 본 적이 있다(래치).</summary>
    public bool Defeated { get; private set; }

    /// <summary>지금 상태로 벨트를 보일지. <paramref name="keyMonsterDefeatedNow"/> 가 한 번 true 면 이후 늘 false.</summary>
    public bool Evaluate(int activatedMask, int panelCount, bool keyMonsterDefeatedNow)
    {
        if (keyMonsterDefeatedNow) Defeated = true;
        return !Defeated && AllPanelsActive(activatedMask, panelCount);
    }

    /// <summary>패널 0..<paramref name="panelCount"/>-1 비트가 모두 켜졌는가. 패널이 없으면 false(켤 조건이 없다).</summary>
    public static bool AllPanelsActive(int activatedMask, int panelCount)
    {
        if (panelCount <= 0 || panelCount > 31) return false;
        int all = (1 << panelCount) - 1;
        return (activatedMask & all) == all;
    }
}

/// <summary>
/// 존 프리팹 안 대시 벨트(<see cref="LobbyFloorDashBelt"/>, 예: <c>ZoneL_typeB/Navigate</c> 아래 4개)를 켜고 끈다 — 할 일 257, 모든 피어 로컬 표시.
/// <see cref="ZoneBridgeGate"/> 가 Awake 에서 만들고(벨트가 하나도 없으면 만들지 않음) OnDestroy 에서 정리한다 — 프리팹에 붙일 컴포넌트가 없다.
///
/// 새 RPC·NetworkVariable 없음 — 기존 복제 상태 두 개에서만 파생한다(늦게 들어온 참가자도 같은 결과):
/// <list type="bullet">
/// <item>패널 활성 = <see cref="ZoneBridgeGateManager"/> 의 복제 <c>GateState.ActivatedMask</c>. 매니저가 바뀔 때마다(등록·레이트 조인 포함) 패널마다
///   <see cref="ZoneBridgeGate.SetPanelActivatedVisual"/> 를 부르고, 게이트가 그 비트를 <see cref="SetPanelActivated"/> 로 넘긴다.</item>
/// <item>건틀렛 봇 처치 = <see cref="MapSceneDirector"/> 의 복제 열림 마스크 중 <see cref="MapSceneGate.G9_Corridor4Entry"/>. 서버 존 장부가
///   존4 핵심 몬스터(= 이 존 인스턴스의 <c>CenterIsland_GauntletBot</c> 스폰 마커로 스폰된 봇) 전원 처치(KeyDead)로 존4 클리어를 내면 디렉터가 G9 를 연다.
///   봇은 처치 뒤 디스폰돼 클라·늦은 참가자가 직접 볼 수 없으므로 봇 대신 이 복제 게이트를 본다. 갱신 신호 = <see cref="MapSceneDirector.StateChanged"/>(전 피어, 스폰 직후 1회 포함).</item>
/// </list>
/// 켜고 끄기 = 벨트 GameObject SetActive(벨트 Rebuild 가 렌더러 enabled 를 직접 쓰고, 캐릭터 선택 숨김
/// <see cref="CharacterSelectHudHider"/> 는 forceRenderingOff 를 쓰므로 어느 쪽과도 겹치지 않는다).
/// 한계: 튜토리얼 디렉터가 없거나 비활성인 맵(G9 가 열리지 않음)에선 패널 4개 뒤 켜진 채로 남는다. G9 는 존4(구역) 단위라
/// 튜토리얼처럼 이 존이 존4 일 때만 "이 존의 봇"과 같다.
/// </summary>
public sealed class ZoneDashBeltSwitch : IDisposable
{
    private readonly ZoneDashBeltRule _rule = new ZoneDashBeltRule();
    private readonly List<GameObject> _belts = new List<GameObject>();
    private readonly int _panelCount;
    private int _activatedMask;
    private bool _shown = true; // 처음 Apply(false) 가 반드시 끄도록 — 배치 상태는 켜져 있다
    private bool _disposed;

    /// <summary>지금 보이는가.</summary>
    public bool Shown => _shown;

    /// <summary>건틀렛 봇 처치를 봤다(래치).</summary>
    public bool Defeated => _rule.Defeated;

    private ZoneDashBeltSwitch(Transform root, int panelCount)
    {
        _panelCount = panelCount;
        foreach (LobbyFloorDashBelt belt in root.GetComponentsInChildren<LobbyFloorDashBelt>(true))
            if (belt != null) _belts.Add(belt.gameObject);

        MapSceneDirector.StateChanged += Refresh;
        Refresh();
    }

    /// <summary>존 루트 아래 대시 벨트가 있으면 스위치를 만들고(처음엔 끔), 없으면 null.</summary>
    public static ZoneDashBeltSwitch TryCreate(Transform zoneRoot, int panelCount)
    {
        if (zoneRoot == null || zoneRoot.GetComponentInChildren<LobbyFloorDashBelt>(true) == null) return null;
        return new ZoneDashBeltSwitch(zoneRoot, panelCount);
    }

    /// <summary>패널 i 의 복제 활성 상태(게이트가 매니저 반영 때마다 넘긴다).</summary>
    public void SetPanelActivated(int index, bool activated)
    {
        if (index < 0 || index >= 31) return;
        int mask = activated ? _activatedMask | (1 << index) : _activatedMask & ~(1 << index);
        if (mask == _activatedMask) return;
        _activatedMask = mask;
        Refresh();
    }

    private void Refresh()
    {
        if (_disposed) return;
        MapSceneDirector director = MapSceneDirector.Instance;
        bool defeated = director != null && director.IsGateOpen(MapSceneGate.G9_Corridor4Entry);
        bool show = _rule.Evaluate(_activatedMask, _panelCount, defeated);
        if (show == _shown) return;
        _shown = show;

        foreach (GameObject belt in _belts)
            if (belt != null && belt.activeSelf != show) belt.SetActive(show);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MapSceneDirector.StateChanged -= Refresh;
    }
}
