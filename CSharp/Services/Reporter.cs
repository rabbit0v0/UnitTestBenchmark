/// <summary>Console reporter for rule matching results.</summary>
static class Reporter
{
    public static void PrintTestSummary(List<TestFacts> tests)
    {
        Console.WriteLine($"\n📋 Found {tests.Count} test methods\n");
        foreach (var t in tests)
        {
            var factCounts = string.Join(", ",
                t.Facts.GroupBy(f => f.GetType().Name.Replace("Fact", ""))
                       .Select(g => $"{g.Count()} {g.Key}"));
            Console.WriteLine($"   • {t.TestName}");
            Console.WriteLine($"     Facts: {factCounts}");
        }
    }

    public static void PrintResults(List<RuleResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("📊 RULE VERIFICATION (Constraint Satisfaction)");
        Console.WriteLine("═══════════════════════════════════════════════════════════\n");

        // Build parent→children lookup
        var childrenOf = results
            .Where(r => r.Rule.Parent != null)
            .GroupBy(r => r.Rule.Parent!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var childIds = new HashSet<string>(results.Where(r => r.Rule.Parent != null).Select(r => r.Rule.Id));

        // Build OR group lookup
        var orGroups = results
            .Where(r => r.Rule.OrGroup != null)
            .GroupBy(r => r.Rule.OrGroup!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var orGroupIds = new HashSet<string>(results.Where(r => r.Rule.OrGroup != null).Select(r => r.Rule.Id));
        var printedOrGroups = new HashSet<string>();

        foreach (var r in results)
        {
            // Skip children here; they are printed under their parent
            if (childIds.Contains(r.Rule.Id)) continue;

            // Handle OR groups: print as a group when first member is encountered
            if (r.Rule.OrGroup != null)
            {
                if (printedOrGroups.Contains(r.Rule.OrGroup)) continue;
                printedOrGroups.Add(r.Rule.OrGroup);
                PrintOrGroup(r.Rule.OrGroup, orGroups[r.Rule.OrGroup]);
                continue;
            }

            PrintSingleResult(r, indent: "");

            // Print children indented
            if (childrenOf.TryGetValue(r.Rule.Id, out var children))
            {
                Console.WriteLine($"   📎 Edge cases ({children.Count}):");
                foreach (var child in children)
                    PrintSingleResult(child, indent: "   ");
            }
        }

        // Summary: OR groups count as 1
        var orGroupCount = orGroups.Count;
        var orGroupImplemented = orGroups.Values.Count(g => g.Any(r => r.IsImplemented));
        var standalone = results.Where(r => r.Rule.OrGroup == null).ToList();
        var totalLogical = standalone.Count + orGroupCount;
        var totalImplemented = standalone.Count(r => r.IsImplemented) + orGroupImplemented;

        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine($"Summary: {totalImplemented}/{totalLogical} rules implemented");
        if (orGroupCount > 0)
            Console.WriteLine($"   ({orGroupCount} OR group(s), each counting as 1 rule)");

        // Category breakdowns
        PrintCategorySummary(results, "Throws",
            r => r.Rule.Assert.Any(a => a.SemanticMeaning.Equals("Throws", StringComparison.OrdinalIgnoreCase)));
        PrintCategorySummary(results, "Mocks",
            r => r.Rule.Given.Any(g => g is MockClause));

        Console.WriteLine();
        Console.WriteLine("── Implementation Counts ───────────────────────────────");
        var printedOrGroupsBars = new HashSet<string>();
        foreach (var r in results)
        {
            // OR group members: show group line once, then individual alternatives indented
            if (r.Rule.OrGroup != null)
            {
                if (printedOrGroupsBars.Contains(r.Rule.OrGroup)) continue;
                printedOrGroupsBars.Add(r.Rule.OrGroup);
                var groupMembers = orGroups[r.Rule.OrGroup];
                var groupCount = groupMembers.Sum(m => m.AllMatchedTests.Count);
                var groupBar = new string('█', groupCount) + (groupCount == 0 ? "░" : "");
                Console.WriteLine($"   {"[OR] " + r.Rule.OrGroup,-30} {groupBar} {groupCount}");
                foreach (var m in groupMembers)
                {
                    var mCount = m.AllMatchedTests.Count;
                    var mBar = new string('█', mCount) + (mCount == 0 ? "░" : "");
                    Console.WriteLine($"     └─ {m.Rule.Id,-28} {mBar} {mCount}");
                }
                continue;
            }

            var prefix = r.Rule.Parent != null ? "  └─ " : "";
            var count = r.AllMatchedTests.Count;
            var bar = new string('█', count) + (count == 0 ? "░" : "");
            Console.WriteLine($"   {prefix}{r.Rule.Id,-30} {bar} {count}");
        }
        Console.WriteLine("═══════════════════════════════════════════════════════════");
    }

    static void PrintOrGroup(string groupName, List<RuleResult> alternatives)
    {
        var anyImplemented = alternatives.Any(r => r.IsImplemented);
        var icon = anyImplemented ? "✅" : "❌";
        var status = anyImplemented ? "IMPLEMENTED" : "NOT IMPLEMENTED";

        Console.WriteLine($"{icon} OR GROUP: {groupName} — {status}");
        Console.WriteLine($"   Any one of {alternatives.Count} alternatives suffices:");

        foreach (var r in alternatives)
        {
            var altIcon = r.IsImplemented ? "✓" : "✗";
            Console.WriteLine($"   {altIcon} {r.Rule.Id}: {r.Rule.Description}");
            if (r.IsImplemented)
            {
                Console.WriteLine($"      Test: {r.MatchedTest}");
                if (r.Bindings != null)
                {
                    var bindStr = string.Join(", ", r.Bindings.Select(kv => $"{kv.Key}={kv.Value}"));
                    Console.WriteLine($"      Bindings: {bindStr}");
                }
                Console.WriteLine($"      🔢 Implemented {r.AllMatchedTests.Count} time(s)");
            }
        }
        Console.WriteLine();
    }

    static void PrintSingleResult(RuleResult r, string indent)
    {
        var parentLabel = r.Rule.Parent != null ? $" (edge case of {r.Rule.Parent})" : "";
        if (r.IsImplemented)
        {
            Console.WriteLine($"{indent}✅ IMPLEMENTED: {r.Rule.Id}{parentLabel}");
            Console.WriteLine($"{indent}   {r.Rule.Description}");
            Console.WriteLine($"{indent}   Test: {r.MatchedTest}");
            if (r.Bindings != null)
            {
                var bindStr = string.Join(", ", r.Bindings.Select(kv => $"{kv.Key}={kv.Value}"));
                Console.WriteLine($"{indent}   Bindings: {bindStr}");
            }
            Console.WriteLine($"{indent}   ✓ {r.GivenMatched.Count} given + {r.AssertMatched.Count} assert matched");
            Console.WriteLine($"{indent}   🔢 Implemented {r.AllMatchedTests.Count} time(s) across tests:");
            foreach (var t in r.AllMatchedTests)
                Console.WriteLine($"{indent}      • {t}");
        }
        else
        {
            Console.WriteLine($"{indent}❌ NOT IMPLEMENTED: {r.Rule.Id}{parentLabel}");
            Console.WriteLine($"{indent}   {r.Rule.Description}");
            if (r.GivenMissing.Count > 0)
            {
                Console.WriteLine($"{indent}   Missing given:");
                foreach (var g in r.GivenMissing) Console.WriteLine($"{indent}      • {g}");
            }
            if (r.AssertMissing.Count > 0)
            {
                Console.WriteLine($"{indent}   Missing assert:");
                foreach (var a in r.AssertMissing) Console.WriteLine($"{indent}      • {a}");
            }
        }
        Console.WriteLine();
    }

    static void PrintCategorySummary(List<RuleResult> results, string label, Func<RuleResult, bool> predicate)
    {
        var category = results.Where(predicate).ToList();
        if (category.Count == 0)
        {
            Console.WriteLine($"   {label}: N/A (no rules)");
            return;
        }
        var impl = category.Count(r => r.IsImplemented);
        var pct = (int)Math.Round(100.0 * impl / category.Count);
        Console.WriteLine($"   {label}: {impl}/{category.Count} implemented ({pct}%)");
    }

    public static void PrintFacts(List<TestFacts> tests)
    {
        Console.WriteLine("\n── Extracted facts (debug) ──────────────────────────────\n");
        foreach (var t in tests)
        {
            Console.WriteLine($"Test: {t.TestName}");
            foreach (var f in t.Facts)
            {
                var desc = f switch
                {
                    CreatedFact c => $"  [{c.Sequence}] Created {c.Variable}: {c.Type}({string.Join(", ", c.Arguments)})",
                    CalledFact c => $"  [{c.Sequence}] Called {c.Receiver ?? "(static)"}.{c.Method}({string.Join(", ", c.Arguments)})" +
                                   (c.ResultVariable != null ? $" → {c.ResultVariable}" : "") +
                                   (c.ReceiverType != null ? $" [type: {c.ReceiverType}]" : "") +
                                   (c.CallbackAssignTarget != null ? $" [cb→{c.CallbackAssignTarget}]" : "") +
                                   (c.CallbackThrowType != null ? $" [throws:{c.CallbackThrowType}]" : ""),
                    AssignedFact a => $"  [{a.Sequence}] Assigned {a.Target} = {a.Source}" + (a.CastType != null ? $" as {a.CastType}" : ""),
                    MockSetupFact m => $"  [{m.Sequence}] Mock {m.MockVariable}.{m.Method} returns {m.ReturnValue ?? $"throws {m.ThrowsType}"}",
                    AssertedFact a => $"  [{a.Sequence}] Assert {a.SemanticMeaning}({string.Join(", ", a.Arguments)})",
                    _ => f.ToString() ?? ""
                };
                Console.WriteLine(desc);
            }
            if (t.VariableTypes.Count > 0)
            {
                Console.WriteLine($"  Variable types: {string.Join(", ", t.VariableTypes.Select(kv => $"{kv.Key}:{kv.Value}"))}");
            }
            Console.WriteLine();
        }
    }
}
