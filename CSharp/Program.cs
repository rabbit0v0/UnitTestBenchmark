// ============================================================
//  C# Test Rule Verifier — Constraint Satisfaction Approach
// ============================================================
//
//  Usage: dotnet run -- <test-file.cs> <rules.yaml> [--debug]
//
//  Extracts semantic facts from C# test methods using Roslyn AST,
//  then matches YAML rules via constraint satisfaction with variable bindings.
//

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <test-file.cs> <rules.yaml> [--debug]");
    Console.WriteLine();
    Console.WriteLine("  test-file.cs   Path to the C# unit test file");
    Console.WriteLine("  rules.yaml     Path to the YAML rules file");
    Console.WriteLine("  --debug        Show extracted facts per test (optional)");
    return 1;
}

var testFile = args[0];
var rulesFile = args[1];
var debug = args.Any(a => a == "--debug");

if (!File.Exists(testFile))
{
    Console.Error.WriteLine($"Test file not found: {testFile}");
    return 1;
}
if (!File.Exists(rulesFile))
{
    Console.Error.WriteLine($"Rules file not found: {rulesFile}");
    return 1;
}

// 1. Parse test file → extract facts
var sourceCode = File.ReadAllText(testFile);
var allTests = FactExtractor.ExtractAll(sourceCode);

Reporter.PrintTestSummary(allTests);

if (debug)
    Reporter.PrintFacts(allTests);

// 2. Parse rules YAML
var yamlContent = File.ReadAllText(rulesFile);
var rules = RuleParser.Parse(yamlContent);

Console.WriteLine($"\n📋 Rules: {rules.Count}\n");

// 3. Match each rule against all tests
var results = rules.Select(r => Solver.MatchAgainstAll(r, allTests)).ToList();

// 4. Report
Reporter.PrintResults(results);

// Count implemented: OR groups count as 1 if any alternative matches
var orGroups = results.Where(r => r.Rule.OrGroup != null).GroupBy(r => r.Rule.OrGroup!);
var orGroupCount = orGroups.Count();
var orGroupImplemented = orGroups.Count(g => g.Any(r => r.IsImplemented));
var standalone = results.Where(r => r.Rule.OrGroup == null).ToList();
var totalLogical = standalone.Count + orGroupCount;
var totalImplemented = standalone.Count(r => r.IsImplemented) + orGroupImplemented;

return totalLogical > 0 && totalImplemented == totalLogical ? 0 : 1;
