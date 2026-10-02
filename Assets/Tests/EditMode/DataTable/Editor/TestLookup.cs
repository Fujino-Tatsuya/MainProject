using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 테스트용 에셋 조회 — 시트 이름 DataTableTestData 만 알고, 넘겨받은 메모리 SO 를 이름으로 찾는다.
internal sealed class TestLookup : IDataTableAssetLookup
{
    private readonly Dictionary<string, ScriptableObject> assets;

    public TestLookup(params ScriptableObject[] assets)
    {
        this.assets = assets.ToDictionary(a => a.name);
    }

    public Type FindType(string sheetName, out string error)
    {
        error = sheetName == nameof(DataTableTestData) ? null : $"no type {sheetName}";
        return error == null ? typeof(DataTableTestData) : null;
    }

    public IReadOnlyDictionary<string, ScriptableObject> AssetsOf(Type type, out IReadOnlyList<string> duplicateNames)
    {
        duplicateNames = Array.Empty<string>();
        return assets;
    }
}
