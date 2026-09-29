using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace VeyTrace.Rendering.Occlusion
{
    // 한 구역에서 함께 사라지는 벽 묶음의 표현을 담당한다.
    //
    // 렌더러별 MaterialPropertyBlock은 SRP Batcher를 깨므로 쓰지 않는다. 원본 머티리얼 종류마다
    // 런타임 인스턴스를 하나만 만들고, 같은 원본을 쓰던 모든 슬롯이 그 인스턴스를 공유한다.
    [DisallowMultipleComponent]
    public sealed class WallTransparencyGroup : MonoBehaviour
    {
        private static readonly int OpacityId = Shader.PropertyToID("_WallOcclusionOpacity");

        // 🔴 _WallOccBaseY / _WallOccFadeHeight 는 **상시 하단 그라데이션**이고 머티리얼에 박힌 값이다.
        //    이 컴포넌트는 그것을 건드리지 않는다 — 건드리면 그룹에 들어간 벽만 상시 그라데이션을
        //    잃는다(2026-09-29 분리 이전에 실제로 그랬다).
        //    아래 둘은 **원본에서 인스턴스로 되읽어 맞추는 용도로만** 쓴다(SyncBottomGradientFromSource).
        //    이 컴포넌트가 값을 만들어 내지 않는다.
        private static readonly int BaseYId = Shader.PropertyToID("_WallOccBaseY");
        private static readonly int FadeHeightId = Shader.PropertyToID("_WallOccFadeHeight");

        //    여기서 쓰는 것은 구역 진입으로만 켜지는 **상단 그라데이션** 전용 프로퍼티다.
        private static readonly int ZoneBaseYId = Shader.PropertyToID("_WallOccZoneBaseY");
        private static readonly int ZoneFadeHeightId = Shader.PropertyToID("_WallOccZoneFadeHeight");

        // 이 그룹의 런타임 인스턴스에서만 켜는 셰이더 키워드.
        //
        // 벽용 Material Variant 를 따로 만들지 않는 이유가 이것이다. 벽과 바닥이 같은 원본
        // 머티리얼(Generic_01_A)을 쓰는데, 그룹에 담긴 렌더러만 인스턴스를 받으므로 키워드도
        // 그것들에만 붙는다. 바닥·파이프는 원본 그대로라 디더 코드가 컴파일에서 빠진 채 그려진다.
        // 벽 프리팹의 머티리얼을 손으로 교체할 일도 없어진다.
        //
        // 🔴 그래프에서 이 키워드는 반드시 Multi Compile 이어야 한다. Shader Feature 는
        // "에셋 머티리얼이 켜놓은 조합"만 컴파일하는데, 우리는 코드로 켜므로 켜진 에셋이 하나도
        // 없다 — 빌드에서 변종이 잘려나가 에디터에서만 동작하게 된다.
        private const string DitherKeyword = "WALL_OCCLUSION_DITHER";

        // 벽 한 층의 높이. Wall_2stack.prefab 의 자식 Y(0 / 2.5 / 5)와 존 프리팹의 벽
        // 클러스터(-5.5 / -3.0 / -0.5)에서 실측한 값이다.
        private const float WallLevelHeight = 2.5f;

        [Header("대상")]
        [Tooltip("이 그룹에서 함께 투명해질 렌더러. 서브메시 머티리얼 슬롯도 모두 검사한다.")]
        [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();

        [Header("페이드")]
        [Tooltip("구역에 들어갈 때 설정값1 → 설정값2 로 가는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float fadeInDuration = 0.2f;

        [Tooltip("구역에서 나올 때 설정값2 → 설정값1 로 돌아오는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float fadeOutDuration = 0.2f;

        // 구역 효과 = **상단 그라데이션 설정값을 설정값1 ↔ 설정값2 로 보간**하는 것이다.
        // Opacity 는 쓰지 않는다 — 항상 1 이다.
        //
        // 상시 하단 그라데이션(_WallOccBaseY / _WallOccFadeHeight)은 머티리얼에 박힌 값이고
        // 여기서 만들지 않는다. 원본에서 읽어 인스턴스에 맞추기만 한다.
        //
        // 🔴 두 설정값은 **보간해도 자연스러운 쌍**이어야 한다. FadeHeight 는 부호가 방향이고
        //    0 이 "끔" 이라, 0 → -1 처럼 잡으면 시작 직후(-0.001 근처)에 BaseY 위가 통째로
        //    사라졌다가 서서히 제자리를 찾는 "팝" 이 생긴다. 두 값의 **부호를 같게** 두고
        //    BaseY 와 크기만 다르게 잡는 것이 안전하다.
        [Header("구역 상단 그라데이션 — 설정값1(밖) ↔ 설정값2(안)")]
        [Tooltip("설정값1 — 구역 밖일 때의 기준 높이 오프셋. 이 컴포넌트 위치의 Y에 더해진다.")]
        [SerializeField] private float baseYOffsetOutside;

        [Tooltip("설정값1 — 구역 밖일 때의 높이차.\n" +
                 "🔴 부호가 방향(음수: 위가 투명 / 양수: 아래가 투명). 0 은 그라데이션 끔.")]
        [SerializeField] private float fadeHeightOutside;

        [Tooltip("설정값2 — 구역 안일 때의 기준 높이 오프셋.")]
        [FormerlySerializedAs("baseYOffset")]
        [FormerlySerializedAs("zoneBaseYOffset")]
        [SerializeField] private float baseYOffsetInside;

        [Tooltip("설정값2 — 구역 안일 때의 높이차.\n" +
                 "🔴 부호가 방향(음수: 위가 투명 / 양수: 아래가 투명). 0 은 그라데이션 끔.")]
        [FormerlySerializedAs("fadeHeight")]
        [FormerlySerializedAs("zoneFadeHeight")]
        [SerializeField] private float fadeHeightInside = -1f;

        private readonly Dictionary<Material, Material> _instancesBySource =
            new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _sourceByInstance =
            new Dictionary<Material, Material>();
        private int _transparencyRequestCount;

        /// <summary>0 = 설정값1(구역 밖), 1 = 설정값2(구역 안). 그 사이를 fadeIn/fadeOut 으로 오간다.</summary>
        private float _zoneBlend;
        private bool _initialized;

        private void Awake()
        {
            InitializeMaterials();
        }

        private void Update()
        {
            if (!_initialized || _instancesBySource.Count == 0)
                return;

            // 구역 안이면 설정값2(1) 쪽으로, 밖이면 설정값1(0) 쪽으로 간다.
            // 거리는 항상 0~1 이라 duration 이 그대로 "끝까지 가는 데 걸리는 초" 가 된다.
            float target = IsTransparencyActive ? 1f : 0f;
            if (Mathf.Approximately(_zoneBlend, target))
                return;

            float duration = target > _zoneBlend ? fadeInDuration : fadeOutDuration;
            _zoneBlend = duration <= 0f
                ? target
                : Mathf.MoveTowards(_zoneBlend, target, Time.deltaTime / duration);

            ApplyHeightGradientToAll();
        }

        // 여러 구역이 같은 그룹을 공유할 수 있으므로 요청 수가 0에서 1이 될 때만 페이드 아웃한다.
        public void AcquireTransparency()
        {
            if (!_initialized)
                InitializeMaterials();

            if (_transparencyRequestCount == int.MaxValue)
            {
                Debug.LogError("[WallTransparencyGroup] 투명화 참조 카운트가 허용 범위를 넘었습니다.", this);
                return;
            }

            _transparencyRequestCount++;
        }

        // 요청 수가 1에서 0이 될 때만 원래 불투명도로 돌아간다.
        public void ReleaseTransparency()
        {
            if (_transparencyRequestCount <= 0)
            {
                Debug.LogWarning("[WallTransparencyGroup] 짝이 없는 투명화 해제 요청을 무시합니다.", this);
                return;
            }

            _transparencyRequestCount--;
        }

        private void InitializeMaterials()
        {
            if (_initialized)
                return;

            _initialized = true;
            _zoneBlend = 0f;

            if (targetRenderers == null)
                return;

            for (int rendererIndex = 0; rendererIndex < targetRenderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = targetRenderers[rendererIndex];
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                bool changed = false;

                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material source = materials[slot];
                    if (source == null || !source.HasProperty(OpacityId))
                        continue;

                    if (!_instancesBySource.TryGetValue(source, out Material instance))
                    {
                        instance = Instantiate(source);
                        instance.name = $"{source.name} (Wall Transparency Group)";
                        instance.SetFloat(OpacityId, 1f);
                        instance.EnableKeyword(DitherKeyword);
                        // 사전에 먼저 넣어야 ApplyHeightGradient 안의 원본 역참조가 성립한다.
                        _instancesBySource.Add(source, instance);
                        _sourceByInstance.Add(instance, source);
                        ApplyHeightGradient(instance);
                    }

                    materials[slot] = instance;
                    changed = true;
                }

                if (changed)
                    targetRenderer.sharedMaterials = materials;
            }
        }

        /// <summary>에디터 시각화(Group Painter 의 "벽 그룹" 표시)가 읽는다. 런타임은 쓰지 않는다.</summary>
        public IReadOnlyList<Renderer> TargetRenderers => targetRenderers;

        private bool IsTransparencyActive => _transparencyRequestCount > 0;

        // 상단 그라데이션 = 설정값1 ↔ 설정값2 를 _zoneBlend 로 보간한 것.
        // Opacity 는 항상 1 이다 — 구역 효과를 불투명도가 아니라 그라데이션으로 낸다.
        //
        // 기준 높이는 월드 Y 라야 한다. 존 프리팹의 벽은 존 로컬 원점 기준 음수 좌표에
        // 놓이고 존은 런타임에 배치되므로, 셰이더에 절대값을 박을 수 없다.
        private void ApplyHeightGradient(Material instance)
        {
            SyncBottomGradientFromSource(instance);

            if (instance.HasProperty(OpacityId))
                instance.SetFloat(OpacityId, 1f);

            float baseYOffset = Mathf.Lerp(baseYOffsetOutside, baseYOffsetInside, _zoneBlend);
            float fadeHeight = Mathf.Lerp(fadeHeightOutside, fadeHeightInside, _zoneBlend);

            if (instance.HasProperty(ZoneBaseYId))
                instance.SetFloat(ZoneBaseYId, transform.position.y + baseYOffset);

            if (instance.HasProperty(ZoneFadeHeightId))
                instance.SetFloat(ZoneFadeHeightId, fadeHeight);
        }

        // 🔴 인스턴스는 Instantiate 로 뜬 원본의 **스냅샷**이라 상시 하단 값까지 복사해 간다.
        //    그 뒤 원본(Generic_01_A 등)의 하단 값을 고쳐도 인스턴스는 따라오지 않는다.
        //    그러면 **같은 벽인데 그룹에 든 것만 옛 값**으로 남아 화면이 뒤죽박죽 섞인다.
        //    스냅샷 시점이 "플레이어가 그 구역에 처음 들어간 때" 라 그룹마다 제각각이기도 하다.
        //    그래서 인스턴스를 만질 때마다 원본에서 다시 읽어 맞춘다.
        private void SyncBottomGradientFromSource(Material instance)
        {
            if (!_sourceByInstance.TryGetValue(instance, out Material source) || source == null)
                return;

            if (instance.HasProperty(BaseYId) && source.HasProperty(BaseYId))
                instance.SetFloat(BaseYId, source.GetFloat(BaseYId));

            if (instance.HasProperty(FadeHeightId) && source.HasProperty(FadeHeightId))
                instance.SetFloat(FadeHeightId, source.GetFloat(FadeHeightId));
        }

        private void ApplyHeightGradientToAll()
        {
            foreach (Material instance in _instancesBySource.Values)
            {
                if (instance != null)
                    ApplyHeightGradient(instance);
            }
        }

        private void OnDestroy()
        {
            // 렌더러가 그룹보다 오래 살아남는 경우에도 파괴된 머티리얼 참조를 남기지 않는다.
            int rendererCount = targetRenderers?.Length ?? 0;
            for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
            {
                Renderer targetRenderer = targetRenderers[rendererIndex];
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                bool changed = false;

                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material current = materials[slot];
                    if (current != null && _sourceByInstance.TryGetValue(current, out Material source))
                    {
                        materials[slot] = source;
                        changed = true;
                    }
                }

                if (changed)
                    targetRenderer.sharedMaterials = materials;
            }

            foreach (Material instance in _instancesBySource.Values)
            {
                if (instance != null)
                    Destroy(instance);
            }

            _instancesBySource.Clear();
            _sourceByInstance.Clear();
        }

        private void OnValidate()
        {
            fadeInDuration = Mathf.Max(0f, fadeInDuration);
            fadeOutDuration = Mathf.Max(0f, fadeOutDuration);

            // 높이차는 클램프하지 않는다 — 부호가 방향이고, 0 은 "끔" 이라
            // 셋 다 의미 있는 값이다.

#if UNITY_EDITOR
            // 인스펙터에서 값을 바꾸면 즉시 반영한다. 이게 없으면 구역 진입/이탈 때만 써지므로
            // "값을 바꿨는데 안 변한다 / 나갔다 와야 변한다" 로 보인다.
            if (Application.isPlaying && _initialized)
                ApplyHeightGradientToAll();

            if (targetRenderers == null)
                return;

            var warnedMaterials = new HashSet<Material>();
            for (int rendererIndex = 0; rendererIndex < targetRenderers.Length; rendererIndex++)
            {
                Renderer targetRenderer = targetRenderers[rendererIndex];
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || material.HasProperty(OpacityId) || !warnedMaterials.Add(material))
                        continue;

                    Debug.LogWarning(
                        $"[WallTransparencyGroup] '{targetRenderer.name}'의 머티리얼 " +
                        $"'{material.name}'에 _WallOcclusionOpacity 프로퍼티가 없어 투명해지지 않습니다.",
                        targetRenderer);
                }
            }
#endif
        }
    }
}
