# Java Test Rule Verifier

Constraint-satisfaction rule verifier for Java unit tests. Parses Java test files using [JavaParser](https://javaparser.org/) AST analysis, then matches YAML-defined rules via backtracking with variable bindings.

## Usage

```bash
# Build
mvn package

# Run
mvn exec:java -Dexec.args="<test-file.java> <rules.yaml> [--debug]"

# Or with the JAR directly
java -cp target/test-rule-verifier-1.0-SNAPSHOT.jar:target/dependency/* \
    com.testruleverifier.Main <test-file.java> <rules.yaml> [--debug]
```

### Example

```bash
mvn exec:java -Dexec.args="examples/CalculatorTest.java examples/calculator_rules.yaml --debug"
```

## Supported Testing Frameworks

The fact extractor recognizes test methods and assertions from:

| Framework         | Test Annotations                       | Assertions                                                    |
|-------------------|----------------------------------------|---------------------------------------------------------------|
| **JUnit 5**       | `@Test`, `@ParameterizedTest`          | `assertEquals`, `assertNull`, `assertNotNull`, `assertTrue`, `assertFalse`, `assertThrows`, `assertNotEquals` |
| **JUnit 4**       | `@Test`, `@Test(expected=...)`         | `assertEquals`, `assertNull`, `assertNotNull`, `assertTrue`, `assertFalse` |
| **TestNG**        | `@Test`                                | `assertEquals`, `assertNull`, `assertNotNull`, `assertTrue`, `assertFalse`, `expectThrows` |
| **AssertJ**       | —                                      | `assertThat(...).isNull()`, `.isEqualTo()`, `.isNotNull()`, `.isTrue()`, `.isFalse()`, `.contains()`, `.isInstanceOf()`, etc. |
| **Hamcrest**      | —                                      | `assertThat(actual, equalTo(...))`, `assertThat(actual, nullValue())`, etc. |
| **Mockito**       | —                                      | `when(...).thenReturn(...)`, `when(...).thenThrow(...)`, `mock()`, `spy()` |

## Architecture

Same 4-stage pipeline as the C# and Python versions:

1. **FactExtractor** — JavaParser AST → semantic facts (Created, Called, Assigned, MockSetup, Asserted)
2. **RuleParser** — YAML → Rule objects with given/assert clauses and `$`-prefixed bind variables
3. **Solver** — Backtracking constraint satisfaction: binds rule variables to test code variables
4. **Reporter** — Console output with pass/fail, bindings, OR groups, parent/child rules, bar charts

## Requirements

- Java 17+
- Maven 3.8+
