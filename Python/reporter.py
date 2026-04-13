"""Console reporter for rule matching results."""

from models import RuleResult, TestFacts, MockClause


def print_test_summary(tests: list[TestFacts]) -> None:
    print(f"\n📋 Found {len(tests)} test functions\n")
    for t in tests:
        type_counts: dict[str, int] = {}
        for f in t.facts:
            name = type(f).__name__.replace("Fact", "")
            type_counts[name] = type_counts.get(name, 0) + 1
        counts_str = ", ".join(f"{v} {k}" for k, v in type_counts.items())
        print(f"   • {t.test_name}")
        print(f"     Facts: {counts_str}")


def print_results(results: list[RuleResult]) -> None:
    print()
    print("═══════════════════════════════════════════════════════════")
    print("📊 RULE VERIFICATION (Constraint Satisfaction)")
    print("═══════════════════════════════════════════════════════════\n")

    # Build parent→children lookup
    children_of: dict[str, list[RuleResult]] = {}
    child_ids: set[str] = set()
    for r in results:
        if r.rule.parent:
            children_of.setdefault(r.rule.parent, []).append(r)
            child_ids.add(r.rule.id)

    for r in results:
        if r.rule.id in child_ids:
            continue
        _print_single_result(r, indent="")
        if r.rule.id in children_of:
            children = children_of[r.rule.id]
            print(f"   📎 Edge cases ({len(children)}):")
            for child in children:
                _print_single_result(child, indent="   ")

    implemented = sum(1 for r in results if r.is_implemented)
    print("═══════════════════════════════════════════════════════════")
    print(f"Summary: {implemented}/{len(results)} rules implemented")

    _print_category_summary(results, "Raises",
        lambda r: any(a.semantic_meaning.lower() == "raises" for a in r.rule.asserts))
    _print_category_summary(results, "Mocks",
        lambda r: any(isinstance(g, MockClause) for g in r.rule.given))

    print()
    print("── Implementation Counts ───────────────────────────────")
    for r in results:
        prefix = "  └─ " if r.rule.parent else ""
        count = len(r.all_matched_tests)
        bar = "█" * count + ("░" if count == 0 else "")
        print(f"   {prefix}{r.rule.id:<30} {bar} {count}")
    print("═══════════════════════════════════════════════════════════")


def _print_single_result(r: RuleResult, indent: str) -> None:
    parent_label = f" (edge case of {r.rule.parent})" if r.rule.parent else ""
    if r.is_implemented:
        print(f"{indent}✅ IMPLEMENTED: {r.rule.id}{parent_label}")
        print(f"{indent}   {r.rule.description}")
        print(f"{indent}   Test: {r.matched_test}")
        if r.bindings:
            bind_str = ", ".join(f"{k}={v}" for k, v in r.bindings.items())
            print(f"{indent}   Bindings: {bind_str}")
        print(f"{indent}   ✓ {len(r.given_matched)} given + {len(r.assert_matched)} assert matched")
        print(f"{indent}   🔢 Implemented {len(r.all_matched_tests)} time(s) across tests:")
        for t in r.all_matched_tests:
            print(f"{indent}      • {t}")
    else:
        print(f"{indent}❌ NOT IMPLEMENTED: {r.rule.id}{parent_label}")
        print(f"{indent}   {r.rule.description}")
        if r.given_missing:
            print(f"{indent}   Missing given:")
            for g in r.given_missing:
                print(f"{indent}      • {g}")
        if r.assert_missing:
            print(f"{indent}   Missing assert:")
            for a in r.assert_missing:
                print(f"{indent}      • {a}")
    print()


def _print_category_summary(results: list[RuleResult], label: str, predicate) -> None:
    category = [r for r in results if predicate(r)]
    if not category:
        print(f"   {label}: N/A (no rules)")
        return
    impl = sum(1 for r in category if r.is_implemented)
    pct = round(100.0 * impl / len(category))
    print(f"   {label}: {impl}/{len(category)} implemented ({pct}%)")


def print_facts(tests: list[TestFacts]) -> None:
    from models import CreatedFact, CalledFact, AssignedFact, MockSetupFact, AssertedFact

    print("\n── Extracted facts (debug) ──────────────────────────────\n")
    for t in tests:
        print(f"Test: {t.test_name}")
        for f in t.facts:
            if isinstance(f, CreatedFact):
                desc = f"  [{f.sequence}] Created {f.variable}: {f.type_name}({', '.join(f.arguments)})"
            elif isinstance(f, CalledFact):
                recv = f.receiver or "(static)"
                desc = f"  [{f.sequence}] Called {recv}.{f.method}({', '.join(f.arguments)})"
                if f.result_variable:
                    desc += f" → {f.result_variable}"
                if f.receiver_type:
                    desc += f" [type: {f.receiver_type}]"
                if f.callback_assign_target:
                    desc += f" [cb→{f.callback_assign_target}]"
                if f.callback_throw_type:
                    desc += f" [throws:{f.callback_throw_type}]"
            elif isinstance(f, AssignedFact):
                desc = f"  [{f.sequence}] Assigned {f.target} = {f.source}"
                if f.cast_type:
                    desc += f" as {f.cast_type}"
            elif isinstance(f, MockSetupFact):
                ret = f.return_value if f.return_value else f"side_effect={f.side_effect}"
                desc = f"  [{f.sequence}] Mock {f.mock_variable}.{f.method} returns {ret}"
            elif isinstance(f, AssertedFact):
                desc = f"  [{f.sequence}] Assert {f.semantic_meaning}({', '.join(f.arguments)})"
            else:
                desc = f"  [{f.sequence}] {f}"
            print(desc)
        if t.variable_types:
            types_str = ", ".join(f"{k}:{v}" for k, v in t.variable_types.items())
            print(f"  Variable types: {types_str}")
        print()
