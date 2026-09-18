using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 허수아비 전용 피격 플래시.
//
// 🔴 HitFlash.cs 의 복제본이다. 갈라진 곳은 "구독하는 이벤트" 하나뿐이므로,
//    HitFlash 가 고쳐지면 두 파일을 diff 해서 같은 수정을 여기에도 옮길 것.
//
// 왜 HitFlash 를 그대로 못 쓰나 — HitFlash 는 Unit.ClientDamaged(복제된 "실제 HP 감소")를 구독한다.
// 허수아비는 죽지 않으려고 체력을 1 에서 멈추므로, 1 에 붙어 있는 동안 감소량이 0 이라
// 플래시가 아예 안 터진다. 그래서 명목 피해를 싣는 TrainingDummy.NominalDamaged 를 구독한다.
// (Unit.ClientDamaged 는 virtual 이 아니고, C# 은 파생 클래스가 기반 클래스의 이벤트를
//  발화하는 것을 허용하지 않아 상속으로는 해결되지 않는다.)
//
// - TrainingDummy.OnNetworkSpawn 이 자동 부착된 HitFlash 를 제거하고 이 컴포넌트가 대신 맡는다.
// - MaterialPropertyBlock 만 사용(머티리얼 인스턴스화 없음). URP _BaseColor / 레거시 _Color 지원.
[DisallowMultipleComponent]
[RequireComponent(typeof(TrainingDummy))]
public class TrainingDummyHitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField]
    [Tooltip("피격 순간 틴트 색.")]
    Color flashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField, Min(0.02f)]
    [Tooltip("플래시 지속 시간(초). 진입 즉시 최대 틴트 → 이 시간 동안 원색으로 복귀.")]
    float flashDuration = 0.35f;

    TrainingDummy _dummy;
    Renderer[] _renderers;
    Color[] _originalColors; // 렌더러별 원래 베이스 색(sharedMaterial 기준)
    bool _hasBaseTint;       // 베이스 틴트 오버라이드 여부
    Color _baseTint;
    int[] _propIds;          // 렌더러별 사용할 색 프로퍼티(_BaseColor 우선, 없으면 _Color, 없으면 0)
    MaterialPropertyBlock _mpb;
    Coroutine _routine;

    void Awake()
    {
        _dummy = GetComponent<TrainingDummy>();
        _mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        if (_dummy == null) _dummy = GetComponent<TrainingDummy>();
        if (_dummy != null) _dummy.NominalDamaged += OnNominalDamaged;
    }

    void OnDisable()
    {
        if (_dummy != null) _dummy.NominalDamaged -= OnNominalDamaged;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        ClearTint();
    }

    /// <summary>
    /// "원색" 자리를 이 색으로 덮는다. <see cref="ClearBaseTint"/> 로 해제.
    ///
    /// 🔴 이 진입점이 필요한 이유: 어떤 색을 피격 플래시와 같은 경로(MPB)로 칠하면
    /// <b>피격 한 번에 날아간다</b> — 플래시가 끝날 때 MPB 를 머티리얼 원색으로 되돌리기 때문이다.
    /// 여기로 넣으면 플래시가 이 색 <b>위에서</b> Lerp 하고, 끝나도 이 색으로 돌아온다.
    /// </summary>
    public void SetBaseTint(Color color)
    {
        _baseTint = color;
        _hasBaseTint = true;
        ApplyBaseTint();
    }

    /// <summary>베이스 틴트 오버라이드를 해제하고 머티리얼 원색으로 되돌린다.</summary>
    public void ClearBaseTint()
    {
        _hasBaseTint = false;
        ApplyBaseTint();
    }

    // 플래시가 도는 중이면 손대지 않는다 — FlashRoutine 이 다음 프레임에 새 베이스로 Lerp 한다.
    void ApplyBaseTint()
    {
        if (!isActiveAndEnabled) return;
        if (_renderers == null) CacheRenderers();
        if (_routine != null) return;

        if (!_hasBaseTint) { ClearTint(); return; }
        for (int i = 0; i < _renderers.Length; i++)
            ApplyTint(i, _baseTint);
    }

    // 플래시가 되돌아갈 색 = 베이스 틴트가 있으면 그것, 없으면 머티리얼 원색.
    Color BaseColorOf(int index) => _hasBaseTint ? _baseTint : _originalColors[index];

    // HitFlash.OnDamaged 와 같은 본문. 인자는 쓰지 않는다 — 플래시는 피해량과 무관하다.
    void OnNominalDamaged(int amount, ulong attackerClientId)
    {
        if (!isActiveAndEnabled) return;
        if (_renderers == null) CacheRenderers(); // 첫 피격 시 지연 수집(스폰 직후 모델 조립 순서 영향 최소화)
        if (_renderers.Length == 0) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FlashRoutine());
    }

    void CacheRenderers()
    {
        List<Renderer> list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.sharedMaterial == null) continue;
            if (r.GetComponentInParent<AoeTelegraph>() != null) continue; // 장판 등 연출용 제외

            // 🔴 **연출용 렌더러는 스스로 제외를 표시한다**(2026-08-13). 위의 AoeTelegraph 예외만으로는
            //    부족했다 — 앞뒤 방향 표식의 호는 런타임에 만들어지는 그냥 MeshRenderer 라 걸리지 않아
            //    피격 때 함께 빨개졌고, 원래 색을 sharedMaterial 에서 캐시하기 때문에 **플래시가 끝난
            //    뒤에도 재질 원색(빨강)으로 복원**돼 표식이 영구히 빨강이 됐다.
            //    타입을 하나씩 예외로 추가하면 새 연출마다 같은 버그가 재발하므로 마커로 바꾼다.
            if (r.GetComponentInParent<NoHitFlash>() != null) continue;
            list.Add(r);
        }

        _renderers = list.ToArray();
        _originalColors = new Color[_renderers.Length];
        _propIds = new int[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            Material m = _renderers[i].sharedMaterial;
            if (m.HasProperty(BaseColorId)) { _propIds[i] = BaseColorId; _originalColors[i] = m.GetColor(BaseColorId); }
            else if (m.HasProperty(ColorId)) { _propIds[i] = ColorId; _originalColors[i] = m.GetColor(ColorId); }
            else { _propIds[i] = 0; _originalColors[i] = Color.white; }
        }
    }

    IEnumerator FlashRoutine()
    {
        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / flashDuration); // 1→0: 최대 틴트에서 베이스로
            for (int i = 0; i < _renderers.Length; i++)
                ApplyTint(i, Color.Lerp(BaseColorOf(i), flashColor, k));
            yield return null;
        }
        _routine = null;
        ApplyBaseTint(); // 베이스 틴트가 있으면 그 색으로, 없으면 MPB 해제(머티리얼 원색)
    }

    void ApplyTint(int index, Color c)
    {
        Renderer r = _renderers[index];
        if (r == null || _propIds[index] == 0) return;
        r.GetPropertyBlock(_mpb);
        _mpb.SetColor(_propIds[index], c);
        r.SetPropertyBlock(_mpb);
    }

    // MPB 해제 → 머티리얼 원래 값으로 복귀(원색 오버라이드 잔존 방지).
    void ClearTint()
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(null);
        }
    }
}
