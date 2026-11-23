namespace ERP.Domain.Catalog;

public static class VariantGenerator {
    /// <summary>Özelliklerin kartezyen çarpımı; özellik Sort sırası, sonra değer sırası.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Combinations(IReadOnlyList<VariantAttribute> attrs) {
        var ordered = attrs.Where(a => a.Values.Count > 0).OrderBy(a => a.Sort).ToList();
        if (ordered.Count == 0) return [];

        IEnumerable<Dictionary<string, string>> acc = [new Dictionary<string, string>()];
        foreach (var attr in ordered) {
            var current = attr;
            acc = acc.SelectMany(d => current.Values.Select(v => new Dictionary<string, string>(d) { [current.Name] = v }));
        }

        return acc.Select(d => (IReadOnlyDictionary<string, string>)d).ToList();
    }
}
