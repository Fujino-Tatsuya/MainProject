using System;
using UnityEngine;

/// <summary>
/// 포드 클립 애니 이벤트 수신(할 일 106) — 발사대·포드 Animator 와 같은 GameObject 에 런타임으로 붙는다(<see cref="PodAnimatorDriver"/> 가 붙인다, 프리팹 무수정).
/// 클립 이벤트 함수 이름 = <see cref="FunctionName"/>. 이벤트는 FBX(SVN, SM_DropPod_LaunchStation_Combined_v001) 임포트 설정의 클립 이벤트에 있다 —
/// 사용자가 임포트 창 Animation 탭에서 끌어 옮겨 튜닝한다(처음 넣기·검사: Tools/Lobby/포드 애니 이벤트 메뉴).
/// 어느 클립의 이벤트인지는 이벤트가 실린 클립 이름으로 안다(문자열 인자는 클립 정보가 없을 때만 쓰는 예비).
/// 모든 피어에서 불린다 — 단계 확정은 받는 쪽이 서버에서만 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PodAnimEventRelay : MonoBehaviour
{
    /// <summary>클립 이벤트 함수 이름.</summary>
    public const string FunctionName = nameof(PodClipEvent);

    /// <summary>이벤트가 실린 클립 이름(= Animator 상태 이름).</summary>
    public event Action<string> Fired;

    // 애니메이션 이벤트.
    public void PodClipEvent(AnimationEvent evt)
    {
        AnimationClip clip = evt.animatorClipInfo.clip;
        string clipName = clip != null ? clip.name : evt.stringParameter;
        if (string.IsNullOrEmpty(clipName))
            return;

        Fired?.Invoke(clipName);
    }
}
