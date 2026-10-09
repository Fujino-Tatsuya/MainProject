using UnityEngine;

/// <summary>
/// 상호작용 키 프롬프트의 "지금 누가 무엇 위에 띄웠나" 상태. 동시에 1개만 — 마지막 Show 가 이긴다.
/// Hide 는 지금 띄운 소유자만 끌 수 있다(다른 소스가 켠 프롬프트를 실수로 끄지 않는다).
/// MonoBehaviour 밖 순수 상태라 EditMode 테스트로 고정한다.
/// </summary>
public sealed class InteractPromptSlot
{
    public object Owner { get; private set; }
    public Transform Target { get; private set; }
    public Vector3 WorldOffset { get; private set; }

    public bool IsShowing => Owner != null;

    /// <summary>owner 가 target 위에 프롬프트를 띄운다. 대상이 없으면 그 소유자의 프롬프트를 끈다.</summary>
    public void Show(object owner, Transform target, Vector3 worldOffset)
    {
        if (owner == null)
            return;
        if (target == null)
        {
            Hide(owner);
            return;
        }

        Owner = owner;
        Target = target;
        WorldOffset = worldOffset;
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
    /// 이번 프레임에 그릴 월드 위치. 소유자·대상이 파괴됐으면(Hide 없이 사라진 경우) 비우고 false.
    /// </summary>
    public bool TryGetWorldAnchor(out Vector3 anchor)
    {
        anchor = default;
        if (!IsShowing)
            return false;
        if (Target == null || (Owner is Object unityOwner && unityOwner == null))
        {
            Clear();
            return false;
        }

        anchor = Target.position + WorldOffset;
        return true;
    }

    private void Clear()
    {
        Owner = null;
        Target = null;
        WorldOffset = default;
    }
}
