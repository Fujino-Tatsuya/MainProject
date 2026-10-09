using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 상호작용 키 프롬프트의 "지금 누가 어느 뷰를 띄웠나" 상태. 동시에 1개만 — 마지막 Show 가 이긴다.
/// Hide 는 지금 띄운 소유자만 끌 수 있다(다른 소스가 켠 프롬프트를 실수로 끄지 않는다).
/// 뷰는 대상 오브젝트의 자식으로 미리 놓여 있는 씬 인스턴스다 — 슬롯은 만들지 않고 고르기만 한다.
/// 다른 뷰로 Show 가 오면 이전 뷰는 더 이상 <see cref="View"/> 가 아니므로 스스로 숨는다.
/// MonoBehaviour 밖 순수 상태로 두고 EditMode 테스트로 고정한다.
/// </summary>
public sealed class InteractPromptSlot
{
    public object Owner { get; private set; }
    public InteractPromptView View { get; private set; }

    public bool IsShowing => Owner != null;

    /// <summary>owner 가 view 를 띄운다. view 가 없으면 그 소유자의 프롬프트를 끈다.</summary>
    public void Show(object owner, InteractPromptView view)
    {
        if (owner == null)
            return;

        if (view == null)
        {
            Hide(owner);
            return;
        }

        Owner = owner;
        View = view;
    }

    /// <summary>지금 띄운 소유자일 때만 끈다. 껐으면 true.</summary>
    public bool Hide(object owner)
    {
        if (owner == null || !ReferenceEquals(owner, Owner))
            return false;

        Clear();
        return true;
    }

    public bool IsShownBy(object owner) => owner != null && ReferenceEquals(owner, Owner);

    /// <summary>
    /// view 가 이번 프레임에 표시 뷰인가. 소유자·뷰가 파괴됐으면(Hide 없이 사라진 경우) 비우고 false.
    /// </summary>
    public bool IsDisplaying(InteractPromptView view)
    {
        if (!IsShowing)
            return false;
        if (View == null || (Owner is Object unityOwner && unityOwner == null))
        {
            Clear();
            return false;
        }

        return view != null && ReferenceEquals(view, View);
    }

    private void Clear()
    {
        Owner = null;
        View = null;
    }
}
