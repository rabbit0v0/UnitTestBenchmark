using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Parses YAML rule files into Rule objects.
///
/// YAML format — each given/assert item is a single-key YAML mapping:
///
///   rules:
///     - id: my_rule
///       description: "..."
///       given:
///         - create $acc: Accessor($val)
///         - call: $setter.SetValue($newVal) on IAccessorSetter&lt;any&gt;
///         - assign $setter = $acc: IAccessorSetter&lt;any&gt;
///         - mock: OtherFunc returns false
///       assert:
///         - IsNull: $acc.Value
///         - AreEqual: [$a, $b]
///         - Throws: any
/// </summary>
static class RuleParser
{
    public static List<Rule> Parse(string yamlContent)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var doc = deserializer.Deserialize<YamlRoot>(yamlContent);

        return doc.Rules.Select(ParseRule).ToList();
    }

    static Rule ParseRule(YamlRule yr)
    {
        var rule = new Rule { Id = yr.Id, Parent = yr.Parent, OrGroup = yr.OrGroup, Description = yr.Description ?? "" };

        foreach (var givenMap in yr.Given ?? new())
            rule.Given.Add(ParseGivenClause(givenMap));

        foreach (var assertMap in yr.Assert ?? new())
            rule.Assert.Add(ParseAssertClause(assertMap));

        return rule;
    }

    // ── Given clause parsing ─────────────────────────────

    static GivenClause ParseGivenClause(Dictionary<string, object> map)
    {
        var (key, value) = map.First();
        var val = value?.ToString() ?? "";

        // Check for "not " prefix → negated clause
        bool negated = false;
        if (key.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            negated = true;
            key = key[4..]; // strip "not "
        }

        GivenClause clause = ParseGivenClauseCore(key, val);
        clause.Negated = negated;
        return clause;
    }

    static GivenClause ParseGivenClauseCore(string key, string val)
    {
        // create $var: TypePattern($arg1, $arg2)
        // YAML parses "create $acc" as key, "Accessor($val)" as value
        var createMatch = Regex.Match(key, @"^create\s+(\$\w+)$");
        if (createMatch.Success)
            return ParseCreateClause(createMatch.Groups[1].Value, val);

        // $result = call: ...
        var resultCallMatch = Regex.Match(key, @"^(\$\w+)\s*=\s*call$");
        if (resultCallMatch.Success)
            return ParseCallClause(val, resultBindVar: resultCallMatch.Groups[1].Value);

        // call: ...
        if (key == "call")
            return ParseCallClause(val);

        // assign $target = $source: CastType
        var assignMatch = Regex.Match(key, @"^assign\s+(\$\w+)\s*=\s*(\$\w+)$");
        if (assignMatch.Success)
        {
            return new AssignClause
            {
                TargetBindVar = assignMatch.Groups[1].Value,
                SourceBindVar = assignMatch.Groups[2].Value,
                CastTypePattern = val
            };
        }

        // mock: ...
        if (key == "mock")
            return ParseMockClause(val);

        throw new InvalidOperationException($"Unknown given clause key: '{key}'");
    }

    static CreateClause ParseCreateClause(string bindVar, string value)
    {
        // value: "Accessor" or "Accessor<any>" or "Accessor($val, $arg2)"
        var match = Regex.Match(value, @"^([\w<>,\s?]+?)(?:\(([^)]*)\))?$");
        if (!match.Success)
            throw new InvalidOperationException($"Cannot parse create value: '{value}'");

        var clause = new CreateClause
        {
            BindVar = bindVar,
            TypePattern = match.Groups[1].Value.Trim()
        };

        if (match.Groups[2].Success)
        {
            clause.ExactArgCount = true;
            if (!string.IsNullOrWhiteSpace(match.Groups[2].Value))
                clause.ArgBindings = SplitArgs(match.Groups[2].Value);
        }

        return clause;
    }

    static CallClause ParseCallClause(string value, string? resultBindVar = null)
    {
        // value: "MethodName" or "$recv.Method($arg)" or "Method on Type" or "Method($a) -> $cb" or "Method throws $exc"
        // Full pattern: [receiver.]Method[(args)] [on TypePattern] [-> $cbVar] [throws $excVar]
        var match = Regex.Match(value, @"^(?:(\$\w+)\.)?(\w+)(?:\(([^)]*)\))?\s*(?:on\s+(.+?))?\s*(?:->\s*(\$\w+))?\s*(?:throws\s+(\$\w+|\w+))?$");
        if (!match.Success)
            throw new InvalidOperationException($"Cannot parse call value: '{value}'");

        var clause = new CallClause
        {
            ResultBindVar = resultBindVar,
            MethodName = match.Groups[2].Value
        };

        if (match.Groups[1].Success && !string.IsNullOrEmpty(match.Groups[1].Value))
            clause.ReceiverBindVar = match.Groups[1].Value;

        if (match.Groups[3].Success && !string.IsNullOrWhiteSpace(match.Groups[3].Value))
            clause.ArgBindings = SplitArgs(match.Groups[3].Value);

        if (match.Groups[4].Success && !string.IsNullOrEmpty(match.Groups[4].Value))
            clause.OnTypePattern = match.Groups[4].Value.Trim();

        if (match.Groups[5].Success && !string.IsNullOrEmpty(match.Groups[5].Value))
            clause.CallbackBindVar = match.Groups[5].Value;

        if (match.Groups[6].Success && !string.IsNullOrEmpty(match.Groups[6].Value))
            clause.ThrowsBindVar = match.Groups[6].Value;

        return clause;
    }

    static MockClause ParseMockClause(string value)
    {
        // value: "OtherFunc returns false" or "$mock.OtherFunc returns false"
        var match = Regex.Match(value, @"^(?:(\$\w+)\.)?(\w+)\s+returns\s+(.+)$");
        if (!match.Success)
            throw new InvalidOperationException($"Cannot parse mock value: '{value}'");

        return new MockClause
        {
            MockBindVar = match.Groups[1].Success ? match.Groups[1].Value : null,
            MethodName = match.Groups[2].Value,
            ReturnValue = match.Groups[3].Value.Trim()
        };
    }

    // ── Assert clause parsing ────────────────────────────

    static AssertClause ParseAssertClause(Dictionary<string, object> map)
    {
        var (key, value) = map.First();
        var clause = new AssertClause { SemanticMeaning = key };

        if (value is List<object> list)
        {
            clause.Args = list.Select(o =>
            {
                if (o == null) return "null";
                var s = o.ToString() ?? "null";
                // YAML "" is an empty string; C# AST represents it as ""
                if (s.Length == 0) return "\"\"";
                return s;
            }).ToList();
        }
        else if (value != null)
        {
            var s = value.ToString() ?? "";
            if (s.Length == 0) s = "\"\"";
            if (s != "any")
                clause.Args.Add(s);
        }

        return clause;
    }

    // ── Utility ──────────────────────────────────────────

    static List<string> SplitArgs(string argStr)
    {
        return argStr.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
    }

    // ── YAML deserialization models ──────────────────────

    class YamlRoot
    {
        public List<YamlRule> Rules { get; set; } = new();
    }

    class YamlRule
    {
        public string Id { get; set; } = "";
        public string? Parent { get; set; }
        public string? OrGroup { get; set; }
        public string? Description { get; set; }
        public List<Dictionary<string, object>>? Given { get; set; }
        public List<Dictionary<string, object>>? Assert { get; set; }
    }
}
