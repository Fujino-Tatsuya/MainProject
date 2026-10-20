using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 외곽선(할 일 58)의 이번 프레임 그릴 목록 — <see cref="PlayerEnemyHoverOutline"/>(오너 로컬)이 LateUpdate 에 채우고
/// <see cref="EnemyOutlineFeature"/> 가 렌더에서 읽는다. 렌더러 피처는 렌더러 애셋의 서브에셋이라 씬 쪽에서 참조를 잡기
/// 번거롭다 — <see cref="PlayerSilhouetteFeature.DevEnabled"/> 처럼 정적 한 곳으로 넘긴다(프로세스당 오너는 하나, MPPM 은 피어마다 프로세스).
/// 색은 인덱스 두 개뿐이다 — 마스크 G 채널이 0/1 로 구분한다.
/// </summary>
public static class EnemyOutlineDrawList
{
    public const int PrimaryColorIndex = 0;   // 호버·스킬 범위(빨강)
    public const int AggroColorIndex = 1;     // 나를 노리는 몬스터(주황)
    public const int MinWidthPixels = 1;
    public const int MaxWidthPixels = 8;

    public struct Item
    {
        public Renderer Renderer;
        public int SubMeshCount;
        public int ColorIndex;
    }

    static readonly List<Item> s_items = new List<Item>(64);

    public static int Count => s_items.Count;
    public static Item Get(int index) => s_items[index];

    public static Color PrimaryColor { get; private set; } = Color.red;
    public static Color AggroColor { get; private set; } = new Color(1f, 0.55f, 0.1f, 1f);
    public static int WidthPixels { get; private set; } = 3;

    /// <summary>이번 프레임 목록을 새로 시작한다(이전 프레임 항목은 버린다).</summary>
    public static void Begin(Color primary, Color aggro, int widthPixels)
    {
        s_items.Clear();
        PrimaryColor = primary;
        AggroColor = aggro;
        WidthPixels = ClampWidth(widthPixels);
    }

    /// <summary>렌더러 하나를 넣는다 — 꺼졌거나(<c>enabled</c>·<c>forceRenderingOff</c>·비활성) 메시가 없으면 넣지 않는다.</summary>
    public static bool Add(Renderer renderer, int colorIndex)
    {
        if (!EnemyOutlineRenderers.IsDrawable(renderer))
            return false;

        int subMeshes = EnemyOutlineRenderers.SubMeshCount(renderer);
        if (subMeshes <= 0)
            return false;

        s_items.Add(new Item
        {
            Renderer = renderer,
            SubMeshCount = subMeshes,
            ColorIndex = colorIndex == AggroColorIndex ? AggroColorIndex : PrimaryColorIndex,
        });
        return true;
    }

    public static void Clear() => s_items.Clear();

    public static int ClampWidth(int widthPixels) =>
        widthPixels < MinWidthPixels ? MinWidthPixels : widthPixels > MaxWidthPixels ? MaxWidthPixels : widthPixels;
}
