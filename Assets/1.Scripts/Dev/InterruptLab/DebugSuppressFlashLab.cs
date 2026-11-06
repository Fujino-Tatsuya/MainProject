using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 허수아비 간파 = 제압 간파 재현(179 후속 3) — 랩 대상(허수아비)은 제압 칸이 없어 제압 간파 표식이 나오지 않는다.
/// 켜 두면 허수아비의 서버 간파 성공(<see cref="TrainingDummyInterrupt.ServerInterruptSucceeded"/> — 같은 판정 호출 안, 성공 사건 바로 앞)에서
/// 23호 칸 0 분기와 같은 <see cref="InterruptProductionDirector.ServerMarkSuppressInterrupt"/> 를 부른다.
/// 허수아비·판정 코드 무수정(구독만). 표식은 호스트에서만 의미가 있다 — 클라 화면의 토글은 아무것도 안 한다.
/// 11-04(할 일 211)부터 제압 간파도 단계 설정(<c>suppressTier</c>, 기본 V3) 연출이라 이 토글은 '받은 성공 = 제압 간파' 표시·제압 단계 설정 확인용이다.
/// </summary>
public sealed class DebugSuppressInterruptRepro
{
    private const float RefreshInterval = 1f;

    private readonly HashSet<TrainingDummyInterrupt> _subscribed = new HashSet<TrainingDummyInterrupt>();
    private readonly List<TrainingDummyInterrupt> _stale = new List<TrainingDummyInterrupt>();
    private float _nextRefresh;

    /// <summary>켜면 허수아비 간파 성공마다 제압 간파 표식.</summary>
    public bool Enabled;

    /// <summary>이번 Play 에서 표식을 찍은 횟수.</summary>
    public int MarkedCount { get; private set; }

    /// <summary>구독 중인 허수아비 수.</summary>
    public int DummyCount => _subscribed.Count;

    /// <summary>이 피어가 서버(호스트)인가 — 아니면 표식이 나가지 않는다.</summary>
    public static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>매 프레임 — 실제 시간 1초마다 씬의 허수아비를 다시 찾아 새로 생긴 것만 구독한다.</summary>
    public void Tick(float now)
    {
        // 219 — 켜 둔 동안 오너 판정 순간 제압 예측도 허수아비를 제압으로 본다(23호처럼 판정 순간 3단계, 서버 표식은 보험). 끄면 런타임 기본.
        InterruptProductionDirector.DebugSuppressForecastOverride = Enabled ? IsDummyForecast : null;

        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshInterval;
        Refresh();
    }

    public void Refresh()
    {
        _stale.Clear();
        foreach (TrainingDummyInterrupt dummy in _subscribed)
            if (dummy == null)
                _stale.Add(dummy);
        foreach (TrainingDummyInterrupt dummy in _stale)
            _subscribed.Remove(dummy);

        foreach (TrainingDummyInterrupt dummy in Object.FindObjectsByType<TrainingDummyInterrupt>(FindObjectsSortMode.None))
        {
            if (_subscribed.Add(dummy))
                dummy.ServerInterruptSucceeded += HandleSucceeded;
        }
    }

    // 캐시된 델리게이트(매 프레임 새로 만들지 않게).
    private static readonly System.Func<GameObject, bool> IsDummyForecast = target => target.GetComponent<TrainingDummyInterrupt>() != null;

    /// <summary>구독 전부 해제.</summary>
    public void Dispose()
    {
        InterruptProductionDirector.DebugSuppressForecastOverride = null;
        foreach (TrainingDummyInterrupt dummy in _subscribed)
            if (dummy != null)
                dummy.ServerInterruptSucceeded -= HandleSucceeded;
        _subscribed.Clear();
    }

    private void HandleSucceeded(TrainingDummyInterruptSuccess success)
    {
        if (!Enabled)
            return;

        InterruptProductionDirector.ServerMarkSuppressInterrupt();
        MarkedCount++;
    }
}

/// <summary>
/// 간파 연출 단계 재현(할 일 192) — 랩 버튼 하나로 이 화면에서 실패/근접/대쉬/제압 단계 연출을 돌려 본다(서버·슬로우·판정 없음 — 오너 연출만).
/// 시도(예측) → <see cref="ResolveDelay"/> 초(실제 시간) 뒤 결론. 시전자 = 내 플레이어, 대상 = 가장 가까운 간파 대상(허수아비 등).
/// 슬로우가 없어 줌은 안 보인다 — 줌·슬로우·팀원 화면은 실제 간파로 본다.
/// </summary>
public sealed class DebugInterruptTierPreview
{
    /// <summary>시도 → 결론 사이(초, 실제 시간).</summary>
    public const float ResolveDelay = 0.4f;

    public static readonly InterruptPresentationTier[] All =
    {
        InterruptPresentationTier.Fail, InterruptPresentationTier.Normal, InterruptPresentationTier.Dash, InterruptPresentationTier.Suppress,
    };

    private readonly List<InterruptTargetRegistry.Entry> _targets = new List<InterruptTargetRegistry.Entry>();
    private bool _pending;
    private float _resolveAt;
    private InterruptPresentationTier _tier;
    private Vector3 _flashPosition;

    /// <summary>마지막 재현 결과 문구(실패 이유 포함).</summary>
    public string Status { get; private set; } = "";

    public static string Label(InterruptPresentationTier tier) => tier switch
    {
        InterruptPresentationTier.Fail => "실패",
        InterruptPresentationTier.Normal => "간파(근접)",
        InterruptPresentationTier.Dash => "대쉬 간파",
        _ => "제압 간파",
    };

    // ---- 단계별 적중 라이트(할 일 194) — 일반·대쉬·제압 단계별 암순응 가/나·위치. 버전 V0~V3 버튼은 11-06(할 일 238) 삭제(런타임 = V3) ----

    /// <summary>설정을 고를 수 있는 단계 — 제압도 11-04(할 일 211)부터 같은 표(옛 섬광 시퀀스는 Legacy).</summary>
    public static readonly InterruptPresentationTier[] VersionTiers =
        { InterruptPresentationTier.Normal, InterruptPresentationTier.Dash, InterruptPresentationTier.Suppress };

    public static readonly InterruptDarkAdaptMode[] DarkAdaptModes = { InterruptDarkAdaptMode.ScreenPass, InterruptDarkAdaptMode.SceneLight };

    /// <summary>지금 실제 섬광 표시 — V3 데이터·프리팹이 없으면 V0 로 돌아간 것까지 보인다.</summary>
    public static string LookLabel(InterruptSuccessLook look) => look switch
    {
        InterruptSuccessLook.HitFlash => "V0",
        InterruptSuccessLook.HitLight => "V1",
        InterruptSuccessLook.HitLightWhiteout => "V2",
        InterruptSuccessLook.HitLightReference => "V3",
        _ => "?",
    };

    public static string DarkAdaptLabel(InterruptDarkAdaptMode mode) => mode == InterruptDarkAdaptMode.SceneLight ? "나 씬 조명" : "가 화면";

    /// <summary>월드 이펙트 발생 위치 버튼 순서(194 후속) = 틈 수직(기본) / 오른손 무기 / 대상.</summary>
    public static readonly InterruptHitLightOrigin[] Origins =
        { InterruptHitLightOrigin.Gap, InterruptHitLightOrigin.CasterWeapon, InterruptHitLightOrigin.Target };

    public static string OriginLabel(InterruptHitLightOrigin origin) => origin switch
    {
        InterruptHitLightOrigin.Target => "위치 대상",
        InterruptHitLightOrigin.Gap => "위치 틈",
        _ => "위치 무기",
    };

    /// <summary>선택된 버튼 글자 — 지금 값이면 [ ] 로 감싼다.</summary>
    public static string Selected(string label, bool selected) => selected ? $"[{label}]" : label;

    /// <summary>재현 시작. 디렉터·내 플레이어가 없거나 앞 시도가 아직 결론 전이면 false.</summary>
    public bool Begin(InterruptProductionDirector director, InterruptPresentationTier tier, float now)
    {
        if (director == null || _pending)
            return false;

        NetworkObject playerObject = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null
            ? NetworkManager.Singleton.LocalClient.PlayerObject
            : null;
        if (playerObject == null)
        {
            Status = "내 플레이어 없음(호스트·클라 접속 후)";
            return false;
        }

        PlayerSkillController skills = playerObject.GetComponentInChildren<PlayerSkillController>();
        PlayerInterruptSkillBase skill = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt) as PlayerInterruptSkillBase : null;
        Unit target = FindNearestTarget(playerObject.transform.position);
        Vector3 ownerPosition = playerObject.transform.position;
        _flashPosition = target != null ? target.transform.position : ownerPosition;
        Vector3 direction = _flashPosition - ownerPosition;
        direction.y = 0f;

        if (!director.DebugPreviewAttempt(tier, skill, playerObject.gameObject, target != null ? target.gameObject : null,
                direction.sqrMagnitude > 0.0001f ? direction.normalized : playerObject.transform.forward))
        {
            Status = "앞 간파 연출이 아직 진행 중";
            return false;
        }

        _tier = tier;
        _pending = true;
        _resolveAt = now + ResolveDelay;
        Status = $"재현: {Label(tier)}" + (target != null ? $" → {target.name}" : " (대상 없음 — 시전자 위치)");
        return true;
    }

    /// <summary>매 프레임(실제 시간) — 결론 시각이 되면 결론.</summary>
    public void Tick(InterruptProductionDirector director, float now)
    {
        if (!_pending || now < _resolveAt)
            return;

        _pending = false;
        if (director != null)
            director.DebugPreviewResolve(_tier, _flashPosition);
    }

    private Unit FindNearestTarget(Vector3 position)
    {
        InterruptTargetRegistry.CopyLiveTo(_targets);
        Unit best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _targets.Count; i++)
        {
            Unit unit = _targets[i].Unit;
            if (unit == null)
                continue;

            Vector3 d = unit.transform.position - position;
            float sqr = d.x * d.x + d.z * d.z;
            if (sqr < bestSqr)
            {
                best = unit;
                bestSqr = sqr;
            }
        }

        _targets.Clear();
        return best;
    }
}
