using UnityEngine;

/// <summary>
/// 어쌔신 특성 그림자 회귀(ASN_2_RETURN) 연출 — 복귀할 출발점에 꽂힌 그림자 단검 표식(2026-11-04 민경, PLAN.md · handoff-shadow-return-vfx.md).
/// <c>Player_Assassin/VFX/ShadowReturn</c> 에 붙는다. 판정·수치 무관, 새 네트워크 없음 — 전 피어가 각자 본다:
/// - 켜짐 = Q 칸이 후속(복귀)으로 열림(<see cref="PlayerSkillController.IsFollowUpOpen"/>, 복제) + 원래 Q 가 이 피어에서 저장한 출발점
///   (<see cref="AssassinDashStrikeSkill.LocalReturnOrigin"/>, 돌진 시작 위치). 일반·변신 Q 모두(장치가 지금 칸의 원래 Q 를 알려 준다).
/// - 복귀 돌진이 시작되면(그 피어의 복귀 단계가 출발점을 가져감) <see cref="fadeOutReturn"/> 로, 칸이 그냥 닫히면(만료·R·사망) <see cref="fadeOutExpire"/> 로 사라진다.
/// - 남은 시간은 복제되지 않아 근사한다 — 창 시작 = 칸 열림 + 첫 돌진 시간(준비 + 거리/속도), 길이 = 원래 Q 의 창 길이.
///   마지막 <see cref="warnTime"/>초는 흐려지며 깜빡인다.
/// 페이드는 표식 <b>메시</b> 재질 <c>_BaseColor</c> 알파에 배율을 곱한다(MaterialPropertyBlock). 비어 있는 칸은 연출이 빠진다.
/// 단검 림(<c>Mesh/Rim</c>, <c>VFX/RimOverlay</c>)은 <c>_BaseColor</c> 가 없어 같은 배율을 <c>_Fade</c> 로 받는다(다른 재질엔 무시되는 값).
/// 검은 오러(파티클, 2026-11-09 민경 — PLAN.md 「그림자 회귀 표식에 검은 오러」)는 그 페이드·깜빡임에서 빠진다 —
/// 표식이 나오면 방출로 차오르고, 사라질 때 방출만 멈춰 남은 연기가 흩어진 뒤 표식을 파괴한다.
/// </summary>
public sealed class AssassinShadowReturnView : MonoBehaviour
{
    [Tooltip("출발점 표식 — FX_ShadowReturn_Marker_Shadow(루트 = 발밑, +Z = 첫 돌진 방향).")]
    [SerializeField] private GameObject markerPrefab;

    [SerializeField, Min(0f)] private float fadeIn = 0.15f;

    [Tooltip("복귀 돌진이 시작될 때 사라지는 길이(초).")]
    [SerializeField, Min(0f)] private float fadeOutReturn = 0.2f;

    [Tooltip("만료·R 전환·사망(칸 닫힘)으로 사라지는 길이(초).")]
    [SerializeField, Min(0f)] private float fadeOutExpire = 0.4f;

    [Tooltip("창 끝 몇 초 전부터 흐려지며 깜빡이나.")]
    [SerializeField, Min(0f)] private float warnTime = 1f;

    [Tooltip("첫 돌진 준비 시간(초) — 창 시작 근사에 더한다(데이터 prepare 와 맞출 것).")]
    [SerializeField, Min(0f)] private float dashPrepare = 0.1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private PlayerSkillController controller;
    private Player owner;
    private AssassinDashStrikeSkill source;   // 지금 보이는 표식을 연 원래 Q
    private GameObject marker;
    private ParticleSystem[] markerParticles;   // 검은 오러 — 메시 페이드에서 빠지고 방출 정지로 사라진다
    private MaterialPropertyBlock block;
    private float alpha, target, fadeLength;
    private float windowEnd = -1f;
    private bool shown;    // 지금 열린 칸에 대해 표식을 냈다
    private bool hidden;   // 그 표식을 이미 사라지게 했다(복귀 시작)
    private AssassinDashStrikeSkill shownSource;   // 일반·변신 Q 가 각자 회차를 센다
    private int shownVersion = -1;   // 낸 출발점의 저장 회차 — 만료 뒤 남은 옛 출발점으로 다시 내지 않는다

    private void Awake()
    {
        controller = GetComponentInParent<PlayerSkillController>();
        owner = GetComponentInParent<Player>();
    }

    private void OnDisable()
    {
        if (marker != null)
            Destroy(marker);
        marker = null;
        markerParticles = null;
        shown = hidden = false;
        source = null;
        alpha = target = 0f;
    }

    private void Update()
    {
        if (controller == null)
            return;

        bool open = controller.IsFollowUpOpen(PlayerSkillSlot.Main);
        if (!open)
        {
            // 칸이 닫혔다 — 복귀가 출발점을 가져갔으면 복귀 시작, 아니면 만료·R·사망.
            if (shown && !hidden)
                Hide(source != null && !source.LocalReturnOrigin.HasValue ? fadeOutReturn : fadeOutExpire);
            shown = hidden = false;
        }
        else if (!shown)
            TryShow();
        else if (!hidden && (source == null || !source.LocalReturnOrigin.HasValue))
            Hide(fadeOutReturn);   // 복귀 돌진 시작(칸이 아직 열려 있어도)

        TickMarker();
    }

    // 칸이 열렸고 이 피어가 출발점을 저장했으면 낸다(저장이 늦으면 다음 프레임에 다시).
    private void TryShow()
    {
        PlayerSkillBase followUp = controller.GetSkill(PlayerSkillSlot.Main);
        var dash = controller.GetFollowUpSource(followUp) as AssassinDashStrikeSkill;
        if (dash == null || !dash.LocalReturnOrigin.TryPeek(out Vector3 origin) || (dash == shownSource && dash.LocalReturnOrigin.Version == shownVersion))
            return;

        source = dash;
        shown = true;
        hidden = false;
        shownSource = dash;
        shownVersion = dash.LocalReturnOrigin.Version;
        Vector3 forward = owner != null ? owner.transform.position - origin : Vector3.zero;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
            forward = owner != null ? owner.transform.forward : Vector3.forward;
        forward.y = 0f;
        Quaternion rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;

        if (marker != null)
            Destroy(marker);
        marker = markerPrefab != null ? Instantiate(markerPrefab, origin, rotation) : null;
        markerParticles = marker != null ? marker.GetComponentsInChildren<ParticleSystem>(true) : null;

        AssassinDashStrikeSkillData data = dash.DashData;
        float dashTime = dashPrepare + (data != null ? data.DashDuration : 0f);
        windowEnd = Time.time + dashTime + dash.FollowUpWindowSeconds;
        alpha = 0f;
        target = 1f;
        fadeLength = fadeIn;
        Apply();
    }

    private void Hide(float length)
    {
        hidden = true;
        windowEnd = -1f;
        target = 0f;
        fadeLength = length;

        // 오러는 방출만 멈춘다 — 이미 나온 연기는 자기 수명대로 흩어진다.
        if (markerParticles != null)
            foreach (ParticleSystem ps in markerParticles)
                if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    private void TickMarker()
    {
        if (marker == null)
            return;
        if (!Mathf.Approximately(alpha, target))
            alpha = fadeLength <= 0f ? target : Mathf.MoveTowards(alpha, target, Time.deltaTime / fadeLength);
        Apply();
        // 메시가 다 흐려지고 남은 오러 입자도 다 흩어졌을 때 파괴한다.
        if (alpha <= 0f && target <= 0f && !AnyParticleAlive())
        {
            Destroy(marker);
            marker = null;
            markerParticles = null;
        }
    }

    private bool AnyParticleAlive()
    {
        if (markerParticles == null)
            return false;
        foreach (ParticleSystem ps in markerParticles)
            if (ps != null && ps.IsAlive(false))
                return true;
        return false;
    }

    private void Apply()
    {
        if (marker == null)
            return;
        float remain = windowEnd >= 0f ? windowEnd - Time.time : float.MaxValue;
        float k = alpha * WarnFactor(remain, warnTime, Time.time);
        block ??= new MaterialPropertyBlock();
        foreach (Renderer r in marker.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer)
                continue;   // 오러 — 페이드·깜빡임·렌더러 끄기 없음(방출 정지로 사라진다)
            Material m = r.sharedMaterial;
            Color c = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            c.a *= k;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetFloat(FadeId, k);   // 림(VFX/RimOverlay)은 _BaseColor 가 없고 _Fade 로 흐려진다
            r.SetPropertyBlock(block);
            r.enabled = k > 0.001f;
        }
    }

    /// <summary>남은 시간 경고 배율 — 마지막 <paramref name="warn"/>초 동안 1 → 0.5 로 흐려지며 초당 4번 깜빡(0.55~1 배). 미리보기 테스터와 같은 식.</summary>
    public static float WarnFactor(float remain, float warn, float time)
    {
        if (warn <= 0f || remain >= warn)
            return 1f;
        float p = 1f - Mathf.Clamp01(remain / warn);
        float fade = Mathf.Lerp(1f, 0.5f, p);
        float blink = 0.775f + 0.225f * Mathf.Cos(time * Mathf.PI * 2f * 4f);
        return fade * blink;
    }
}
