using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 피격 플래시: Unit의 HP/쉴드 "감소" 복제 알림(Unit.ClientDamaged)을 받아, 모든 피어에서
// 렌더러를 잠깐 빨갛게 틴트했다가 원래 색으로 복귀시킨다(일반 게임식 피격 표시).
//
// - Unit.OnNetworkSpawn이 자동 부착 — Unit 계열 전체(플레이어/몬스터/보스) 공통. 프리팹에 미리
//   붙여 색/시간을 오버라이드해도 된다(자동 부착은 없을 때만).
// - 데미지 판정과 무관한 순수 로컬 연출(복제된 HP/쉴드 감소 기반) — RPC/추가 트래픽 없음.
// - MaterialPropertyBlock만 사용(머티리얼 인스턴스화 없음). URP _BaseColor / 레거시 _Color 지원.
// - AoeTelegraph(장판) 등 연출용 렌더러는 틴트에서 제외.
[DisallowMultipleComponent]
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField]
    [Tooltip("피격 순간 틴트 색.")]
    Color flashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField, Min(0.02f)]
    [Tooltip("플래시 지속 시간(초). 진입 즉시 최대 틴트 → 이 시간 동안 원색으로 복귀.")]
    float flashDuration = 0.35f;

    Unit _unit;
    Renderer[] _renderers;
    Color[] _originalColors; // 렌더러별 원래 베이스 색(sharedMaterial 기준)
    int[] _propIds;          // 렌더러별 사용할 색 프로퍼티(_BaseColor 우선, 없으면 _Color, 없으면 0)
    MaterialPropertyBlock _mpb;
    Coroutine _routine;

    void Awake()
    {
        _unit = GetComponent<Unit>();
        _mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        if (_unit == null) _unit = GetComponent<Unit>();
        if (_unit != null) _unit.ClientDamaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_unit != null) _unit.ClientDamaged -= OnDamaged;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        ClearTint();
    }

    void OnDamaged()
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
            float k = 1f - Mathf.Clamp01(t / flashDuration); // 1→0: 최대 틴트에서 원색으로
            for (int i = 0; i < _renderers.Length; i++)
                ApplyTint(i, Color.Lerp(_originalColors[i], flashColor, k));
            yield return null;
        }
        ClearTint();
        _routine = null;
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
