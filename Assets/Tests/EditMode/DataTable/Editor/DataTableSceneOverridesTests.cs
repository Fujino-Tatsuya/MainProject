using System.Linq;
using NUnit.Framework;

public sealed class DataTableSceneOverridesTests
{
    // 실제 MapScene 의 GameRule 인스턴스 형식(2026-10-02) — 수정 항목마다 target / propertyPath / value / objectReference.
    private const string Scene =
        "--- !u!1001 &1630000000\n" +
        "PrefabInstance:\n" +
        "  m_Modification:\n" +
        "    serializedVersion: 3\n" +
        "    m_TransformParent: {fileID: 0}\n" +
        "    m_Modifications:\n" +
        "    - target: {fileID: 5649730361181887339, guid: bf0b56a02af32ff499a78063fb4bd3a1, type: 3}\n" +
        "      propertyPath: m_LocalPosition.x\n" +
        "      value: 0\n" +
        "      objectReference: {fileID: 0}\n" +
        "    - target: {fileID: -4216859302048453862, guid: bf0b56a02af32ff499a78063fb4bd3a1, type: 3}\r\n" +
        "      propertyPath: defaultLifeCount\r\n" +
        "      value: 1\r\n" +
        "      objectReference: {fileID: 0}\n" +
        "    - target: {fileID: 11, guid: 0123456789abcdef0123456789abcdef, type: 3}\n" +
        "      propertyPath: phases.Array.data[2]\n" +
        "      value: 7\n";

    [Test]
    public void ParseModifications_ReadsEveryEntry_IncludingNegativeIdsCrlfAndArrayPaths()
    {
        var entries = DataTableSceneOverrides.ParseModifications(Scene).ToList();

        Assert.That(entries, Is.EqualTo(new[]
        {
            ("bf0b56a02af32ff499a78063fb4bd3a1", 5649730361181887339L, "m_LocalPosition.x"),
            ("bf0b56a02af32ff499a78063fb4bd3a1", -4216859302048453862L, "defaultLifeCount"),
            ("0123456789abcdef0123456789abcdef", 11L, "phases.Array.data[2]"),
        }));
    }
}
