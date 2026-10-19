using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>로비 상호작용 오브젝트 종류 — PLAN-48 §4.6.</summary>
public enum LobbyInteractableKind
{
    /// <summary>F = 내 준비 토글(<see cref="LobbySession.RequestToggleReady"/>). 서버가 거리를 다시 본다.</summary>
    Ready = 0,

    /// <summary>F = 캐릭터 선택 패널(C12). C10 에서는 자리만 있고 상호작용하지 않는다.</summary>
    CharacterSelect = 1,
}

/// <summary>
/// 로비에 놓는 상호작용 오브젝트 표식(씬 오브젝트, 네트워크 없음) — PLAN-48 §4.6, C10.
/// 판정은 <see cref="LobbyInteractor"/>(로컬 프롬프트·F)와 <see cref="LobbySession"/>(서버 거리 재검증)이 <see cref="Registry"/> 를 훑어 한다.
/// 기준점 = 이 Transform 위치(평면 거리). 프롬프트 뷰는 자식 InteractPrompt.prefab 인스턴스다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyInteractable : MonoBehaviour
{
    /// <summary>서버 거리 재검증 여유(m) — 클라가 반경 경계에서 누른 순간 서버 위치가 조금 뒤처진 몫.</summary>
    public const float ServerReachTolerance = 0.5f;

    private static readonly List<LobbyInteractable> registry = new List<LobbyInteractable>();

    [SerializeField] private LobbyInteractableKind kind = LobbyInteractableKind.Ready;

    [Tooltip("반경 수치 원본. 비우면 radiusOverride, 그것도 0 이하면 2m.")]
    [SerializeField] private LobbyRules rules;

    [Tooltip("0 이하 = LobbyRules.InteractRadius 를 쓴다.")]
    [Min(0f)]
    [SerializeField] private float radiusOverride;

    [Tooltip("F 프롬프트(자식 InteractPrompt.prefab 인스턴스).")]
    [SerializeField] private InteractPromptView promptView;

    [Tooltip("프롬프트 글자(InteractPrompt 인스턴스의 InteractText). 비우면 프리팹 기본 문구 그대로.")]
    [SerializeField] private TMP_Text promptLabel;

    private const float FallbackRadius = 2f;

    /// <summary>켜져 있는 로비 상호작용 오브젝트 전부(씬 순서 아님 — 켜진 순서).</summary>
    public static IReadOnlyList<LobbyInteractable> Registry => registry;

    public LobbyInteractableKind Kind => kind;
    public InteractPromptView PromptView => promptView;
    public Vector3 Position => transform.position;

    public float Radius
    {
        get
        {
            if (radiusOverride > 0f)
                return radiusOverride;
            return rules != null ? rules.InteractRadius : FallbackRadius;
        }
    }

    /// <summary>프롬프트 문구 키 — 종류(준비는 내 준비 상태)로 정한다.</summary>
    public UiString GetPromptString(bool localReady)
    {
        switch (kind)
        {
            case LobbyInteractableKind.CharacterSelect:
                return LobbyUiStrings.InteractCharacterSelect;
            default:
                return localReady ? LobbyUiStrings.InteractUnready : LobbyUiStrings.InteractReady;
        }
    }

    /// <summary>프롬프트 글자를 바꾼다. 프리팹의 LocalizedText 가 언어 변경 때 기본 문구로 되돌려도 다음 프레임에 다시 쓴다.</summary>
    public void ApplyPromptText(string text)
    {
        if (promptLabel != null && promptLabel.text != text)
            promptLabel.text = text;
    }

    /// <summary>[서버] 이 종류의 켜진 오브젝트 중 하나라도 반경(+여유) 안인가. 오브젝트가 없으면 false.</summary>
    public static bool AnyWithinServerReach(LobbyInteractableKind kind, Vector3 playerPosition, out float nearestDistance)
    {
        nearestDistance = float.MaxValue;
        var within = false;
        for (var i = 0; i < registry.Count; i++)
        {
            var target = registry[i];
            if (target == null || target.kind != kind)
                continue;

            nearestDistance = Mathf.Min(nearestDistance, NearestInteractable.PlanarDistance(playerPosition, target.Position));
            if (NearestInteractable.IsWithinReach(playerPosition, target.Position, target.Radius, ServerReachTolerance))
                within = true;
        }

        return within;
    }

    private void OnEnable()
    {
        if (!registry.Contains(this))
            registry.Add(this);
    }

    private void OnDisable()
    {
        registry.Remove(this);   // 프롬프트는 띄운 LobbyInteractor 가 다음 프레임에 끈다(소유자 = 인터랙터)
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = kind == LobbyInteractableKind.Ready ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}
