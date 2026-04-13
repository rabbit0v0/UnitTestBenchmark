#!/usr/bin/env python3
"""
Pytest Rule Verifier — Constraint Satisfaction Approach

Usage: python main.py <test_file.py> <rules.yaml> [--debug]

Extracts semantic facts from Python test functions using AST analysis,
then matches YAML rules via constraint satisfaction with variable bindings.
"""

import sys
import os

import rule_parser
import fact_extractor
import solver
import reporter


def main() -> int:
    if len(sys.argv) < 3:
        print("Usage: python main.py <test_file.py> <rules.yaml> [--debug]")
        print()
        print("  test_file.py   Path to the Python unit test file")
        print("  rules.yaml     Path to the YAML rules file")
        print("  --debug        Show extracted facts per test (optional)")
        return 1

    test_file = sys.argv[1]
    rules_file = sys.argv[2]
    debug = "--debug" in sys.argv

    if not os.path.isfile(test_file):
        print(f"Test file not found: {test_file}", file=sys.stderr)
        return 1
    if not os.path.isfile(rules_file):
        print(f"Rules file not found: {rules_file}", file=sys.stderr)
        return 1

    # 1. Parse test file → extract facts
    with open(test_file, encoding="utf-8") as f:
        source_code = f.read()

    all_tests = fact_extractor.extract_all(source_code)
    reporter.print_test_summary(all_tests)

    if debug:
        reporter.print_facts(all_tests)

    # 2. Parse rules YAML
    with open(rules_file, encoding="utf-8") as f:
        yaml_content = f.read()

    rules = rule_parser.parse(yaml_content)
    print(f"\n📋 Rules: {len(rules)}\n")

    # 3. Match each rule against all tests
    results = [solver.match_against_all(r, all_tests) for r in rules]

    # 4. Report
    reporter.print_results(results)

    return 0 if all(r.is_implemented for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
