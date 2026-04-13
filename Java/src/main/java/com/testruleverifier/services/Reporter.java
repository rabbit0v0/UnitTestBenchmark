package com.testruleverifier.services;

import com.testruleverifier.models.*;

import java.util.*;
import java.util.stream.Collectors;

/**
 * Console reporter for rule matching results.
 */
public class Reporter {

    public static void printTestSummary(List<TestFacts> tests) {
        System.out.println("\n\uD83D\uDCCB Found " + tests.size() + " test methods\n");
        for (TestFacts t : tests) {
            String factCounts = t.getFacts().stream()
                    .collect(Collectors.groupingBy(f -> f.getClass().getSimpleName().replace("Fact", ""), Collectors.counting()))
                    .entrySet().stream()
                    .map(e -> e.getValue() + " " + e.getKey())
                    .collect(Collectors.joining(", "));
            System.out.println("   \u2022 " + t.getTestName());
            System.out.println("     Facts: " + factCounts);
        }
    }

    public static void printResults(List<RuleResult> results) {
        System.out.println();
        System.out.println("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        System.out.println("\uD83D\uDCCA RULE VERIFICATION (Constraint Satisfaction)");
        System.out.println("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\n");

        // Build parent→children lookup
        Map<String, List<RuleResult>> childrenOf = results.stream()
                .filter(r -> r.getRule().getParent() != null)
                .collect(Collectors.groupingBy(r -> r.getRule().getParent()));
        Set<String> childIds = results.stream()
                .filter(r -> r.getRule().getParent() != null)
                .map(r -> r.getRule().getId())
                .collect(Collectors.toSet());

        // Build OR group lookup
        Map<String, List<RuleResult>> orGroups = results.stream()
                .filter(r -> r.getRule().getOrGroup() != null)
                .collect(Collectors.groupingBy(r -> r.getRule().getOrGroup()));
        Set<String> printedOrGroups = new HashSet<>();

        for (RuleResult r : results) {
            if (childIds.contains(r.getRule().getId())) continue;

            if (r.getRule().getOrGroup() != null) {
                if (printedOrGroups.contains(r.getRule().getOrGroup())) continue;
                printedOrGroups.add(r.getRule().getOrGroup());
                printOrGroup(r.getRule().getOrGroup(), orGroups.get(r.getRule().getOrGroup()));
                continue;
            }

            printSingleResult(r, "");

            List<RuleResult> children = childrenOf.get(r.getRule().getId());
            if (children != null) {
                System.out.println("   \uD83D\uDCCE Edge cases (" + children.size() + "):");
                for (RuleResult child : children) {
                    printSingleResult(child, "   ");
                }
            }
        }

        // Summary
        long orGroupCount = orGroups.size();
        long orGroupImplemented = orGroups.values().stream()
                .filter(g -> g.stream().anyMatch(RuleResult::isImplemented))
                .count();
        List<RuleResult> standalone = results.stream()
                .filter(r -> r.getRule().getOrGroup() == null)
                .collect(Collectors.toList());
        long totalLogical = standalone.size() + orGroupCount;
        long totalImplemented = standalone.stream().filter(RuleResult::isImplemented).count() + orGroupImplemented;

        System.out.println("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        System.out.println("Summary: " + totalImplemented + "/" + totalLogical + " rules implemented");
        if (orGroupCount > 0)
            System.out.println("   (" + orGroupCount + " OR group(s), each counting as 1 rule)");

        printCategorySummary(results, "Throws",
                r -> r.getRule().getAssertClauses().stream()
                        .anyMatch(a -> a.getSemanticMeaning().equalsIgnoreCase("Throws")));
        printCategorySummary(results, "Mocks",
                r -> r.getRule().getGiven().stream().anyMatch(g -> g instanceof MockClause));

        System.out.println();
        System.out.println("\u2500\u2500 Implementation Counts \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
        Set<String> printedOrGroupBars = new HashSet<>();
        for (RuleResult r : results) {
            if (r.getRule().getOrGroup() != null) {
                if (printedOrGroupBars.contains(r.getRule().getOrGroup())) continue;
                printedOrGroupBars.add(r.getRule().getOrGroup());
                List<RuleResult> groupMembers = orGroups.get(r.getRule().getOrGroup());
                int groupCount = groupMembers.stream().mapToInt(m -> m.getAllMatchedTests().size()).sum();
                String groupBar = "\u2588".repeat(groupCount) + (groupCount == 0 ? "\u2591" : "");
                System.out.printf("   %-30s %s %d%n", "[OR] " + r.getRule().getOrGroup(), groupBar, groupCount);
                for (RuleResult m : groupMembers) {
                    int mCount = m.getAllMatchedTests().size();
                    String mBar = "\u2588".repeat(mCount) + (mCount == 0 ? "\u2591" : "");
                    System.out.printf("     \u2514\u2500 %-28s %s %d%n", m.getRule().getId(), mBar, mCount);
                }
                continue;
            }

            String prefix = r.getRule().getParent() != null ? "  \u2514\u2500 " : "";
            int count = r.getAllMatchedTests().size();
            String bar = "\u2588".repeat(count) + (count == 0 ? "\u2591" : "");
            System.out.printf("   %s%-30s %s %d%n", prefix, r.getRule().getId(), bar, count);
        }
        System.out.println("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
    }

    private static void printOrGroup(String groupName, List<RuleResult> alternatives) {
        boolean anyImplemented = alternatives.stream().anyMatch(RuleResult::isImplemented);
        String icon = anyImplemented ? "\u2705" : "\u274C";
        String status = anyImplemented ? "IMPLEMENTED" : "NOT IMPLEMENTED";

        System.out.println(icon + " OR GROUP: " + groupName + " \u2014 " + status);
        System.out.println("   Any one of " + alternatives.size() + " alternatives suffices:");

        for (RuleResult r : alternatives) {
            String altIcon = r.isImplemented() ? "\u2713" : "\u2717";
            System.out.println("   " + altIcon + " " + r.getRule().getId() + ": " + r.getRule().getDescription());
            if (r.isImplemented()) {
                System.out.println("      Test: " + r.getMatchedTest());
                if (r.getBindings() != null) {
                    String bindStr = r.getBindings().entrySet().stream()
                            .map(e -> e.getKey() + "=" + e.getValue())
                            .collect(Collectors.joining(", "));
                    System.out.println("      Bindings: " + bindStr);
                }
                System.out.println("      \uD83D\uDD22 Implemented " + r.getAllMatchedTests().size() + " time(s)");
            }
        }
        System.out.println();
    }

    private static void printSingleResult(RuleResult r, String indent) {
        String parentLabel = r.getRule().getParent() != null ? " (edge case of " + r.getRule().getParent() + ")" : "";
        if (r.isImplemented()) {
            System.out.println(indent + "\u2705 IMPLEMENTED: " + r.getRule().getId() + parentLabel);
            System.out.println(indent + "   " + r.getRule().getDescription());
            System.out.println(indent + "   Test: " + r.getMatchedTest());
            if (r.getBindings() != null) {
                String bindStr = r.getBindings().entrySet().stream()
                        .map(e -> e.getKey() + "=" + e.getValue())
                        .collect(Collectors.joining(", "));
                System.out.println(indent + "   Bindings: " + bindStr);
            }
            System.out.println(indent + "   \u2713 " + r.getGivenMatched().size() + " given + " + r.getAssertMatched().size() + " assert matched");
            System.out.println(indent + "   \uD83D\uDD22 Implemented " + r.getAllMatchedTests().size() + " time(s) across tests:");
            for (String t : r.getAllMatchedTests()) {
                System.out.println(indent + "      \u2022 " + t);
            }
        } else {
            System.out.println(indent + "\u274C NOT IMPLEMENTED: " + r.getRule().getId() + parentLabel);
            System.out.println(indent + "   " + r.getRule().getDescription());
            if (!r.getGivenMissing().isEmpty()) {
                System.out.println(indent + "   Missing given:");
                for (String g : r.getGivenMissing()) System.out.println(indent + "      \u2022 " + g);
            }
            if (!r.getAssertMissing().isEmpty()) {
                System.out.println(indent + "   Missing assert:");
                for (String a : r.getAssertMissing()) System.out.println(indent + "      \u2022 " + a);
            }
        }
        System.out.println();
    }

    private static void printCategorySummary(List<RuleResult> results, String label,
                                             java.util.function.Predicate<RuleResult> predicate) {
        List<RuleResult> category = results.stream().filter(predicate).collect(Collectors.toList());
        if (category.isEmpty()) {
            System.out.println("   " + label + ": N/A (no rules)");
            return;
        }
        long impl = category.stream().filter(RuleResult::isImplemented).count();
        int pct = (int) Math.round(100.0 * impl / category.size());
        System.out.println("   " + label + ": " + impl + "/" + category.size() + " implemented (" + pct + "%)");
    }

    public static void printFacts(List<TestFacts> tests) {
        System.out.println("\n\u2500\u2500 Extracted facts (debug) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\n");
        for (TestFacts t : tests) {
            System.out.println("Test: " + t.getTestName());
            for (Fact f : t.getFacts()) {
                String desc;
                if (f instanceof CreatedFact c) {
                    desc = "  [" + c.getSequence() + "] Created " + c.getVariable() + ": " + c.getType() + "(" + String.join(", ", c.getArguments()) + ")";
                } else if (f instanceof CalledFact c) {
                    desc = "  [" + c.getSequence() + "] Called " + (c.getReceiver() != null ? c.getReceiver() : "(static)") + "." + c.getMethod() + "(" + String.join(", ", c.getArguments()) + ")"
                            + (c.getResultVariable() != null ? " \u2192 " + c.getResultVariable() : "")
                            + (c.getReceiverType() != null ? " [type: " + c.getReceiverType() + "]" : "")
                            + (c.getCallbackAssignTarget() != null ? " [cb\u2192" + c.getCallbackAssignTarget() + "]" : "")
                            + (c.getCallbackThrowType() != null ? " [throws:" + c.getCallbackThrowType() + "]" : "");
                } else if (f instanceof AssignedFact a) {
                    desc = "  [" + a.getSequence() + "] Assigned " + a.getTarget() + " = " + a.getSource()
                            + (a.getCastType() != null ? " as " + a.getCastType() : "");
                } else if (f instanceof MockSetupFact m) {
                    desc = "  [" + m.getSequence() + "] Mock " + m.getMockVariable() + "." + m.getMethod()
                            + " returns " + (m.getReturnValue() != null ? m.getReturnValue() : "throws " + m.getThrowsType());
                } else if (f instanceof AssertedFact a) {
                    desc = "  [" + a.getSequence() + "] Assert " + a.getSemanticMeaning() + "(" + String.join(", ", a.getArguments()) + ")";
                } else {
                    desc = f.toString();
                }
                System.out.println(desc);
            }
            if (!t.getVariableTypes().isEmpty()) {
                String types = t.getVariableTypes().entrySet().stream()
                        .map(e -> e.getKey() + ":" + e.getValue())
                        .collect(Collectors.joining(", "));
                System.out.println("  Variable types: " + types);
            }
            System.out.println();
        }
    }
}
