using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>로비 상호작용 오브젝트 종류 — PLAN-48 §4.6.</summary>
public enum LobbyInteractableKind
{
    /// <summary>
    /// [퇴역] 옛 준비 패드 — 할 일 86 T2b 에서 출격 집결 구역으로 바뀌어 씬에 없고 <see cref="LobbyInteractor"/> 도 고르지 않는다.
    /// qa/LobbyDemo 하네스가 이 값을 참조해 남겼다(직렬화 값 0 유지).
    /// </summary>
    Ready = 0,

    /// <summary>F = 캐릭터 선택 패널(<see cref="CharacterSelectPanel"/>, C12). 고르면 서버가 거리(영역)를 다시 보고 디스폰 → 같은 자리 새 Variant 로 교체한다.</summary>
    CharacterSelect = 1,
}

/// <summary>
/// 로비에 놓는 상호작용 오브젝트 표식(씬 오브젝트, 네트워크 없음) — PLAN-48 §4.6, C10.
/// 판정은 <see cref="LobbyInteractor"/>(로컬 프롬프트·F)와 <see cref="LobbySession"/>(서버 재검증)이 <see cref="Registry"/> 를 훑어 한다.
/// 사정거리 = <see cref="area"/>(영역 볼륨)가 있으면 그 박스 안(할 일 161 — 캐릭터 선택 = 라커룸 <c>Area_Locker</c> 와 같은 볼륨),
/// 없으면 이 Transform 위치 기준 평면 반경. 프롬프트 뷰는 자식 InteractPrompt.prefab 인스턴스다.
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyInteractable : MonoBehaviour
{
    /// <summary>서버 재검증 여유(m) — 클라가 반경·영역 경계에서 누른 순간 서버 위치가 조금 뒤처진 몫(영역이면 바닥 footprint 만 넓힌다).</summary>
    public const float ServerReachTolerance = 0.5f;

    private static readonly List<LobbyInteractable> registry = new List<LobbyInteractable>();

    [SerializeField] private LobbyInteractableKind kind = LobbyInteractableKind.Ready;

    [Tooltip("상호작용 영역(할 일 161). 채우면 반경 대신 이 볼륨 박스 안(높이 포함)에서만 프롬프트·F 가 된다. 캐릭터 선택 = Area_Locker.")]
    [SerializeField] private LobbyAreaVolume area;

    [Tooltip("반경 수치 원본(area 가 비었을 때만). 비우면 radiusOverride, 그것도 0 이하면 2m.")]
    [SerializeField] private LobbyRules rules;

    [Tooltip("0 이하 = LobbyRules.InteractRadius 를 쓴다(area 가 비었을 때만).")]
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
    public LobbyAreaVolume Area => area;

    public float Radius
    {
        get
        {
            if (radiusOverride > 0f)
                return radiusOverride;
            return rules != null ? rules.InteractRadius : FallbackRadius;
        }
    }

    /// <summary>
    /// 이 위치가 사정거리 안인가 — 영역이 있으면 박스 안(footprint 만 <paramref name="tolerance"/> 만큼 넓힘, 높이는 엄격),
    /// 없으면 평면 반경 + 여유. 클라 프롬프트·안내 스텝은 여유 0, 서버 재검증은 <see cref="ServerReachTolerance"/>.
    /// </summary>
    public bool IsInReach(Vector3 playerPosition, float tolerance)
    {
        return area != null
            ? area.ContainsPoint(playerPosition, tolerance)
            : NearestInteractable.IsWithinReach(playerPosition, Position, Radius, tolerance);
    }

    /// <summary>클라 고르기 후보 — 영역이면 안/밖으로, 반경이면 반경 그대로(<see cref="NearestInteractable.Pick"/>).</summary>
    public NearestInteractable.Candidate ToCandidate(Vector3 playerPosition, bool usable)
    {
        return area != null
            ? NearestInteractable.Candidate.InArea(Position, area.ContainsPoint(playerPosition), usable)
            : new NearestInteractable.Candidate(Position, Radius, usable);
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

    /// <summary>[서버] 이 종류의 켜진 오브젝트 중 하나라도 사정거리(+여유) 안인가. 오브젝트가 없으면 false. 거리는 로그용 평면 거리(기준점까지).</summary>
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
            if (target.IsInReach(playerPosition, ServerReachTolerance))
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
        if (area != null)
            return;   // 영역은 LobbyAreaVolume 기즈모가 그린다

        Gizmos.color = kind == LobbyInteractableKind.Ready ? Color.green : Color.cyan;
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}
