using System;
using System.Collections.Generic;
using System.Linq;
using Object = UnityEngine.Object;

// 테스트용 대상 조회 — 넘겨받은 객체를 (시트 이름, Id) 로 찾는다. Id 를 안 주면 객체 이름.
// 시트 이름 = 객체 타입 이름. Group 을 주면 그 이름이 모든 대상 타입의 묶음 시트가 된다([DataTableSheet("묶음")] 흉내).
internal sealed class TestLookup : IDataTableAssetLookup
{
    private readonly List<(string id, Object target)> targets;

    public TestLookup(params Object[] targets)
        : this(targets.Select(t => (t.name, t)).ToArray())
    {
    }

    public TestLookup(params (string id, Object target)[] targets)
    {
        this.targets = targets.ToList();
    }

    /// <summary>묶음 시트 이름. 주면 개별 타입 이름 시트로는 못 찾는다(실제 조회기와 같은 규칙).</summary>
    public string Group { get; set; }

    public IReadOnlyList<Type> FindTypes(string sheetName, out string error)
    {
        List<Type> types = targets.Select(t => t.target.GetType()).Distinct().ToList();
        if (Group != null)
        {
            error = sheetName == Group ? null : $"no sheet {sheetName}";
            return error == null ? types : null;
        }

        List<Type> named = types.Where(t => t.Name == sheetName).ToList();
        error = named.Count == 1 ? null : $"no type {sheetName}";
        return error == null ? named : null;
    }

    public Object FindTarget(Type requested, string id, out string error)
    {
        Object found = targets.Where(t => t.id == id && t.target.GetType() == requested).Select(t => t.target).FirstOrDefault();
        error = found != null ? null : $"no {requested.Name} target {id}";
        return found;
    }

    public IReadOnlyCollection<string> AllIds(Type requested) =>
        targets.Where(t => t.target.GetType() == requested).Select(t => t.id).ToList();
}
