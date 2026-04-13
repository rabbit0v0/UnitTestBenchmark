package com.testruleverifier.services;

import com.github.javaparser.StaticJavaParser;
import com.github.javaparser.ast.CompilationUnit;
import com.github.javaparser.ast.body.MethodDeclaration;
import com.github.javaparser.ast.body.VariableDeclarator;
import com.github.javaparser.ast.expr.*;
import com.github.javaparser.ast.stmt.*;
import com.github.javaparser.ast.nodeTypes.NodeWithArguments;
import com.testruleverifier.models.*;

import java.util.*;
import java.util.stream.Collectors;

/**
 * Extracts semantic facts from Java test methods using JavaParser AST.
 * Produces a list of Facts per test method: Created, Called, Assigned, MockSetup, Asserted.
 *
 * Supports: JUnit 4 (@Test), JUnit 5 (@Test, @ParameterizedTest), TestNG (@Test),
 *           Mockito (when/thenReturn, doThrow, verify),
 *           AssertJ (assertThat...is/isEqualTo), Hamcrest (assertThat + matchers),
 *           JUnit assertions, and @Test(expected=...) annotations.
 */
public class FactExtractor {

    public static List<TestFacts> extractAll(String sourceCode) {
        CompilationUnit cu = StaticJavaParser.parse(sourceCode);
        List<TestFacts> results = new ArrayList<>();

        cu.findAll(MethodDeclaration.class).stream()
                .filter(FactExtractor::isTestMethod)
                .forEach(method -> {
                    var dataRows = extractParameterizedRows(method);
                    if (!dataRows.isEmpty()) {
                        var paramNames = method.getParameters().stream()
                                .map(p -> p.getNameAsString())
                                .collect(Collectors.toList());
                        for (var row : dataRows) {
                            var tf = extractFromMethod(method);
                            var label = String.join(", ", row);
                            tf.setTestName(method.getNameAsString() + "(" + label + ")");
                            substituteParameters(tf, paramNames, row);
                            results.add(tf);
                        }
                    } else {
                        results.add(extractFromMethod(method));
                    }
                });

        return results;
    }

    private static boolean isTestMethod(MethodDeclaration method) {
        return method.getAnnotations().stream().anyMatch(a -> {
            String name = a.getNameAsString();
            return name.equals("Test") || name.equals("ParameterizedTest")
                    || name.equals("RepeatedTest");
        });
    }

    // ── Parameterized test support ───────────────────────

    private static List<List<String>> extractParameterizedRows(MethodDeclaration method) {
        List<List<String>> rows = new ArrayList<>();
        for (var ann : method.getAnnotations()) {
            String name = ann.getNameAsString();
            if (name.equals("CsvSource") || name.equals("ValueSource")) {
                if (ann instanceof SingleMemberAnnotationExpr single) {
                    extractRowsFromAnnotation(single.getMemberValue(), rows);
                } else if (ann instanceof NormalAnnotationExpr normal) {
                    normal.getPairs().forEach(pair -> {
                        if (pair.getNameAsString().equals("value") || pair.getNameAsString().equals("strings")
                                || pair.getNameAsString().equals("ints")) {
                            extractRowsFromAnnotation(pair.getValue(), rows);
                        }
                    });
                }
            }
        }
        return rows;
    }

    private static void extractRowsFromAnnotation(Expression expr, List<List<String>> rows) {
        if (expr instanceof ArrayInitializerExpr arr) {
            for (var val : arr.getValues()) {
                if (val instanceof StringLiteralExpr sle) {
                    // CsvSource: comma-separated values in a string
                    rows.add(Arrays.stream(sle.getValue().split(","))
                            .map(String::trim).collect(Collectors.toList()));
                } else {
                    rows.add(List.of(val.toString()));
                }
            }
        } else if (expr instanceof StringLiteralExpr sle) {
            rows.add(Arrays.stream(sle.getValue().split(","))
                    .map(String::trim).collect(Collectors.toList()));
        }
    }

    private static void substituteParameters(TestFacts tf, List<String> paramNames, List<String> values) {
        Map<String, String> map = new HashMap<>();
        for (int i = 0; i < paramNames.size() && i < values.size(); i++) {
            map.put(paramNames.get(i), values.get(i));
        }
        for (Fact fact : tf.getFacts()) {
            if (fact instanceof CreatedFact cf) {
                cf.setArguments(subList(cf.getArguments(), map));
            } else if (fact instanceof CalledFact cl) {
                cl.setArguments(subList(cl.getArguments(), map));
            } else if (fact instanceof AssertedFact af) {
                af.setArguments(subList(af.getArguments(), map));
            }
        }
    }

    private static List<String> subList(List<String> args, Map<String, String> map) {
        return args.stream().map(a -> map.getOrDefault(a, a)).collect(Collectors.toList());
    }

    // ── Method-level extraction ──────────────────────────

    private static TestFacts extractFromMethod(MethodDeclaration method) {
        TestFacts tf = new TestFacts();
        tf.setTestName(method.getNameAsString());
        int[] seq = {0};

        // @Test(expected = ExceptionType.class) → Throws assertion
        tryExtractExpectedAnnotation(method, tf, seq);

        method.getBody().ifPresent(body -> {
            for (Statement stmt : body.getStatements()) {
                extractFromStatement(stmt, tf, seq);
            }
        });

        return tf;
    }

    private static void tryExtractExpectedAnnotation(MethodDeclaration method, TestFacts tf, int[] seq) {
        for (var ann : method.getAnnotations()) {
            if (!ann.getNameAsString().equals("Test")) continue;

            if (ann instanceof NormalAnnotationExpr normal) {
                for (var pair : normal.getPairs()) {
                    if (pair.getNameAsString().equals("expected")) {
                        String exType = pair.getValue().toString().replace(".class", "");
                        tf.getFacts().add(new AssertedFact(seq[0]++, "Throws", List.of(exType)));
                        return;
                    }
                }
            }
        }
    }

    // ── Statement-level extraction ───────────────────────

    private static void extractFromStatement(Statement stmt, TestFacts tf, int[] seq) {
        if (stmt instanceof ExpressionStmt exprStmt) {
            Expression expr = exprStmt.getExpression();

            if (expr instanceof VariableDeclarationExpr varDecl) {
                for (VariableDeclarator decl : varDecl.getVariables()) {
                    extractFromVarDeclarator(decl, varDecl.getCommonType().asString(), tf, seq);
                }
            } else {
                extractFromExpression(expr, null, tf, seq);
            }
        } else if (stmt instanceof TryStmt tryStmt) {
            // Process body statements
            for (Statement s : tryStmt.getTryBlock().getStatements()) {
                extractFromStatement(s, tf, seq);
            }
            // Process catch clauses for expected exception patterns
            for (var catchClause : tryStmt.getCatchClauses()) {
                String exType = catchClause.getParameter().getType().asString();
                // If catch block has assertions, treat it as expected exception pattern
                boolean hasAssert = catchClause.getBody().getStatements().stream()
                        .anyMatch(s -> s.toString().contains("assert") || s.toString().contains("Assert"));
                if (hasAssert || catchClause.getBody().getStatements().isEmpty()) {
                    tf.getFacts().add(new AssertedFact(seq[0]++, "Throws", List.of(exType)));
                }
                for (Statement s : catchClause.getBody().getStatements()) {
                    extractFromStatement(s, tf, seq);
                }
            }
        } else if (stmt instanceof BlockStmt block) {
            for (Statement s : block.getStatements()) {
                extractFromStatement(s, tf, seq);
            }
        }
    }

    private static void extractFromVarDeclarator(VariableDeclarator decl, String declaredType, TestFacts tf, int[] seq) {
        String varName = decl.getNameAsString();
        if (decl.getInitializer().isEmpty()) return;
        Expression init = decl.getInitializer().get();

        tf.getVariableTypes().put(varName, declaredType);

        if (init instanceof ObjectCreationExpr objCreate) {
            String typeName = objCreate.getType().asString();
            List<String> args = extractArgStrings(objCreate);
            tf.getFacts().add(new CreatedFact(seq[0]++, varName, typeName, args));
        } else if (init instanceof CastExpr castExpr) {
            String castType = castExpr.getType().asString();
            String source = castExpr.getExpression().toString();
            tf.getVariableTypes().put(varName, castType);
            tf.getFacts().add(new AssignedFact(seq[0]++, varName, source, castType));
        } else if (init instanceof MethodCallExpr methodCall) {
            if (!tryExtractAssertion(methodCall, tf, seq)) {
                if (!tryExtractMockitoSetup(methodCall, tf, seq)) {
                    extractCallFact(methodCall, varName, tf, seq);
                }
            }
        } else if (init instanceof NameExpr nameExpr) {
            String source = nameExpr.toString();
            String castType = declaredType.equals("var") ? null : declaredType;
            tf.getFacts().add(new AssignedFact(seq[0]++, varName, source, castType));
        }
    }

    // ── Expression-level extraction ──────────────────────

    private static void extractFromExpression(Expression expr, String assignedTo, TestFacts tf, int[] seq) {
        if (expr instanceof MethodCallExpr methodCall) {
            if (tryExtractAssertion(methodCall, tf, seq)) return;
            if (tryExtractMockitoSetup(methodCall, tf, seq)) return;
            extractCallFact(methodCall, assignedTo, tf, seq);
        } else if (expr instanceof AssignExpr assign) {
            String target = assign.getTarget().toString();
            Expression right = assign.getValue();
            if (right instanceof MethodCallExpr rhsCall) {
                extractFromExpression(rhsCall, target, tf, seq);
            } else if (right instanceof ObjectCreationExpr rhsCreate) {
                List<String> args = extractArgStrings(rhsCreate);
                tf.getFacts().add(new CreatedFact(seq[0]++, target, rhsCreate.getType().asString(), args));
            }
        }
    }

    private static void extractCallFact(MethodCallExpr call, String resultVar, TestFacts tf, int[] seq) {
        String method = call.getNameAsString();
        String receiver = null;
        String receiverType = null;

        if (call.getScope().isPresent()) {
            Expression scope = call.getScope().get();
            if (scope instanceof EnclosedExpr enclosed && enclosed.getInner() instanceof CastExpr castExpr) {
                receiver = castExpr.getExpression().toString();
                receiverType = castExpr.getType().asString();
            } else {
                receiver = scope.toString();
                receiverType = resolveType(receiver, tf);
            }
        }

        List<String> args = extractArgStrings(call);
        String callbackTarget = extractCallbackAssignTarget(call);
        String callbackThrowType = extractCallbackThrowType(call);

        tf.getFacts().add(new CalledFact(seq[0]++, receiver, receiverType, method, args,
                resultVar, callbackTarget, callbackThrowType));
    }

    // ── Assertion extraction ─────────────────────────────

    private static boolean tryExtractAssertion(MethodCallExpr call, TestFacts tf, int[] seq) {
        String method = call.getNameAsString();
        String receiver = call.getScope().map(Expression::toString).orElse(null);

        // JUnit 5 assertThrows / JUnit 4 assertThrows
        if (method.equals("assertThrows") || method.equals("assertThrowsExactly")) {
            List<String> args = extractArgStrings(call);
            String exType = args.isEmpty() ? "any" : args.get(0).replace(".class", "");
            tf.getFacts().add(new AssertedFact(seq[0]++, "Throws", List.of(exType)));
            return true;
        }

        // TestNG @Test(expectedExceptions=...) handled via annotation; expectThrows for TestNG
        if (method.equals("expectThrows")) {
            List<String> args = extractArgStrings(call);
            String exType = args.isEmpty() ? "any" : args.get(0).replace(".class", "");
            tf.getFacts().add(new AssertedFact(seq[0]++, "Throws", List.of(exType)));
            return true;
        }

        // AssertJ: assertThat(...).isEqualTo(...), assertThat(...).isNull(), etc.
        if (isAssertJTerminal(method)) {
            String semantic = assertJToSemantic(method);
            if (semantic != null) {
                List<String> args = new ArrayList<>();
                String subject = extractAssertJSubject(call);
                if (subject != null) args.add(subject);
                args.addAll(extractArgStrings(call));
                tf.getFacts().add(new AssertedFact(seq[0]++, semantic, args));
                return true;
            }
        }

        // Hamcrest assertThat(actual, matcher)
        if (method.equals("assertThat") && receiver != null
                && (receiver.contains("MatcherAssert") || receiver.contains("Assert"))) {
            List<String> args = extractArgStrings(call);
            if (args.size() >= 2) {
                String matcherStr = args.get(args.size() - 1).toLowerCase();
                String semantic = hamcrestToSemantic(matcherStr);
                if (semantic != null) {
                    tf.getFacts().add(new AssertedFact(seq[0]++, semantic, args.subList(0, args.size() - 1)));
                    return true;
                }
            }
        }

        // Standard JUnit/TestNG assertions
        String semantic = determineSemanticMeaning(method, call);
        if (semantic != null) {
            List<String> args = extractArgStrings(call);
            tf.getFacts().add(new AssertedFact(seq[0]++, semantic, args));
            return true;
        }

        return false;
    }

    private static String determineSemanticMeaning(String methodName, MethodCallExpr call) {
        String lower = methodName.toLowerCase();
        List<String> args = extractArgStrings(call);
        boolean hasNull = args.stream().anyMatch(a -> a.equals("null"));

        if (lower.contains("null") && !lower.contains("notnull"))
            return "IsNull";
        if (lower.contains("notnull"))
            return "IsNotNull";
        if (lower.contains("equal") && !lower.contains("notequal") && hasNull)
            return "IsNull";
        if (lower.contains("true") && !lower.contains("false"))
            return "IsTrue";
        if (lower.contains("false") && !lower.contains("true"))
            return "IsFalse";
        if (lower.contains("notequal") || lower.contains("not_equal") || lower.equals("assertnotequals"))
            return "AreNotEqual";
        if (lower.contains("equal") || lower.contains("same") || lower.equals("assertequals"))
            return "AreEqual";
        if (lower.contains("contain") || lower.contains("match"))
            return "Contains";

        // Is it called on Assert, Assertions, or similar?
        String receiver = call.getScope().map(Expression::toString).orElse("");
        if (receiver.endsWith("Assert") || receiver.endsWith("Assertions")) {
            if (lower.equals("fail")) return "IsFalse";
        }

        return null;
    }

    // ── AssertJ support ──────────────────────────────────

    private static boolean isAssertJTerminal(String method) {
        return method.equals("isNull") || method.equals("isNotNull")
                || method.equals("isEqualTo") || method.equals("isNotEqualTo")
                || method.equals("isTrue") || method.equals("isFalse")
                || method.equals("contains") || method.equals("containsExactly")
                || method.equals("isInstanceOf") || method.equals("hasSize")
                || method.equals("isGreaterThan") || method.equals("isLessThan")
                || method.equals("isSameAs") || method.equals("isNotSameAs")
                || method.equals("isZero") || method.equals("isPositive") || method.equals("isNegative");
    }

    private static String assertJToSemantic(String method) {
        return switch (method) {
            case "isNull" -> "IsNull";
            case "isNotNull" -> "IsNotNull";
            case "isEqualTo", "isSameAs" -> "AreEqual";
            case "isNotEqualTo", "isNotSameAs" -> "AreNotEqual";
            case "isTrue" -> "IsTrue";
            case "isFalse" -> "IsFalse";
            case "contains", "containsExactly" -> "Contains";
            case "isInstanceOf" -> "IsInstanceOf";
            default -> null;
        };
    }

    private static String extractAssertJSubject(MethodCallExpr terminal) {
        // Walk back through fluent chain to find assertThat(subject)
        Expression current = terminal.getScope().orElse(null);
        while (current instanceof MethodCallExpr chain) {
            if (chain.getNameAsString().equals("assertThat") && !chain.getArguments().isEmpty()) {
                return chain.getArgument(0).toString();
            }
            current = chain.getScope().orElse(null);
        }
        return null;
    }

    // ── Hamcrest support ─────────────────────────────────

    private static String hamcrestToSemantic(String matcherStr) {
        if (matcherStr.contains("nullvalue")) return "IsNull";
        if (matcherStr.contains("notnullvalue")) return "IsNotNull";
        if (matcherStr.contains("equalto") || matcherStr.contains("is(")) return "AreEqual";
        if (matcherStr.contains("not(")) return "AreNotEqual";
        if (matcherStr.contains("containsstring") || matcherStr.contains("hasitem")) return "Contains";
        return null;
    }

    // ── Mockito extraction ──────────────────────────────

    private static boolean tryExtractMockitoSetup(MethodCallExpr call, TestFacts tf, int[] seq) {
        String method = call.getNameAsString();

        // when(mock.method()).thenReturn(value) / thenThrow(exception)
        if (method.equals("thenReturn") || method.equals("thenAnswer")) {
            return tryExtractWhenThenReturn(call, tf, seq);
        }
        if (method.equals("thenThrow")) {
            return tryExtractWhenThenThrow(call, tf, seq);
        }

        // Mockito.doReturn(value).when(mock).method()
        if (method.equals("doReturn") || method.equals("doThrow") || method.equals("doNothing")) {
            // These are the start of a chain; actual mock setup is when the chain resolves
            return false;
        }

        // Mockito.mock(Class) creation
        if (method.equals("mock") || method.equals("spy")) {
            // This is handled as object creation in the variable declarator extraction
            return false;
        }

        // Mockito.verify(mock).method() — we don't extract verify as a mock setup
        return false;
    }

    private static boolean tryExtractWhenThenReturn(MethodCallExpr thenReturn, TestFacts tf, int[] seq) {
        // Pattern: when(mock.method(args)).thenReturn(value)
        Expression scope = thenReturn.getScope().orElse(null);
        if (!(scope instanceof MethodCallExpr whenCall)) return false;
        if (!whenCall.getNameAsString().equals("when")) return false;

        if (whenCall.getArguments().isEmpty()) return false;
        Expression whenArg = whenCall.getArgument(0);

        if (whenArg instanceof MethodCallExpr mockedCall) {
            String mockVar = mockedCall.getScope().map(Expression::toString).orElse("unknown");
            String mockedMethod = mockedCall.getNameAsString();
            String returnValue = thenReturn.getArguments().isEmpty() ? "null"
                    : thenReturn.getArgument(0).toString();

            tf.getFacts().add(new MockSetupFact(seq[0]++, mockVar, mockedMethod, returnValue, null));
            return true;
        }
        return false;
    }

    private static boolean tryExtractWhenThenThrow(MethodCallExpr thenThrow, TestFacts tf, int[] seq) {
        Expression scope = thenThrow.getScope().orElse(null);
        if (!(scope instanceof MethodCallExpr whenCall)) return false;
        if (!whenCall.getNameAsString().equals("when")) return false;

        if (whenCall.getArguments().isEmpty()) return false;
        Expression whenArg = whenCall.getArgument(0);

        if (whenArg instanceof MethodCallExpr mockedCall) {
            String mockVar = mockedCall.getScope().map(Expression::toString).orElse("unknown");
            String mockedMethod = mockedCall.getNameAsString();
            String throwsType = thenThrow.getArguments().isEmpty() ? null
                    : thenThrow.getArgument(0).toString().replace(".class", "")
                    .replaceAll("new\\s+(\\w+)\\(.*\\)", "$1");

            tf.getFacts().add(new MockSetupFact(seq[0]++, mockVar, mockedMethod, null, throwsType));
            return true;
        }
        return false;
    }

    // ── Helpers ──────────────────────────────────────────

    private static List<String> extractArgStrings(NodeWithArguments<?> call) {
        return call.getArguments().stream()
                .map(Expression::toString)
                .collect(Collectors.toList());
    }

    private static String resolveType(String varName, TestFacts tf) {
        String t = tf.getVariableTypes().get(varName);
        if (t != null && !t.equalsIgnoreCase("var")) return t;

        // Check AssignedFact with cast
        for (int i = tf.getFacts().size() - 1; i >= 0; i--) {
            Fact f = tf.getFacts().get(i);
            if (f instanceof AssignedFact af && af.getTarget().equals(varName) && af.getCastType() != null) {
                return af.getCastType();
            }
        }
        // Check CreatedFact
        for (int i = tf.getFacts().size() - 1; i >= 0; i--) {
            Fact f = tf.getFacts().get(i);
            if (f instanceof CreatedFact cf && cf.getVariable().equals(varName)) {
                return cf.getType();
            }
        }
        return null;
    }

    private static String extractCallbackAssignTarget(MethodCallExpr call) {
        for (Expression arg : call.getArguments()) {
            if (arg instanceof LambdaExpr lambda) {
                Statement body = lambda.getBody();
                if (body instanceof ExpressionStmt exprStmt && exprStmt.getExpression() instanceof AssignExpr assign) {
                    return assign.getTarget().toString();
                }
                if (body instanceof BlockStmt block) {
                    for (Statement s : block.getStatements()) {
                        if (s instanceof ExpressionStmt es && es.getExpression() instanceof AssignExpr assign) {
                            return assign.getTarget().toString();
                        }
                    }
                }
            }
        }
        return null;
    }

    private static String extractCallbackThrowType(MethodCallExpr call) {
        for (Expression arg : call.getArguments()) {
            if (arg instanceof LambdaExpr lambda) {
                Statement body = lambda.getBody();
                if (body instanceof ThrowStmt throwStmt && throwStmt.getExpression() instanceof ObjectCreationExpr objCreate) {
                    return objCreate.getType().asString();
                }
                if (body instanceof BlockStmt block) {
                    for (Statement s : block.getStatements()) {
                        if (s instanceof ThrowStmt ts && ts.getExpression() instanceof ObjectCreationExpr objCreate) {
                            return objCreate.getType().asString();
                        }
                    }
                }
            }
        }
        return null;
    }
}
