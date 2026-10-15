using UnityEngine;

/// <summary>
/// 머리 위 대시 아이콘의 표시·색 판정. MonoBehaviour 밖 순수 계산이라 EditMode 테스트로 고정한다.
/// 준비/미준비 두 상태만 있다 — 채움·숫자 없음.
/// </summary>
public static class OverheadDashIconRules
{
    /// <summary>
    /// 로컬(오너) 플레이어이고 살아 있을 때만 보인다. 사망 연출·Soul(대시 금지)·영구 사망(관전)은 숨긴다.
    /// 생명주기 컴포넌트가 없으면 살아 있는 것으로 본다.
    /// </summary>
    public static bool ShouldShow(bool isLocalPlayer, bool hasLifeCycle, PlayerLifeState lifeState)
        => isLocalPlayer && (!hasLifeCycle || lifeState == PlayerLifeState.Alive);

    /// <summary>준비 = 밝은 색, 미준비 = 어두운 색. 실제 색은 바의 직렬화 값이 정한다.</summary>
    public static Color Tint(bool ready, Color readyColor, Color notReadyColor)
        => ready ? readyColor : notReadyColor;
}
