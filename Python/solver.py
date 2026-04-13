"""
Constraint-satisfaction solver: matches rules against extracted test facts.

For each rule and each test, tries to find a consistent binding of rule variables
($x, $obj, $result, ...) to actual code variable names that satisfies ALL given
clauses (in order) and ALL assert clauses.

Algorithm: backtracking search over given clauses with ordered facts.
"""

import re
from models import (
    Rule, RuleResult, TestFacts, Fact,
    GivenClause, CreateClause, CallClause, AssignClause, MockClause,
    AssertClause,
    CreatedFact, CalledFact, AssignedFact, MockSetupFact, AssertedFact,
)


def match(rule: Rule, test: TestFacts) -> RuleResult:
    result = RuleResult(rule=rule)
    final_bindings: dict[str, str] = {}

    if _solve_given(rule.given, 0, test.facts, 0, {}, rule.asserts, test, final_bindings):
        result.is_implemented = True
        result.matched_test = test.test_name
        result.bindings = dict(final_bindings)
        result.given_matched = [_describe_given(g) for g in rule.given]
        result.assert_matched = [_describe_assert(a) for a in rule.asserts]
    return result


def match_against_all(rule: Rule, all_tests: list[TestFacts]) -> RuleResult:
    first_match: RuleResult | None = None
    all_matched: list[str] = []

    for test in all_tests:
        result = match(rule, test)
        if result.is_implemented:
            all_matched.append(test.test_name)
            if first_match is None:
                first_match = result

    if first_match is not None:
        first_match.all_matched_tests = all_matched
        return first_match

    return RuleResult(
        rule=rule,
        is_implemented=False,
        given_missing=[_describe_given(g) for g in rule.given],
        assert_missing=[_describe_assert(a) for a in rule.asserts],
    )


# ── Backtracking solver ──────────────────────────────

def _solve_given(
    givens: list[GivenClause], given_idx: int,
    facts: list[Fact], fact_start: int,
    bindings: dict[str, str],
    asserts: list[AssertClause], test: TestFacts,
    final_bindings: dict[str, str],
) -> bool:
    if given_idx >= len(givens):
        if _check_asserts(asserts, test, bindings):
            final_bindings.clear()
            final_bindings.update(bindings)
            return True
        return False

    clause = givens[given_idx]

    # Negated clause: verify NO fact matches, then continue without advancing fact index
    if clause.negated:
        for fi in range(len(facts)):
            probe = _try_match_given(clause, facts[fi], bindings, test)
            if probe is not None:
                return False  # found a match → negation fails
        # No match found → negation succeeds, continue with same fact_start
        return _solve_given(givens, given_idx + 1, facts, fact_start, bindings, asserts, test, final_bindings)

    for fi in range(fact_start, len(facts)):
        new_bindings = _try_match_given(clause, facts[fi], bindings, test)
        if new_bindings is not None:
            if _solve_given(givens, given_idx + 1, facts, fi + 1, new_bindings, asserts, test, final_bindings):
                return True

    return False


# ── Given clause matching ─────────────────────────────

def _try_match_given(clause: GivenClause, fact: Fact, bindings: dict[str, str], test: TestFacts) -> dict[str, str] | None:
    if isinstance(clause, CreateClause):
        return _try_match_create(clause, fact, bindings)
    if isinstance(clause, CallClause):
        return _try_match_call(clause, fact, bindings, test)
    if isinstance(clause, AssignClause):
        return _try_match_assign(clause, fact, bindings)
    if isinstance(clause, MockClause):
        return _try_match_mock(clause, fact, bindings)
    return None


def _try_match_create(clause: CreateClause, fact: Fact, bindings: dict[str, str]) -> dict[str, str] | None:
    if not isinstance(fact, CreatedFact):
        return None
    if not _type_matches(clause.type_pattern, fact.type_name):
        return None

    b = dict(bindings)
    if not _try_bind(b, clause.bind_var, fact.variable):
        return None

    if clause.exact_arg_count and len(fact.arguments) != len(clause.arg_bindings):
        return None

    for i, arg_bind in enumerate(clause.arg_bindings):
        if i >= len(fact.arguments):
            return None
        if not _try_bind_or_match(b, arg_bind, fact.arguments[i]):
            return None

    return b


def _try_match_call(clause: CallClause, fact: Fact, bindings: dict[str, str], test: TestFacts) -> dict[str, str] | None:
    if not isinstance(fact, CalledFact):
        return None
    if fact.method.lower() != clause.method_name.lower():
        return None

    b = dict(bindings)

    if clause.receiver_bind_var is not None:
        if fact.receiver is None:
            return None
        if not _try_bind(b, clause.receiver_bind_var, fact.receiver):
            return None

    if clause.on_type_pattern is not None:
        if fact.receiver_type is None:
            return None
        if not _type_matches(clause.on_type_pattern, fact.receiver_type):
            return None

    if clause.result_bind_var is not None:
        if fact.result_variable is None:
            return None
        if not _try_bind(b, clause.result_bind_var, fact.result_variable):
            return None

    for i, arg_bind in enumerate(clause.arg_bindings):
        if i >= len(fact.arguments):
            return None
        if not _try_bind_or_match(b, arg_bind, fact.arguments[i]):
            return None

    if clause.callback_bind_var is not None:
        if fact.callback_assign_target is None:
            return None
        if not _try_bind(b, clause.callback_bind_var, fact.callback_assign_target):
            return None

    if clause.throws_bind_var is not None:
        if fact.callback_throw_type is None:
            return None
        if not _try_bind_or_match(b, clause.throws_bind_var, fact.callback_throw_type):
            return None

    return b


def _try_match_assign(clause: AssignClause, fact: Fact, bindings: dict[str, str]) -> dict[str, str] | None:
    if not isinstance(fact, AssignedFact):
        return None
    if fact.cast_type is None:
        return None
    if not _type_matches(clause.cast_type_pattern, fact.cast_type):
        return None

    b = dict(bindings)
    if not _try_bind(b, clause.target_bind_var, fact.target):
        return None
    if not _try_bind(b, clause.source_bind_var, fact.source):
        return None
    return b


def _try_match_mock(clause: MockClause, fact: Fact, bindings: dict[str, str]) -> dict[str, str] | None:
    if not isinstance(fact, MockSetupFact):
        return None
    if fact.method.lower() != clause.method_name.lower():
        return None

    if not clause.return_value.lower() == "any":
        if fact.return_value is None:
            return None
        if fact.return_value.lower() != clause.return_value.lower():
            return None

    b = dict(bindings)
    if clause.mock_bind_var is not None:
        if not _try_bind(b, clause.mock_bind_var, fact.mock_variable):
            return None
    return b


# ── Assert checking ───────────────────────────────────

def _check_asserts(asserts: list[AssertClause], test: TestFacts, bindings: dict[str, str]) -> bool:
    assert_facts = [f for f in test.facts if isinstance(f, AssertedFact)]
    aliases = _build_alias_map(test)

    for ac in asserts:
        if not any(_assert_matches(ac, af, bindings, aliases) for af in assert_facts):
            return False
    return True


def _build_alias_map(test: TestFacts) -> dict[str, set[str]]:
    alias_map: dict[str, set[str]] = {}
    for fact in test.facts:
        if isinstance(fact, AssignedFact):
            alias_map.setdefault(fact.source, {fact.source}).add(fact.target)
            alias_map.setdefault(fact.target, {fact.target}).add(fact.source)

    # Transitive closure
    changed = True
    while changed:
        changed = False
        for key, group in alias_map.items():
            to_add = set()
            for a in group:
                to_add |= alias_map.get(a, set())
            if not to_add.issubset(group):
                group |= to_add
                changed = True

    return alias_map


def _assert_matches(clause: AssertClause, fact: AssertedFact, bindings: dict[str, str], aliases: dict[str, set[str]]) -> bool:
    # Semantic meaning must match
    if clause.semantic_meaning.lower() != "any":
        if fact.semantic_meaning.lower() != clause.semantic_meaning.lower():
            return False

    if not clause.args:
        return True

    resolved = [_resolve_expression(a, bindings) for a in clause.args]

    # For 2-arg assertions, try both orderings
    if len(resolved) == 2 and len(fact.arguments) >= 2:
        return (
            (_arg_matches(resolved[0], fact.arguments[0], aliases) and _arg_matches(resolved[1], fact.arguments[1], aliases))
            or (_arg_matches(resolved[0], fact.arguments[1], aliases) and _arg_matches(resolved[1], fact.arguments[0], aliases))
        )

    # Single-arg or multi-arg: each resolved arg must appear somewhere
    for r in resolved:
        if r.lower() == "any":
            continue
        if not any(_arg_matches(r, fa, aliases) for fa in fact.arguments):
            return False
    return True


# ── Expression resolution ─────────────────────────────

def _resolve_expression(expr: str, bindings: dict[str, str]) -> str:
    if not expr.startswith("$"):
        return expr

    dot_idx = expr.find(".")
    if dot_idx >= 0:
        var_part = expr[:dot_idx]
        rest = expr[dot_idx:]
    else:
        var_part = expr
        rest = ""

    if var_part in bindings:
        return bindings[var_part] + rest
    return expr


def _arg_matches(resolved: str, fact_arg: str, aliases: dict[str, set[str]]) -> bool:
    if resolved.lower() == "any":
        return True
    if resolved.lower() == fact_arg.lower():
        return True

    # Normalize whitespace
    rn = resolved.replace(" ", "")
    fn = fact_arg.replace(" ", "")
    if rn.lower() == fn.lower():
        return True

    # Lambda contains match
    if "lambda" in fn.lower() and rn.lower() in fn.lower():
        return True

    # Alias-based matching
    r_dot = resolved.find(".")
    f_dot = fact_arg.find(".")
    if r_dot > 0 and f_dot > 0:
        r_prefix = resolved[:r_dot]
        r_suffix = resolved[r_dot:]
        f_prefix = fact_arg[:f_dot]
        f_suffix = fact_arg[f_dot:]
        if r_suffix.lower() == f_suffix.lower():
            if r_prefix in aliases and f_prefix in aliases[r_prefix]:
                return True

    return False


# ── Binding helpers ───────────────────────────────────

def _try_bind(bindings: dict[str, str], variable: str, value: str) -> bool:
    if variable in ("any", "$any"):
        return True
    if variable in bindings:
        return bindings[variable] == value
    bindings[variable] = value
    return True


def _try_bind_or_match(bindings: dict[str, str], pattern: str, actual: str) -> bool:
    if pattern.lower() == "any":
        return True
    if pattern.startswith("$"):
        return _try_bind(bindings, pattern, actual)
    # Literal match
    return pattern.lower() == actual.lower()


# ── Type matching ─────────────────────────────────────

def _type_matches(pattern: str, actual: str) -> bool:
    if pattern.lower() == "any":
        return True
    if pattern.lower() == actual.lower():
        return True

    # Convert pattern to regex
    escaped = re.escape(pattern)
    regex_str = escaped.replace(r"\<any\>", r"<.+>")
    # Also allow pattern "MyType" to match "MyType[whatever]" (Python generics)
    regex_str = regex_str.replace(r"\[any\]", r"\[.+\]")
    if "[" not in pattern and "<" not in pattern:
        regex_str += r"(?:\[.+\])?(?:<.+>)?"
    regex_str = "^" + regex_str + "$"

    return bool(re.match(regex_str, actual, re.IGNORECASE))


# ── Description helpers ───────────────────────────────

def _describe_given(g: GivenClause) -> str:
    prefix = "NOT " if g.negated else ""
    if isinstance(g, CreateClause):
        args_str = f"({', '.join(g.arg_bindings)})" if g.arg_bindings else ""
        return prefix + f"create {g.bind_var}: {g.type_pattern}{args_str}"
    if isinstance(g, CallClause):
        parts = []
        if g.result_bind_var:
            parts.append(f"{g.result_bind_var} = ")
        parts.append("call: ")
        if g.receiver_bind_var:
            parts.append(f"{g.receiver_bind_var}.")
        parts.append(g.method_name)
        if g.arg_bindings:
            parts.append(f"({', '.join(g.arg_bindings)})")
        if g.on_type_pattern:
            parts.append(f" on {g.on_type_pattern}")
        if g.callback_bind_var:
            parts.append(f" -> {g.callback_bind_var}")
        if g.throws_bind_var:
            parts.append(f" throws {g.throws_bind_var}")
        return prefix + "".join(parts)
    if isinstance(g, AssignClause):
        return prefix + f"assign {g.target_bind_var} = {g.source_bind_var}: {g.cast_type_pattern}"
    if isinstance(g, MockClause):
        return prefix + f"mock: {g.method_name} returns {g.return_value}"
    return prefix + str(g)


def _describe_assert(a: AssertClause) -> str:
    if a.args:
        return f"{a.semantic_meaning}: {', '.join(a.args)}"
    return a.semantic_meaning
