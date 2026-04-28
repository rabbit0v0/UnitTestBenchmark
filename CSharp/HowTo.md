# HowTo: Write C# Rule Files

This project verifies whether a C# unit test file implements expected behaviors described in a YAML rules file. The verifier does not read intent from prose. It extracts concrete facts from test code and matches them against concrete rule clauses. A good rules file therefore needs to be specific, structurally correct, and aligned with what the extractor and solver actually support.

## What a Rule File Is

A rule file is a YAML document with a top-level `rules` array.

Each rule describes one testable behavior using:

- `id`: a unique identifier for the rule
- `description`: a human-readable explanation of the behavior
- `given`: the setup or actions that should exist in a test
- `assert`: the expected assertion facts that should exist in a test
- `parent`: optional metadata to relate a specialized rule to a broader one
- `or_group`: optional metadata for alternative rules where any one implementation counts as satisfying the same logical behavior

Minimal example:

```yaml
rules:
  - id: constructor_without_value_sets_null
    description: "Accessor created without value should expose null Value"
    given:
      - create $acc: Accessor()
    assert:
      - IsNull: $acc.Value
```

## What Is Supported

The current C# implementation supports rule matching against facts extracted from these kinds of test code:

- MSTest test methods such as `[TestMethod]`
- xUnit test methods such as `[Fact]` and `[Theory]`
- NUnit test methods such as `[Test]`
- Parameterized rows from `[DataRow]`, `[InlineData]`, and `[TestCase]`
- Standard assertion calls recognized as semantic assertions
- FluentAssertions chains such as `x.Should().BeNull()`
- Moq setup chains such as `mock.Setup(...).Returns(value)` and `SetupGet(...).Returns(value)`
- Object creation, method calls, assignments, casts, lambda callback assignment targets, and callback exception types

The verifier extracts these fact categories from tests:

- `create`: object construction assigned to a variable
- `call`: method calls, optionally with receiver, arguments, result variable, callback effects, or callback throw type
- `assign`: variable assignment and cast assignment
- `mock`: Moq setup facts
- assertion facts with semantic meanings such as `IsNull`, `AreEqual`, and `Throws`

## Rule File Structure

Use this structure:

```yaml
rules:
  - id: unique_rule_id
    description: "What behavior this rule represents"
    given:
      - create $var: Type($arg)
      - call: $var.Method($arg2)
    assert:
      - AreEqual: [$var.Property, $arg]
```

Authoring expectations:

- Every rule should describe one distinct behavior.
- A rule should be distinguished from other rules.
- A rule itself should not be too vague.
- A rule should be able to exactly test the code or situation it targets.
- A rule should not be impacted or distracted by other situations.

In practice, this means the `given` and `assert` clauses should isolate the exact branch, state, or exception case you want the verifier to detect.

## Supported Given Clauses

### `create`

Use `create` to match object construction assigned to a variable.

Formats:

```yaml
- create $acc: Accessor
- create $acc: Accessor()
- create $acc: Accessor($value)
- create $acc: Dictionary<string, int>($arg1, $arg2)
```

Notes:

- The left side must be a bind variable such as `$acc`.
- The type pattern can match generic types.
- `Accessor` can match constructed forms such as `Accessor<string>`.
- If parentheses are omitted, the rule only constrains the type.
- If parentheses are included, the constructor argument count must match exactly.

### `call`

Use `call` to match method invocations.

Formats:

```yaml
- call: MethodName
- call: $recv.MethodName
- call: $recv.MethodName($arg1, $arg2)
- call: MethodName on IService<any>
- call: $recv.MethodName($arg) on IService<any>
- $result = call: $recv.MethodName($arg)
- call: OnFirstSet -> $callbackTarget
- call: OnFirstSet throws $excType
```

Supported capabilities:

- receiver binding through `$recv.MethodName(...)`
- method argument binding
- receiver type filtering through `on TypePattern`
- result variable binding through `$result = call: ...`
- callback assignment target capture through `-> $callbackTarget`
- callback exception type capture through `throws $excType`

Important behavior:

- Given clauses are matched in source order.
- If your rule describes “first do X, then do Y”, keep that order in `given`.

### `assign`

Use `assign` to match assignments, especially interface casts and aliases.

Format:

```yaml
- assign $target = $source: IAccessorSetter<any>
```

This is useful when the test interacts with a casted or interface-typed alias of an object.

### `mock`

Use `mock` to match Moq setup return values.

Formats:

```yaml
- mock: MethodName returns false
- mock: MethodName returns null
- mock: $mock.MethodName returns $value
- mock: $mock.MethodName returns any
```

Important limitation:

- The extractor records both Moq returns and Moq throws.
- The current rule parser and solver only support `mock: ... returns ...` syntax for matching.
- If you need to describe mocked exceptions, document that behavior through other supported call and assert patterns, or extend the implementation first.

### Negated `given` clauses

You can negate a `given` clause by prefixing the key with `not `.

Example:

```yaml
- not call: SetValue on IAccessorSetter<any>
```

This means no matching fact may exist in the test.

## Supported Assert Clauses

The matcher works with semantic assertion meanings. The current implementation emits and matches these meanings reliably:

- `IsNull`
- `IsNotNull`
- `IsTrue`
- `IsFalse`
- `AreEqual`
- `AreNotEqual`
- `Throws`
- `Contains`

Examples:

```yaml
assert:
  - IsNull: $acc.Value
  - IsNotNull: $result
  - IsTrue: $flag
  - IsFalse: $flag
  - AreEqual: [$acc.Value, $value]
  - AreNotEqual: [$actual, $unexpected]
  - Throws: [InvalidOperationException, $acc.GetValueOrThrow]
  - Contains: [$text, "needle"]
```

Notes:

- Two-argument equality assertions are matched in either argument order.
- Single-argument assertions match when the expected argument appears in the extracted assertion fact.
- `Throws` can match an exception type alone or an exception type plus the invoked expression.
- The special semantic `any` can be used, but it is less precise and should be avoided unless precision is impossible.

## Special Values and Binding Rules

### Bind variables

Use `$name` to bind one value and reuse it across clauses.

Example:

```yaml
given:
  - create $acc: Accessor($value)
assert:
  - AreEqual: [$acc.Value, $value]
```

This means the constructor argument and the asserted value must refer to the same bound value.

### Wildcard `any`

Use `any` only when the exact value does not matter.

Examples:

```yaml
- create $acc: Accessor(any)
- mock: SomeMethod returns any
```

Avoid overusing `any`. If a value is important to distinguishing the rule from another rule, bind it or write it literally.

### Literals

Supported literal conventions include:

- `null`
- `""` for the empty string
- numeric and boolean literals as written in test code
- concrete expressions exactly as extracted from source where needed

### Dotted expressions

You can refer to a bound variable’s member, such as:

```yaml
- IsNull: $acc.Value
```

If `$acc` binds to `accessor`, the matcher resolves this as `accessor.Value`.

## Supported Assertion Sources in Test Code

Rules should target assertion semantics the extractor understands. The current implementation recognizes assertions from:

- MSTest, xUnit, and NUnit style `Assert.*` calls that resolve to supported semantics
- exception assertions such as `Assert.Throws<T>`, `Assert.ThrowsException<T>`, `Assert.ThrowsAsync<T>`, and `Assert.ThrowsAny<T>`
- NUnit `Assert.That(..., Throws.TypeOf<T>())` and `Throws.InstanceOf<T>()`
- MSTest `[ExpectedException(typeof(T))]`
- FluentAssertions methods such as:
  - `BeNull`
  - `NotBeNull`
  - `Be`
  - `NotBe`
  - `BeTrue`
  - `BeFalse`
  - `Contain`
  - `BeEquivalentTo`

## How to Write Strong Rules

Write rules so that each one maps to one behavior and one reason for a test to exist.

Good rule-writing principles:

- Cover a single branch, edge case, or exception path per rule.
- Include the setup that makes the behavior unique.
- Include the action that triggers the behavior when that action matters.
- Include the assertion that proves the behavior.
- Bind important values with `$variables` so the matcher can verify consistency.
- Use literal values like `null` or `""` when those values are the point of the test.
- Use `not call` when absence is part of the behavior.
- Use `or_group` when multiple distinct test shapes should count as satisfying the same logical behavior.

Weak rule:

```yaml
- id: constructor_works
  description: "Constructor works"
  given:
    - create $obj: Accessor
```

Why it is weak:

- It does not identify a specific behavior.
- It does not distinguish one constructor scenario from another.
- It can match many unrelated tests.

Stronger rule:

```yaml
- id: constructor_without_value_sets_null
  description: "Accessor created without a value should expose null Value"
  given:
    - create $acc: Accessor()
  assert:
    - IsNull: $acc.Value
```

Why it is stronger:

- It targets a specific constructor branch.
- It states the exact state being asserted.
- It is unlikely to be confused with a different scenario.

## When to Split Rules

Create separate rules when any of these differ:

- the branch condition
- the input category
- the expected assertion
- the exception type
- the action order
- whether a collaborator or mock changes the behavior

Examples of cases that usually deserve separate rules:

- null input versus empty string input
- success return versus thrown exception
- default user path versus named user path
- callback registered before a value is set versus after a value is set

## `parent` and `or_group`

### `parent`

Use `parent` to show that a rule is a specialization of another rule. This is organizational metadata and helps keep related variants together.

Example:

```yaml
- id: with_value_stores
  description: "Accessor created with value should store that value"

- id: with_value_stores_null
  parent: with_value_stores
  description: "Accessor created with null should store null"
```

### `or_group`

Use `or_group` when multiple rule variants are acceptable alternative implementations of the same logical requirement.

Example:

```yaml
- id: callback_immediate_1
  or_group: callback_immediate
  given:
    - create $acc: Accessor($value)
    - call: OnFirstSet

- id: callback_immediate_2
  or_group: callback_immediate
  given:
    - create $acc: Accessor
    - call: SetValue($value) on IAccessorSetter<any>
    - call: OnFirstSet
```

If any rule in the same `or_group` matches, that logical requirement is considered implemented.

## Practical Limitations to Keep in Mind

Write rules to fit the current implementation, not an imagined one.

Current limitations include:

- Mock rules support `returns`, but not a direct `mock: ... throws ...` rule syntax.
- The parser splits arguments by commas naively, so very complex nested argument expressions may not be parsed well in rule text.
- Rule matching is based on extracted facts, not deep semantic compilation, so rules should follow the concrete code shape the extractor can see.
- Assertion semantics are limited to the meanings the extractor maps today.
- A rule with too little detail may accidentally match unrelated tests.

## Recommended Authoring Process

1. Identify one behavior from the production code.
2. Decide what setup makes that behavior unique.
3. Decide what action triggers it.
4. Decide what assertion proves it.
5. Write a rule with explicit `given` and `assert` clauses.
6. Check whether the rule could also match another scenario by accident.
7. If it could, tighten the rule by adding the missing distinguishing clause or by using literal values instead of `any`.

## Checklist for Each Rule

- The rule has a unique `id`.
- The `description` states one precise behavior.
- The `given` section captures the relevant setup and action order.
- The `assert` section proves the intended outcome.
- The rule is distinct from other rules.
- The rule is not vague.
- The rule exactly targets the code path or situation it intends to cover.
- The rule is not likely to match unrelated situations.
- The rule uses only syntax and semantics supported by the current verifier.

## Example Template

```yaml
rules:
  - id: method_condition_expected_result
    description: "When a specific condition holds, the method should produce the expected result"
    given:
      - create $obj: SomeType($input)
      - call: $obj.SomeMethod($arg)
    assert:
      - AreEqual: [$expected, any]
```

Use that template as a starting point, then replace broad placeholders with precise setup, action, and assertion details that uniquely identify the scenario.