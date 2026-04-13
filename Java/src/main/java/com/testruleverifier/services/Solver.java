package com.testruleverifier.services;

import com.testruleverifier.models.*;

import java.util.*;
import java.util.regex.Pattern;
import java.util.stream.Collectors;

/**
 * Constraint-satisfaction solver: matches rules against extracted test facts.
 *
 * For each rule and each test, tries to find a consistent binding of rule variables
 * ($x, $acc, $result, ...) to actual code variable names that satisfies ALL given
 * clauses (in order) and ALL assert clauses.
 *
 * Algorithm: backtracking search over given clauses with ordered facts.
 */
public class Solver {

    public static RuleResult match(Rule rule, TestFacts test) {
        RuleResult result = new RuleResult();
        result.setRule(rule);
        Map<String, String> bindings = new HashMap<>();
        Map<String, String> finalBindings = new HashMap<>();

        if (solveGiven(rule.getGiven(), 0, test.getFacts(), 0, bindings,
                rule.getAssertClauses(), test, result, finalBindings)) {
            result.setImplemented(true);
            result.setMatchedTest(test.getTestName());
            result.setBindings(finalBindings);
            result.setGivenMatched(rule.getGiven().stream().map(Solver::describeGiven).collect(Collectors.toList()));
            result.setAssertMatched(rule.getAssertClauses().stream().map(Solver::describeAssert).collect(Collectors.toList()));
        } else {
            result.setImplemented(false);
        }

        return result;
    }

    public static RuleResult matchAgainstAll(Rule rule, List<TestFacts> allTests) {
        RuleResult firstMatch = null;
        List<String> allMatched = new ArrayList<>();

        for (TestFacts test : allTests) {
            RuleResult result = match(rule, test);
            if (result.isImplemented()) {
                allMatched.add(test.getTestName());
                if (firstMatch == null) firstMatch = result;
            }
        }

        if (firstMatch != null) {
            firstMatch.setAllMatchedTests(allMatched);
            return firstMatch;
        }

        RuleResult failed = new RuleResult();
        failed.setRule(rule);
        failed.setImplemented(false);
        failed.setGivenMissing(rule.getGiven().stream().map(Solver::describeGiven).collect(Collectors.toList()));
        failed.setAssertMissing(rule.getAssertClauses().stream().map(Solver::describeAssert).collect(Collectors.toList()));
        return failed;
    }

    // ── Backtracking solver for Given clauses ─────────────

    private static boolean solveGiven(
            List<GivenClause> givens, int givenIdx,
            List<Fact> facts, int factStartIdx,
            Map<String, String> bindings,
            List<AssertClause> asserts, TestFacts test,
            RuleResult result, Map<String, String> finalBindings) {

        if (givenIdx >= givens.size()) {
            if (checkAsserts(asserts, test, bindings)) {
                finalBindings.clear();
                finalBindings.putAll(bindings);
                return true;
            }
            return false;
        }

        GivenClause clause = givens.get(givenIdx);

        // Negated clause
        if (clause.isNegated()) {
            for (int fi = 0; fi < facts.size(); fi++) {
                Map<String, String> probe = tryMatchGiven(clause, facts.get(fi), bindings, test);
                if (probe != null) return false;
            }
            return solveGiven(givens, givenIdx + 1, facts, factStartIdx, bindings, asserts, test, result, finalBindings);
        }

        for (int fi = factStartIdx; fi < facts.size(); fi++) {
            Map<String, String> newBindings = tryMatchGiven(clause, facts.get(fi), bindings, test);
            if (newBindings != null) {
                if (solveGiven(givens, givenIdx + 1, facts, fi + 1, newBindings, asserts, test, result, finalBindings)) {
                    return true;
                }
            }
        }

        return false;
    }

    // ── Given clause matching ─────────────────────────────

    private static Map<String, String> tryMatchGiven(GivenClause clause, Fact fact, Map<String, String> bindings, TestFacts test) {
        if (clause instanceof CreateClause c) return tryMatchCreate(c, fact, bindings);
        if (clause instanceof CallClause c) return tryMatchCall(c, fact, bindings, test);
        if (clause instanceof AssignClause c) return tryMatchAssign(c, fact, bindings);
        if (clause instanceof MockClause c) return tryMatchMock(c, fact, bindings);
        return null;
    }

    private static Map<String, String> tryMatchCreate(CreateClause clause, Fact fact, Map<String, String> bindings) {
        if (!(fact instanceof CreatedFact cf)) return null;
        if (!typeMatches(clause.getTypePattern(), cf.getType())) return null;

        Map<String, String> b = new HashMap<>(bindings);
        if (!tryBind(b, clause.getBindVar(), cf.getVariable())) return null;

        if (clause.isExactArgCount() && cf.getArguments().size() != clause.getArgBindings().size())
            return null;

        for (int i = 0; i < clause.getArgBindings().size(); i++) {
            if (i >= cf.getArguments().size()) return null;
            if (!tryBindOrMatch(b, clause.getArgBindings().get(i), cf.getArguments().get(i))) return null;
        }

        return b;
    }

    private static Map<String, String> tryMatchCall(CallClause clause, Fact fact, Map<String, String> bindings, TestFacts test) {
        if (!(fact instanceof CalledFact cf)) return null;
        if (!cf.getMethod().equalsIgnoreCase(clause.getMethodName())) return null;

        Map<String, String> b = new HashMap<>(bindings);

        if (clause.getReceiverBindVar() != null) {
            if (cf.getReceiver() == null) return null;
            if (!tryBind(b, clause.getReceiverBindVar(), cf.getReceiver())) return null;
        }

        if (clause.getOnTypePattern() != null) {
            if (cf.getReceiverType() == null) return null;
            if (!typeMatches(clause.getOnTypePattern(), cf.getReceiverType())) return null;
        }

        if (clause.getResultBindVar() != null) {
            if (cf.getResultVariable() == null) return null;
            if (!tryBind(b, clause.getResultBindVar(), cf.getResultVariable())) return null;
        }

        for (int i = 0; i < clause.getArgBindings().size(); i++) {
            if (i >= cf.getArguments().size()) return null;
            if (!tryBindOrMatch(b, clause.getArgBindings().get(i), cf.getArguments().get(i))) return null;
        }

        if (clause.getCallbackBindVar() != null) {
            if (cf.getCallbackAssignTarget() == null) return null;
            if (!tryBind(b, clause.getCallbackBindVar(), cf.getCallbackAssignTarget())) return null;
        }

        if (clause.getThrowsBindVar() != null) {
            if (cf.getCallbackThrowType() == null) return null;
            if (!tryBindOrMatch(b, clause.getThrowsBindVar(), cf.getCallbackThrowType())) return null;
        }

        return b;
    }

    private static Map<String, String> tryMatchAssign(AssignClause clause, Fact fact, Map<String, String> bindings) {
        if (!(fact instanceof AssignedFact af)) return null;
        if (af.getCastType() == null) return null;
        if (!typeMatches(clause.getCastTypePattern(), af.getCastType())) return null;

        Map<String, String> b = new HashMap<>(bindings);
        if (!tryBind(b, clause.getTargetBindVar(), af.getTarget())) return null;
        if (!tryBind(b, clause.getSourceBindVar(), af.getSource())) return null;

        return b;
    }

    private static Map<String, String> tryMatchMock(MockClause clause, Fact fact, Map<String, String> bindings) {
        if (!(fact instanceof MockSetupFact mf)) return null;
        if (!mf.getMethod().equalsIgnoreCase(clause.getMethodName())) return null;

        if (!clause.getReturnValue().equalsIgnoreCase("any")) {
            if (mf.getReturnValue() == null) return null;
            if (!mf.getReturnValue().equalsIgnoreCase(clause.getReturnValue())) return null;
        }

        Map<String, String> b = new HashMap<>(bindings);
        if (clause.getMockBindVar() != null) {
            if (!tryBind(b, clause.getMockBindVar(), mf.getMockVariable())) return null;
        }

        return b;
    }

    // ── Assert checking ───────────────────────────────────

    private static boolean checkAsserts(List<AssertClause> asserts, TestFacts test, Map<String, String> bindings) {
        List<AssertedFact> assertFacts = test.getFacts().stream()
                .filter(f -> f instanceof AssertedFact)
                .map(f -> (AssertedFact) f)
                .collect(Collectors.toList());

        Map<String, Set<String>> aliases = buildAliasMap(test);

        for (AssertClause ac : asserts) {
            boolean found = assertFacts.stream().anyMatch(af -> assertMatches(ac, af, bindings, aliases));
            if (!found) return false;
        }
        return true;
    }

    private static Map<String, Set<String>> buildAliasMap(TestFacts test) {
        Map<String, Set<String>> map = new HashMap<>();

        for (Fact f : test.getFacts()) {
            if (f instanceof AssignedFact af) {
                map.computeIfAbsent(af.getSource(), k -> new HashSet<>(Set.of(k))).add(af.getTarget());
                map.computeIfAbsent(af.getTarget(), k -> new HashSet<>(Set.of(k))).add(af.getSource());
            }
        }

        // Transitive closure
        boolean changed = true;
        while (changed) {
            changed = false;
            for (var entry : map.entrySet()) {
                Set<String> group = entry.getValue();
                Set<String> toAdd = new HashSet<>();
                for (String a : group) {
                    Set<String> other = map.get(a);
                    if (other != null) {
                        for (String o : other) {
                            if (!group.contains(o)) toAdd.add(o);
                        }
                    }
                }
                if (!toAdd.isEmpty()) {
                    group.addAll(toAdd);
                    changed = true;
                }
            }
        }

        return map;
    }

    private static boolean assertMatches(AssertClause clause, AssertedFact fact,
                                         Map<String, String> bindings, Map<String, Set<String>> aliases) {
        if (!clause.getSemanticMeaning().equalsIgnoreCase("any")) {
            if (!fact.getSemanticMeaning().equalsIgnoreCase(clause.getSemanticMeaning())) return false;
        }

        if (clause.getArgs().isEmpty()) return true;

        List<String> resolvedArgs = clause.getArgs().stream()
                .map(a -> resolveExpression(a, bindings))
                .collect(Collectors.toList());

        // For 2-arg assertions, try both orderings
        if (resolvedArgs.size() == 2 && fact.getArguments().size() >= 2) {
            return (argMatches(resolvedArgs.get(0), fact.getArguments().get(0), aliases)
                    && argMatches(resolvedArgs.get(1), fact.getArguments().get(1), aliases))
                    || (argMatches(resolvedArgs.get(0), fact.getArguments().get(1), aliases)
                    && argMatches(resolvedArgs.get(1), fact.getArguments().get(0), aliases));
        }

        for (String resolved : resolvedArgs) {
            if (resolved.equalsIgnoreCase("any")) continue;
            boolean found = fact.getArguments().stream().anyMatch(fa -> argMatches(resolved, fa, aliases));
            if (!found) return false;
        }
        return true;
    }

    // ── Expression resolution ─────────────────────────────

    private static String resolveExpression(String expr, Map<String, String> bindings) {
        if (!expr.startsWith("$")) return expr;

        int dotIdx = expr.indexOf('.');
        String varPart, rest;
        if (dotIdx >= 0) {
            varPart = expr.substring(0, dotIdx);
            rest = expr.substring(dotIdx);
        } else {
            varPart = expr;
            rest = "";
        }

        String bound = bindings.get(varPart);
        if (bound != null) return bound + rest;
        return expr;
    }

    private static boolean argMatches(String resolved, String factArg, Map<String, Set<String>> aliases) {
        if (resolved.equalsIgnoreCase("any")) return true;
        if (resolved.equalsIgnoreCase(factArg)) return true;

        String rn = resolved.replace(" ", "");
        String fn = factArg.replace(" ", "");
        if (rn.equalsIgnoreCase(fn)) return true;

        // Lambda contains match
        if (fn.contains("->") && fn.toLowerCase().contains(rn.toLowerCase())) return true;

        // Alias-based matching
        int rDot = resolved.indexOf('.');
        int fDot = factArg.indexOf('.');
        if (rDot > 0 && fDot > 0) {
            String rPrefix = resolved.substring(0, rDot);
            String rSuffix = resolved.substring(rDot);
            String fPrefix = factArg.substring(0, fDot);
            String fSuffix = factArg.substring(fDot);

            if (rSuffix.equalsIgnoreCase(fSuffix)) {
                Set<String> group = aliases.get(rPrefix);
                if (group != null && group.contains(fPrefix)) return true;
            }
        }

        return false;
    }

    // ── Binding helpers ───────────────────────────────────

    private static boolean tryBind(Map<String, String> bindings, String variable, String value) {
        if (variable.equals("any") || variable.equals("$any")) return true;

        String existing = bindings.get(variable);
        if (existing != null) return existing.equals(value);

        bindings.put(variable, value);
        return true;
    }

    private static boolean tryBindOrMatch(Map<String, String> bindings, String pattern, String actual) {
        if (pattern.equalsIgnoreCase("any")) return true;
        if (pattern.startsWith("$")) return tryBind(bindings, pattern, actual);
        return pattern.equalsIgnoreCase(actual);
    }

    // ── Type matching ─────────────────────────────────────

    private static boolean typeMatches(String pattern, String actual) {
        if (pattern.equalsIgnoreCase("any")) return true;
        if (pattern.equalsIgnoreCase(actual)) return true;

        String escaped = Pattern.quote(pattern);
        String regexStr = escaped.replace("<any>", "\\E<.+>\\Q");
        if (!pattern.contains("<")) {
            regexStr += "(?:<.+>)?";
        }
        regexStr = "^" + regexStr + "$";

        return Pattern.compile(regexStr, Pattern.CASE_INSENSITIVE).matcher(actual).matches();
    }

    // ── Description helpers ───────────────────────────────

    private static String describeGiven(GivenClause g) {
        String prefix = g.isNegated() ? "NOT " : "";
        if (g instanceof CreateClause c) {
            String argStr = c.getArgBindings().isEmpty() ? "" : "(" + String.join(", ", c.getArgBindings()) + ")";
            return prefix + "create " + c.getBindVar() + ": " + c.getTypePattern() + argStr;
        }
        if (g instanceof CallClause c) {
            StringBuilder sb = new StringBuilder(prefix);
            if (c.getResultBindVar() != null) sb.append(c.getResultBindVar()).append(" = ");
            sb.append("call: ");
            if (c.getReceiverBindVar() != null) sb.append(c.getReceiverBindVar()).append(".");
            sb.append(c.getMethodName());
            if (!c.getArgBindings().isEmpty()) sb.append("(").append(String.join(", ", c.getArgBindings())).append(")");
            if (c.getOnTypePattern() != null) sb.append(" on ").append(c.getOnTypePattern());
            if (c.getCallbackBindVar() != null) sb.append(" -> ").append(c.getCallbackBindVar());
            if (c.getThrowsBindVar() != null) sb.append(" throws ").append(c.getThrowsBindVar());
            return sb.toString();
        }
        if (g instanceof AssignClause c) {
            return prefix + "assign " + c.getTargetBindVar() + " = " + c.getSourceBindVar() + ": " + c.getCastTypePattern();
        }
        if (g instanceof MockClause c) {
            return prefix + "mock: " + c.getMethodName() + " returns " + c.getReturnValue();
        }
        return prefix + g.toString();
    }

    private static String describeAssert(AssertClause a) {
        return a.getSemanticMeaning() + (a.getArgs().isEmpty() ? "" : ": " + String.join(", ", a.getArgs()));
    }
}
