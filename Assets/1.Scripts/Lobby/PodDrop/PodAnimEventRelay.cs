using System;
using UnityEngine;

/// <summary>
/// 포드 클립 애니 이벤트 수신(할 일 106) — 발사대·포드 Animator 와 같은 GameObject 에 런타임으로 붙는다(<see cref="PodAnimatorDriver"/> 가 붙인다, 프리팹 무수정).
/// 클립 이벤트 함수 이름 = <see cref="FunctionName"/>. 이벤트는 FBX(SVN, SM_DropPod_LaunchStation_Combined_v001) 임포트 설정의 클립 이벤트에 있다 —
/// 사용자가 임포트 창 Animation 탭에서 끌어 옮겨 튜닝한다(처음 넣기·검사: Tools/Lobby/포드 애니 이벤트 메뉴).
/// 어느 클립의 이벤트인지는 이벤트가 실린 클립 이름으로 안다(문자열 인자는 클립 정보가 없을 때만 쓰는 예비).
/// 정수 인자 = 이벤트 종류 — <see cref="KindStage"/>(0, 다음 단계) · <see cref="KindSpeedUp"/>(1, 로비 발사 클립 가속 1단계, 할 일 106 추가 2)
/// · <see cref="KindSpeedUp2"/>(2, 로비 발사 클립 가속 2단계, 할 일 121) · <see cref="KindDescent"/>(3, 로비 포드 하강 시작 — 끝 페이드 기준, 할 일 198).
/// 모든 피어에서 불린다 — 단계 확정은 받는 쪽이 서버에서만 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PodAnimEventRelay : MonoBehaviour
{
    /// <summary>클립 이벤트 함수 이름.</summary>
    public const string FunctionName = nameof(PodClipEvent);

    /// <summary>이벤트 종류(정수 인자) — 다음 단계로 넘긴다(기존 이벤트 전부).</summary>
    public const int KindStage = 0;

    /// <summary>이벤트 종류(정수 인자) — 여기부터 발사 클립을 가속 1단계 배율로 재생한다(단계는 넘기지 않는다).</summary>
    public const int KindSpeedUp = 1;

    /// <summary>이벤트 종류(정수 인자) — 여기부터 발사 클립을 가속 2단계 배율로 재생한다(단계는 넘기지 않는다).</summary>
    public const int KindSpeedUp2 = 2;

    /// <summary>
    /// 이벤트 종류(정수 인자) — 로비 발사 클립에서 포드가 아래로 내려가기 시작하는 순간(할 일 198). 여기 + 끝 대기 뒤 끝 페이드 아웃.
    /// 실시간 콜백으로는 아무것도 안 한다 — 각 피어가 클립의 이 이벤트 위치(<see cref="AnimationClip.events"/>)를 읽어 복제 시각으로 계산한다
    /// (<see cref="PodDropLobbyTimeline.LaunchElapsedAtClipSeconds"/>, 늦게 본 피어도 같은 값).
    /// </summary>
    public const int KindDescent = 3;

    /// <summary>이벤트가 실린 클립 이름(= Animator 상태 이름), 이벤트 종류(<see cref="KindStage"/>·<see cref="KindSpeedUp"/>·<see cref="KindSpeedUp2"/>·<see cref="KindDescent"/>).</summary>
    public event Action<string, int> Fired;

    // 애니메이션 이벤트.
    public void PodClipEvent(AnimationEvent evt)
    {
        AnimationClip clip = evt.animatorClipInfo.clip;
        string clipName = clip != null ? clip.name : evt.stringParameter;
        if (string.IsNullOrEmpty(clipName))
            return;

        Fired?.Invoke(clipName, evt.intParameter);
    }
}
