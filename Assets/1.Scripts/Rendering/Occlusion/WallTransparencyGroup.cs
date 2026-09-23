using System;
using System.Collections.Generic;
using UnityEngine;

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
        private static readonly int BaseYId = Shader.PropertyToID("_WallOccBaseY");
        private static readonly int FadeHeightId = Shader.PropertyToID("_WallOccFadeHeight");

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
        [Tooltip("구역에서 나온 뒤 원래 불투명도로 돌아오는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float fadeInDuration = 0.2f;

        [Tooltip("구역에 들어간 뒤 목표 불투명도로 사라지는 시간(초).")]
        [Min(0f)]
        [SerializeField] private float fadeOutDuration = 0.2f;

        [Tooltip("투명화가 켜졌을 때 남길 불투명도. 기존 minimumOpacity와 같은 0.15를 권장한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float targetOpacity = 0.15f;

        [Header("높이 그라데이션")]
        [Tooltip("1층 벽 바닥의 월드 Y 오프셋. 이 컴포넌트 위치의 Y에 더해 기준 높이를 만든다. " +
                 "여기가 가장 많이 사라지는 지점이다. 구역 오브젝트를 벽 바닥에 맞춰 두면 0이면 된다.")]
        [SerializeField] private float baseYOffset;

        [Tooltip("그라데이션이 끝나는 높이차. 아래가 사라지고 위가 남는 방향이다. " +
                 "기준 높이에서 이만큼 올라가면 원래대로 돌아오고, 그보다 위는 전부 그대로다. " +
                 "벽 한 층이 2.5이므로 2층에 걸쳐 복귀시키려면 5.")]
        [Min(0.01f)]
        [SerializeField] private float fadeHeight = WallLevelHeight * 2f;

        private readonly Dictionary<Material, Material> _instancesBySource =
            new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _sourceByInstance =
            new Dictionary<Material, Material>();
        private int _transparencyRequestCount;
        private float _currentOpacity = 1f;
        private bool _initialized;

        private void Awake()
        {
            InitializeMaterials();
        }

        private void Update()
        {
            if (!_initialized || _instancesBySource.Count == 0)
                return;

            float desiredOpacity = _transparencyRequestCount > 0 ? targetOpacity : 1f;
            if (Mathf.Approximately(_currentOpacity, desiredOpacity))
                return;

            float duration = desiredOpacity < _currentOpacity ? fadeOutDuration : fadeInDuration;
            float fullFadeDistance = Mathf.Abs(1f - targetOpacity);
            _currentOpacity = duration <= 0f
                ? desiredOpacity
                : Mathf.MoveTowards(
                    _currentOpacity,
                    desiredOpacity,
                    fullFadeDistance * Time.deltaTime / duration);

            ApplyOpacity(_currentOpacity);
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
            _currentOpacity = 1f;

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
                        ApplyHeightGradient(instance);
                        _instancesBySource.Add(source, instance);
                        _sourceByInstance.Add(instance, source);
                    }

                    materials[slot] = instance;
                    changed = true;
                }

                if (changed)
                    targetRenderer.sharedMaterials = materials;
            }
        }

        private void ApplyOpacity(float opacity)
        {
            foreach (Material instance in _instancesBySource.Values)
            {
                if (instance != null)
                    instance.SetFloat(OpacityId, opacity);
            }
        }

        // 기준 높이는 월드 Y 라야 한다. 존 프리팹의 벽은 존 로컬 원점 기준 음수 좌표에
        // 놓이고 존은 런타임에 배치되므로, 셰이더에 절대값을 박을 수 없다.
        private void ApplyHeightGradient(Material instance)
        {
            if (instance.HasProperty(BaseYId))
                instance.SetFloat(BaseYId, transform.position.y + baseYOffset);

            if (instance.HasProperty(FadeHeightId))
                instance.SetFloat(FadeHeightId, fadeHeight);
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
            targetOpacity = Mathf.Clamp01(targetOpacity);
            fadeHeight = Mathf.Max(0.01f, fadeHeight);

#if UNITY_EDITOR
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
