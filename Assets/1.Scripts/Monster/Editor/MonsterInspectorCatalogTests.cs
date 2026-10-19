using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 데이터 인스펙터 칸 사전(PLAN-monster-data-inspector) — 사전이 실제 칸과 맞는지 고정한다.
/// 🔴 사전에 없는 칸도 인스펙터에서 사라지지 않는다(미분류) — 아래 "Covers" 는 지금 칸이 전부 제자리에 있는지만 본다.
/// 실패하면 메시지에 나온 칸을 MonsterInspectorCatalog.cs 에 한 줄씩 추가하면 된다.
/// </summary>
public sealed class MonsterInspectorCatalogTests
{
    [Test]
    public void EveryCatalogField_ExistsOnBossData()
    {
        var data = ScriptableObject.CreateInstance<BossDataSO>();
        try
        {
            var so = new SerializedObject(data);
            string[] missing = MonsterInspectorCatalog.Fields.Keys.Where(name => so.FindProperty(name) == null).ToArray();
            Assert.IsEmpty(missing, "사전에 있지만 실제로 없는 칸(이름 바뀜·삭제?): " + string.Join(", ", missing));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void Catalog_CoversEveryCurrentField()
    {
        var data = ScriptableObject.CreateInstance<BossDataSO>();
        try
        {
            string[] unclassified = TopLevelFields(new SerializedObject(data))
                .Where(name => MonsterInspectorCatalog.GroupOf(name) == MonsterInspectorCatalog.Unclassified)
                .ToArray();
            Assert.IsEmpty(unclassified, "미분류로 나오는 칸 — 사전에 묶음을 정해 줄 것: " + string.Join(", ", unclassified));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void UnknownField_GoesToUnclassified()
    {
        Assert.AreEqual(MonsterInspectorCatalog.Unclassified, MonsterInspectorCatalog.GroupOf("someFieldAddedTomorrow"));
    }

    [Test]
    public void EveryGroupKey_IsDeclared()
    {
        var declared = new HashSet<string>(MonsterInspectorCatalog.Groups.Select(g => g.Key));
        string[] stray = MonsterInspectorCatalog.Fields.Values.Select(f => f.Group).Where(g => !declared.Contains(g)).Distinct().ToArray();
        Assert.IsEmpty(stray, "Groups 에 없는 묶음 키 — 그 칸은 인스펙터에 안 나온다: " + string.Join(", ", stray));
    }

    [Test]
    public void AttackAndPhaseRows_MatchEntryFields()
    {
        var data = ScriptableObject.CreateInstance<BossDataSO>();
        try
        {
            var so = new SerializedObject(data);
            SerializedProperty attack = so.FindProperty("attacks").GetArrayElementAtIndex(0);
            string[] attackMissing = MonsterInspectorCatalog.AttackFields.Keys.Where(n => attack.FindPropertyRelative(n) == null).ToArray();
            Assert.IsEmpty(attackMissing, "공격 행에 없는 칸: " + string.Join(", ", attackMissing));

            string[] attackUnlabeled = Children(attack).Where(n => !MonsterInspectorCatalog.AttackFields.ContainsKey(n)).ToArray();
            Assert.IsEmpty(attackUnlabeled, "공격 행 칸 중 한국어 이름이 없는 것: " + string.Join(", ", attackUnlabeled));

            SerializedProperty phase = so.FindProperty("phases").GetArrayElementAtIndex(0);
            string[] phaseMissing = MonsterInspectorCatalog.PhaseFields.Keys.Where(n => phase.FindPropertyRelative(n) == null).ToArray();
            Assert.IsEmpty(phaseMissing, "페이즈 행에 없는 칸: " + string.Join(", ", phaseMissing));
        }
        finally
        {
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void Editor_IsUsedForMonsterAndBossData()
    {
        foreach (System.Type type in new[] { typeof(MonsterDataSO), typeof(BossDataSO) })
        {
            ScriptableObject data = ScriptableObject.CreateInstance(type);
            Editor editor = Editor.CreateEditor(data);
            try
            {
                Assert.IsInstanceOf<MonsterDataSOEditor>(editor, type.Name);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(data);
            }
        }
    }

    private static IEnumerable<string> TopLevelFields(SerializedObject so)
    {
        SerializedProperty iterator = so.GetIterator();
        bool enter = true;
        while (iterator.NextVisible(enter))
        {
            enter = false;
            if (iterator.name != "m_Script")
            {
                yield return iterator.name;
            }
        }
    }

    private static IEnumerable<string> Children(SerializedProperty entry)
    {
        SerializedProperty child = entry.Copy();
        SerializedProperty end = entry.GetEndProperty();
        if (!child.NextVisible(true))
        {
            yield break;
        }

        while (!SerializedProperty.EqualContents(child, end))
        {
            yield return child.name;
            if (!child.NextVisible(false))
            {
                yield break;
            }
        }
    }
}
