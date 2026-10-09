using UnityEngine;

/// <summary>
/// 로컬 전용 상호작용 키 프롬프트(F 아이콘) 서비스. 네트워크 동기화 없음 — 각 피어가 자기 로컬 플레이어 기준으로만 부른다.
/// 동시에 1개만 띄우고 마지막 Show 가 이긴다. Hide 는 소유자 기준이라 다른 소스가 켠 것을 끄지 않는다.
/// 소유자는 표시 뷰 <b>프리팹</b>(기본 <c>Assets/2.Prefabs/UI/InteractPrompt.prefab</c>)을 참조 필드로 들고 Show 때 넘긴다.
/// 서비스가 프리팹마다 인스턴스를 한 번 만들어(DontDestroyOnLoad) 재사용한다 — 외형은 그 프리팹 하나만 고치면 모든 대상에 반영된다.
/// 소비처(게이트·부활·상자 등)는 "지금 F 를 누르면 실행될 대상"이 바뀔 때 Show/Hide 를 부르고, 파괴·비활성 시 Hide 한다.
/// </summary>
public static class InteractPrompt
{
    static readonly InteractPromptSlot slot = new InteractPromptSlot(CreateInstance);
    static bool warnedMissingPrefab;

    /// <summary>owner 가 prefab 의 인스턴스로 target 위(월드 오프셋만큼)에 프롬프트를 띄운다. target 이 null 이면 owner 의 프롬프트를 끈다.
    /// prefab 이 null 이면 경고 1회 후 표시하지 않는다(owner 의 기존 프롬프트도 끈다).</summary>
    public static void Show(object owner, InteractPromptView prefab, Transform target, Vector3 worldOffset)
    {
        if (prefab == null && owner != null && target != null && !warnedMissingPrefab)
        {
            warnedMissingPrefab = true;
            Debug.LogWarning($"[InteractPrompt] {owner} 가 프롬프트 프리팹 없이 Show 했습니다 — 프롬프트를 띄우지 않습니다. " +
                             "소유자의 프롬프트 프리팹 참조(InteractPrompt.prefab) 배선을 확인하세요.", owner as Object);
        }

        slot.Show(owner, prefab, target, worldOffset);
    }

    /// <summary>owner 가 띄운 프롬프트일 때만 끈다. OnDestroy·종료 중에 불러도 안전하다.</summary>
    public static void Hide(object owner) => slot.Hide(owner);

    public static bool IsShownBy(object owner) => slot.IsShownBy(owner);

    /// <summary>view 가 지금 표시 인스턴스면 그릴 월드 위치를 준다. <see cref="InteractPromptView"/> 가 매 프레임 묻는다.</summary>
    public static bool TryGetWorldAnchor(InteractPromptView view, out Vector3 anchor) => slot.TryGetWorldAnchor(view, out anchor);

    // 씬 전환에도 살아남게 루트째 DontDestroyOnLoad. 씬 정리 등으로 파괴되면 슬롯이 다음 Show 때 다시 부른다.
    static InteractPromptView CreateInstance(InteractPromptView prefab)
    {
        InteractPromptView instance = Object.Instantiate(prefab);
        instance.name = prefab.name;
        if (Application.isPlaying)
            Object.DontDestroyOnLoad(instance.transform.root.gameObject);
        return instance;
    }
}
