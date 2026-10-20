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
/// 각 조각은 <b>윤곽선</b>(전체 크기 고정, 내부는 텅 빔 = "어디에")과
/// <b>채움</b>(0 → 전체로 자람 = "언제") 두 겹이다. 채움이 가득 차는 순간이 곧 공격이다.
///
/// 🎨 <b>색·마스크 = 몬스터 데이터의 <see cref="TelegraphStyleSO"/></b>(PLAN-telegraph-style, 팀장 10-13).
///    모양(윤곽선·채움)은 텍스처 없이 셰이더(<c>SG_EnemyC3Decal</c>)가 그리고(할 일 57 S2·S4a), 색·마스크는 같은
///    셰이더가 재질 값으로 입힌다 — 그래서 스타일·크기·진행이 바뀌어도 재질 값만 바뀐다. Play 중 스타일 값을 바꾸면 <see cref="TelegraphStyleSO.Version"/> 을 보고
///    재질 값만 다시 넣는다(<c>Show</c> 를 다시 부르지 않음 — 부르면 채움 진행이 0 으로 돌아간다).
///    마스크 좌표 = 각 조각 기준점의 로컬 좌표(<c>TelegraphMask.hlsl</c>) — 채움 프로젝터가 커져도 패턴이 미끄러지지 않고,
///    보스가 돌면 패턴도 같이 돈다.
///
/// ⚠️ 이 컴포넌트는 <b>런타임에 보스가 붙인다</b>(프리팹 수정 없음). 스타일은 붙은 몬스터의 데이터에서 찾는다(모든 피어 — RPC 무관).
/// </summary>
[DisallowMultipleComponent]
public sealed class BossAttackConeTelegraph : MonoBehaviour
{
    /// <summary>예고 색 묶음. 모양·채움 규약은 같고 색만 다르다.</summary>
    public enum Palette
    {
        Attack,     // 빨강 — 일반 공격
        Interrupt,  // 파랑 — 간파(인터럽트)가 통하는 공격
    }

    Palette _palette = Palette.Attack;

    const float ProjectionDepth = 6f;   // 표식과 같은 정도 — 경사면에서도 투영되게 넉넉히
    const float MinFillSize = 0.15f;     // 0 이면 프로젝터가 사라져 깜빡인다

    // ── 스타일 셰이더 속성(SG_TelegraphDecal · TelegraphMask.hlsl)
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
    static readonly int MaskParamsId = Shader.PropertyToID("_MaskParams");
    static readonly int MaskOriginId = Shader.PropertyToID("_MaskOrigin");
    static readonly int MaskAxesId = Shader.PropertyToID("_MaskAxes");
    static readonly int MaskExtentId = Shader.PropertyToID("_MaskExtent");

    // 🔴 예고 전용 재질은 Resources 에 둔다 — 프리팹 연결 없이도 빌드에 항상 들어간다.
    //    방향 표식(BossDirectionIndicator)은 기존 재질 그대로(이번 범위 밖).
    const string TelegraphMaterialPath = "Telegraph/MA_TelegraphDecal";
    static Material _telegraphMaterial;
    static bool _triedLoadMaterial;

    BossDirectionIndicator _indicator;   // 재질 대체 출처(예고 재질이 없을 때만)

    /// <summary>
    /// 표식(<see cref="BossDirectionIndicator"/>)이 없는 몹(중간보스)이 재질을 직접 넘기는 창구.
    /// 예고 전용 재질(<c>Resources/Telegraph/MA_TelegraphDecal</c>)이 있으면 그쪽이 우선이다.
    /// </summary>
    public Material MaterialOverride { get; set; }

    TelegraphStyleSO _style;

    /// <summary>색·마스크 스타일. 비우면 붙은 몬스터의 데이터(<see cref="MonsterDataSO.telegraphStyle"/>) → 코드 기본값.</summary>
    public TelegraphStyleSO Style
    {
        get => _style != null ? _style : (_style = TelegraphStyleSO.For(this));
        set { _style = value; _appliedVersion = -1; }
    }

    TelegraphDecalStyle CurrentStyle => Style.Decal(_palette);
    string C3Id
    {
        get
        {
            if (_coneRadius > 0 && _pathLength > 0) return "02";
            if (_coneRadius > 0) return "03";
            MonsterBase owner=GetComponent<MonsterBase>();
            string kind=owner!=null?owner.GetType().Name:"";
            if (kind=="SpinnerBot") return _palette==Palette.Interrupt?"11":"10";
            if (kind=="GauntletBot") return "08";
            if (_palette==Palette.Interrupt) return "04";
            return _growTime<=0?"05":"01";
        }
    }


    // 예고 재질(_BaseColor·마스크)인가. 아니면(대체 재질) 마스크 없이 채움 색만 재질에 넣는다.
    bool _shaderColor;
    int _appliedVersion = -1;

    // 끝점 부채꼴
    Transform _coneRoot;
    DecalProjector _coneOutline, _coneFill;
    Vector3 _builtConeKey = new Vector3(-1f, -1f, -1f);   // (반각, 반경, 두께) — 두께 UV 가 반경에 의존한다
    float _coneHalfAngle = 180f;

    // 전진 경로 띠
    Transform _bandRoot;
    DecalProjector _bandOutline, _bandFill;
    Vector3 _builtBandKey = new Vector3(-1f, -1f, -1f);   // (반폭, 길이, 두께)

    // 합집합(열쇠구멍) — 띠와 끝점이 둘 다 있을 때는 **한 도형**으로 그린다(팀장 확정 2026-09-18).
    Transform _unionRoot;
    DecalProjector _unionOutline, _unionFill;
    float _builtUnionKey = -1f;      // 모양이 바뀜을 때만 외곽선 값을 다시 넣는다
    int _builtUnionFillStep = -1;    // 채움 전선은 25단계로 끊는다(예전 텍스처 굽기와 같은 겉모습)
    bool _useUnion;
    const int UnionFillSteps = 24;   // 단계 0~24 = 25 상태

    string _builtC3Id;
    bool _active;
    bool _fillInward;              // 바깥 → 보스로 차오르기(잡기)
    int _builtInwardStep = -1;
    float _elapsed, _growTime;
    float _coneRadius, _coneOffsetAngle, _coneForwardOffset;
    float _pathRadius, _pathLength, _pathForwardOffset, _pathLateralOffset;
    bool _warnedNoMaterial;

    /// <param name="coneRadius">끝점 부채꼴 반경(m). 판정과 **같은 값**이어야 한다. 0 이면 안 그린다.</param>
    /// <param name="coneAngleDeg">전체 각(도). 180 이면 전방 반원, 360 이면 원형.</param>
    /// <param name="coneOffsetAngleDeg">보스 정면에서 좌/우로 치우치는 각(도). + = 오른쪽.</param>
    /// <param name="coneForwardOffset">부채꼴 꼭짓점을 보스 앞으로 미는 거리(m) = 전진 거리.</param>
    /// <param name="pathRadius">경로 띠 반폭(m) = 경로 판정 반경. 0 이면 띠를 안 그린다.</param>
    /// <param name="pathLength">경로 띠 길이(m) = 전진 거리.</param>
    /// <param name="growTime">채움이 가득 차는 데 걸리는 시간(초). 0 이면 처음부터 가득.</param>
    /// <param name="palette">예고 색. 기본 = 빨강(일반 공격).</param>
    public void Show(float coneRadius, float coneAngleDeg, float coneOffsetAngleDeg,
                     float coneForwardOffset, float pathRadius, float pathLength, float growTime,
                     float pathForwardOffset = 0f, float pathLateralOffset = 0f, bool fillInward = false,
                     Palette palette = Palette.Attack)
    {
        if (!EnsureBuilt()) return;

        // 모양은 팔레트와 무관하다(색은 아래 ApplyStyle 이 재질에 넣는다). 두께는 팔레트별로 다를 수 있어 아래 키가 잡는다.
        _palette = palette;

        _fillInward = fillInward;
        _builtInwardStep = -1;

        _coneRadius = Mathf.Max(0f, coneRadius);
        _coneOffsetAngle = coneOffsetAngleDeg;
        _coneForwardOffset = coneForwardOffset;
        _pathRadius = Mathf.Max(0f, pathRadius);
        _pathLength = Mathf.Max(0f, pathLength);
        _pathForwardOffset = pathForwardOffset;
        _pathLateralOffset = pathLateralOffset;
        _growTime = Mathf.Max(0f, growTime);
        _elapsed = 0f;
        _active = true;

        if (_builtC3Id != C3Id) { _builtC3Id=C3Id; InvalidateShapes(); }
        float width = CurrentStyle.outlineWidth;

        // ── 끝점 부채꼴
        bool hasCone = _coneRadius > 0f;
        float halfAngle = Mathf.Clamp(coneAngleDeg, 0f, 360f) * 0.5f;
        // 🔴 키에 **반경·두께**까지 넣는다 — 윤곽선 두께 UV(두께/반경)가 반경에 의존해, 각도만 보면
        //    크기가 다른 공격이 이전 두께를 그대로 썼다(Codex 10-13 지적 · 기존 버그).
        var coneKey = new Vector3(halfAngle, _coneRadius, width);
        if (hasCone && coneKey != _builtConeKey)
        {
            RebuildConeOutline(halfAngle);
            _builtConeKey = coneKey;
        }
        // 채움은 가득 찬 부채꼴로 되돌린다 — 잡기가 도중에 끊겨 **반쯤 찬 고리**가 남아 있어도 다음 예고에 새지 않는다.
        // 잡기면 ApplyFill 이 곧바로 고리 값으로 바꾼다.
        _coneHalfAngle = halfAngle;
        if (hasCone) EnemyC3Visuals.SetArcFill(_coneFill.material, halfAngle, 0f);
        var coneExtent=new Vector2(_coneRadius*2,_coneRadius*2);
        SetSquareSize(_coneOutline, _coneRadius+(_coneRadius>0?EnemyC3Visuals.DecalPadding(C3Id,coneExtent,_coneRadius,0,0):0));
        _coneOutline.enabled = hasCone;
        _coneFill.enabled = hasCone;

        // ── 경로 띠
        bool hasBand = _pathRadius > 0f && _pathLength > 0f;
        if (hasBand)
        {
            var bandKey = new Vector3(_pathRadius, _pathLength, width);
            if (bandKey != _builtBandKey)
            {
                RebuildBandOutline();
                _builtBandKey = bandKey;
            }
            float pad=EnemyC3Visuals.DecalPadding(C3Id,new Vector2(_pathRadius*2,_pathLength),0,_pathRadius*2,_pathLength);
            SetRectSize(_bandOutline, _pathRadius * 2f+pad*2, _pathLength+pad*2);
        }
        _bandOutline.enabled = hasBand;
        _bandFill.enabled = hasBand;

        // 🔴 띠와 끝점이 둘 다 있으면 **한 도형**으로 그린다. 따로 그리면 둘이 각자
        //    자라 두 덩어리로 보이고, 그러면 한 공격이 아니라 두 공격처럼 읽힌다(팀장 지적).
        _useUnion = hasCone && hasBand;
        if (_useUnion)
        {
            _coneOutline.enabled = _coneFill.enabled = false;
            _bandOutline.enabled = _bandFill.enabled = false;

            float key = _pathRadius * 1e6f + _pathLength * 1e3f + _coneRadius
                      + _coneForwardOffset * 1e-3f + width * 1e-5f;
            if (!Mathf.Approximately(key, _builtUnionKey))
            {
                RebuildUnionOutline();
                _builtUnionKey = key;
                _builtUnionFillStep = -1;   // 모양이 바뀌었으니 채움도 다시
            }

            Vector2 u = UnionExtent;
            float pad=EnemyC3Visuals.DecalPadding(C3Id,u,_coneRadius,_pathRadius*2,_pathLength);
            SetRectSize(_unionOutline, u.x+pad*2, u.y+pad*2);
            SetRectSize(_unionFill, u.x, u.y);
        }
        _unionOutline.enabled = _unionFill.enabled = _useUnion;

        ApplyStyle();
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
        if (_unionOutline != null) _unionOutline.enabled = false;
        if (_unionFill != null) _unionFill.enabled = false;
    }

    // 보스가 조준하며 도는 동안 예고가 따라붙어야 한다 — 그래서 매 프레임 다시 놓고 다시 채운다.
    // 🔴 LateUpdate 다. 애니메이션·NetworkTransform 반영 뒤에 놓아야 한 프레임 밀리지 않는다.
    void LateUpdate()
    {
        if (!_active) return;

        // Play 중 스타일 값이 바뀌었다 — 진행(_elapsed)은 그대로 두고 색·마스크만 다시 넣는다.
        if (_appliedVersion != TelegraphStyleSO.Version) ApplyStyle();

        _elapsed += Time.deltaTime;
        ApplyFill();
        Place();
    }

    // 채움 진행도 = 경과 / 성장시간. 다 차는 순간이 곧 공격이 나가는 순간이다.
    float FillT => _growTime <= 0f ? 1f : Mathf.Clamp01(_elapsed / _growTime);

    void ApplyFill()
    {
        if (_useUnion)
        {
            // 채움은 **전선 하나**가 앞으로 쓸고 간다 — 띠를 지나 끝점으로 자연스럽게 이어진다.
            // 전선은 25단계로 끊는다 — 예전 텍스처 굽기의 겉모습 그대로. 단계가 바뀔 때만 재질 값을 넣는다.
            int step = Mathf.Clamp(Mathf.RoundToInt(FillT * UnionFillSteps), 0, UnionFillSteps);
            if (step != _builtUnionFillStep)
            {
                RebuildUnionFill((float)step / UnionFillSteps);
                _builtUnionFillStep = step;
            }
            return;
        }

        float t = FillT;

        // 끌어당기는 공격(잡기) — 바깥 끝에서 보스 쪽으로 차오른다(2026-09-28 팀장).
        // 채움 데칼은 전체 반경에 고정하고, 안쪽 반경이 1 → 0 으로 줄어드는 고리를 단계별로 바꾼다(합집합과 같은 25단계).
        bool coneOnly = _coneFill != null && _coneFill.enabled && !(_bandFill != null && _bandFill.enabled);
        if (_fillInward && coneOnly)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(t * UnionFillSteps), 0, UnionFillSteps);
            if (step != _builtInwardStep)
            {
                RebuildInwardConeFill(1f - (float)step / UnionFillSteps);
                _builtInwardStep = step;
            }
            SetSquareSize(_coneFill, _coneRadius);
            return;
        }

        // 🔴 **띠와 끝점이 하나의 전선으로 채워진다**(팀장 확정 2026-09-18).
        //    이전에는 둘이 각자 0→최대로 자라 — 동시에 끝나긴 해도 띠는 앞으로,
        //    원은 반경으로 커져서 **따로 놓인 두 개**로 보였다.
        //    이제 보스에서 바깥으로 가는 거리 하나(front)가 둘을 같이 밀어낸다.
        float bandLen = _bandFill != null && _bandFill.enabled ? _pathLength : 0f;
        float coneLen = _coneFill != null && _coneFill.enabled ? _coneRadius : 0f;
        float total = bandLen + coneLen;
        float front = total > 0f ? total * t : 0f;

        // 띠는 **길이**가 자란다(보스에서 앞으로). 폭은 그대로 — 위험 폭은 처음부터 정해져 있다.
        if (_bandFill != null && _bandFill.enabled)
            SetRectSize(_bandFill, _pathRadius * 2f,
                        Mathf.Max(MinFillSize, Mathf.Min(bandLen, front)));

        // 끝점은 띠가 다 차고 난 뒤부터 반경이 자란다 — 전선이 그대로 이어진다.
        if (_coneFill != null && _coneFill.enabled)
            SetSquareSize(_coneFill,
                          Mathf.Max(MinFillSize, Mathf.Clamp(front - bandLen, 0f, coneLen)));
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
        // 합집합 — 보스에서 앞으로 뻗은 한 덩어리. 텍스처가 모양을 다 들고 있어 배치만 하면 된다.
        if (_useUnion && _unionRoot != null)
        {
            Vector2 u = UnionExtent;
            _unionRoot.position = basePos + fwd * (u.y * 0.5f);
            _unionRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            SetMaskFrame(_unionRoot, _unionOutline, _unionFill);
        }

        if (_coneRoot != null)
        {
            _coneRoot.position = basePos + fwd * _coneForwardOffset;
            _coneRoot.rotation = Quaternion.LookRotation(
                Quaternion.AngleAxis(_coneOffsetAngle, Vector3.up) * fwd, Vector3.up);
            if (!_useUnion) SetMaskFrame(_coneRoot, _coneOutline, _coneFill);
        }

        // 띠 — 보스에서 앞으로. 채움이 자라는 동안 **중심이 함께 앞으로 간다**(뒤끝은 보스에 고정).
        if (_bandRoot != null)
        {
            // 가로 치우침은 **회전이 아니라 평행이동**이다 — 네모는 보스 정면을 보는 채로 옆으로 비킨다.
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            _bandRoot.position = basePos
                               + fwd * (_pathForwardOffset + _pathLength * 0.5f)
                               + right * _pathLateralOffset;
            _bandRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);

            // 채움 조각만 뒤끝을 보스에 붙인 채 앞으로 자란다 — 로컬 오프셋으로 처리한다.
            if (_bandFill != null)
            {
                // 채움 길이는 ApplyFill 과 **같은 식**이어야 한다 — 아니면 뒤끝이 보스에서 떨어진다.
                float total = _pathLength + (_coneFill != null && _coneFill.enabled ? _coneRadius : 0f);
                float grown = Mathf.Min(_pathLength, (total > 0f ? total : _pathLength) * FillT);
                _bandFill.transform.localPosition =
                    new Vector3(0f, 0f, -(_pathLength - grown) * 0.5f);
            }
            if (!_useUnion) SetMaskFrame(_bandRoot, _bandOutline, _bandFill);
        }
    }

    // ── 스타일(색·마스크) ─────────────────────────────────────────

    // 색·마스크 값을 재질에 넣는다. 모양·진행은 건드리지 않는다.
    void ApplyStyle()
    {
        _appliedVersion = TelegraphStyleSO.Version;
        if (!_shaderColor)
        {
            // 대체 재질 — 예전엔 채움 색을 텍스처에 구웠다. 이제 채움도 셰이더가 그려 색만 넣는다(마스크 없음).
            Color fill = CurrentStyle.fill.color;
            SetFillColor(_coneFill, fill); SetFillColor(_bandFill, fill); SetFillColor(_unionFill, fill);
            return;
        }

        TelegraphDecalStyle s = new TelegraphDecalStyle
        {
            outline = new TelegraphLayer(Color.white),
            fill = new TelegraphLayer(EnemyC3Visuals.FillColor(C3Id))
        };
        Vector2 cone = new Vector2(_coneRadius * 2f, _coneRadius * 2f);
        Vector2 band = new Vector2(_pathRadius * 2f, _pathLength);
        Vector2 union = UnionExtent;

        SetLayer(_coneOutline, s.outline, cone);   SetLayer(_coneFill, s.fill, cone);
        SetLayer(_bandOutline, s.outline, band);   SetLayer(_bandFill, s.fill, band);
        SetLayer(_unionOutline, s.outline, union); SetLayer(_unionFill, s.fill, union);
    }

    // extent = 늘이기 모드의 기준 크기(윤곽선 전체 = 최종 크기). 채움이 자라는 중에도 이 값이라 패턴이 커지지 않는다.
    static void SetLayer(DecalProjector decal, TelegraphLayer layer, Vector2 extent)
    {
        if (decal == null || decal.material == null) return;
        Material m = decal.material;
        m.SetColor(BaseColorId, layer.color);
        TelegraphMask mask = layer.mask;
        bool on = mask != null && mask.Active;
        m.SetTexture(MaskTexId, on ? mask.texture : Texture2D.whiteTexture);
        m.SetVector(MaskParamsId, new Vector4(
            on ? (float)mask.mode : 0f,
            on ? mask.strength : 0f,
            on ? mask.tileSize : 1f,
            on ? mask.rotation * Mathf.Deg2Rad : 0f));
        m.SetVector(MaskExtentId, new Vector4(Mathf.Max(0.01f, extent.x), Mathf.Max(0.01f, extent.y), 0f, 0f));
    }

    static void SetFillColor(DecalProjector decal, Color color)
    {
        if (decal == null || decal.material == null || !decal.material.HasProperty(BaseColorId)) return;
        decal.material.SetColor(BaseColorId, color);
    }

    // 마스크 좌표계 = 조각 기준점(root)의 위치·좌우·정면. 매 프레임(보스가 돌고 움직인다).
    // 🔴 채움 데칼 자신의 위치가 아니라 **root** 다 — 띠 채움은 자라며 중심이 움직여서, 그걸 쓰면 패턴이 미끄러진다.
    void SetMaskFrame(Transform root, DecalProjector outline, DecalProjector fill)
    {
        if (!_shaderColor || root == null) return;
        Vector3 p = root.position;
        Vector3 r = root.right; r.y = 0f; r.Normalize();
        Vector3 f = root.forward; f.y = 0f; f.Normalize();
        var origin = new Vector4(p.x, p.y, p.z, 0f);
        var axes = new Vector4(r.x, r.z, f.x, f.z);
        if (outline != null && outline.enabled && outline.material != null)
        {
            outline.material.SetVector(MaskOriginId, origin);
            outline.material.SetVector(MaskAxesId, axes);
        }
        if (fill != null && fill.enabled && fill.material != null)
        {
            fill.material.SetVector(MaskOriginId, origin);
            fill.material.SetVector(MaskAxesId, axes);
        }
    }

    void InvalidateShapes()
    {
        _builtConeKey = new Vector3(-1f, -1f, -1f);
        _builtBandKey = new Vector3(-1f, -1f, -1f);
        _builtUnionKey = -1f;
        _builtUnionFillStep = -1;
    }

    static Material TelegraphMaterial
    {
        get
        {
            if (!_triedLoadMaterial)
            {
                _triedLoadMaterial = true;
                _telegraphMaterial = Resources.Load<Material>(TelegraphMaterialPath);
            }
            return _telegraphMaterial;
        }
    }

    bool EnsureBuilt()
    {
        if (_coneFill != null) return true;

        Material source = TelegraphMaterial;
        if (source == null)
        {
            // 대체 — 예전 경로(색을 텍스처에 굽는다). 마스크는 안 먹는다.
            if (_indicator == null) _indicator = GetComponentInChildren<BossDirectionIndicator>(true);
            source = _indicator != null ? _indicator.DecalMaterial : null;
            if (source == null) source = MaterialOverride;
            Debug.LogWarning($"{name}: 예고 재질(Resources/{TelegraphMaterialPath})이 없다 — 색만 구워서 그리고 마스크는 무시한다.", this);
        }
        if (source == null)
        {
            if (!_warnedNoMaterial)
            {
                _warnedNoMaterial = true;
                Debug.LogWarning(
                    $"{name}: 공격 예고 재질이 없어 예고를 그릴 수 없다 — " +
                    "판정은 그대로 나가고 예고만 빠진다(보이지 않는 공격).", this);
            }
            return false;
        }
        _shaderColor = source.HasProperty(BaseColorId) && source.HasProperty(MaskTexId);

        _coneRoot = new GameObject("AttackConeTelegraph").transform;
        _coneRoot.SetParent(transform, false);
        _coneOutline = CreateDecal(source, _coneRoot, "ConeOutline");
        _coneFill = CreateDecal(source, _coneRoot, "ConeFill");

        _bandRoot = new GameObject("AttackPathTelegraph").transform;
        _bandRoot.SetParent(transform, false);
        _bandOutline = CreateDecal(source, _bandRoot, "PathOutline");
        _bandFill = CreateDecal(source, _bandRoot, "PathFill");
        EnemyC3Visuals.SetRectFill(_bandFill.material);   // 띠 채움은 모양이 하나 — 길이는 프로젝터 크기가 자란다

        _unionRoot = new GameObject("AttackUnionTelegraph").transform;
        _unionRoot.SetParent(transform, false);
        _unionOutline = CreateDecal(source, _unionRoot, "UnionOutline");
        _unionFill = CreateDecal(source, _unionRoot, "UnionFill");
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
        int artOrder=childName.EndsWith("Outline") ? 1 : 0;
        // 외곽선·채움 = C3 데칼 셰이더(할 일 57 S2·S4a) — 모양을 텍스처로 굽지 않고 재질 값만 넣는다.
        // 그리기 순서·데칼 설정(Normal_Blend·각도 페이드·메시 바이어스)은 예고 재질(source) 기준 그대로 —
        // NewDecalInstance 가 복사하고(할 일 57 R), 여기선 조각별 순서(artOrder)만 그 위에 덮는다.
        Material shape = EnemyC3Visuals.DecalMaterial;
        decal.material = shape != null                  // 인스턴스 — 애셋 오염 금지(조각마다 색·마스크가 다르다)
            ? EnemyC3Visuals.NewDecalInstance(source, shape.HasProperty(BaseColorId) ? shape.GetColor(BaseColorId) : Color.white)
            : new Material(source);
        DecalReceivers.ConfigureFloorProjector(decal); // 캐릭터·벽에 묻지 않게(표식과 같은 계약)
        if(decal.material.HasProperty("_DrawOrder"))decal.material.SetInt("_DrawOrder",artOrder);
        decal.material.renderQueue = source.renderQueue + artOrder;
        decal.fadeFactor = 1f;                         // 🔴 알파는 셰이더에서 색 알파로 **한 번만** 곱한다
        decal.enabled = false;
        return decal;
    }

    // 🔴 윤곽선은 **경계만** 그리고 내부는 비운다 — 장판 규약이 "윤곽선이 생기고 내부가 텅 빈
    //    상태에서 차오른다"이기 때문. 내부를 미리 칠하면 채움이 어디까지 찼는지 안 읽힌다.
    void RebuildConeOutline(float halfAngle)
    {
        EnemyC3Visuals.SetDecalProperties(_coneOutline.material,C3Id,new Vector2(_coneRadius*2f,_coneRadius*2f),_coneRadius,0,halfAngle,0,0);
    }

    void RebuildBandOutline()
    {
        EnemyC3Visuals.SetDecalProperties(_bandOutline.material,C3Id,new Vector2(_pathRadius*2f,_pathLength),0,0,0,_pathRadius*2f,_pathLength);
    }

    // ─── 합집합(열쇠구멍) — 띠 + 끝점 원을 한 도형으로 ─────────────

    /// <summary>합집합의 바운딩 박스 — (폭, 길이). 길이는 보스 기준 0 에서 시작한다.</summary>
    Vector2 UnionExtent
    {
        get
        {
            float halfW = Mathf.Max(_pathRadius, _coneRadius);
            float len = Mathf.Max(_pathLength, _coneForwardOffset + _coneRadius);
            return new Vector2(halfW * 2f, Mathf.Max(0.01f, len));
        }
    }

    void RebuildUnionOutline()
    {
        Vector2 u = UnionExtent;
        EnemyC3Visuals.SetDecalProperties(_unionOutline.material,C3Id,u,_coneRadius,_coneForwardOffset,180,_pathRadius*2f,_pathLength);
    }

    // 안쪽 반경(정규화 0~1)만 비운 부채꼴 고리. inner 1 = 아직 빈 상태, 0 = 가득.
    void RebuildInwardConeFill(float inner)
    {
        EnemyC3Visuals.SetArcFill(_coneFill.material, _coneHalfAngle, inner);
    }

    /// <summary>
    /// 띠(폭 <c>_pathRadius</c>×2, 길이 <c>_pathLength</c>) 과 끝점 원(<c>_coneRadius</c>, 중심은
    /// <c>_coneForwardOffset</c>)의 <b>합집합</b> 채움을 전선 <paramref name="t"/>(0~1)까지 그린다.
    ///
    /// 🔴 거리함수(SDF)의 <b>최솟값</b>을 쓰므로 두 도형이 만나는 곳에 이음선이 생기지 않는다 —
    ///    그게 “한 도형으로 보이게” 하는 핵심이다(셰이더 <c>EnemyC3Decal.hlsl</c> 의 채움 5).
    /// </summary>
    void RebuildUnionFill(float t)
    {
        Vector2 u = UnionExtent;
        EnemyC3Visuals.SetUnionFill(_unionFill.material, u, _pathRadius, _pathLength, _coneForwardOffset, _coneRadius,
                                    u.y * Mathf.Clamp01(t));
    }

    // 🔴 재질 인스턴스를 정리한다(10-13 이전엔 부채꼴·띠만 정리해 합집합이 새고 있었다 — Codex 지적).
    void OnDestroy()
    {
        DestroyMaterial(_coneOutline); DestroyMaterial(_coneFill);
        DestroyMaterial(_bandOutline); DestroyMaterial(_bandFill);
        DestroyMaterial(_unionOutline); DestroyMaterial(_unionFill);
    }

    static void DestroyMaterial(DecalProjector d) { if (d != null && d.material != null) Destroy(d.material); }
}
