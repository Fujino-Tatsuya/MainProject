using UnityEngine;

/// <summary>
/// 로컬 전용 상호작용 키 프롬프트(F 아이콘) 서비스. 네트워크 동기화 없음 — 각 피어가 자기 로컬 플레이어 기준으로만 부른다.
/// 동시에 1개만 띄우고 마지막 Show 가 이긴다. Hide 는 소유자 기준이라 다른 소스가 켠 것을 끄지 않는다.
/// 뷰는 대상 오브젝트의 자식으로 놓인 <c>Assets/2.Prefabs/UI/InteractPrompt.prefab</c> 인스턴스다 — 서비스는 만들지 않고 고르기만 한다.
/// 외형은 그 프리팹, 높이는 대상 안 인스턴스의 로컬 위치로 정한다.
/// 소비처(게이트·부활·상자 등)는 "지금 F 를 누르면 실행될 대상"이 바뀔 때 Show/Hide 를 부르고, 파괴·비활성 시 Hide 한다.
/// </summary>
public static class InteractPrompt
{
    static readonly InteractPromptSlot slot = new InteractPromptSlot();
    static bool warnedMissingView;

    /// <summary>owner 가 view 를 띄운다. view 가 null 이면 경고 1회 후 표시하지 않는다(owner 의 기존 프롬프트도 끈다).</summary>
    public static void Show(object owner, InteractPromptView view)
    {
        if (view == null && owner != null && !warnedMissingView)
        {
            warnedMissingView = true;
            Debug.LogWarning($"[InteractPrompt] {owner} 가 프롬프트 뷰 없이 Show 했습니다 — 프롬프트를 띄우지 않습니다. " +
                             "대상 오브젝트 자식에 InteractPrompt.prefab 인스턴스가 있는지 확인하세요.", owner as Object);
        }

        slot.Show(owner, view);
    }

    /// <summary>owner 가 띄운 프롬프트일 때만 끈다. OnDestroy·종료 중에 불러도 안전하다.</summary>
    public static void Hide(object owner) => slot.Hide(owner);

    public static bool IsShownBy(object owner) => slot.IsShownBy(owner);

    /// <summary>view 가 지금 표시 뷰인가. <see cref="InteractPromptView"/> 가 매 프레임 묻는다.</summary>
    public static bool IsDisplaying(InteractPromptView view) => slot.IsDisplaying(view);
}
