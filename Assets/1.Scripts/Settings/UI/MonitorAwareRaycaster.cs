using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 설정창 드롭다운이 **펼칠 때 새로 만드는 캔버스**(목록·바깥 클릭 차단막)용 레이캐스터.
/// 루트 캔버스가 타이틀 모니터 RT 캔버스(<see cref="TitleScreenRaycaster"/>)면 그와 똑같이 화면 좌표 → RT 픽셀로 바꿔 판정하고,
/// 아니면(인게임 ESC 의 일반 Overlay) 기본 판정을 그대로 한다 — 같은 프리팹을 두 씬이 쓰기 때문.
///
/// 🔴 왜 필요한가(팀장 10-12 Play): TMP 드롭다운은 목록·차단막에 **기본 GraphicRaycaster** 를 붙인다. 타이틀에서 그건
///    실제 마우스 좌표로 판정해 목록 항목이 안 눌렸고(값이 안 바뀜), 화면 전체 차단막이 남아 모니터 클릭을 가로챘다.
/// </summary>
[RequireComponent(typeof(Canvas))]
public sealed class MonitorAwareRaycaster : GraphicRaycaster
{
    bool? _onMonitor;

    bool OnMonitor
    {
        get
        {
            if (_onMonitor.HasValue) return _onMonitor.Value;
            Canvas c = GetComponent<Canvas>();
            Canvas root = c != null ? c.rootCanvas : null;
            _onMonitor = root != null && root.GetComponent<TitleScreenRaycaster>() != null;
            return _onMonitor.Value;
        }
    }

    public override Camera eventCamera
    {
        get
        {
            if (!OnMonitor) return base.eventCamera;
            var display = TitleMonitorDisplay.Active;
            return display != null ? display.UICamera : base.eventCamera;
        }
    }

    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        if (!OnMonitor)
        {
            base.Raycast(eventData, resultAppendList);
            return;
        }

        var display = TitleMonitorDisplay.Active;
        if (display == null || !display.TryScreenToUIPixel(eventData.position, out Vector2 pixel))
            return;

        // TitleScreenRaycaster 와 같은 규칙 — 호출 동안만 바꾸고 반드시 되돌린다.
        Vector2 original = eventData.position;
        eventData.position = pixel;
        try { base.Raycast(eventData, resultAppendList); }
        finally { eventData.position = original; }
    }

    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        _onMonitor = null;   // 템플릿 → 목록 복제 뒤 부모가 바뀐다 — 다시 판단
    }
}
