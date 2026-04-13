"""
Parses YAML rule files into Rule objects.

YAML format:
  rules:
    - id: my_rule
      description: "..."
      given:
        - create $obj: ClassName($arg)
        - call: $obj.method($arg)
        - mock: $mock.some_func returns value
      assert:
        - IsNone: $obj.attr
        - AreEqual: [$a, $b]
        - Raises: ValueError
"""

import re
import yaml
from models import (
    Rule, AssertClause, GivenClause,
    CreateClause, CallClause, AssignClause, MockClause,
)


def parse(yaml_content: str) -> list[Rule]:
    doc = yaml.safe_load(yaml_content)
    return [_parse_rule(yr) for yr in doc["rules"]]


def _parse_rule(yr: dict) -> Rule:
    rule = Rule(id=yr["id"], parent=yr.get("parent"), description=yr.get("description", ""))
    for given_map in yr.get("given") or []:
        rule.given.append(_parse_given_clause(given_map))
    for assert_map in yr.get("assert") or []:
        rule.asserts.append(_parse_assert_clause(assert_map))
    return rule


# ── Given clause parsing ─────────────────────────────

def _parse_given_clause(mapping: dict) -> GivenClause:
    key, value = next(iter(mapping.items()))
    val = str(value) if value is not None else ""

    # Check for "not " prefix → negated clause
    negated = False
    if key.lower().startswith("not "):
        negated = True
        key = key[4:]  # strip "not "

    clause = _parse_given_clause_core(key, val)
    clause.negated = negated
    return clause


def _parse_given_clause_core(key: str, val: str) -> GivenClause:
    # create $var: TypePattern($arg1, $arg2)
    m = re.match(r"^create\s+(\$\w+)$", key)
    if m:
        return _parse_create_clause(m.group(1), val)

    # $result = call: ...
    m = re.match(r"^(\$\w+)\s*=\s*call$", key)
    if m:
        return _parse_call_clause(val, result_bind_var=m.group(1))

    # call: ...
    if key == "call":
        return _parse_call_clause(val)

    # assign $target = $source: CastType
    m = re.match(r"^assign\s+(\$\w+)\s*=\s*(\$\w+)$", key)
    if m:
        return AssignClause(
            target_bind_var=m.group(1),
            source_bind_var=m.group(2),
            cast_type_pattern=val,
        )

    # mock: ...
    if key == "mock":
        return _parse_mock_clause(val)

    raise ValueError(f"Unknown given clause key: '{key}'")


def _parse_create_clause(bind_var: str, value: str) -> CreateClause:
    m = re.match(r"^([\w<>,\s?]+?)(?:\(([^)]*)\))?$", value)
    if not m:
        raise ValueError(f"Cannot parse create value: '{value}'")

    clause = CreateClause(bind_var=bind_var, type_pattern=m.group(1).strip())
    if m.group(2) is not None:
        clause.exact_arg_count = True
        if m.group(2).strip():
            clause.arg_bindings = _split_args(m.group(2))
    return clause


def _parse_call_clause(value: str, result_bind_var: str | None = None) -> CallClause:
    m = re.match(
        r"^(?:(\$\w+)\.)?(\w+)(?:\(([^)]*)\))?\s*(?:on\s+(.+?))?\s*(?:->\s*(\$\w+))?\s*(?:throws\s+(\$\w+|\w+))?$",
        value,
    )
    if not m:
        raise ValueError(f"Cannot parse call value: '{value}'")

    clause = CallClause(result_bind_var=result_bind_var, method_name=m.group(2))
    if m.group(1):
        clause.receiver_bind_var = m.group(1)
    if m.group(3) and m.group(3).strip():
        clause.arg_bindings = _split_args(m.group(3))
    if m.group(4):
        clause.on_type_pattern = m.group(4).strip()
    if m.group(5):
        clause.callback_bind_var = m.group(5)
    if m.group(6):
        clause.throws_bind_var = m.group(6)
    return clause


def _parse_mock_clause(value: str) -> MockClause:
    m = re.match(r"^(?:(\$\w+)\.)?(\w+)\s+returns\s+(.+)$", value)
    if not m:
        raise ValueError(f"Cannot parse mock value: '{value}'")
    return MockClause(
        mock_bind_var=m.group(1) if m.group(1) else None,
        method_name=m.group(2),
        return_value=m.group(3).strip(),
    )


# ── Assert clause parsing ────────────────────────────

def _parse_assert_clause(mapping: dict) -> AssertClause:
    key, value = next(iter(mapping.items()))
    clause = AssertClause(semantic_meaning=key)

    if isinstance(value, list):
        clause.args = [_normalize_arg(o) for o in value]
    elif value is not None:
        s = _normalize_arg(value)
        if s != "any":
            clause.args.append(s)

    return clause


def _normalize_arg(o) -> str:
    if o is None:
        return "None"
    s = str(o)
    if len(s) == 0:
        return '""'
    return s


def _split_args(arg_str: str) -> list[str]:
    return [a.strip() for a in arg_str.split(",") if a.strip()]
