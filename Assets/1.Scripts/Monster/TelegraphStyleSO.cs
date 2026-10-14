using System;
using UnityEngine;

/// <summary>마스크 이미지를 모양에 입히는 방식(PLAN-telegraph-style T3).</summary>
public enum TelegraphMaskMode
{
    Stretch = 0,   // 늘이기 — 모양 전체(바운딩 박스)에 이미지 한 장
    Tile = 1,      // 반복 — 반복 크기(m)마다 한 장. 예고 크기가 달라도 패턴 크기가 같다
}

/// <summary>한 층(윤곽선 또는 채움)의 마스크.</summary>
[Serializable]
public class TelegraphMask
{
    [Tooltip("마스크 이미지. 흰 = 보임, 검·투명 = 안 보임(값 = R × A). 비우면 마스크 없음.\n" +
             "규격 = Docs/tech/telegraph-style.md")]
    public Texture2D texture;
    [Tooltip("늘이기 = 모양 전체에 한 장 / 반복 = 반복 크기(m)마다 한 장.")]
    public TelegraphMaskMode mode = TelegraphMaskMode.Tile;
    [Tooltip("반복 모드에서 이미지 한 장이 덮는 크기(m).")]
    [Min(0.05f)] public float tileSize = 1f;
    [Tooltip("마스크를 얼마나 먹일지. 0 = 마스크 없음과 같음, 1 = 마스크 그대로.")]
    [Range(0f, 1f)] public float strength = 1f;
    [Tooltip("패턴 회전(도). 0 = 이미지 위쪽이 공격 방향(예고 정면).")]
    [Range(-180f, 180f)] public float rotation;

    public bool Active => texture != null && strength > 0f;
}

/// <summary>한 층 = 색 + 마스크.</summary>
[Serializable]
public class TelegraphLayer
{
    [Tooltip("색(알파 포함). 알파가 곧 불투명도 — 너무 쨍하면 알파나 채도를 낮춘다.")]
    public Color color = Color.white;
    public TelegraphMask mask = new TelegraphMask();

    public TelegraphLayer() { }
    public TelegraphLayer(Color c) { color = c; }
}

/// <summary>데칼 예고(부채꼴·경로 띠·합집합) 한 벌 — 공격 / 인터럽트.</summary>
[Serializable]
public class TelegraphDecalStyle
{
    [Tooltip("윤곽선 — 전체 크기로 처음부터 보이는 테두리(\"어디에\"). 안쪽은 비어 있다.")]
    public TelegraphLayer outline = new TelegraphLayer();
    [Tooltip("윤곽선 두께(m, 월드). 예고 크기와 무관하게 같은 두께.")]
    [Min(0.01f)] public float outlineWidth = 0.25f;
    [Tooltip("채움 — 0 에서 전체로 차오른다(\"언제\"). 다 차는 순간이 공격.")]
    public TelegraphLayer fill = new TelegraphLayer();
}

/// <summary>바닥 판 예고(드론 착탄 원 · 전기 장판 칸) 한 벌.</summary>
[Serializable]
public class TelegraphPlateStyle
{
    [Tooltip("외곽 판 — 전체 크기로 처음부터 보이는 연한 판.")]
    public TelegraphLayer outline = new TelegraphLayer();
    [Tooltip("채움 판 — 안쪽에서 차오르는 진한 판.")]
    public TelegraphLayer fill = new TelegraphLayer();
    [Tooltip("판 테두리 두께(판 크기에 대한 **비율** 0~0.5 — m 가 아니다). 테두리는 진하게, 안쪽은 0.55 배로 그린다.")]
    [Range(0.01f, 0.5f)] public float rimRatio = 0.08f;
}

/// <summary>
/// 공격 예고 색·마스크 스타일(PLAN-telegraph-style). 몬스터 데이터(<see cref="MonsterDataSO.telegraphStyle"/>)가 참조한다.
/// 비어 있으면 <see cref="Default"/>(코드 기본값 = 2026-10-13 화면 그대로).
///
/// 🔴 <b>Play 중에 값을 바꾸면 켜진 예고에 바로 반영되고, 바꾼 값은 에셋에 그대로 남는다</b>(팀장 10-13 T6 — 아트가 보며 맞춘 값을 그대로 쓴다).
///    갱신은 <see cref="Version"/> 을 보고 각 예고가 재질 값만 다시 넣는다 — 차오르던 진행은 끊기지 않는다.
/// 공격 = 빨강("여기 맞는다"), 인터럽트 = 파랑("간파 가능") 규약은 색을 바꿔도 **의미가 구분되게** 유지할 것.
/// </summary>
[CreateAssetMenu(fileName = "TelegraphStyle", menuName = "Monster/Telegraph Style")]
public class TelegraphStyleSO : ScriptableObject
{
    [Header("공격 예고 (부채꼴·경로 띠) — 23호·건틀렛 펀치·스피너 채찍")]
    public TelegraphDecalStyle attack = new TelegraphDecalStyle
    {
        outline = new TelegraphLayer(new Color(1f, 0.12f, 0.08f, 0.85f)),
        fill = new TelegraphLayer(new Color(1f, 0.10f, 0.05f, 0.55f)),
    };

    [Header("인터럽트 예고 — 간파 가능한 공격(23호 돌진·스피너 돌진) + 건틀렛 스매시 원 색(채움 RGB)")]
    public TelegraphDecalStyle interrupt = new TelegraphDecalStyle
    {
        outline = new TelegraphLayer(new Color(0.021842957f, 0f, 1f, 0.85f)),
        fill = new TelegraphLayer(new Color(0.021842957f, 0f, 1f, 0.55f)),
    };

    [Header("원형 판 — 웰즈 드론 착탄 원")]
    public TelegraphPlateStyle circle = new TelegraphPlateStyle
    {
        outline = new TelegraphLayer(new Color(1f, 0.15f, 0.1f, 0.3f)),
        fill = new TelegraphLayer(new Color(1f, 0.05f, 0.02f, 0.6f)),
        rimRatio = 0.08f,
    };

    [Header("사각 판 — 23호 전기 장판 칸")]
    public TelegraphPlateStyle floorTile = new TelegraphPlateStyle
    {
        outline = new TelegraphLayer(new Color(1f, 0.15f, 0.1f, 0.28f)),
        fill = new TelegraphLayer(new Color(1f, 0.05f, 0.02f, 0.55f)),
        rimRatio = 0.1f,
    };

    /// <summary>값이 바뀔 때마다 오른다. 켜진 예고가 이걸 보고 재질 값을 다시 넣는다.</summary>
    public static int Version { get; private set; }

    // 🔴 OnValidate 는 로딩 스레드에서도 불릴 수 있다 — 여기서 텍스처·재질을 만지지 않고 번호만 올린다.
    void OnValidate() => Version++;

    static TelegraphStyleSO _default;

    /// <summary>코드 기본값(= 2026-10-13 화면). 몬스터 데이터의 칸이 비었을 때 쓴다 — 빌드에서도 null 이 아니다.</summary>
    public static TelegraphStyleSO Default
    {
        get
        {
            if (_default == null)
            {
                _default = CreateInstance<TelegraphStyleSO>();
                _default.name = "TelegraphStyle (코드 기본값)";
                _default.hideFlags = HideFlags.DontSave;
            }
            return _default;
        }
    }

    public static TelegraphStyleSO Resolve(TelegraphStyleSO style) => style != null ? style : Default;

    /// <summary>이 컴포넌트가 속한 몬스터의 스타일(없으면 기본). 23호 패턴 컴포넌트(드론·전기 장판)도 이걸로 찾는다.</summary>
    public static TelegraphStyleSO For(Component c)
    {
        MonsterBase owner = c != null ? c.GetComponentInParent<MonsterBase>() : null;
        return Resolve(owner != null && owner.MonsterData != null ? owner.MonsterData.telegraphStyle : null);
    }

    public TelegraphDecalStyle Decal(BossAttackConeTelegraph.Palette palette) =>
        palette == BossAttackConeTelegraph.Palette.Interrupt ? interrupt : attack;
}
