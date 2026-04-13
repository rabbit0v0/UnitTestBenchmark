// Semantic facts extracted from test code by Roslyn AST analysis.
// Each fact represents one observable event in the test method.

/// <summary>Base type for all extracted facts. Sequence preserves source order.</summary>
abstract record Fact(int Sequence);

/// <summary>Object creation: new TypeName(args) assigned to a variable.</summary>
record CreatedFact(
    int Sequence,
    string Variable,          // the variable name it's assigned to
    string Type,              // full type name, e.g. "Accessor<string>"
    List<string> Arguments    // constructor argument expressions
) : Fact(Sequence);

/// <summary>Method call: receiver.Method(args), optionally assigned to a result variable.</summary>
record CalledFact(
    int Sequence,
    string? Receiver,              // receiver variable name (or synthetic for inline casts)
    string? ReceiverType,          // resolved declared/cast type of receiver
    string Method,                 // method name
    List<string> Arguments,        // argument expressions
    string? ResultVariable,        // variable the result is assigned to (if any)
    string? CallbackAssignTarget,  // first assignment target inside a lambda arg (if any)
    string? CallbackThrowType      // exception type from throw inside a lambda arg (if any)
) : Fact(Sequence);

/// <summary>Assignment: target = source, optionally with a cast or interface type.</summary>
record AssignedFact(
    int Sequence,
    string Target,           // target variable name
    string Source,           // source expression / variable name
    string? CastType         // if assigned via cast or explicit interface type
) : Fact(Sequence);

/// <summary>Moq mock setup: mock.Setup(m => m.Method()).Returns(value).</summary>
record MockSetupFact(
    int Sequence,
    string MockVariable,     // the mock variable name
    string Method,           // mocked method name
    string? ReturnValue,     // value from .Returns(), null if .Throws()
    string? ThrowsType       // exception type from .Throws<T>()
) : Fact(Sequence);

/// <summary>Assertion call with resolved semantic meaning.</summary>
record AssertedFact(
    int Sequence,
    string SemanticMeaning,       // "IsNull", "AreEqual", "IsTrue", "Throws", etc.
    List<string> Arguments        // assertion argument expressions
) : Fact(Sequence);

/// <summary>All facts extracted from a single test method.</summary>
class TestFacts
{
    public string TestName { get; set; } = "";
    public List<Fact> Facts { get; } = new();
    public Dictionary<string, string> VariableTypes { get; } = new(); // varName → declared type
}
