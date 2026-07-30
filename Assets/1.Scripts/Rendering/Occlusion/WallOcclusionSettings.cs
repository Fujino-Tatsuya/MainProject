using System;
using UnityEngine;

namespace VeyTrace.Rendering.Occlusion
{
    // 벽 투명화 튜닝값. 불투명도 계산은 전부 셰이더가 프래그먼트 단위로 하므로
    // 여기에는 곡선 파라미터와 머티리얼 매핑만 있다.
    [CreateAssetMenu(
        fileName = "WallOcclusionSettings",
        menuName = "Rendering/Wall Occlusion Settings")]
    public sealed class WallOcclusionSettings : ScriptableObject
    {
        [Header("Fade Shape")]
        [Tooltip("카메라-플레이어 시선축에서 이 거리 안쪽은 minimumOpacity로 완전히 비운다.")]
        [Min(0f)] public float innerRadius = 1.2f;

        [Tooltip("이 거리 바깥은 원래 불투명도. inner~outer 사이가 그라데이션 구간이다.")]
        [Min(0.01f)] public float outerRadius = 4.5f;

        [Tooltip("가장 많이 가려지는 지점에 남길 불투명도. 0이면 완전히 비어 보인다.")]
        [Range(0f, 1f)] public float minimumOpacity = 0.15f;

        [Tooltip("플레이어보다 뒤로 이 거리만큼 지나면 원래 불투명도로 되돌아온다.")]
        [Min(0.01f)] public float behindFalloff = 1.5f;

        [Header("Floor Guard")]
        [Tooltip("노멀이 이만큼 위를 향하면 바닥 후보로 본다. 낮출수록 경사면까지 바닥으로 취급한다.")]
        [Range(0f, 0.95f)] public float floorNormalThreshold = 0.35f;

        [Tooltip("바닥 후보가 플레이어보다 이만큼 아래에 있어야 실제로 보호한다. " +
                 "이 장치가 없으면 벽 윗면·선반·창틀 윗면까지 보호돼서 벽 윤곽만 남는다.")]
        [Min(0.01f)] public float floorGuardDepth = 0.5f;

        [Header("Opaque Objects")]
        [Tooltip("이 이름 조각을 포함한 오브젝트(또는 그 부모)는 머티리얼을 교체하지 않아 항상 불투명하다. " +
                 "밟고 다니는 면이 대상이다.")]
        [SerializeField]
        private string[] excludedNameFragments = { "trenchcover", "slope" };

        /// <summary>
        /// 이름으로 투명화 대상에서 제외할지 판단한다.
        ///
        /// ⚠️ 왜 머티리얼이 아니라 이름으로 빼는가 — 참호 덮개·경사면은 벽과 <b>같은 머티리얼</b>
        /// (MA_lay·Generic_01_A·MA_prop* 등)을 공유한다. 저작 단계의 머티리얼 이름 제외
        /// (일반 바닥이 쓰는 방식)로 빼면 같은 머티리얼을 쓰는 실제 벽까지 불투명해진다.
        ///
        /// ⚠️ 왜 셰이더 Floor Guard로 안 되는가 — 그쪽은 노멀이 위를 향하는 면 중
        /// <see cref="floorGuardDepth"/>만큼 플레이어보다 아래에 있는 것만 보호한다.
        /// 밟고 다니는 면은 플레이어 발밑 높이라 그 조건에서 탈락한다. 임계값을 낮추면
        /// 벽 윗면·창틀까지 보호돼 벽 윤곽만 남는다(과거에 실제로 그랬다).
        /// </summary>
        public bool IsExcludedByName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName) || excludedNameFragments == null)
                return false;

            for (int i = 0; i < excludedNameFragments.Length; i++)
            {
                string fragment = excludedNameFragments[i];
                if (string.IsNullOrEmpty(fragment))
                    continue;

                if (objectName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        [Header("Runtime Materials")]
        [SerializeField] private Material[] sourceMaterials = Array.Empty<Material>();
        [SerializeField] private Material[] occlusionMaterials = Array.Empty<Material>();

        public bool HasValidMaterialMappings =>
            sourceMaterials != null &&
            occlusionMaterials != null &&
            sourceMaterials.Length > 0 &&
            sourceMaterials.Length == occlusionMaterials.Length;

        // current가 매핑된 원본이면 대응 변종을, 이미 변종이면 그대로 돌려준다(멱등).
        public bool TryResolveOcclusionMaterial(Material current, out Material variant)
        {
            variant = null;
            if (current == null || !HasValidMaterialMappings)
                return false;

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                if (current == sourceMaterials[i])
                {
                    variant = occlusionMaterials[i];
                    return variant != null;
                }

                if (current == occlusionMaterials[i])
                {
                    variant = current;
                    return true;
                }
            }

            return false;
        }

        public void ConfigureMaterialMappings(Material[] sources, Material[] variants)
        {
            sourceMaterials = sources ?? Array.Empty<Material>();
            occlusionMaterials = variants ?? Array.Empty<Material>();
        }

        private void OnValidate()
        {
            innerRadius = Mathf.Max(0f, innerRadius);
            outerRadius = Mathf.Max(innerRadius + 0.01f, outerRadius);
            minimumOpacity = Mathf.Clamp01(minimumOpacity);
            behindFalloff = Mathf.Max(0.01f, behindFalloff);
            floorNormalThreshold = Mathf.Clamp(floorNormalThreshold, 0f, 0.95f);
            floorGuardDepth = Mathf.Max(0.01f, floorGuardDepth);
        }
    }
}
