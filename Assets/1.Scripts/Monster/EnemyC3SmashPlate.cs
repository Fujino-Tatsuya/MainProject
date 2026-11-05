using UnityEngine;

/// <summary>
/// 할 일 221 — 건틀렛 스매시 예고 원(FX_Mob_Smah_Warning 의 L_circle 스프라이트)을 <b>C3 판 셰이더(EnemyC3Plate, 원 09)</b>로 그린다.
/// 예전엔 에디터에서 미리 구운 sprite(EnemyC3_Smash09.png)를 SpriteRenderer 기본 셰이더로 그려 바닥 블렌딩(221)이 안 먹었다.
///
/// - 판 = 스프라이트 자식의 바닥 판(BossPatternVisuals.CreateFlat)을 스프라이트 평면(로컬 XY)에 눕힌 것. 크기 =
///   원본 sprite 폭 × <see cref="EnemyC3Visuals.OuterScale"/>("09") — 예전 구운 sprite 가 덮던 크기와 같다(중심 피벗).
/// - 그림 = <c>PaintLayer(…, "09", 원, 경계)</c> — 채움 아래 깔기 + 띠 + 핀 + 안쪽 선 + 외곽선, 구운 sprite 와 같은 CPU 식(EnemyC3BakeReference.Circle).
/// - 알파 = <see cref="FadeInHoldEffect"/> 가 <see cref="IFadeInHoldAlphaTarget"/> 로 넘겨 주는 값을 판 MaterialPropertyBlock 의 _Color.a 로.
///   스프라이트는 끈다(색 값은 FadeInHoldEffect 가 계속 쓰지만 그려지지 않는다).
/// - 풀 재사용 — 같은 스프라이트에 다시 붙이면 있는 판을 그대로 쓴다(새 오브젝트·재질·텍스처 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyC3SmashPlate : MonoBehaviour, IFadeInHoldAlphaTarget
{
    MeshRenderer _plate;

    /// <summary>그리는 판(테스트·확인 씬용).</summary>
    public MeshRenderer Plate => _plate;

    /// <summary>스프라이트에 판을 붙이고(없으면 만든다) 스프라이트를 끈다. 현재 스프라이트 알파로 바로 칠한다.</summary>
    public static EnemyC3SmashPlate Attach(SpriteRenderer sr, FadeInHoldEffect fade)
    {
        if (sr == null || sr.sprite == null) return null;
        var p = sr.GetComponent<EnemyC3SmashPlate>();
        if (p == null)
        {
            p = sr.gameObject.AddComponent<EnemyC3SmashPlate>();
            p.Build(sr);
        }
        sr.enabled = false;
        if (fade != null) fade.AddAlphaTarget(p);
        p.SetFadeAlpha(sr.color.a);
        return p;
    }

    /// <summary>원본 sprite 폭(스프라이트 로컬 단위)에서 판 한 변 — 예전 구운 sprite 의 폭과 같다.</summary>
    public static float PlateSize(Sprite source) => source.bounds.size.x * EnemyC3Visuals.OuterScale(EnemyC3Visuals.SmashId);

    void Build(SpriteRenderer sr)
    {
        _plate = BossPatternVisuals.CreateFlat("EnemyC3SmashPlate", sr.transform);
        _plate.gameObject.AddComponent<NoHitFlash>();   // 연출용 — 피격 틴트에서 빠진다
        Transform t = _plate.transform;
        t.localPosition = Vector3.zero;                  // 구운 sprite 도 중심 피벗이었다
        t.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 판(XZ, UV v = +Z) → 스프라이트 평면(XY, v = +Y)
        float size = PlateSize(sr.sprite);
        t.localScale = new Vector3(size, 1f, size);
        _plate.sortingLayerID = sr.sortingLayerID;
        _plate.sortingOrder = sr.sortingOrder;
        _plate.gameObject.SetActive(true);
    }

    public void SetFadeAlpha(float alpha)
    {
        if (_plate != null) EnemyC3Visuals.PaintLayer(_plate, EnemyC3Visuals.SmashId, false, false, alpha);
    }
}
