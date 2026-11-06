using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Color 속성의 어느 성분에 배율을 곱하나. Float 속성은 이 값과 무관하게 값 자체에 곱한다.</summary>
public enum EffectMaterialColorChannel
{
    /// <summary>a(알파)만 — 예: Shader_IntegratedEffect 의 "Color"(<c>_TintColor</c>), a 가 실제 알파.</summary>
    Alpha = 0,
    /// <summary>r·g·b(밝기)만 — 예: HS_Blend_CG 의 "Color"(<c>_Color</c>), a 는 셰이더가 안 쓰고 RGB 만 BaseColor·Emission 으로 간다.</summary>
    Rgb = 1,
}

/// <summary>
/// 머티리얼 속성 배율 한 줄 — 이 머티리얼을 쓰는 렌더러의 <see cref="property"/> 에 <see cref="multiplier"/> 를 곱한다(2026-11-06 민경).
/// 속성이 Color 면 <see cref="channel"/>(a 또는 RGB)에, Float 면 값 자체에 곱한다(셰이더마다 알파가 있는 곳이 다르다 — 예: <c>_TintColor</c>.a · <c>_Opacity</c>).
/// HDR 이 큰 재질은 알파만 낮춰도 하얗게 타므로 밝기(예: <c>_Color</c> RGB)도 같은 머티리얼에 한 줄 더 둔다.
/// 같은 머티리얼의 여러 줄은 한 복사본에 함께 걸린다.
/// </summary>
[Serializable]
public struct EffectMaterialAlphaRule
{
    [Tooltip("이 머티리얼을 쓰는 렌더러 슬롯만 바꾼다")]
    public Material material;

    [Tooltip("곱할 속성 이름(인스펙터 표시 이름이 아니라 셰이더 속성 이름 — 예: \"Color\" 칸 = _TintColor 또는 _Color). " +
             "Color 면 아래 성분에, Float 면 그 값에 곱한다. 셰이더가 실제로 쓰는 속성이어야 효과가 있다")]
    public string property;

    [Tooltip("Color 속성일 때 곱할 성분. Alpha = a 만 / Rgb = 밝기(r·g·b)만. Float 속성이면 무시")]
    public EffectMaterialColorChannel channel;

    [Tooltip("원래 값에 곱할 배율(0 = 없음, 1 = 그대로)")]
    [Range(0f, 1f)] public float multiplier;

    public EffectMaterialAlphaRule(Material material, string property, float multiplier,
                                   EffectMaterialColorChannel channel = EffectMaterialColorChannel.Alpha)
    {
        this.material = material;
        this.property = property;
        this.channel = channel;
        this.multiplier = multiplier;
    }

    /// <summary>Color 에 배율을 건 결과(<paramref name="channel"/> 성분만). 순수 계산 — 테스트용.</summary>
    public static Color ScaledColor(Color original, float multiplier, EffectMaterialColorChannel channel)
    {
        if (channel == EffectMaterialColorChannel.Rgb)
        {
            original.r = Scaled(original.r, multiplier);
            original.g = Scaled(original.g, multiplier);
            original.b = Scaled(original.b, multiplier);
        }
        else
        {
            original.a = Scaled(original.a, multiplier);
        }
        return original;
    }

    /// <summary>원래 알파 × 배율(0~1 로 자른 배율). 순수 계산 — 테스트용.</summary>
    public static float Scaled(float original, float multiplier) => original * Mathf.Clamp01(multiplier);
}

/// <summary>
/// <b>머티리얼 알파 재생</b>의 인스턴스 쪽 — 규칙에 맞는 렌더러 슬롯을 알파를 곱한 머티리얼 <b>복사본</b>으로 바꾸고, 반납 직전 원래 머티리얼로 되돌린다.
/// <see cref="EffectManager"/> 만 쓴다(<see cref="EffectManager.SetMaterialAlpha"/>). <see cref="EffectRecolor"/> 와 같은 수명 규칙.
///
/// <b>왜 MaterialPropertyBlock 이 아닌가.</b> 한계 돌파 색(<see cref="EffectRecolor"/>)이 같은 렌더러에 블록을 걸고 반납 때 빈 블록으로 지운다 —
/// 서로 덮어쓴다. 게다가 흰 재질에 블록을 걸면 이 파티클 셰이더들이 다르게 그려졌다(handoff-overdrive-vfx). 머티리얼 교체는 그 둘과 겹치지 않는다.
/// 복사본은 (원본, 속성, 배율)마다 한 번만 만든다 — 풀 재사용마다 새로 만들지 않는다.
///
/// 🔴 색 지정 재생(<see cref="EffectRecolor"/>)은 처음 칠할 때 렌더러의 머티리얼 색을 원래 색으로 기억한다.
///    그래서 같은 재생에서는 <b>색을 먼저, 알파를 나중에</b> 건다(EffectManager 가 지연 파트도 그 순서로 건다).
/// </summary>
[DisallowMultipleComponent]
public sealed class EffectMaterialAlpha : MonoBehaviour
{
    // 키 = (원본, 맞는 규칙들의 "속성:배율|…" 서명)
    private static readonly Dictionary<(Material, string), Material> Copies = new Dictionary<(Material, string), Material>();
    private static readonly HashSet<(Material, string)> Warned = new HashSet<(Material, string)>();

    private struct Swapped
    {
        public Renderer renderer;
        public Material[] original;
    }

    private readonly List<Swapped> _swapped = new List<Swapped>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        foreach (Material copy in Copies.Values)
            if (copy != null) Destroy(copy);
        Copies.Clear();
        Warned.Clear();
    }

    /// <summary>인스턴스에 규칙을 건다. 이미 걸려 있으면 먼저 되돌린 뒤 다시 건다. 규칙이 비면 되돌리기만 한다.</summary>
    public static void Apply(GameObject instance, IReadOnlyList<EffectMaterialAlphaRule> rules)
    {
        if (instance == null) return;

        EffectMaterialAlpha alpha = instance.GetComponent<EffectMaterialAlpha>();
        if (rules == null || rules.Count == 0)
        {
            if (alpha != null) alpha.Restore();
            return;
        }

        if (alpha == null) alpha = instance.AddComponent<EffectMaterialAlpha>();
        alpha.Restore();
        alpha.Swap(rules);
    }

    private void Swap(IReadOnlyList<EffectMaterialAlphaRule> rules)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            Material[] shared = renderer.sharedMaterials;
            Material[] replaced = null;

            for (int m = 0; m < shared.Length; m++)
            {
                Material copy = CopyFor(shared[m], rules);
                if (copy == null) continue;
                replaced ??= (Material[])shared.Clone();
                replaced[m] = copy;
            }

            if (replaced == null) continue;
            _swapped.Add(new Swapped { renderer = renderer, original = shared });
            renderer.sharedMaterials = replaced;
        }
    }

    /// <summary>원래 머티리얼로 되돌린다. 걸린 게 없으면 no-op.</summary>
    public void Restore()
    {
        for (int i = 0; i < _swapped.Count; i++)
            if (_swapped[i].renderer != null) _swapped[i].renderer.sharedMaterials = _swapped[i].original;
        _swapped.Clear();
    }

    private static readonly List<EffectMaterialAlphaRule> Matching = new List<EffectMaterialAlphaRule>();
    private static readonly System.Text.StringBuilder KeyBuilder = new System.Text.StringBuilder();

    // 이 슬롯에 맞는 규칙들의 복사본. 같은 머티리얼의 규칙은 <b>전부 한 복사본에</b> 건다(예: 알파 _Opacity + 밝기 _Color RGB).
    // 맞는 규칙이 없거나(배율 1 포함) 적용된 속성이 하나도 없으면 null(그대로 둔다).
    private static Material CopyFor(Material source, IReadOnlyList<EffectMaterialAlphaRule> rules)
    {
        if (source == null) return null;

        Matching.Clear();
        KeyBuilder.Clear();
        for (int i = 0; i < rules.Count; i++)
        {
            EffectMaterialAlphaRule rule = rules[i];
            if (rule.material != source || string.IsNullOrWhiteSpace(rule.property)) continue;
            if (Mathf.Approximately(Mathf.Clamp01(rule.multiplier), 1f)) continue;
            rule.property = rule.property.Trim();   // 인스펙터에 붙여 넣다 섞인 탭·공백 무시
            Matching.Add(rule);
            KeyBuilder.Append(rule.property).Append(':').Append((int)rule.channel).Append(':')
                      .Append(Mathf.Clamp01(rule.multiplier).ToString("R")).Append('|');
        }
        if (Matching.Count == 0) return null;

        var key = (source, KeyBuilder.ToString());
        if (Copies.TryGetValue(key, out Material cached) && cached != null) return cached;

        Material copy = MakeCopy(source, Matching);
        if (copy != null) Copies[key] = copy;
        return copy;
    }

    private static Material MakeCopy(Material source, List<EffectMaterialAlphaRule> rules)
    {
        Material copy = null;
        for (int i = 0; i < rules.Count; i++)
        {
            string property = rules[i].property;
            float multiplier = rules[i].multiplier;

            if (source.HasColor(property))
            {
                // 같은 속성에 Alpha·Rgb 두 줄을 둘 수 있게 복사본의 현재 값에 곱한다
                copy ??= NewCopy(source);
                copy.SetColor(property, EffectMaterialAlphaRule.ScaledColor(copy.GetColor(property), multiplier, rules[i].channel));
            }
            else if (source.HasFloat(property))
            {
                copy ??= NewCopy(source);
                copy.SetFloat(property, EffectMaterialAlphaRule.Scaled(source.GetFloat(property), multiplier));
            }
            else if (Warned.Add((source, property)))
            {
                // 경고는 (머티리얼, 속성)당 1회 — 루프·연속 재생마다 찍지 않는다.
                Edit.LogWarning($"[Effect] 머티리얼 '{source.name}' 에 속성 '{property}'(Color/Float)가 없어 그 규칙을 건너뛴다.", source);
            }
        }
        return copy;
    }

    private static Material NewCopy(Material source) =>
        new Material(source) { name = $"{source.name} (other client)", hideFlags = HideFlags.DontSave };
}
