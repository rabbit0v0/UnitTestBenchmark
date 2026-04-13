"""
Extracts semantic facts from Python test functions using the ast module.
Produces a list of Facts per test function: Created, Called, Assigned, MockSetup, Asserted.

Supports:
- pytest test functions (test_*) and unittest TestCase methods
- pytest.raises, pytest.approx
- unittest assertions (assertEqual, assertRaises, etc.)
- unittest.mock (patch, MagicMock, Mock, return_value, side_effect)
- Plain assert statements with comparison operators
- @pytest.mark.parametrize expansion
"""

import ast
import re
from models import (
    TestFacts, Fact, CreatedFact, CalledFact, AssignedFact,
    MockSetupFact, AssertedFact,
)


def extract_all(source_code: str) -> list[TestFacts]:
    tree = ast.parse(source_code)
    results: list[TestFacts] = []

    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if not _is_test_function(node):
                continue

            param_rows = _extract_parametrize_rows(node)
            if param_rows:
                param_names = _extract_parametrize_param_names(node)
                for row in param_rows:
                    tf = _extract_from_function(node)
                    label = ", ".join(str(v) for v in row)
                    tf.test_name = f"{node.name}({label})"
                    _substitute_parameters(tf, param_names, row)
                    results.append(tf)
            else:
                results.append(_extract_from_function(node))

    return results


def _is_test_function(node: ast.FunctionDef) -> bool:
    if node.name.startswith("test_"):
        return True
    for dec in node.decorator_list:
        src = ast.dump(dec)
        if "pytest" in src or "unittest" in src:
            return True
    return False


# ── Parametrize support ───────────────────────────────

def _extract_parametrize_rows(node: ast.FunctionDef) -> list[list[str]]:
    rows = []
    for dec in node.decorator_list:
        if not _is_parametrize_decorator(dec):
            continue
        if isinstance(dec, ast.Call) and len(dec.args) >= 2:
            second_arg = dec.args[1]
            if isinstance(second_arg, (ast.List, ast.Tuple)):
                for elt in second_arg.elts:
                    if isinstance(elt, (ast.List, ast.Tuple)):
                        rows.append([ast.literal_eval(e) if _is_literal(e) else _unparse(e) for e in elt.elts])
                    else:
                        val = ast.literal_eval(elt) if _is_literal(elt) else _unparse(elt)
                        rows.append([str(val)])
    return rows


def _extract_parametrize_param_names(node: ast.FunctionDef) -> list[str]:
    for dec in node.decorator_list:
        if not _is_parametrize_decorator(dec):
            continue
        if isinstance(dec, ast.Call) and len(dec.args) >= 1:
            first_arg = dec.args[0]
            if isinstance(first_arg, ast.Constant) and isinstance(first_arg.value, str):
                return [p.strip() for p in first_arg.value.split(",")]
    return []


def _is_parametrize_decorator(dec) -> bool:
    src = _unparse(dec) if isinstance(dec, ast.Call) else _unparse(dec)
    return "parametrize" in src


def _substitute_parameters(tf: TestFacts, param_names: list[str], values: list) -> None:
    value_map = {}
    for i, name in enumerate(param_names):
        if i < len(values):
            value_map[name] = str(values[i])

    for i, fact in enumerate(tf.facts):
        if isinstance(fact, CreatedFact):
            fact.arguments = [value_map.get(a, a) for a in fact.arguments]
        elif isinstance(fact, CalledFact):
            fact.arguments = [value_map.get(a, a) for a in fact.arguments]
        elif isinstance(fact, AssertedFact):
            fact.arguments = [value_map.get(a, a) for a in fact.arguments]


# ── Function-level extraction ─────────────────────────

def _extract_from_function(node: ast.FunctionDef) -> TestFacts:
    tf = TestFacts(test_name=node.name)
    seq = [0]

    for stmt in node.body:
        _extract_from_statement(stmt, tf, seq)

    return tf


def _extract_from_statement(stmt: ast.stmt, tf: TestFacts, seq: list[int]) -> None:
    if isinstance(stmt, ast.Assign):
        _extract_from_assign(stmt, tf, seq)
    elif isinstance(stmt, ast.AnnAssign):
        _extract_from_ann_assign(stmt, tf, seq)
    elif isinstance(stmt, ast.Expr):
        _extract_from_expression(stmt.value, None, tf, seq)
    elif isinstance(stmt, ast.Assert):
        _extract_assert_stmt(stmt, tf, seq)
    elif isinstance(stmt, ast.With):
        _extract_from_with(stmt, tf, seq)
    elif isinstance(stmt, ast.If):
        for child in stmt.body + stmt.orelse:
            _extract_from_statement(child, tf, seq)
    elif isinstance(stmt, ast.Try):
        for child in stmt.body:
            _extract_from_statement(child, tf, seq)
        for handler in stmt.handlers:
            for child in handler.body:
                _extract_from_statement(child, tf, seq)


def _extract_from_assign(stmt: ast.Assign, tf: TestFacts, seq: list[int]) -> None:
    if len(stmt.targets) != 1:
        return
    target = stmt.targets[0]
    target_name = _unparse(target)
    value = stmt.value

    # Mock return_value / side_effect setup: mock_obj.method.return_value = val
    if isinstance(target, ast.Attribute) and target.attr in ("return_value", "side_effect"):
        _extract_mock_attr_setup(target, value, tf, seq)
        return

    if isinstance(value, ast.Call):
        _extract_from_call_assignment(target_name, value, tf, seq)
    elif isinstance(value, ast.Name) or isinstance(value, ast.Attribute):
        source = _unparse(value)
        tf.facts.append(AssignedFact(
            sequence=_next_seq(seq), target=target_name, source=source, cast_type=None
        ))


def _extract_from_ann_assign(stmt: ast.AnnAssign, tf: TestFacts, seq: list[int]) -> None:
    if stmt.target is None or stmt.value is None:
        return
    target_name = _unparse(stmt.target)
    ann_type = _unparse(stmt.annotation)
    tf.variable_types[target_name] = ann_type

    if isinstance(stmt.value, ast.Call):
        _extract_from_call_assignment(target_name, stmt.value, tf, seq)


def _extract_from_call_assignment(target_name: str, call: ast.Call, tf: TestFacts, seq: list[int]) -> None:
    func_name = _unparse(call.func)
    args = [_unparse(a) for a in call.args]
    args += [f"{kw.arg}={_unparse(kw.value)}" for kw in call.keywords if kw.arg]

    # Mock/MagicMock creation
    if _is_mock_creation(func_name):
        tf.variable_types[target_name] = func_name
        tf.facts.append(CreatedFact(
            sequence=_next_seq(seq), variable=target_name,
            type_name=func_name, arguments=args
        ))
        return

    # patch() call
    if "patch" in func_name:
        tf.variable_types[target_name] = "Mock"
        tf.facts.append(CreatedFact(
            sequence=_next_seq(seq), variable=target_name,
            type_name="Mock", arguments=args
        ))
        return

    # Regular object creation: looks like a class constructor (capitalized)
    base_name = func_name.rsplit(".", 1)[-1] if "." in func_name else func_name
    if base_name and base_name[0].isupper():
        tf.variable_types[target_name] = base_name
        tf.facts.append(CreatedFact(
            sequence=_next_seq(seq), variable=target_name,
            type_name=base_name, arguments=args
        ))
    else:
        # Function/method call assigned to variable
        receiver, method = _split_receiver_method(func_name)
        tf.facts.append(CalledFact(
            sequence=_next_seq(seq), receiver=receiver,
            receiver_type=_resolve_type(receiver, tf) if receiver else None,
            method=method, arguments=args,
            result_variable=target_name
        ))


def _extract_from_expression(expr: ast.expr, assigned_to: str | None, tf: TestFacts, seq: list[int]) -> None:
    if isinstance(expr, ast.Call):
        # Check assertion first
        if _try_extract_assertion_call(expr, tf, seq):
            return
        # Check mock setup: mock.configure_mock, mock.assert_called_with, etc.
        if _try_extract_mock_setup_call(expr, tf, seq):
            return
        # Regular call
        _extract_call_fact(expr, assigned_to, tf, seq)


def _extract_call_fact(call: ast.Call, result_var: str | None, tf: TestFacts, seq: list[int]) -> None:
    func_name = _unparse(call.func)
    args = [_unparse(a) for a in call.args]
    args += [f"{kw.arg}={_unparse(kw.value)}" for kw in call.keywords if kw.arg]
    receiver, method = _split_receiver_method(func_name)

    callback_target = _extract_callback_assign_target(call)
    callback_throw_type = _extract_callback_throw_type(call)

    tf.facts.append(CalledFact(
        sequence=_next_seq(seq), receiver=receiver,
        receiver_type=_resolve_type(receiver, tf) if receiver else None,
        method=method, arguments=args,
        result_variable=result_var,
        callback_assign_target=callback_target,
        callback_throw_type=callback_throw_type,
    ))


# ── With statement (pytest.raises, patch) ─────────────

def _extract_from_with(stmt: ast.With, tf: TestFacts, seq: list[int]) -> None:
    for item in stmt.items:
        ctx = item.context_expr
        if isinstance(ctx, ast.Call):
            func_name = _unparse(ctx.func)

            # pytest.raises(ExceptionType)
            if "raises" in func_name:
                args = [_unparse(a) for a in ctx.args]
                exc_type = args[0] if args else "any"
                # Check for match keyword arg
                match_arg = None
                for kw in ctx.keywords:
                    if kw.arg == "match":
                        match_arg = _unparse(kw.value)
                assert_args = [exc_type]
                if match_arg:
                    assert_args.append(match_arg)
                tf.facts.append(AssertedFact(
                    sequence=_next_seq(seq), semantic_meaning="Raises",
                    arguments=assert_args
                ))

            # patch(...) as mock_var
            elif "patch" in func_name:
                var_name = _unparse(item.optional_vars) if item.optional_vars else None
                if var_name:
                    tf.variable_types[var_name] = "Mock"
                    args = [_unparse(a) for a in ctx.args]
                    tf.facts.append(CreatedFact(
                        sequence=_next_seq(seq), variable=var_name,
                        type_name="Mock", arguments=args
                    ))

    # Process body
    for child in stmt.body:
        _extract_from_statement(child, tf, seq)


# ── Assert statement extraction ───────────────────────

def _extract_assert_stmt(stmt: ast.Assert, tf: TestFacts, seq: list[int]) -> None:
    test_expr = stmt.test

    if isinstance(test_expr, ast.Compare):
        _extract_compare_assert(test_expr, tf, seq)
    elif isinstance(test_expr, ast.Call):
        if _try_extract_assertion_call(test_expr, tf, seq):
            return
        # assert some_func() — treat as IsTrue
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="IsTrue",
            arguments=[_unparse(test_expr)]
        ))
    elif isinstance(test_expr, ast.UnaryOp) and isinstance(test_expr.op, ast.Not):
        operand = test_expr.operand
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="IsFalse",
            arguments=[_unparse(operand)]
        ))
    elif isinstance(test_expr, ast.NameConstant if hasattr(ast, "NameConstant") else ast.Constant):
        # assert True / assert False
        val = test_expr.value if isinstance(test_expr, ast.Constant) else test_expr.value
        meaning = "IsTrue" if val else "IsFalse"
        tf.facts.append(AssertedFact(sequence=_next_seq(seq), semantic_meaning=meaning, arguments=[]))
    else:
        # assert expr — treat as IsTrue
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="IsTrue",
            arguments=[_unparse(test_expr)]
        ))


def _extract_compare_assert(cmp: ast.Compare, tf: TestFacts, seq: list[int]) -> None:
    if len(cmp.ops) != 1 or len(cmp.comparators) != 1:
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="IsTrue",
            arguments=[_unparse(cmp)]
        ))
        return

    left = _unparse(cmp.left)
    right = _unparse(cmp.comparators[0])
    op = cmp.ops[0]

    if isinstance(op, (ast.Eq, ast.Is)):
        if right == "None" or left == "None":
            target = left if right == "None" else right
            tf.facts.append(AssertedFact(
                sequence=_next_seq(seq), semantic_meaning="IsNone",
                arguments=[target]
            ))
        else:
            tf.facts.append(AssertedFact(
                sequence=_next_seq(seq), semantic_meaning="AreEqual",
                arguments=[left, right]
            ))
    elif isinstance(op, (ast.NotEq, ast.IsNot)):
        if right == "None" or left == "None":
            target = left if right == "None" else right
            tf.facts.append(AssertedFact(
                sequence=_next_seq(seq), semantic_meaning="IsNotNone",
                arguments=[target]
            ))
        else:
            tf.facts.append(AssertedFact(
                sequence=_next_seq(seq), semantic_meaning="AreNotEqual",
                arguments=[left, right]
            ))
    elif isinstance(op, ast.In):
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="Contains",
            arguments=[left, right]
        ))
    elif isinstance(op, ast.NotIn):
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="NotContains",
            arguments=[left, right]
        ))
    elif isinstance(op, ast.Lt):
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="LessThan",
            arguments=[left, right]
        ))
    elif isinstance(op, ast.Gt):
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="GreaterThan",
            arguments=[left, right]
        ))
    else:
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="IsTrue",
            arguments=[_unparse(cmp)]
        ))


# ── Assertion call extraction ─────────────────────────

def _try_extract_assertion_call(call: ast.Call, tf: TestFacts, seq: list[int]) -> bool:
    func_name = _unparse(call.func)
    args = [_unparse(a) for a in call.args]

    # pytest.raises used as function call (not context manager)
    if "raises" in func_name and ("pytest" in func_name or func_name == "raises"):
        exc_type = args[0] if args else "any"
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning="Raises",
            arguments=[exc_type]
        ))
        return True

    # unittest-style assertions: self.assertEqual, self.assertRaises, etc.
    method_name = func_name.rsplit(".", 1)[-1] if "." in func_name else func_name

    semantic = _determine_semantic_meaning(method_name, args)
    if semantic:
        tf.facts.append(AssertedFact(
            sequence=_next_seq(seq), semantic_meaning=semantic, arguments=args
        ))
        return True

    return False


def _determine_semantic_meaning(method_name: str, args: list[str]) -> str | None:
    lower = method_name.lower()

    # pytest.raises / assertRaises
    if "raises" in lower:
        return "Raises"

    # None checks
    if "none" in lower and "not" not in lower:
        return "IsNone"
    if "notnone" in lower or "not_none" in lower or "isnotnone" in lower:
        return "IsNotNone"

    # Boolean checks
    if "true" in lower and "false" not in lower:
        return "IsTrue"
    if "false" in lower:
        return "IsFalse"

    # Inequality before equality (since "notequal" contains "equal")
    if "notequal" in lower or "not_equal" in lower:
        return "AreNotEqual"

    # Equality
    if "equal" in lower or "same" in lower:
        return "AreEqual"

    # Contains
    if "contain" in lower or "in" == lower or "assertin" == lower:
        return "Contains"

    # isinstance check
    if "isinstance" in lower or "isinstanceof" in lower:
        return "IsInstance"

    return None


# ── Mock extraction ───────────────────────────────────

def _is_mock_creation(func_name: str) -> bool:
    base = func_name.rsplit(".", 1)[-1] if "." in func_name else func_name
    return base in ("Mock", "MagicMock", "AsyncMock", "PropertyMock", "create_autospec")


def _extract_mock_attr_setup(target: ast.Attribute, value: ast.expr, tf: TestFacts, seq: list[int]) -> None:
    """Handle: mock_obj.method.return_value = val or mock_obj.method.side_effect = exc"""
    attr_type = target.attr  # "return_value" or "side_effect"
    val_str = _unparse(value)

    # Traverse to find mock var and method chain
    # e.g. mock_obj.some_method.return_value → mock_var=mock_obj, method=some_method
    if isinstance(target.value, ast.Attribute):
        mock_var = _unparse(target.value.value)
        method = target.value.attr
    elif isinstance(target.value, ast.Name):
        mock_var = target.value.id
        method = "(root)"
    else:
        return

    if attr_type == "return_value":
        tf.facts.append(MockSetupFact(
            sequence=_next_seq(seq), mock_variable=mock_var,
            method=method, return_value=val_str
        ))
    else:  # side_effect
        tf.facts.append(MockSetupFact(
            sequence=_next_seq(seq), mock_variable=mock_var,
            method=method, return_value=None, side_effect=val_str
        ))


def _try_extract_mock_setup_call(call: ast.Call, tf: TestFacts, seq: list[int]) -> bool:
    """Handle mock.patch.object(...).start(), mock.return_value.method, etc."""
    func_name = _unparse(call.func)

    # mock.assert_called_with, mock.assert_called_once_with, etc.
    # These are verification calls, not setups — skip them for now
    if "assert_called" in func_name or "assert_not_called" in func_name:
        return False

    return False


def _extract_callback_assign_target(call: ast.Call) -> str | None:
    """Extract the first assignment target inside a lambda argument."""
    for arg in call.args:
        if isinstance(arg, ast.Lambda):
            body = arg.body
            if isinstance(body, ast.NamedExpr):
                return _unparse(body.target)
    return None


def _extract_callback_throw_type(call: ast.Call) -> str | None:
    """Extract exception type from a raise inside a lambda/callable argument.
    
    Handles patterns like:
      func(lambda: (_ for _ in ()).throw(ExcType()))  — not common in Python
      func(callback)  where callback raises — can't statically detect
    
    For Python, the more common pattern is side_effect on mocks or
    direct raise in a function passed as callback. We detect:
      - Lambda body that is a Call to an exception constructor: lambda: ExcType(...)
        (used with side_effect = ExcType or side_effect = ExcType(...))
      - Lambda body that directly constructs an exception (less common)
    """
    for arg in call.args:
        if isinstance(arg, ast.Lambda):
            body = arg.body
            # lambda: raise is not valid Python, but lambda: ExcType() is
            # Check if body is calling an exception-like constructor
            if isinstance(body, ast.Call):
                func_name = _unparse(body.func)
                base = func_name.rsplit(".", 1)[-1] if "." in func_name else func_name
                if base and base[0].isupper():
                    return base
    
    # Check keyword args (e.g. side_effect=ValueError("msg"))
    for kw in call.keywords:
        if kw.arg == "side_effect":
            if isinstance(kw.value, ast.Call):
                func_name = _unparse(kw.value.func)
                base = func_name.rsplit(".", 1)[-1] if "." in func_name else func_name
                if base and base[0].isupper():
                    return base
            elif isinstance(kw.value, ast.Name):
                return kw.value.id
    
    return None


# ── Helpers ───────────────────────────────────────────

def _split_receiver_method(func_name: str) -> tuple[str | None, str]:
    if "." in func_name:
        parts = func_name.rsplit(".", 1)
        return parts[0], parts[1]
    return None, func_name


def _resolve_type(var_name: str | None, tf: TestFacts) -> str | None:
    if not var_name:
        return None
    if var_name in tf.variable_types:
        return tf.variable_types[var_name]
    for fact in reversed(tf.facts):
        if isinstance(fact, CreatedFact) and fact.variable == var_name:
            return fact.type_name
        if isinstance(fact, AssignedFact) and fact.target == var_name and fact.cast_type:
            return fact.cast_type
    return None


def _unparse(node: ast.AST) -> str:
    return ast.unparse(node)


def _is_literal(node: ast.expr) -> bool:
    return isinstance(node, ast.Constant)


def _next_seq(seq: list[int]) -> int:
    val = seq[0]
    seq[0] += 1
    return val
