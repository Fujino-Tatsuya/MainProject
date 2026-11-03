using System.Collections.Generic;
using UnityEngine;

/// <summary>플레이어 공용 원샷 이펙트 종류(2026-11-02 민경). 직렬화 값이라 끝에만 추가한다.</summary>
public enum PlayerOneShotVfx
{
    /// <summary>체력 회복 — 불굴의 의지 본인 회복·나누는 불굴 받는 아군.</summary>
    Heal = 0,
    /// <summary>증강 획득 — 고른 본인 화면만(계기 = <see cref="TraitAcquiredVfxPresenter"/>).</summary>
    LevelUp = 1,
}

/// <summary>
/// 플레이어 공용 원샷 이펙트 표(2026-11-02 민경). 공용 1개(<c>9.ScriptableObject/Effects/PlayerOneShotVfxSet.asset</c>)를
/// 각 플레이어의 <see cref="PlayerOneShotVfxPresenter"/> 가 읽는다.
///
/// <b>표는 "무엇을 어디에"만 정한다.</b> "언제"(그리고 누구 화면에만)는 부르는 쪽(계기)이 정한다 —
/// 힐 = 불굴의 의지 RPC·나누는 불굴 리본 도착, 레벨업 = 증강 획득 감시.
/// 붙는 자리의 실제 위치는 캐릭터마다 <see cref="StatusEffectVfxPresenter"/> 의 자리 트랜스폼을 같이 쓴다.
///
/// 루프 상태 이펙트(<see cref="StatusEffectVfxSet"/>)와는 켜고 끄는 규칙이 달라 별도 표다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerOneShotVfxSet", menuName = "Effects/Player One-Shot Vfx Set")]
public sealed class PlayerOneShotVfxSet : ScriptableObject
{
    [System.Serializable]
    public sealed class Row
    {
        [Tooltip("이 줄의 종류. 같은 종류가 둘이면 위의 줄만 쓴다.")]
        public PlayerOneShotVfx kind = PlayerOneShotVfx.Heal;

        [Tooltip("원샷 엔트리.")]
        public EffectEntry effect;

        [Tooltip("붙는 자리 — 머리 위 / 몸 / 발밑. 위치는 캐릭터마다 StatusEffectVfxPresenter 의 자리 트랜스폼이 정한다.")]
        public StatusEffectVfxSet.Anchor anchor = StatusEffectVfxSet.Anchor.Feet;

        [Tooltip("자리 기준 월드 단위 오프셋(m). 배율에 곱해지지 않는다.")]
        public Vector3 offset;

        [Tooltip("프리팹 저작 크기에 곱하는 배율.")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("켜면 재생하는 동안 몸을 따라간다(걸어가도 뒤에 남지 않는다). 끄면 재생 순간 자리에 고정.")]
        public bool follow = true;
    }

    [SerializeField] private Row[] rows = new Row[0];

    public IReadOnlyList<Row> Rows => rows;

    /// <summary>그 종류의 줄(없거나 엔트리가 비면 null).</summary>
    public Row Find(PlayerOneShotVfx kind)
    {
        if (rows == null)
            return null;
        foreach (Row row in rows)
        {
            if (row != null && row.kind == kind)
                return row.effect != null ? row : null;
        }
        return null;
    }

    private void OnValidate()
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null && rows[i].effect == null)
                Edit.LogWarning($"[PlayerOneShotVfxSet] '{name}' {i}번 줄({rows[i].kind})에 엔트리가 비어 있다 — 이 연출은 나오지 않는다.", this);
        }
    }
}
