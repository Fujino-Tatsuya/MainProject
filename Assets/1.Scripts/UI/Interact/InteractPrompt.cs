using UnityEngine;

/// <summary>
/// 로컬 전용 상호작용 키 프롬프트(F 아이콘) 서비스. 네트워크 동기화 없음 — 각 피어가 자기 로컬 플레이어 기준으로만 부른다.
/// 동시에 1개만 띄우고 마지막 Show 가 이긴다. Hide 는 소유자 기준이라 다른 소스가 켠 것을 끄지 않는다.
/// 소비처(게이트·부활·상자 등)는 "지금 F 를 누르면 실행될 대상"이 바뀔 때 Show/Hide 를 부르고, 파괴·비활성 시 Hide 한다.
/// </summary>
public static class InteractPrompt
{
    static readonly InteractPromptSlot slot = new InteractPromptSlot();
    static InteractPromptView view;

    /// <summary>owner 가 target 위(월드 오프셋만큼)에 프롬프트를 띄운다. target 이 null 이면 owner 의 프롬프트를 끈다.</summary>
    public static void Show(object owner, Transform target, Vector3 worldOffset)
    {
        slot.Show(owner, target, worldOffset);
        if (slot.IsShowing && view == null)
            view = InteractPromptView.Create(slot);
    }

    /// <summary>owner 가 띄운 프롬프트일 때만 끈다. 뷰를 새로 만들지 않으므로 OnDestroy·종료 중에 불러도 안전하다.</summary>
    public static void Hide(object owner) => slot.Hide(owner);

    public static bool IsShownBy(object owner) => slot.IsShownBy(owner);
}
