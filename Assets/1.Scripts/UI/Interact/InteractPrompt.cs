using UnityEngine;

/// <summary>
/// 로컬 전용 상호작용 키 프롬프트(F 아이콘) 서비스. 네트워크 동기화 없음 — 각 피어가 자기 로컬 플레이어 기준으로만 부른다.
/// 동시에 1개만 띄우고 마지막 Show 가 이긴다. Hide 는 소유자 기준이라 다른 소스가 켠 것을 끄지 않는다.
/// 표시 뷰(<see cref="InteractPromptView"/>)는 소유자 프리팹에 들어 있고 Show 때 소유자가 넘긴다 — 다른 뷰로 Show 가 오면 이전 뷰는 숨는다.
/// 소비처(게이트·부활·상자 등)는 "지금 F 를 누르면 실행될 대상"이 바뀔 때 Show/Hide 를 부르고, 파괴·비활성 시 Hide 한다.
/// </summary>
public static class InteractPrompt
{
    static readonly InteractPromptSlot slot = new InteractPromptSlot();
    static bool warnedMissingView;

    /// <summary>owner 가 view 로 target 위(월드 오프셋만큼)에 프롬프트를 띄운다. target 이 null 이면 owner 의 프롬프트를 끈다.
    /// view 가 null 이면 경고 1회 후 표시하지 않는다(owner 의 기존 프롬프트도 끈다).</summary>
    public static void Show(object owner, InteractPromptView view, Transform target, Vector3 worldOffset)
    {
        if (view == null && owner != null && target != null && !warnedMissingView)
        {
            warnedMissingView = true;
            Debug.LogWarning($"[InteractPrompt] {owner} 가 뷰 없이 Show 했습니다 — 프롬프트를 띄우지 않습니다. " +
                             "소유자 프리팹의 InteractPromptView 배선을 확인하세요.", owner as Object);
        }

        slot.Show(owner, view, target, worldOffset);
    }

    /// <summary>owner 가 띄운 프롬프트일 때만 끈다. OnDestroy·종료 중에 불러도 안전하다.</summary>
    public static void Hide(object owner) => slot.Hide(owner);

    public static bool IsShownBy(object owner) => slot.IsShownBy(owner);

    /// <summary>view 가 지금 표시 뷰면 그릴 월드 위치를 준다. <see cref="InteractPromptView"/> 가 매 프레임 묻는다.</summary>
    public static bool TryGetWorldAnchor(InteractPromptView view, out Vector3 anchor) => slot.TryGetWorldAnchor(view, out anchor);
}
