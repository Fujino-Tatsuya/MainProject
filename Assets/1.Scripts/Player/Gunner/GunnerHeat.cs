using System;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 거너 패시브 — 과열. 서버가 기준점(<see cref="GunnerHeatState"/>)을 발사·E 때만 쓰고 전 피어에 복제한다.
/// 현재 과열도·단계·과열 여부는 각 피어가 같은 함수로 계산한다 — 오너 HUD 가 매 프레임 복제 없이 즉시 움직인다.
/// (character_gunner.md §5 · D7, PLAN-gunner.md G3)
/// </summary>
public class GunnerHeat : BaseNetworkBehaviour, IPassiveTooltipProvider
{
    [SerializeField] private GunnerHeatData data;

    private readonly NetworkVariable<GunnerHeatState> state = new NetworkVariable<GunnerHeatState>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 오프라인(네트워크 없이 실행) 경로용 로컬 사본
    private GunnerHeatState offlineState;

    private int lastStage;
    private bool lastOverheated;

    /// <summary>[전 피어] 단계가 바뀔 때(0~3). VFX·HUD 훅.</summary>
    public event Action<int> StageChanged;

    /// <summary>[전 피어] 과열 진입(true)·해제(false). VFX 훅(연출은 민경).</summary>
    public event Action<bool> OverheatChanged;

    public GunnerHeatData Data => data;
    public ISkillTooltipSource PassiveTooltip => data;
    public float MaxHeat => data != null ? data.MaxHeat : 1f;
    public float CurrentHeat => data != null && HeatEnabled ? GunnerHeatModel.HeatAt(State, Now(), data) : 0f;
    public float Normalized => Mathf.Clamp01(CurrentHeat / MaxHeat);
    public bool IsOverheated => data != null && HeatEnabled && GunnerHeatModel.OverheatedAt(State, Now(), data);

    /// <summary>현재 단계(0~3). 과열 상태면 3. 정온 운용이면 0.</summary>
    public int CurrentStage => data != null && HeatEnabled ? GunnerHeatModel.StageAt(State, Now(), data) : 0;

    /// <summary>Q·궁 시전 시작 순간의 단계 저장용(§5.6). 이후 값이 바뀌어도 호출자가 들고 있는 값은 그대로다.</summary>
    public int CaptureStage() => CurrentStage;

    public float StageDamageMultiplier(int stage) => data != null ? data.StageDamageMultiplier(stage) : 1f;

    /// <summary>
    /// 2차 과열 모드(할 일 127 3/4) — 한계 돌파·축열 포격·정온 운용. 서버·클라가 같은 특성 행을 읽는다.
    /// 피해표·과열 증가량은 스킬이 이 값으로 계산하고, 정온 운용의 과열 비활성은 이 컴포넌트가 직접 건다(<see cref="HeatEnabled"/>).
    /// </summary>
    public GunnerHeatTraits Traits => GunnerHeatTraits.From(TraitModifiers);

    /// <summary>
    /// 과열 로직이 켜져 있는가. 정온 운용이면 false — 증가(<see cref="ServerAddShot"/>)·냉각(<see cref="ServerResetToZero"/>)을 하지 않고
    /// 현재 열·단계·과열 상태 질의가 0/false 다(복제 상태를 0 으로 덮어쓰지 않는다). 과열 UI 는 이 값으로 숨는다.
    /// </summary>
    public bool HeatEnabled => Traits.HeatEnabled;

    private PlayerTraitModifiers TraitModifiers => traitModifiers != null ? traitModifiers : traitModifiers = PlayerTraitModifiers.For(this);
    private PlayerTraitModifiers traitModifiers;

    private GunnerHeatState State => IsNetworkActive ? state.Value : offlineState;

    /// <summary>[서버] 기본 공격 1회 발사분 증가(적중 여부 무관 — §5.2). 정온 운용이면 없음.</summary>
    public void ServerAddShot(float amount)
    {
        if (!HasStateAuthority || data == null || !HeatEnabled)
            return;
        Write(GunnerHeatModel.AddShot(State, Now(), amount, data));
    }

    /// <summary>[서버] E 냉각 — 과열도 0, 과열 해제. 정온 운용이면 냉각 로직이 없다(E 는 백스텝만).</summary>
    public void ServerResetToZero()
    {
        if (!HasStateAuthority || !HeatEnabled)
            return;
        Write(GunnerHeatModel.Reset(Now()));
    }

    /// <summary>
    /// [서버] 축열 포격 — 지금 남은 열을 모두 소모하고 소모 비율(0~1)을 돌려준다. 열 0·과열 잠금 해제를 같은 호출에서 쓴다(Q 발사 커밋과 원자).
    /// 권한이 없거나 과열 로직이 꺼져 있으면 0(소모 없음).
    /// </summary>
    public float ServerSpendAll()
    {
        if (!HasStateAuthority || data == null || !HeatEnabled)
            return 0f;
        Write(GunnerHeatModel.SpendAll(State, Now(), data, out float spentRatio));
        return spentRatio;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // 런 시작·재생성 시 0(§5.7). 플레이어는 씬마다 새로 스폰되므로 스폰 시점이 곧 초기화 시점이다.
        if (IsServer)
            state.Value = GunnerHeatModel.Reset(Now());
    }

    private void Write(GunnerHeatState next)
    {
        if (IsNetworkActive)
            state.Value = next;
        else
            offlineState = next;
    }

    private void Update()
    {
        if (data == null)
            return;

        int stage = CurrentStage;
        if (stage != lastStage)
        {
            lastStage = stage;
            StageChanged?.Invoke(stage);
        }

        bool overheated = IsOverheated;
        if (overheated != lastOverheated)
        {
            lastOverheated = overheated;
            OverheatChanged?.Invoke(overheated);
        }
    }

    // 상태이상·보호막과 같은 시간 도메인.
    private double Now()
        => NetworkClock.Instance != null
            ? NetworkClock.Instance.GameNow
            : (NetworkManager != null && IsNetworkActive ? NetworkManager.ServerTime.Time : Time.timeAsDouble);
}
