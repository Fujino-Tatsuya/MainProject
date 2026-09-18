using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// [G2] 공격 **예고**를 바닥 데칼로 그린다. 훅·어퍼가 예고 구간 동안 켜고, 공격이 나갈 때 끈다.
///
/// 🔴 <b>예고는 판정보다 먼저 끝나야 한다.</b> 보스는 이 표시가 다 차는 동안 준비 자세에서 멈춰 있고,
///    다 찬 뒤에야 전진·공격이 나간다(<c>BossAttackPhase.Telegraph</c>). 예고와 공격을 같은 순간에
///    시작하면 반응 시간이 0 이라 예고가 아니라 사후 통보다(2026-09-16 팀장 지적).
///
/// 🔴 <b>예고는 판정에 대해 거짓말하면 안 된다.</b> 그래서 <b>두 조각</b>을 그린다:
///    <list type="bullet">
///    <item><b>경로 띠</b> — 전진하는 동안 몸통이 훑는 자리(반경 <c>lungePathRadius</c> × 길이 <c>lungeDistance</c>).</item>
///    <item><b>끝점 부채꼴</b> — 전진을 마친 자리에서 나가는 주먹(<c>coneRadius</c>/<c>coneAngle</c>/치우침).</item>
///    </list>
///    끝점만 그리면 경로에서 맞는 사람이 <b>표시 없이</b> 맞고(과소 표시), 보스 위치에 크게 그리면
///    맞지도 않는 옆구리까지 위험하다고 말한다(과대 표시). 둘 중 <b>과소 표시가 더 나쁘다</b> —
///    안전하다는 정보를 믿고 움직인 플레이어를 처벌하기 때문이다.
///
/// 🔴 <b>표식(<see cref="BossDirectionIndicator"/>)과 재질·부채꼴 생성기를 공유한다.</b> 따로 들면
///    셰이더·리시버 마스크·페더 규약이 조용히 갈라진다. 재질은 인스턴스를 떠서 쓴다(애셋 오염 금지).
///
/// 각 조각은 <b>윤곽선</b>(전체 크기 고정, 내부는 텅 빔 = "어디에")과
/// <b>채움</b>(0 → 전체로 자람 = "언제") 두 겹이다. 채움이 가득 차는 순간이 곧 공격이다.
///
/// ⚠️ 이 컴포넌트는 <b>런타임에 보스가 붙인다</b>(프리팹 수정 없음). 그래서 인스펙터 칸이 없고,
///    색 같은 값은 아래 상수다. 인스펙터 튜닝이 필요해지면 그때 프리팹에 얹으면 된다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossAttackConeTelegraph : MonoBehaviour
{
    // 예고 색 — **빨강**. 장판 규약에서 빨강은 "여기가 곧 맞는다"의 관례색이다.
    static readonly Color OutlineColor = new Color(1f, 0.12f, 0.08f, 0.85f);  // 윤곽선 — "어디에"
    static readonly Color FillColor = new Color(1f, 0.10f, 0.05f, 0.55f);     // 채움 — "언제"

    const int TextureSize = 256;
    const float ProjectionDepth = 6f;   // 표식과 같은 정도 — 경사면에서도 투영되게 넉넉히
    const float OutlineRimWorld = 0.25f; // 윤곽선 두께(m)
    const float MinFillSize = 0.15f;     // 0 이면 프로젝터가 사라져 깜빡인다
    static readonly int DecalBaseMapId = Shader.PropertyToID("Base_Map");

    BossDirectionIndicator _indicator;   // 재질 출처

    // 끝점 부채꼴
    Transform _coneRoot;
    DecalProjector _coneOutline, _coneFill;
    Texture2D _coneOutlineTex, _coneFillTex;
    float _builtConeHalfAngle = -1f;

    // 전진 경로 띠
    Transform _bandRoot;
    DecalProjector _bandOutline, _bandFill;
    Texture2D _bandOutlineTex, _bandFillTex;
    float _builtBandAspect = -1f;

    bool _active;
    float _elapsed, _growTime;
    float _coneRadius, _coneOffsetAngle, _coneForwardOffset;
    float _pathRadius, _pathLength;
    bool _warnedNoMaterial;

    /// <param name="coneRadius">끝점 부채꼴 반경(m). 판정과 **같은 값**이어야 한다. 0 이면 안 그린다.</param>
    /// <param name="coneAngleDeg">전체 각(도). 180 이면 전방 반원, 360 이면 원형.</param>
    /// <param name="coneOffsetAngleDeg">보스 정면에서 좌/우로 치우치는 각(도). + = 오른쪽.</param>
    /// <param name="coneForwardOffset">부채꼴 꼭짓점을 보스 앞으로 미는 거리(m) = 전진 거리.</param>
    /// <param name="pathRadius">경로 띠 반폭(m) = 경로 판정 반경. 0 이면 띠를 안 그린다.</param>
    /// <param name="pathLength">경로 띠 길이(m) = 전진 거리.</param>
    /// <param name="growTime">채움이 가득 차는 데 걸리는 시간(초). 0 이면 처음부터 가득.</param>
    public void Show(float coneRadius, float coneAngleDeg, float coneOffsetAngleDeg,
                     float coneForwardOffset, float pathRadius, float pathLength, float growTime)
    {
        if (!EnsureBuilt()) return;

        _coneRadius = Mathf.Max(0f, coneRadius);
        _coneOffsetAngle = coneOffsetAngleDeg;
        _coneForwardOffset = coneForwardOffset;
        _pathRadius = Mathf.Max(0f, pathRadius);
        _pathLength = Mathf.Max(0f, pathLength);
        _growTime = Mathf.Max(0f, growTime);
        _elapsed = 0f;
        _active = true;

        // ── 끝점 부채꼴
        bool hasCone = _coneRadius > 0f;
        float halfAngle = Mathf.Clamp(coneAngleDeg, 0f, 360f) * 0.5f;
        if (hasCone && !Mathf.Approximately(halfAngle, _builtConeHalfAngle))
        {
            RebuildConeTextures(halfAngle);
            _builtConeHalfAngle = halfAngle;
        }
        SetSquareSize(_coneOutline, _coneRadius);
        _coneOutline.enabled = hasCone;
        _coneFill.enabled = hasCone;

        // ── 경로 띠
        bool hasBand = _pathRadius > 0f && _pathLength > 0f;
        if (hasBand)
        {
            // 텍스처는 **가로세로 비**에만 의존한다(윤곽선 두께를 양축에 맞추기 위해).
            float aspect = _pathLength / (_pathRadius * 2f);
            if (!Mathf.Approximately(aspect, _builtBandAspect))
            {
                RebuildBandTextures(aspect);
                _builtBandAspect = aspect;
            }
            SetRectSize(_bandOutline, _pathRadius * 2f, _pathLength);
        }
        _bandOutline.enabled = hasBand;
        _bandFill.enabled = hasBand;

        ApplyFill();
        Place();
    }

    public void Hide()
    {
        _active = false;
        if (_coneOutline != null) _coneOutline.enabled = false;
        if (_coneFill != null) _coneFill.enabled = false;
        if (_bandOutline != null) _bandOutline.enabled = false;
        if (_bandFill != null) _bandFill.enabled = false;
    }

    // 보스가 조준하며 도는 동안 예고가 따라붙어야 한다 — 그래서 매 프레임 다시 놓고 다시 채운다.
    // 🔴 LateUpdate 다. 애니메이션·NetworkTransform 반영 뒤에 놓아야 한 프레임 밀리지 않는다.
    void LateUpdate()
    {
        if (!_active) return;

        _elapsed += Time.deltaTime;
        ApplyFill();
        Place();
    }

    // 채움 진행도 = 경과 / 성장시간. 다 차는 순간이 곧 공격이 나가는 순간이다.
    float FillT => _growTime <= 0f ? 1f : Mathf.Clamp01(_elapsed / _growTime);

    void ApplyFill()
    {
        float t = FillT;

        // 부채꼴은 **반경**이 자란다(꼭짓점에서 바깥으로).
        if (_coneFill != null && _coneFill.enabled)
            SetSquareSize(_coneFill, Mathf.Max(MinFillSize, _coneRadius * t));

        // 띠는 **길이**가 자란다(보스에서 앞으로). 폭은 그대로 — 위험 폭은 처음부터 정해져 있다.
        if (_bandFill != null && _bandFill.enabled)
            SetRectSize(_bandFill, _pathRadius * 2f, Mathf.Max(MinFillSize, _pathLength * t));
    }

    static void SetSquareSize(DecalProjector decal, float radius)
    {
        if (decal == null) return;
        decal.size = new Vector3(radius * 2f, radius * 2f, ProjectionDepth);
    }

    static void SetRectSize(DecalProjector decal, float width, float length)
    {
        if (decal == null) return;
        decal.size = new Vector3(width, length, ProjectionDepth);
    }

    // 🔴 방향을 **매 프레임 보스 정면에서 다시 계산한다.** Show 시점 값을 들고 있으면 보스가 돌 때
    //    예고만 제자리에 남는다. 판정도 히트 순간의 `transform.forward` 를 쓰므로 이렇게 해야 둘이 맞는다.
    void Place()
    {
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        // 바닥 Y 를 찾아 올린다 — 절대 Y 상수 금지(경사·발판에서 어긋난다).
        // 못 찾으면 보스 발밑을 쓴다(공중에 뜬 예고보다 낫다).
        float y = transform.position.y;
        if (GroundProbe.TryFindGround(transform.position, 0, out RaycastHit ground, out _))
            y = ground.point.y;

        Vector3 basePos = new Vector3(transform.position.x, y, transform.position.z);

        // 부채꼴 — 꼭짓점은 전진 끝점. 중심 방향은 팔 쪽으로 치우친다.
        if (_coneRoot != null)
        {
            _coneRoot.position = basePos + fwd * _coneForwardOffset;
            _coneRoot.rotation = Quaternion.LookRotation(
                Quaternion.AngleAxis(_coneOffsetAngle, Vector3.up) * fwd, Vector3.up);
        }

        // 띠 — 보스에서 앞으로. 채움이 자라는 동안 **중심이 함께 앞으로 간다**(뒤끝은 보스에 고정).
        if (_bandRoot != null)
        {
            _bandRoot.position = basePos + fwd * (_pathLength * 0.5f);
            _bandRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            // 채움 조각만 뒤끝을 보스에 붙인 채 앞으로 자란다 — 로컬 오프셋으로 처리한다.
            if (_bandFill != null)
            {
                float grown = _pathLength * FillT;
                _bandFill.transform.localPosition =
                    new Vector3(0f, 0f, -(_pathLength - grown) * 0.5f);
            }
        }
    }

    bool EnsureBuilt()
    {
        if (_coneFill != null) return true;

        if (_indicator == null) _indicator = GetComponentInChildren<BossDirectionIndicator>(true);
        Material source = _indicator != null ? _indicator.DecalMaterial : null;
        if (source == null)
        {
            if (!_warnedNoMaterial)
            {
                _warnedNoMaterial = true;
                Debug.LogWarning(
                    $"{name}: BossDirectionIndicator 의 데칼 재질이 없어 공격 예고를 그릴 수 없다 — " +
                    "판정은 그대로 나가고 예고만 빠진다(보이지 않는 공격).", this);
            }
            return false;
        }

        _coneRoot = new GameObject("AttackConeTelegraph").transform;
        _coneRoot.SetParent(transform, false);
        _coneOutline = CreateDecal(source, _coneRoot, "ConeOutline");
        _coneFill = CreateDecal(source, _coneRoot, "ConeFill");

        _bandRoot = new GameObject("AttackPathTelegraph").transform;
        _bandRoot.SetParent(transform, false);
        _bandOutline = CreateDecal(source, _bandRoot, "PathOutline");
        _bandFill = CreateDecal(source, _bandRoot, "PathFill");
        return true;
    }

    DecalProjector CreateDecal(Material source, Transform parent, string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        // 🔴 프로젝터는 로컬 +Z 로 투영한다 — X+90 이어야 그 축이 아래를 본다.
        //    이 회전이면 로컬 +Y 가 부모의 정면이 되어 텍스처의 +V 를 "앞쪽"으로 쓸 수 있다.
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var decal = go.AddComponent<DecalProjector>();
        decal.material = new Material(source);          // 인스턴스 — 애셋 오염 금지
        decal.renderingLayerMask = DecalReceivers.Mask; // 캐릭터에 묻지 않게(표식과 같은 마스크)
        decal.fadeFactor = 1f;
        decal.enabled = false;
        return decal;
    }

    void RebuildConeTextures(float halfAngle)
    {
        if (_coneOutlineTex != null) Destroy(_coneOutlineTex);
        if (_coneFillTex != null) Destroy(_coneFillTex);

        // outer 1 = 정규화 → 텍스처가 반경과 무관해지고 각도만 같으면 다시 구울 필요가 없다.
        // 🔴 윤곽선은 **경계만** 그리고 내부는 비운다 — 장판 규약이 "윤곽선이 생기고 내부가 텅 빈
        //    상태에서 차오른다"이기 때문. 내부를 미리 칠하면 채움이 어디까지 찼는지 안 읽힌다.
        float rimUv = _coneRadius > 0f ? OutlineRimWorld / _coneRadius : 0.06f;
        _coneOutlineTex = BossDirectionIndicator.BuildArcTexture(
            OutlineColor, halfAngle, isBack: false, inner: 0f, outer: 1f, size: TextureSize,
            rimWidth: rimUv, interiorAlphaScale: 0f);

        _coneFillTex = BossDirectionIndicator.BuildArcTexture(
            FillColor, halfAngle, isBack: false, inner: 0f, outer: 1f, size: TextureSize);

        AssignTexture(_coneOutline, _coneOutlineTex);
        AssignTexture(_coneFill, _coneFillTex);
    }

    void RebuildBandTextures(float aspect)
    {
        if (_bandOutlineTex != null) Destroy(_bandOutlineTex);
        if (_bandFillTex != null) Destroy(_bandFillTex);

        // 띠는 직사각이라 UV 가 그대로 사각형에 대응한다 — 비(比)만 맞추면 왜곡이 없다.
        // 윤곽선 두께를 월드 기준으로 맞추려면 짧은 축·긴 축에 다른 UV 두께를 줘야 한다.
        float rimU = _pathRadius > 0f ? OutlineRimWorld / (_pathRadius * 2f) : 0.08f;
        float rimV = _pathLength > 0f ? OutlineRimWorld / _pathLength : 0.08f;

        _bandOutlineTex = BuildRectTexture(OutlineColor, TextureSize, rimU, rimV, interiorAlpha: 0f);
        _bandFillTex = BuildRectTexture(FillColor, TextureSize, 0f, 0f, interiorAlpha: 1f);

        AssignTexture(_bandOutline, _bandOutlineTex);
        AssignTexture(_bandFill, _bandFillTex);
    }

    /// <summary>
    /// 직사각 알파 마스크. <paramref name="rimU"/>/<paramref name="rimV"/> 가 0 보다 크면
    /// 테두리만 진하게 그리고 내부는 <paramref name="interiorAlpha"/> 배로 남긴다.
    /// 두 축의 두께를 따로 받는 이유는 데칼이 정사각이 아니어서 — 같은 값을 주면 테두리가 찌그러진다.
    /// </summary>
    static Texture2D BuildRectTexture(Color color, int size, float rimU, float rimV, float interiorAlpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "BossPathBandDecal",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float feather = 1.5f / size;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;   // 0~1
                float v = (y + 0.5f) / size;

                // 가장자리까지의 거리(정규화). 0 = 경계.
                float du = Mathf.Min(u, 1f - u);
                float dv = Mathf.Min(v, 1f - v);

                float a = color.a;
                a *= Mathf.Clamp01(du / feather);
                a *= Mathf.Clamp01(dv / feather);

                if (rimU > 0f || rimV > 0f)
                {
                    float rim = Mathf.Max(
                        rimU > 0f ? 1f - du / rimU : 0f,
                        rimV > 0f ? 1f - dv / rimV : 0f);
                    a *= Mathf.Max(interiorAlpha, Mathf.Clamp01(rim));
                }

                pixels[y * size + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(a));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    static void AssignTexture(DecalProjector decal, Texture2D texture)
    {
        if (decal == null || decal.material == null) return;
        if (decal.material.HasProperty(DecalBaseMapId))
            decal.material.SetTexture(DecalBaseMapId, texture);
    }

    void OnDestroy()
    {
        DestroyIf(_coneOutlineTex); DestroyIf(_coneFillTex);
        DestroyIf(_bandOutlineTex); DestroyIf(_bandFillTex);
        DestroyMaterial(_coneOutline); DestroyMaterial(_coneFill);
        DestroyMaterial(_bandOutline); DestroyMaterial(_bandFill);
    }

    static void DestroyIf(Texture2D t) { if (t != null) Destroy(t); }
    static void DestroyMaterial(DecalProjector d) { if (d != null && d.material != null) Destroy(d.material); }
}
