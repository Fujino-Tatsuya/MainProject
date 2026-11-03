using UnityEngine;

/// <summary>
/// 적중 라이트 연출 월드 이펙트 루트(V1·V2·V3, 할 일 194) — <c>Assets/5.VFX/Interrupt/HitLight/FX_InterruptHitLight.prefab</c>(V3 는 <c>_V3</c>).
/// 적중 지점 포인트 라이트(따뜻한 흰주황, 실시간 곡선) + 자식 파티클(불꽃·메시 파편·빛줄기, V3 는 + 중심 점·가로 빛줄). 모든 피어가 성공 순간 각자 튼다.
/// <list type="bullet">
/// <item>디렉터가 첫 재생 때 한 번 만들어 재사용한다(재생마다 생성 없음). 라이트도 첫 재생 때 자식으로 한 번 만든다.</item>
/// <item>전부 실시간(Q8) — 재생할 때마다 모든 파티클의 useUnscaledTime 을 켠다(히트스톱 중에도 움직인다).</item>
/// <item>수치는 <see cref="InterruptHitLightLook"/> — 재생마다 넣는다.</item>
/// <item>틈 수직(194 후속, 방향 있는 <see cref="InterruptHitLightPlacement"/>)이면 재생 때 자식 몇 개만 방향을 넣는다(이름으로 찾는다):
/// <c>Rays</c> = 수직선 따라 양쪽 부채(<see cref="gapRaysMesh"/> 날개 2개 메시를 ±퍼짐 각으로 여러 줄, 정렬 Local + 카메라 쪽 기울임) ·
/// <c>Streak</c> = 수직선(또는 축) 방향으로 기울임 · <c>Sparks</c> = 수직선 양쪽 원뿔로 직접 방출. 그 밖(<c>Core</c>·<c>Debris</c>·라이트)은 위치만.
/// 방향 없는 재생(무기·대상 모드)은 처음 읽어 둔 프리팹 값(정렬·메시·방출·회전)으로 되돌려 V1·V2 처음 모습 그대로다. 프리팹 값은 바꾸지 않는다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptHitLightFx : MonoBehaviour
{
    public const string RaysName = "Rays";
    public const string StreakName = "Streak";
    public const string SparksName = "Sparks";
    // 데이터가 없을 때의 틈 수직 값(SO 기본값과 같다).
    private const float DefaultRaySpread = 20f;
    private const int DefaultRayCount = 5;
    private const float DefaultSparkSpread = 30f;
    private const int FallbackSparkCount = 30;

    [Tooltip("틈 수직 빛줄기 메시 — 양쪽으로 뻗는 날개 2개(Mesh_InterruptHitLight_Streak). 비면 원래 빛줄기 메시를 기울여 쓴다")]
    [SerializeField] private Mesh gapRaysMesh;

    private ParticleSystem[] _systems;
    private SystemDefaults[] _defaults;
    private Light _light;
    private InterruptHitLightLook _look;
    private float _lightElapsed = -1f;

    // 처음 읽은 프리팹 값 — 틈 수직이 바꾸는 칸만.
    private struct SystemDefaults
    {
        public ParticleSystemRenderer Renderer;
        public ParticleSystemRenderSpace Alignment;
        public Mesh Mesh;
        public bool Emission;
        public Quaternion LocalRotation;
    }

    /// <summary>적중 지점 라이트(첫 재생 전 null) — 확인용.</summary>
    public Light HitLight => _light;
    /// <summary>라이트가 아직 꺼지는 중이다.</summary>
    public bool LightActive => _lightElapsed >= 0f;

    /// <summary>성공 순간 재생(방향 없음) — <paramref name="position"/> = 섬광 중심, <paramref name="scale"/> = 루트 스케일.</summary>
    public void Play(Vector3 position, InterruptHitLightLook look, float scale = 1f) =>
        Play(InterruptHitLightPlacement.At(position), look, scale);

    /// <summary>성공 순간 재생 — 위치 + (틈 수직이면) 방향. 방향은 이 순간 고정이다.</summary>
    public void Play(in InterruptHitLightPlacement placement, InterruptHitLightLook look, float scale = 1f)
    {
        float rootScale = Mathf.Max(0.01f, scale);
        transform.SetPositionAndRotation(placement.Position, Quaternion.identity);
        transform.localScale = Vector3.one * rootScale;
        _look = look;

        PlayParticles(placement, look);

        _light = InterruptEffectLight.Ensure(_light, transform, "HitLight");
        if (look == null)
        {
            StopLight();
            return;
        }

        // 라이트 반경은 부모 스케일을 따르지 않는다 — 루트 스케일을 직접 곱한다.
        InterruptEffectLight.Apply(_light, look.LightColor, look.LightRange * rootScale, look.LightShadows);
        _lightElapsed = 0f;
        InterruptEffectLight.SetIntensity(_light, LightIntensityAt(look, 0f));
    }

    /// <summary>강제 리셋 — 남은 입자까지 지우고 라이트를 끈다.</summary>
    public void Stop()
    {
        if (_systems != null)
        {
            for (int i = 0; i < _systems.Length; i++)
                if (_systems[i] != null)
                    _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        StopLight();
    }

    private void Update() => Tick(Time.unscaledDeltaTime);

    /// <summary>한 프레임(실시간) — 라이트 세기 곡선. 테스트는 직접 부른다.</summary>
    public void Tick(float unscaledDelta)
    {
        if (_lightElapsed < 0f)
            return;

        _lightElapsed += Mathf.Max(0f, unscaledDelta);
        float intensity = LightIntensityAt(_look, _lightElapsed);
        InterruptEffectLight.SetIntensity(_light, intensity);
        if (intensity <= 0f)
            _lightElapsed = -1f;
    }

    /// <summary>재생 후 <paramref name="elapsed"/> 초의 라이트 세기(실시간 곡선). 끝·데이터 없음 = 0.</summary>
    public static float LightIntensityAt(InterruptHitLightLook look, float elapsed) =>
        look != null ? InterruptEffectLight.Pulse(look.LightCurve, look.LightIntensity, elapsed, look.LightDuration) : 0f;

    private void PlayParticles(in InterruptHitLightPlacement placement, InterruptHitLightLook look)
    {
        EnsureSystems();
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem system = _systems[i];
            RestoreDefaults(system, _defaults[i]);
            ParticleSystem.MainModule main = system.main;
            main.useUnscaledTime = true;
            system.Clear(false);

            if (!placement.HasDirection)
            {
                system.Play(false);
                continue;
            }

            switch (system.gameObject.name)
            {
                case RaysName:
                    PlayGapRays(system, _defaults[i].Renderer, placement, look);
                    break;
                case StreakName:
                    PlayGapStreak(system, _defaults[i].Renderer, placement, look);
                    break;
                case SparksName:
                    PlayGapSparks(system, placement, look);
                    break;
                default:
                    system.Play(false);
                    break;
            }
        }
    }

    // 빛줄기 — 날개 2개 메시를 수직선(카메라 쪽 기울임)에서 화면상 ±퍼짐 각 안으로 여러 줄. 양쪽으로 뻗는 납작한 부채.
    private void PlayGapRays(ParticleSystem system, ParticleSystemRenderer renderer, in InterruptHitLightPlacement placement, InterruptHitLightLook look)
    {
        if (renderer != null)
        {
            renderer.alignment = ParticleSystemRenderSpace.Local;
            if (gapRaysMesh != null)
                renderer.mesh = gapRaysMesh;
        }

        system.transform.rotation = InterruptHitLightRules.ViewTilted(placement.Perpendicular, placement.ViewForward);
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false; // 프리팹 버스트 대신 직접 방출
        system.Play(false);

        float spread = look != null ? look.GapRaySpread : DefaultRaySpread;
        int count = look != null ? look.GapRayCount : DefaultRayCount;
        var emit = new ParticleSystem.EmitParams { position = placement.Position, applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            emit.rotation = Random.Range(-spread, spread); // 로컬 Z(= 시선) 둘레 — 화면상 각도
            system.Emit(emit, 1);
        }
    }

    // 가로 빛줄(V3) — 화면 가로 대신 수직선(또는 축)을 카메라 쪽으로 기울인 방향. 프리팹 버스트 그대로.
    private static void PlayGapStreak(ParticleSystem system, ParticleSystemRenderer renderer, in InterruptHitLightPlacement placement, InterruptHitLightLook look)
    {
        if (renderer != null)
            renderer.alignment = ParticleSystemRenderSpace.Local;
        Vector3 line = look != null && look.GapStreakAxis == InterruptHitLightStreakAxis.CasterToTarget ? placement.Axis : placement.Perpendicular;
        system.transform.rotation = InterruptHitLightRules.ViewTilted(line, placement.ViewForward);
        system.Play(false);
    }

    // 불꽃 — 프리팹 버스트 개수만큼, 수직선 양쪽 원뿔(퍼짐 각)로 직접 방출. 속도·수명·크기·색은 프리팹 값.
    private static void PlayGapSparks(ParticleSystem system, in InterruptHitLightPlacement placement, InterruptHitLightLook look)
    {
        int count = FallbackSparkCount;
        ParticleSystem.EmissionModule emission = system.emission;
        if (emission.burstCount > 0)
            count = Mathf.Max(1, Mathf.RoundToInt(emission.GetBurst(0).count.Evaluate(0f, Random.value)));
        emission.enabled = false;
        system.Play(false);

        float spread = look != null ? look.GapSparkSpread : DefaultSparkSpread;
        ParticleSystem.MinMaxCurve speed = system.main.startSpeed;
        var emit = new ParticleSystem.EmitParams { position = placement.Position, applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            Vector3 direction = InterruptHitLightRules.SparkDirection(placement.Perpendicular, (i & 1) == 0, spread, Random.value, Random.value);
            emit.velocity = direction * speed.Evaluate(0f, Random.value);
            system.Emit(emit, 1);
        }
    }

    private void EnsureSystems()
    {
        if (_systems != null)
            return;

        _systems = GetComponentsInChildren<ParticleSystem>(true);
        _defaults = new SystemDefaults[_systems.Length];
        for (int i = 0; i < _systems.Length; i++)
        {
            var renderer = _systems[i].GetComponent<ParticleSystemRenderer>();
            _defaults[i] = new SystemDefaults
            {
                Renderer = renderer,
                Alignment = renderer != null ? renderer.alignment : ParticleSystemRenderSpace.View,
                Mesh = renderer != null ? renderer.mesh : null,
                Emission = _systems[i].emission.enabled,
                LocalRotation = _systems[i].transform.localRotation,
            };
        }
    }

    private static void RestoreDefaults(ParticleSystem system, in SystemDefaults defaults)
    {
        if (defaults.Renderer != null)
        {
            defaults.Renderer.alignment = defaults.Alignment;
            defaults.Renderer.mesh = defaults.Mesh;
        }

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = defaults.Emission;
        system.transform.localRotation = defaults.LocalRotation;
    }

    private void StopLight()
    {
        _lightElapsed = -1f;
        InterruptEffectLight.SetIntensity(_light, 0f);
    }
}
