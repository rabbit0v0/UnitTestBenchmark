// Rule definitions parsed from YAML.
// Rules use $-prefixed bind variables for constraint satisfaction.

class Rule
{
    public string Id { get; set; } = "";
    public string? Parent { get; set; }
    public string? OrGroup { get; set; }
    public string Description { get; set; } = "";
    public List<GivenClause> Given { get; set; } = new();
    public List<AssertClause> Assert { get; set; } = new();
}

// ── Given clauses ──────────────────────────────────────

abstract class GivenClause
{
    /// <summary>When true, the clause asserts ABSENCE: no matching fact may exist.</summary>
    public bool Negated { get; set; }
}

/// <summary>create $var: TypePattern  or  create $var: TypePattern($arg1, $arg2)</summary>
class CreateClause : GivenClause
{
    public string BindVar { get; set; } = "";         // "$acc"
    public string TypePattern { get; set; } = "";     // "Accessor" or "Accessor<any>"
    public List<string> ArgBindings { get; set; } = new(); // ["$val"] captures ctor args
    public bool ExactArgCount { get; set; }            // true when explicit parens used, e.g. Accessor()
}

/// <summary>
/// call: Method  or  call: $recv.Method($arg)  or  $result = call: Method on Type
/// Optionally: -> $cbVar to capture the first assignment target inside a lambda arg.
/// </summary>
class CallClause : GivenClause
{
    public string? ResultBindVar { get; set; }        // "$result" from "$result = call: ..."
    public string? ReceiverBindVar { get; set; }      // "$recv" from "call: $recv.Method"
    public string? OnTypePattern { get; set; }        // type filter from "... on IAccessorSetter<any>"
    public string MethodName { get; set; } = "";      // "SetValue"
    public List<string> ArgBindings { get; set; } = new(); // captures method arguments
    public string? CallbackBindVar { get; set; }      // "$cbVar" from "... -> $cbVar"
    public string? ThrowsBindVar { get; set; }        // "$excType" from "... throws $excType"
}

/// <summary>assign $target = $source: CastType</summary>
class AssignClause : GivenClause
{
    public string TargetBindVar { get; set; } = "";   // "$setter"
    public string SourceBindVar { get; set; } = "";   // "$acc"
    public string CastTypePattern { get; set; } = ""; // "IAccessorSetter<any>"
}

/// <summary>mock: Method returns value  or  mock: $mock.Method returns value</summary>
class MockClause : GivenClause
{
    public string? MockBindVar { get; set; }          // "$mock" (optional)
    public string MethodName { get; set; } = "";      // "OtherFunc"
    public string ReturnValue { get; set; } = "";     // "false", "null", etc.
}

// ── Assert clauses ──────────────────────────────────────

class AssertClause
{
    public string SemanticMeaning { get; set; } = ""; // "IsNull", "AreEqual", "Throws", etc.
    public List<string> Args { get; set; } = new();   // ["$acc.Value"] or ["$acc.Value", "$val"]
}

// ── Solver result ──────────────────────────────────────

class RuleResult
{
    public Rule Rule { get; set; } = null!;
    public bool IsImplemented { get; set; }
    public string? MatchedTest { get; set; }
    public Dictionary<string, string>? Bindings { get; set; }
    public List<string> GivenMatched { get; set; } = new();
    public List<string> GivenMissing { get; set; } = new();
    public List<string> AssertMatched { get; set; } = new();
    public List<string> AssertMissing { get; set; } = new();
    /// <summary>All test methods that implement this rule.</summary>
    public List<string> AllMatchedTests { get; set; } = new();
}
