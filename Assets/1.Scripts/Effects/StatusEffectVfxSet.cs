using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상태이상 → 루프 이펙트 표(2026-10-20 민경). 플레이어 공용 1개(<c>9.ScriptableObject/Effects/PlayerStatusEffectVfxSet.asset</c>)를
/// 각 플레이어의 <see cref="StatusEffectVfxPresenter"/> 가 읽는다.
///
/// <b>새 상태 이펙트는 여기에 한 줄 추가하면 끝이다</b> — 프리팹·코드는 건드리지 않는다.
/// 캐릭터마다 다른 건 "붙는 자리"의 위치뿐이라 그건 Presenter 의 자리 트랜스폼(머리 위 = <c>TopHead</c> 등)이 맡는다.
///
/// 같은 자리에 둘 이상 걸리면 <see cref="Row.priority"/> 가 가장 높은 것 하나만 보인다(다른 자리끼리는 동시).
/// 기본 순서는 머리 위 상태 글자(<c>OverheadStatusLabelRules</c>)와 같게 둔다: 공중 60 > 기절 50 > 속박 40 > 침묵 30 > 약화 20 > 둔화 10.
/// 상태 종류만 다루므로 나중에 몬스터용 Presenter 도 같은 형식을 쓸 수 있다.
/// </summary>
[CreateAssetMenu(fileName = "StatusEffectVfxSet", menuName = "Effects/Status Effect Vfx Set")]
public sealed class StatusEffectVfxSet : ScriptableObject
{
    /// <summary>이펙트가 붙는 자리. 실제 트랜스폼은 캐릭터마다 Presenter 에서 지정한다.</summary>
    public enum Anchor
    {
        Overhead,
        Body,
        Feet,
    }

    [System.Serializable]
    public sealed class Row
    {
        [Tooltip("이 상태가 걸려 있는 동안 재생한다. 플래그 하나만 고른다.")]
        public StatusEffectType status = StatusEffectType.Stunned;

        [Tooltip("루프 엔트리. 상태가 풀리면 엔트리의 outroDuration 동안 흩어지고 반납된다.")]
        public EffectEntry effect;

        [Tooltip("붙는 자리 — 머리 위 / 몸 / 발밑. 위치는 캐릭터마다 Presenter 의 자리 트랜스폼이 정한다.")]
        public Anchor anchor = Anchor.Overhead;

        [Tooltip("자리 기준 월드 단위 오프셋(m). 배율에 곱해지지 않는다.")]
        public Vector3 offset;

        [Tooltip("프리팹 저작 크기에 곱하는 배율.")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("같은 자리에 둘 이상 걸리면 큰 값 하나만 보인다. 공중 60 · 기절 50 · 속박 40 · 침묵 30 · 약화 20 · 둔화 10.")]
        public int priority = 50;
    }

    [SerializeField] private Row[] rows = new Row[0];

    public IReadOnlyList<Row> Rows => rows;

    private void OnValidate()
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i];
            if (row == null)
                continue;

            int bits = (int)row.status;
            if (bits == 0 || (bits & (bits - 1)) != 0)
                Edit.LogWarning($"[StatusEffectVfxSet] '{name}' {i}번 줄의 상태가 '{row.status}' — 플래그를 하나만 골라야 한다.", this);
            if (row.effect == null)
                Edit.LogWarning($"[StatusEffectVfxSet] '{name}' {i}번 줄({row.status})에 엔트리가 비어 있다 — 이 상태는 연출 없이 지나간다.", this);
        }
    }
}
