using UnityEngine;

/// <summary>
/// 플레이어 머리 위 체력바 규칙. 보는 사람 기준으로 내 캐릭터는 초록, 파티원은 파랑.
/// 사망·Soul 상태에서도 숨기지 않는다(체력 0 그대로 표시).
/// 체력바 옆 대시 아이콘(IOverheadDashIndicator)도 공급한다 — 로컬 플레이어가 살아 있을 때만, 준비 상태는 PlayerDashController.IsReady.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerOverheadHealthBarRule : MonoBehaviour, IOverheadHealthBarRule, IOverheadDashIndicator
{
    [Tooltip("비워두면 부모에서 찾는다.")]
    [SerializeField] Player player;
    [SerializeField] Color localPlayerColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [SerializeField] Color partyMemberColor = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("{0} = 1부터 세는 플레이어 번호")]
    [SerializeField] string labelFormat = "P{0}";

    ulong _labelClientId = ulong.MaxValue;
    string _label;
    PlayerDashController _dash;
    PlayerLifeCycleController _lifeCycle;

    void Awake()
    {
        if (player == null)
            player = GetComponentInParent<Player>();
        if (player != null)
        {
            _dash = player.GetComponent<PlayerDashController>();
            _lifeCycle = player.GetComponent<PlayerLifeCycleController>();
        }
    }

    public bool ShouldShow => player != null;

    // 오프라인(IsSpawned=false)은 혼자 하는 로컬 플레이어로 본다.
    // 소유권은 스폰 후에 확정되므로 매 프레임 판정한다.
    bool IsLocalPlayer => player != null && (!player.IsSpawned || player.IsOwner);

    public Color FillColor => IsLocalPlayer ? localPlayerColor : partyMemberColor;

    // 예측 충전은 오너만 진행시키므로 원격 플레이어 머리 위엔 띄우지 않는다.
    public bool ShowDashIcon => _dash != null && OverheadDashIconRules.ShouldShow(
        IsLocalPlayer, _lifeCycle != null, _lifeCycle != null ? _lifeCycle.State : PlayerLifeState.Alive);

    public bool DashReady => _dash != null && _dash.IsReady;

    // 복제되는 이름이 없어 OwnerClientId 로 로컬 계산한다(권한·NetworkVariable 추가 없음).
    // 출시 때는 스팀 닉네임으로 교체 예정.
    public string Label
    {
        get
        {
            if (player == null)
                return null;

            ulong clientId = player.IsSpawned ? player.OwnerClientId : 0UL;
            if (clientId != _labelClientId)
            {
                _labelClientId = clientId;
                _label = string.Format(labelFormat, clientId + 1);
            }
            return _label;
        }
    }
}
