using UnityEngine;

/// <summary>
/// 게임 세계 레이어 이름·마스크 한 곳(할 일 32 벽 개념 — Docs/tech/wall-concept.md).
/// <list type="bullet">
/// <item><b>Wall</b> = 맵의 벽(태그 Wall + 레이어 Wall). 이동을 막고, 외부 힘으로 밀린 유닛이 닿으면 <see cref="UnitWallImpacts"/> 이벤트가 난다.</item>
/// <item><b>Ground</b> = 밟는 바닥(태그 Ground + 레이어 Ground).</item>
/// <item><b>Barrier</b> = 튜토리얼 배리어·개인 뒤 배리어(런타임 생성). 이동은 막지만 벽이 아니다 — 벽 충돌 이벤트가 나지 않고 NavMesh 베이크에도 안 들어간다(카브).</item>
/// </list>
/// 🔴 정적 필드 초기화에서 <see cref="LayerMask.NameToLayer"/> 를 부르지 말 것(직렬화 스레드) — 그래서 전부 프로퍼티다.
/// </summary>
public static class GameLayers
{
    public const string Wall = "Wall";
    public const string Ground = "Ground";
    public const string Barrier = "Barrier";
    public const string Enemy = "Enemy";
    public const string EnemyHurtBox = "EnemyHurtBox";

    /// <summary>Wall 레이어 비트(없으면 0).</summary>
    public static int WallMask => LayerMask.GetMask(Wall);

    /// <summary>Barrier 레이어 비트(없으면 0 — TagManager 에 20번 Barrier 가 있어야 한다. 19번은 Robot Sentries 팩 몬스터 뼈(래그돌 콜라이더)가 쓰므로 쓰지 말 것).</summary>
    public static int BarrierMask => LayerMask.GetMask(Barrier);

    /// <summary>커서로 고르는 적 — 몸(Enemy)+피격 판정(EnemyHurtBox). 단일 대상 스킬 데이터의 TargetableLayers 와 같은 조합이다.</summary>
    public static int EnemyTargetMask => LayerMask.GetMask(Enemy, EnemyHurtBox);

    /// <summary>배리어 오브젝트에 줄 레이어. Barrier 가 없으면 예전처럼 Wall(이동은 계속 막힌다).</summary>
    public static int BarrierLayer
    {
        get
        {
            int layer = LayerMask.NameToLayer(Barrier);
            return layer >= 0 ? layer : LayerMask.NameToLayer(Wall);
        }
    }
}
