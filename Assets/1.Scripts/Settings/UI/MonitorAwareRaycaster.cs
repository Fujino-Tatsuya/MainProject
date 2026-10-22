using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 설정창 드롭다운이 **펼칠 때 새로 만드는 캔버스**(목록·바깥 클릭 차단막)용 레이캐스터.
/// 루트 캔버스가 타이틀 모니터 RT 캔버스(<see cref="TitleScreenRaycaster"/>)면 그와 똑같이 화면 좌표 → RT 픽셀로 바꿔 판정하고,
/// 아니면(로비·인게임 ESC 의 일반 Overlay) 기본 판정을 그대로 한다 — 같은 설정창(DDOL GameMenu)을 모든 씬이 쓰기 때문.
///
/// 🔴 왜 필요한가(팀장 10-12 Play): TMP 드롭다운은 목록·차단막에 **기본 GraphicRaycaster** 를 붙인다. 타이틀에서 그건
///    실제 마우스 좌표로 판정해 목록 항목이 안 눌렸고(값이 안 바뀜), 화면 전체 차단막이 남아 모니터 클릭을 가로챘다.
/// </summary>
[RequireComponent(typeof(Canvas))]
public sealed class MonitorAwareRaycaster : GraphicRaycaster
{
    // 🔴 매번 판정한다(PLAN-settings-ddol 10-22) — 설정창이 DDOL GameMenu 하나라 루트에 TitleScreenRaycaster 가 **꺼진 채** 늘 붙어 있다.
    //    "컴포넌트가 있는가"로 보면 로비·인게임(Overlay)도 모니터로 판정해 목록 항목·차단막이 아무 클릭도 못 받았다(검토 10-22).
    //    지금 모니터 RT 에 그리는 중일 때(루트의 TitleScreenRaycaster 가 켜져 있고 모니터가 살아 있을 때)만 모니터 판정.
    bool OnMonitor
    {
        get
        {
            Canvas c = GetComponent<Canvas>();
            Canvas root = c != null ? c.rootCanvas : null;
            if (root == null || TitleMonitorDisplay.Active == null) return false;
            TitleScreenRaycaster title = root.GetComponent<TitleScreenRaycaster>();
            return title != null && title.isActiveAndEnabled;
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
}
