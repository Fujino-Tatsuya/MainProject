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
