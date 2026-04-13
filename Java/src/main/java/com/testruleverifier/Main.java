package com.testruleverifier;

import com.testruleverifier.models.*;
import com.testruleverifier.services.*;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.List;
import java.util.stream.Collectors;

/**
 * Java Test Rule Verifier — Constraint Satisfaction Approach
 *
 * Usage: mvn exec:java -Dexec.args="<test-file.java> <rules.yaml> [--debug]"
 *
 * Extracts semantic facts from Java test methods using JavaParser AST,
 * then matches YAML rules via constraint satisfaction with variable bindings.
 */
public class Main {

    public static void main(String[] args) {
        System.exit(run(args));
    }

    public static int run(String[] args) {
        if (args.length < 2) {
            System.out.println("Usage: java -jar test-rule-verifier.jar <test-file.java> <rules.yaml> [--debug]");
            System.out.println();
            System.out.println("  test-file.java   Path to the Java unit test file");
            System.out.println("  rules.yaml       Path to the YAML rules file");
            System.out.println("  --debug          Show extracted facts per test (optional)");
            return 1;
        }

        String testFile = args[0];
        String rulesFile = args[1];
        boolean debug = Arrays.asList(args).contains("--debug");

        Path testPath = Path.of(testFile);
        Path rulesPath = Path.of(rulesFile);

        if (!Files.exists(testPath)) {
            System.err.println("Test file not found: " + testFile);
            return 1;
        }
        if (!Files.exists(rulesPath)) {
            System.err.println("Rules file not found: " + rulesFile);
            return 1;
        }

        try {
            // 1. Parse test file → extract facts
            String sourceCode = Files.readString(testPath);
            List<TestFacts> allTests = FactExtractor.extractAll(sourceCode);

            Reporter.printTestSummary(allTests);

            if (debug) {
                Reporter.printFacts(allTests);
            }

            // 2. Parse rules YAML
            String yamlContent = Files.readString(rulesPath);
            List<Rule> rules = RuleParser.parse(yamlContent);

            System.out.println("\n\uD83D\uDCCB Rules: " + rules.size() + "\n");

            // 3. Match each rule against all tests
            List<RuleResult> results = rules.stream()
                    .map(r -> Solver.matchAgainstAll(r, allTests))
                    .collect(Collectors.toList());

            // 4. Report
            Reporter.printResults(results);

            // Count implemented: OR groups count as 1 if any alternative matches
            var orGroups = results.stream()
                    .filter(r -> r.getRule().getOrGroup() != null)
                    .collect(Collectors.groupingBy(r -> r.getRule().getOrGroup()));
            long orGroupCount = orGroups.size();
            long orGroupImplemented = orGroups.values().stream()
                    .filter(g -> g.stream().anyMatch(RuleResult::isImplemented))
                    .count();
            var standalone = results.stream()
                    .filter(r -> r.getRule().getOrGroup() == null)
                    .collect(Collectors.toList());
            long totalLogical = standalone.size() + orGroupCount;
            long totalImplemented = standalone.stream().filter(RuleResult::isImplemented).count() + orGroupImplemented;

            return totalLogical > 0 && totalImplemented == totalLogical ? 0 : 1;

        } catch (IOException e) {
            System.err.println("Error reading files: " + e.getMessage());
            return 1;
        }
    }
}
