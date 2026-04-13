using System.Text.RegularExpressions;

/// <summary>
/// Constraint-satisfaction solver: matches rules against extracted test facts.
///
/// For each rule and each test, tries to find a consistent binding of rule variables
/// ($x, $acc, $result, ...) to actual code variable names that satisfies ALL given
/// clauses (in order) and ALL assert clauses.
///
/// Algorithm: backtracking search over given clauses with ordered facts.
/// Typical complexity: O(N^M) where N = facts per test (5-20) and M = given clauses (1-4).
/// </summary>
static class Solver
{
    /// <summary>
    /// Try to match a rule against a single test's facts.
    /// Returns the result with binding details.
    /// </summary>
    public static RuleResult Match(Rule rule, TestFacts test)
    {
        var result = new RuleResult { Rule = rule };
        var bindings = new Dictionary<string, string>();
        var finalBindings = new Dictionary<string, string>();

        if (SolveGiven(rule.Given, 0, test.Facts, 0, bindings, rule.Assert, test, result, finalBindings))
        {
            result.IsImplemented = true;
            result.MatchedTest = test.TestName;
            result.Bindings = finalBindings;
            result.GivenMatched = rule.Given.Select(DescribeGiven).ToList();
            result.AssertMatched = rule.Assert.Select(DescribeAssert).ToList();
        }
        else
        {
            result.IsImplemented = false;
        }

        return result;
    }

    /// <summary>
    /// Try to match a rule against ALL tests. Returns the best result (first match).
    /// </summary>
    public static RuleResult MatchAgainstAll(Rule rule, List<TestFacts> allTests)
    {
        RuleResult? firstMatch = null;
        var allMatched = new List<string>();

        foreach (var test in allTests)
        {
            var result = Match(rule, test);
            if (result.IsImplemented)
            {
                allMatched.Add(test.TestName);
                firstMatch ??= result;
            }
        }

        if (firstMatch != null)
        {
            firstMatch.AllMatchedTests = allMatched;
            return firstMatch;
        }

        // No test matched — report what's missing
        var failed = new RuleResult
        {
            Rule = rule,
            IsImplemented = false,
            GivenMissing = rule.Given.Select(DescribeGiven).ToList(),
            AssertMissing = rule.Assert.Select(DescribeAssert).ToList()
        };
        return failed;
    }

    // ── Backtracking solver for Given clauses ─────────────

    static bool SolveGiven(
        List<GivenClause> givens, int givenIdx,
        List<Fact> facts, int factStartIdx,
        Dictionary<string, string> bindings,
        List<AssertClause> asserts, TestFacts test,
        RuleResult result, Dictionary<string, string> finalBindings)
    {
        // All given clauses matched — check asserts
        if (givenIdx >= givens.Count)
        {
            if (CheckAsserts(asserts, test, bindings))
            {
                // Propagate successful bindings back to caller
                finalBindings.Clear();
                foreach (var kv in bindings) finalBindings[kv.Key] = kv.Value;
                return true;
            }
            return false;
        }

        var clause = givens[givenIdx];

        // Negated clause: verify NO fact matches, then continue without advancing fact index
        if (clause.Negated)
        {
            for (int fi = 0; fi < facts.Count; fi++)
            {
                var probe = TryMatchGiven(clause, facts[fi], bindings, test);
                if (probe != null)
                    return false; // found a match → negation fails
            }
            // No match found → negation succeeds, continue with same factStartIdx
            return SolveGiven(givens, givenIdx + 1, facts, factStartIdx, bindings, asserts, test, result, finalBindings);
        }

        // Try each fact from factStartIdx onward (preserving order)
        for (int fi = factStartIdx; fi < facts.Count; fi++)
        {
            var newBindings = TryMatchGiven(clause, facts[fi], bindings, test);
            if (newBindings != null)
            {
                if (SolveGiven(givens, givenIdx + 1, facts, fi + 1, newBindings, asserts, test, result, finalBindings))
                    return true;
            }
        }

        return false;
    }

    // ── Given clause matching ─────────────────────────────

    static Dictionary<string, string>? TryMatchGiven(GivenClause clause, Fact fact, Dictionary<string, string> bindings, TestFacts test)
    {
        return clause switch
        {
            CreateClause c => TryMatchCreate(c, fact, bindings),
            CallClause c => TryMatchCall(c, fact, bindings, test),
            AssignClause c => TryMatchAssign(c, fact, bindings),
            MockClause c => TryMatchMock(c, fact, bindings),
            _ => null
        };
    }

    static Dictionary<string, string>? TryMatchCreate(CreateClause clause, Fact fact, Dictionary<string, string> bindings)
    {
        if (fact is not CreatedFact cf) return null;

        // Type must match
        if (!TypeMatches(clause.TypePattern, cf.Type)) return null;

        var b = new Dictionary<string, string>(bindings);

        // Bind the variable
        if (!TryBind(b, clause.BindVar, cf.Variable)) return null;

        // When explicit parens used (e.g. Accessor()), require exact arg count match
        if (clause.ExactArgCount && cf.Arguments.Count != clause.ArgBindings.Count)
            return null;

        // Bind constructor arguments
        for (int i = 0; i < clause.ArgBindings.Count; i++)
        {
            if (i >= cf.Arguments.Count) return null;
            var argBind = clause.ArgBindings[i];
            if (!TryBindOrMatch(b, argBind, cf.Arguments[i])) return null;
        }

        return b;
    }

    static Dictionary<string, string>? TryMatchCall(CallClause clause, Fact fact, Dictionary<string, string> bindings, TestFacts test)
    {
        if (fact is not CalledFact cf) return null;

        // Method name must match
        if (!cf.Method.Equals(clause.MethodName, StringComparison.OrdinalIgnoreCase)) return null;

        var b = new Dictionary<string, string>(bindings);

        // Bind receiver if specified
        if (clause.ReceiverBindVar != null)
        {
            if (cf.Receiver == null) return null;
            if (!TryBind(b, clause.ReceiverBindVar, cf.Receiver)) return null;
        }

        // Check onType if specified
        if (clause.OnTypePattern != null)
        {
            if (cf.ReceiverType == null) return null;
            if (!TypeMatches(clause.OnTypePattern, cf.ReceiverType)) return null;
        }

        // Bind result variable if specified
        if (clause.ResultBindVar != null)
        {
            if (cf.ResultVariable == null) return null;
            if (!TryBind(b, clause.ResultBindVar, cf.ResultVariable)) return null;
        }

        // Bind method arguments
        for (int i = 0; i < clause.ArgBindings.Count; i++)
        {
            if (i >= cf.Arguments.Count) return null;
            if (!TryBindOrMatch(b, clause.ArgBindings[i], cf.Arguments[i])) return null;
        }

        // Bind callback assignment target if specified
        if (clause.CallbackBindVar != null)
        {
            if (cf.CallbackAssignTarget == null) return null;
            if (!TryBind(b, clause.CallbackBindVar, cf.CallbackAssignTarget)) return null;
        }

        // Bind callback throw type if specified
        if (clause.ThrowsBindVar != null)
        {
            if (cf.CallbackThrowType == null) return null;
            if (!TryBindOrMatch(b, clause.ThrowsBindVar, cf.CallbackThrowType)) return null;
        }

        return b;
    }

    static Dictionary<string, string>? TryMatchAssign(AssignClause clause, Fact fact, Dictionary<string, string> bindings)
    {
        if (fact is not AssignedFact af) return null;

        // Cast type must match
        if (af.CastType == null) return null;
        if (!TypeMatches(clause.CastTypePattern, af.CastType)) return null;

        var b = new Dictionary<string, string>(bindings);

        // Bind target
        if (!TryBind(b, clause.TargetBindVar, af.Target)) return null;

        // Bind source
        if (!TryBind(b, clause.SourceBindVar, af.Source)) return null;

        return b;
    }

    static Dictionary<string, string>? TryMatchMock(MockClause clause, Fact fact, Dictionary<string, string> bindings)
    {
        if (fact is not MockSetupFact mf) return null;

        // Method name must match
        if (!mf.Method.Equals(clause.MethodName, StringComparison.OrdinalIgnoreCase)) return null;

        // Return value must match (or "any")
        if (!clause.ReturnValue.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            if (mf.ReturnValue == null) return null;
            if (!mf.ReturnValue.Equals(clause.ReturnValue, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        var b = new Dictionary<string, string>(bindings);

        if (clause.MockBindVar != null)
        {
            if (!TryBind(b, clause.MockBindVar, mf.MockVariable)) return null;
        }

        return b;
    }

    // ── Assert checking ───────────────────────────────────

    static bool CheckAsserts(List<AssertClause> asserts, TestFacts test, Dictionary<string, string> bindings)
    {
        var assertFacts = test.Facts.OfType<AssertedFact>().ToList();
        var aliases = BuildAliasMap(test);

        foreach (var ac in asserts)
        {
            if (!assertFacts.Any(af => AssertMatches(ac, af, bindings, aliases)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Build variable alias groups from AssignedFacts.
    /// If publicAccessor = accessor, then they are aliases of each other.
    /// </summary>
    static Dictionary<string, HashSet<string>> BuildAliasMap(TestFacts test)
    {
        var map = new Dictionary<string, HashSet<string>>();

        foreach (var af in test.Facts.OfType<AssignedFact>())
        {
            if (!map.ContainsKey(af.Source)) map[af.Source] = new HashSet<string> { af.Source };
            if (!map.ContainsKey(af.Target)) map[af.Target] = new HashSet<string> { af.Target };
            map[af.Source].Add(af.Target);
            map[af.Target].Add(af.Source);
        }

        // Transitive closure
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (key, group) in map)
            {
                var toAdd = group.SelectMany(a => map.GetValueOrDefault(a, new HashSet<string>()))
                                 .Where(a => !group.Contains(a)).ToList();
                if (toAdd.Count > 0) { group.UnionWith(toAdd); changed = true; }
            }
        }

        return map;
    }

    static bool AssertMatches(AssertClause clause, AssertedFact fact, Dictionary<string, string> bindings, Dictionary<string, HashSet<string>> aliases)
    {
        // Semantic meaning must match (unless "any")
        if (!clause.SemanticMeaning.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            if (!fact.SemanticMeaning.Equals(clause.SemanticMeaning, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // No args to check → semantic match is enough
        if (clause.Args.Count == 0)
            return true;

        // Resolve rule args through bindings
        var resolvedArgs = clause.Args.Select(a => ResolveExpression(a, bindings)).ToList();

        // For 2-arg assertions (AreEqual, AreNotEqual), try both orderings
        if (resolvedArgs.Count == 2 && fact.Arguments.Count >= 2)
        {
            return (ArgMatches(resolvedArgs[0], fact.Arguments[0], aliases) && ArgMatches(resolvedArgs[1], fact.Arguments[1], aliases))
                || (ArgMatches(resolvedArgs[0], fact.Arguments[1], aliases) && ArgMatches(resolvedArgs[1], fact.Arguments[0], aliases));
        }

        // For single-arg assertions, check each resolved arg appears somewhere in fact args
        foreach (var resolved in resolvedArgs)
        {
            if (resolved.Equals("any", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!fact.Arguments.Any(fa => ArgMatches(resolved, fa, aliases)))
                return false;
        }

        return true;
    }

    // ── Expression resolution ─────────────────────────────

    /// <summary>
    /// Resolve a rule expression through bindings.
    /// "$acc" → "accessor", "$acc.Value" → "accessor.Value"
    /// Literals and "any" pass through unchanged.
    /// </summary>
    static string ResolveExpression(string expr, Dictionary<string, string> bindings)
    {
        if (!expr.StartsWith("$")) return expr;

        var dotIdx = expr.IndexOf('.');
        string varPart, rest;
        if (dotIdx >= 0)
        {
            varPart = expr[..dotIdx];
            rest = expr[dotIdx..]; // includes the dot
        }
        else
        {
            varPart = expr;
            rest = "";
        }

        if (bindings.TryGetValue(varPart, out var bound))
            return bound + rest;

        return expr; // unresolved, leave as-is
    }

    /// <summary>
    /// Check if a resolved rule argument matches a fact argument.
    /// Supports alias resolution: "accessor.Value" matches "publicAccessor.Value"
    /// if publicAccessor is an alias of accessor.
    /// </summary>
    static bool ArgMatches(string resolved, string factArg, Dictionary<string, HashSet<string>> aliases)
    {
        if (resolved.Equals("any", StringComparison.OrdinalIgnoreCase))
            return true;

        // Exact match
        if (resolved.Equals(factArg, StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalize whitespace
        var rn = resolved.Replace(" ", "");
        var fn = factArg.Replace(" ", "");
        if (rn.Equals(fn, StringComparison.OrdinalIgnoreCase))
            return true;

        // Contains match for lambda expressions: factArg like "() => accessor.GetValueOrThrow()"
        // should match resolved "accessor.GetValueOrThrow" or "accessor.GetValueOrThrow()"
        if (fn.Contains("=>") && fn.Contains(rn, StringComparison.OrdinalIgnoreCase))
            return true;

        // Alias-based matching: "accessor.Value" vs "publicAccessor.Value"
        var rDot = resolved.IndexOf('.');
        var fDot = factArg.IndexOf('.');
        if (rDot > 0 && fDot > 0)
        {
            var rPrefix = resolved[..rDot];
            var rSuffix = resolved[rDot..];
            var fPrefix = factArg[..fDot];
            var fSuffix = factArg[fDot..];

            if (rSuffix.Equals(fSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (aliases.TryGetValue(rPrefix, out var group) && group.Contains(fPrefix))
                    return true;
            }
        }

        return false;
    }

    // ── Binding helpers ───────────────────────────────────

    /// <summary>Try to bind a rule variable to a value. Fails if already bound to a different value.</summary>
    static bool TryBind(Dictionary<string, string> bindings, string variable, string value)
    {
        if (variable == "any" || variable == "$any") return true;

        if (bindings.TryGetValue(variable, out var existing))
            return existing == value; // must be consistent

        bindings[variable] = value;
        return true;
    }

    /// <summary>Try to bind or match: if it's a $var, bind it. If it's a literal or "any", match directly.</summary>
    static bool TryBindOrMatch(Dictionary<string, string> bindings, string pattern, string actual)
    {
        if (pattern.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;
        if (pattern.StartsWith("$")) return TryBind(bindings, pattern, actual);
        // Literal match
        return pattern.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    // ── Type matching ─────────────────────────────────────

    /// <summary>
    /// Match a type pattern against an actual type name.
    /// "Accessor" matches "Accessor", "Accessor&lt;string&gt;", "Accessor&lt;int&gt;"
    /// "Accessor&lt;any&gt;" matches "Accessor&lt;string&gt;", "Accessor&lt;int&gt;"
    /// "IAccessorSetter&lt;any&gt;" matches "IAccessorSetter&lt;object&gt;"
    /// </summary>
    static bool TypeMatches(string pattern, string actual)
    {
        if (pattern.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;

        // Exact match
        if (pattern.Equals(actual, StringComparison.OrdinalIgnoreCase)) return true;

        // Convert pattern to regex: MyType<any> → ^MyType<.+>$
        var escaped = Regex.Escape(pattern);
        // Replace <any> with <.+> (Regex.Escape does NOT escape < and >)
        var regexStr = escaped.Replace("<any>", "<.+>");
        // Also allow pattern "MyType" to match "MyType<whatever>"
        if (!pattern.Contains('<'))
            regexStr += @"(?:<.+>)?";

        regexStr = "^" + regexStr + "$";

        return Regex.IsMatch(actual, regexStr, RegexOptions.IgnoreCase);
    }

    // ── Description helpers ───────────────────────────────

    static string DescribeGiven(GivenClause g)
    {
        var prefix = g.Negated ? "NOT " : "";
        return prefix + (g switch
        {
            CreateClause c => $"create {c.BindVar}: {c.TypePattern}" + (c.ArgBindings.Count > 0 ? $"({string.Join(", ", c.ArgBindings)})" : ""),
            CallClause c => (c.ResultBindVar != null ? $"{c.ResultBindVar} = " : "") +
                            "call: " + (c.ReceiverBindVar != null ? $"{c.ReceiverBindVar}." : "") +
                            c.MethodName + (c.ArgBindings.Count > 0 ? $"({string.Join(", ", c.ArgBindings)})" : "") +
                            (c.OnTypePattern != null ? $" on {c.OnTypePattern}" : "") +
                            (c.CallbackBindVar != null ? $" -> {c.CallbackBindVar}" : "") +
                            (c.ThrowsBindVar != null ? $" throws {c.ThrowsBindVar}" : ""),
            AssignClause c => $"assign {c.TargetBindVar} = {c.SourceBindVar}: {c.CastTypePattern}",
            MockClause c => $"mock: {c.MethodName} returns {c.ReturnValue}",
            _ => g.ToString() ?? ""
        });
    }

    static string DescribeAssert(AssertClause a) =>
        a.SemanticMeaning + (a.Args.Count > 0 ? $": {string.Join(", ", a.Args)}" : "");
}
