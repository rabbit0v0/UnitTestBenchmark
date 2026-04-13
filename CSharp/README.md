# C# Test Rule Verifier

A static analysis tool that verifies whether C# unit tests implement expected behaviors defined in YAML rules. It uses Roslyn AST to extract semantic facts from test code and a constraint-satisfaction solver with backtracking to match rules against those facts.

## Requirements

- .NET 10.0 SDK
- Dependencies: `Microsoft.CodeAnalysis.CSharp` (Roslyn), `YamlDotNet`

## Usage

```
dotnet run -- <test-file.cs> <rules.yaml> [--debug]
```

| Argument | Description |
|---|---|
| `test-file.cs` | Path to the C# unit test file |
| `rules.yaml` | Path to the YAML rules file |
| `--debug` | Show extracted facts per test method |

Exit code `0` if all rules are implemented, `1` otherwise.

## How It Works

### Pipeline

1. **FactExtractor** — Parses C# test file with Roslyn and extracts semantic facts from each test method:
   - `CreatedFact` — object creation (`new Accessor<string>(value)`)
   - `CalledFact` — method calls (`accessor.GetValueOrThrow()`)
   - `AssignedFact` — variable assignments and casts (`var setter = (IAccessorSetter<string>)accessor`)
   - `MockSetupFact` — Moq setup chains (`mock.Setup(m => m.Method()).Returns(value)`)
   - `AssertedFact` — assertion calls (`Assert.AreEqual`, `Assert.IsNull`, `Assert.ThrowsException<T>`, etc.)

2. **RuleParser** — Parses YAML rules into structured rule objects with given clauses and assert clauses.

3. **Solver** — For each rule, performs constraint-satisfaction matching against every test. Uses backtracking search over given clauses with `$`-prefixed bind variables that must resolve consistently across all clauses.

4. **Reporter** — Outputs results: which rules are implemented, which tests match, binding details, implementation counts with bar chart, and category breakdowns (Throws, Mocks).

### Supported Test Frameworks

- **MSTest** — `[TestMethod]`, `Assert.*`, `[ExpectedException]`, `[DataRow]`
- **xUnit** — `[Fact]`, `[Theory]`, `[InlineData]`, `Assert.Throws<T>`
- **NUnit** — `[Test]`, `[TestCase]`, `Assert.That(..., Throws.TypeOf<T>())`
- **FluentAssertions** — `x.Should().BeNull()`, `.Be()`, `.BeTrue()`, etc.
- **Moq** — `mock.Setup(...).Returns(...)` / `.Throws<T>()`

### Parameterized Tests

`[DataRow]`, `[InlineData]`, and `[TestCase]` attributes are expanded into separate test entries with parameter values substituted into all facts. For example:

```csharp
[DataRow(null)]
[DataRow("")]
[DataRow("TestValue")]
public void MyTest(string? value) { ... }
```

Produces three test entries: `MyTest(null)`, `MyTest("")`, `MyTest("TestValue")`, each with the parameter `value` replaced by the literal attribute value in all extracted facts.

## YAML Rule Format

```yaml
rules:
  - id: rule_identifier
    description: "Human-readable description"
    given:
      - create $var: TypePattern($arg)     # object creation
      - call: $recv.Method($arg)           # method call
      - call: Method on TypePattern        # call with type constraint
      - assign $target = $source: CastType # cast assignment
      - mock: Method returns value         # Moq setup
    assert:
      - IsNull: $var.Property
      - AreEqual: [$a, $b]
      - Throws: [ExceptionType]
```

### Rule Syntax

| Clause | Format | Description |
|---|---|---|
| `create` | `create $var: Type($args)` | Object creation. `Type` matches generics (e.g. `Accessor` matches `Accessor<string>`). `Type()` requires exact 0 args. |
| `call` | `call: $recv.Method($arg)` | Method call. Optional `on TypePattern` for receiver type constraint. Optional `-> $cbVar` for callback capture. |
| `assign` | `assign $target = $source: CastType` | Cast assignment. `CastType` supports `<any>` wildcard. |
| `mock` | `mock: Method returns value` | Moq setup detection. |
| Assert | `IsNull`, `IsNotNull`, `IsTrue`, `IsFalse`, `AreEqual`, `AreNotEqual`, `Throws`, `Contains` | Assertion matching. |

### Special Values

| Value | Meaning |
|---|---|
| `$name` | Bind variable — resolved by constraint satisfaction |
| `any` | Wildcard — matches anything |
| `null` | Literal null |
| `""` | Literal empty string |
| `$var.Property` | Dotted reference — resolved from bindings |

## Example

Given the rules file `examples/accessor_rules.yaml` and test file `examples/AccessorTestsClaudeHaiku.cs`:

```
$ dotnet run -- examples/AccessorTestsClaudeHaiku.cs examples/accessor_rules.yaml

📋 Found 31 test methods

📋 Rules: 11

═══════════════════════════════════════════════════════════
📊 RULE VERIFICATION (Constraint Satisfaction)
═══════════════════════════════════════════════════════════

✅ IMPLEMENTED: no_value_is_null
   Accessor created without value - Value should be null
   Test: Constructor_WithoutValue_InitializesWithNull
   Bindings: $acc=accessor
   ✓ 1 given + 1 assert matched

❌ NOT IMPLEMENTED: some_rule
   Description of the rule
   Missing given:
      • create $acc: Accessor(null)
   Missing assert:
      • AreEqual: $acc.Value, null

═══════════════════════════════════════════════════════════
Summary: 10/11 rules implemented
   Throws: 2/2 implemented (100%)
   Mocks: N/A (no rules)
```

## Project Structure

```
Program.cs              Entry point — orchestrates the pipeline
Models/
  Facts.cs              Fact records extracted from test code
  Rules.cs              Rule and clause model definitions
Services/
  FactExtractor.cs      Roslyn AST → semantic facts
  RuleParser.cs         YAML → rule objects
  Solver.cs             Constraint-satisfaction matcher
  Reporter.cs           Console output formatting
examples/
  accessor_rules.yaml   Sample rules for Accessor<T>
  AccessorTests*.cs     Sample test files
```
