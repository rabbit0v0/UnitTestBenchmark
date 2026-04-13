# PytestRuleVerifier

A constraint-satisfaction based tool that compares Python pytest/unittest test files against YAML rule definitions to verify test coverage.

## Purpose

Reads a Python unit test file and a YAML rules file, then reports:
- How many rules are implemented
- How many times each rule is implemented across tests
- The percentage of rules involving **mocks** and **raises** (exceptions) that are implemented

## Usage

```bash
pip install pyyaml
python main.py <test_file.py> <rules.yaml> [--debug]
```

### Arguments

| Argument | Description |
|---|---|
| `test_file.py` | Path to the Python test file |
| `rules.yaml` | Path to the YAML rules file |
| `--debug` | Show extracted facts per test (optional) |

### Example

```bash
python main.py examples/test_calculator.py examples/calculator_rules.yaml --debug
```

## YAML Rule Format

```yaml
rules:
  - id: rule_name
    description: "What this rule checks"
    given:
      - create $obj: ClassName($arg)
      - call: $obj.method($arg)
      - mock: $mock.some_func returns value
    assert:
      - IsNone: $obj.attr
      - AreEqual: [$a, $b]
      - Raises: ValueError
```

### Given Clauses

| Clause | Description |
|---|---|
| `create $var: Type($args)` | Object creation |
| `call: $recv.method($args)` | Method call |
| `$result = call: method` | Call with result capture |
| `mock: method returns value` | Mock setup |
| `assign $target = $source: Type` | Variable assignment with type |

### Assert Semantics

| Semantic | Maps to |
|---|---|
| `AreEqual` | `assert x == y`, `assertEqual` |
| `IsNone` | `assert x is None`, `assertIsNone` |
| `IsNotNone` | `assert x is not None` |
| `IsTrue` / `IsFalse` | `assert x`, `assert not x` |
| `Raises` | `pytest.raises(...)`, `assertRaises` |
| `Contains` | `assert x in y` |

### Bind Variables

- `$`-prefixed names are bind variables resolved by constraint satisfaction
- `any` matches anything
- Type patterns: `Calculator` matches `Calculator`, `Calculator[str]`, etc.

## Architecture

| Module | Purpose |
|---|---|
| `models.py` | Data classes for facts, rules, and results |
| `rule_parser.py` | YAML rule file parser |
| `fact_extractor.py` | Python AST-based test fact extraction |
| `solver.py` | Backtracking constraint satisfaction solver |
| `reporter.py` | Console output and summary reporting |
| `main.py` | CLI entry point |
