"""
Semantic facts and rule definitions for pytest rule verification.
Facts are extracted from Python test code via AST analysis.
Rules are parsed from YAML files with $-prefixed bind variables.
"""

from dataclasses import dataclass, field


# ── Facts extracted from test code ──────────────────────

@dataclass
class Fact:
    sequence: int


@dataclass
class CreatedFact(Fact):
    """Object creation: var = TypeName(args)"""
    variable: str
    type_name: str
    arguments: list[str] = field(default_factory=list)


@dataclass
class CalledFact(Fact):
    """Method/function call: receiver.method(args) or func(args)"""
    receiver: str | None
    receiver_type: str | None
    method: str
    arguments: list[str] = field(default_factory=list)
    result_variable: str | None = None
    callback_assign_target: str | None = None
    callback_throw_type: str | None = None


@dataclass
class AssignedFact(Fact):
    """Assignment: target = source"""
    target: str
    source: str
    cast_type: str | None = None


@dataclass
class MockSetupFact(Fact):
    """Mock setup: mock_obj.method.return_value = value or patch"""
    mock_variable: str
    method: str
    return_value: str | None = None
    side_effect: str | None = None


@dataclass
class AssertedFact(Fact):
    """Assertion with resolved semantic meaning."""
    semantic_meaning: str  # "IsNone", "AreEqual", "Raises", "IsTrue", etc.
    arguments: list[str] = field(default_factory=list)


@dataclass
class TestFacts:
    """All facts extracted from a single test function."""
    test_name: str = ""
    facts: list[Fact] = field(default_factory=list)
    variable_types: dict[str, str] = field(default_factory=dict)


# ── Rule definitions ────────────────────────────────────

@dataclass
class GivenClause:
    negated: bool = False


@dataclass
class CreateClause(GivenClause):
    bind_var: str = ""
    type_pattern: str = ""
    arg_bindings: list[str] = field(default_factory=list)
    exact_arg_count: bool = False


@dataclass
class CallClause(GivenClause):
    result_bind_var: str | None = None
    receiver_bind_var: str | None = None
    on_type_pattern: str | None = None
    method_name: str = ""
    arg_bindings: list[str] = field(default_factory=list)
    callback_bind_var: str | None = None
    throws_bind_var: str | None = None


@dataclass
class AssignClause(GivenClause):
    target_bind_var: str = ""
    source_bind_var: str = ""
    cast_type_pattern: str = ""


@dataclass
class MockClause(GivenClause):
    mock_bind_var: str | None = None
    method_name: str = ""
    return_value: str = ""


@dataclass
class AssertClause:
    semantic_meaning: str = ""
    args: list[str] = field(default_factory=list)


@dataclass
class Rule:
    id: str = ""
    parent: str | None = None
    description: str = ""
    given: list[GivenClause] = field(default_factory=list)
    asserts: list[AssertClause] = field(default_factory=list)


@dataclass
class RuleResult:
    rule: Rule = field(default_factory=Rule)
    is_implemented: bool = False
    matched_test: str | None = None
    bindings: dict[str, str] | None = None
    given_matched: list[str] = field(default_factory=list)
    given_missing: list[str] = field(default_factory=list)
    assert_matched: list[str] = field(default_factory=list)
    assert_missing: list[str] = field(default_factory=list)
    all_matched_tests: list[str] = field(default_factory=list)
