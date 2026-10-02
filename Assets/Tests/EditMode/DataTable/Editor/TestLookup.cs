using System;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;

// 테스트용 대상 조회 — 넘겨받은 객체를 (타입 이름, Id) 로 찾는다. Id 를 안 주면 객체 이름.
internal sealed class TestLookup : IDataTableAssetLookup
{
    private readonly Dictionary<string, Object> byId;
    private readonly Type type;

    public TestLookup(params Object[] targets)
        : this(targets.Select(t => (t.name, t)).ToArray())
    {
    }

    public TestLookup(params (string id, Object target)[] targets)
    {
        byId = targets.ToDictionary(t => t.id, t => t.target);
        type = targets.Length > 0 ? targets[0].target.GetType() : typeof(DataTableTestData);
    }

    public Type FindType(string sheetName, out string error)
    {
        error = sheetName == type.Name ? null : $"no type {sheetName}";
        return error == null ? type : null;
    }

    public Object FindTarget(Type requested, string id, out string error)
    {
        error = byId.ContainsKey(id) ? null : $"no target {id}";
        return error == null ? byId[id] : null;
    }

    public IReadOnlyCollection<string> AllIds(Type requested) => byId.Keys;
}
