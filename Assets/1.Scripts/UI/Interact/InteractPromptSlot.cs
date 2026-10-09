using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 상호작용 키 프롬프트의 "지금 누가 어느 뷰로 무엇 위에 띄웠나" 상태. 동시에 1개만 — 마지막 Show 가 이긴다.
/// Hide 는 지금 띄운 소유자만 끌 수 있다(다른 소스가 켠 프롬프트를 실수로 끄지 않는다).
/// 소유자는 뷰 <b>프리팹</b>을 넘기고, 슬롯이 프리팹마다 인스턴스를 처음 한 번 만들어 재사용한다(대상마다 복제하지 않는다).
/// 인스턴스가 파괴됐으면(씬 정리 등) 다음 Show 때 다시 만든다.
/// 다른 프리팹으로 Show 가 오면 이전 인스턴스는 더 이상 <see cref="View"/> 가 아니므로 스스로 숨는다.
/// 인스턴스 생성은 주입받아 MonoBehaviour 밖 순수 상태로 두고 EditMode 테스트로 고정한다.
/// </summary>
public sealed class InteractPromptSlot
{
    readonly Func<InteractPromptView, InteractPromptView> instantiate;
    readonly Dictionary<InteractPromptView, InteractPromptView> instances = new Dictionary<InteractPromptView, InteractPromptView>();

    /// <param name="instantiate">프리팹 → 표시 인스턴스. 프리팹마다 처음 한 번(또는 인스턴스가 파괴된 뒤) 부른다.</param>
    public InteractPromptSlot(Func<InteractPromptView, InteractPromptView> instantiate)
    {
        this.instantiate = instantiate;
    }

    public object Owner { get; private set; }
    /// <summary>지금 표시 중인 뷰 인스턴스(프리팹 아님).</summary>
    public InteractPromptView View { get; private set; }
    public Transform Target { get; private set; }
    public Vector3 WorldOffset { get; private set; }

    public bool IsShowing => Owner != null;

    /// <summary>owner 가 prefab 의 인스턴스로 target 위에 프롬프트를 띄운다. 프리팹이나 대상이 없으면 그 소유자의 프롬프트를 끈다.</summary>
    public void Show(object owner, InteractPromptView prefab, Transform target, Vector3 worldOffset)
    {
        if (owner == null)
            return;

        InteractPromptView view = target != null ? GetOrCreateInstance(prefab) : null;
        if (view == null)
        {
            Hide(owner);
            return;
        }

        Owner = owner;
        View = view;
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
    /// view 가 이번 프레임에 그릴 월드 위치. view 가 지금 표시 인스턴스가 아니면 false.
    /// 소유자·대상이 파괴됐으면(Hide 없이 사라진 경우) 비우고 false.
    /// </summary>
    public bool TryGetWorldAnchor(InteractPromptView view, out Vector3 anchor)
    {
        anchor = default;
        if (!IsShowing)
            return false;
        if (Target == null || (Owner is Object unityOwner && unityOwner == null))
        {
            Clear();
            return false;
        }
        if (view == null || !ReferenceEquals(view, View))
            return false;

        anchor = Target.position + WorldOffset;
        return true;
    }

    private InteractPromptView GetOrCreateInstance(InteractPromptView prefab)
    {
        if (prefab == null)
            return null;
        if (instances.TryGetValue(prefab, out InteractPromptView cached) && cached != null)
            return cached;

        InteractPromptView created = instantiate != null ? instantiate(prefab) : null;
        if (created != null)
            instances[prefab] = created;
        return created;
    }

    private void Clear()
    {
        Owner = null;
        View = null;
        Target = null;
        WorldOffset = default;
    }
}
