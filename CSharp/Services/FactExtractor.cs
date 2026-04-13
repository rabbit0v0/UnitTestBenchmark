using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Extracts semantic facts from C# test methods using Roslyn AST.
/// Produces a list of Facts per test method: Created, Called, Assigned, MockSetup, Asserted.
/// </summary>
static class FactExtractor
{
    public static List<TestFacts> ExtractAll(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();
        var results = new List<TestFacts>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!IsTestMethod(method)) continue;

            var dataRows = ExtractParameterizedRows(method);
            if (dataRows.Count > 0)
            {
                var paramNames = method.ParameterList.Parameters
                    .Select(p => p.Identifier.Text).ToList();
                foreach (var row in dataRows)
                {
                    var baseFacts = ExtractFromMethod(method);
                    var label = string.Join(", ", row);
                    baseFacts.TestName = $"{method.Identifier.Text}({label})";
                    SubstituteParameters(baseFacts, paramNames, row);
                    results.Add(baseFacts);
                }
            }
            else
            {
                results.Add(ExtractFromMethod(method));
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts argument rows from [DataRow], [InlineData], or [TestCase] attributes.
    /// </summary>
    static List<List<string>> ExtractParameterizedRows(MethodDeclarationSyntax method)
    {
        var rows = new List<List<string>>();
        foreach (var attr in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            var name = attr.Name.ToString();
            if (name is not ("DataRow" or "DataRowAttribute"
                          or "InlineData" or "InlineDataAttribute"
                          or "TestCase" or "TestCaseAttribute"))
                continue;

            if (attr.ArgumentList == null) { rows.Add(new List<string>()); continue; }
            var args = attr.ArgumentList.Arguments
                .Select(a => a.Expression.ToString())
                .ToList();
            rows.Add(args);
        }
        return rows;
    }

    /// <summary>
    /// Replaces occurrences of method parameter names with literal values from a data row
    /// across all facts in a TestFacts instance.
    /// </summary>
    static void SubstituteParameters(TestFacts tf, List<string> paramNames, List<string> values)
    {
        var map = new Dictionary<string, string>();
        for (int i = 0; i < paramNames.Count && i < values.Count; i++)
            map[paramNames[i]] = values[i];

        for (int i = 0; i < tf.Facts.Count; i++)
        {
            tf.Facts[i] = tf.Facts[i] switch
            {
                CreatedFact cf => cf with { Arguments = SubList(cf.Arguments, map) },
                CalledFact cl => cl with { Arguments = SubList(cl.Arguments, map) },
                AssertedFact af => af with { Arguments = SubList(af.Arguments, map) },
                _ => tf.Facts[i]
            };
        }
    }

    static List<string> SubList(List<string> args, Dictionary<string, string> map)
    {
        return args.Select(a => map.TryGetValue(a, out var v) ? v : a).ToList();
    }

    static TestFacts ExtractFromMethod(MethodDeclarationSyntax method)
    {
        var tf = new TestFacts { TestName = method.Identifier.Text };
        int seq = 0;

        // [ExpectedException(typeof(T))] attribute → Throws assertion
        TryExtractExpectedExceptionAttribute(method, tf, ref seq);

        if (method.Body != null)
        {
            foreach (var stmt in method.Body.Statements)
                ExtractFromStatement(stmt, tf, ref seq);
        }

        return tf;
    }

    /// <summary>
    /// Extracts [ExpectedException(typeof(T))] attribute as a Throws assertion fact.
    /// </summary>
    static void TryExtractExpectedExceptionAttribute(MethodDeclarationSyntax method, TestFacts tf, ref int seq)
    {
        foreach (var attr in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            var name = attr.Name.ToString();
            if (name is not ("ExpectedException" or "ExpectedExceptionAttribute")) continue;

            // Extract typeof(T) argument
            if (attr.ArgumentList == null) continue;
            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.Expression is TypeOfExpressionSyntax typeOf)
                {
                    var exType = typeOf.Type.ToString();
                    tf.Facts.Add(new AssertedFact(seq++, "Throws", new List<string> { exType }));
                    return;
                }
            }
        }
    }

    // ── Statement-level extraction ───────────────────────

    static void ExtractFromStatement(StatementSyntax stmt, TestFacts tf, ref int seq)
    {
        if (stmt is LocalDeclarationStatementSyntax localDecl)
        {
            var declaredType = localDecl.Declaration.Type.ToString();

            foreach (var declarator in localDecl.Declaration.Variables)
            {
                var varName = declarator.Identifier.Text;
                var init = declarator.Initializer?.Value;
                if (init == null) continue;

                // Record variable type
                tf.VariableTypes[varName] = declaredType;

                // new Type(args) or new() with declared type
                if (init is ObjectCreationExpressionSyntax objCreate)
                {
                    var typeName = objCreate.Type.ToString();
                    var args = ExtractArgStrings(objCreate.ArgumentList);
                    tf.Facts.Add(new CreatedFact(seq++, varName, typeName, args));
                }
                else if (init is ImplicitObjectCreationExpressionSyntax implCreate)
                {
                    var typeName = declaredType;
                    var args = ExtractArgStrings(implCreate.ArgumentList);
                    tf.Facts.Add(new CreatedFact(seq++, varName, typeName, args));
                }
                // (CastType)source  assignment
                else if (init is CastExpressionSyntax cast)
                {
                    var castType = cast.Type.ToString();
                    var source = cast.Expression.ToString();
                    tf.VariableTypes[varName] = castType;
                    tf.Facts.Add(new AssignedFact(seq++, varName, source, castType));
                }
                // Method call assigned to variable: var result = obj.Method(args)
                else if (init is InvocationExpressionSyntax inv)
                {
                    // Check for assertion pattern first (e.g. var ex = Assert.ThrowsException<T>(...))
                    if (!TryExtractAssertion(inv, tf, ref seq))
                        ExtractCallFact(inv, varName, tf, ref seq);
                }
                // Simple assignment: IType x = otherVar
                else if (init is IdentifierNameSyntax id)
                {
                    var source = id.ToString();
                    var castType = declaredType.Equals("var", StringComparison.OrdinalIgnoreCase) ? null : declaredType;
                    tf.Facts.Add(new AssignedFact(seq++, varName, source, castType));
                }
                else
                {
                    // Store variable value as-is for expression-level extraction
                    tf.VariableTypes[varName] = declaredType;
                }
            }
        }
        else if (stmt is ExpressionStatementSyntax exprStmt)
        {
            ExtractFromExpression(exprStmt.Expression, null, tf, ref seq);
        }
    }

    // ── Expression-level extraction ──────────────────────

    static void ExtractFromExpression(ExpressionSyntax expr, string? assignedTo, TestFacts tf, ref int seq)
    {
        if (expr is InvocationExpressionSyntax invocation)
        {
            // Check if this is an assertion first
            if (TryExtractAssertion(invocation, tf, ref seq))
                return;

            // Check for Moq Setup chain: mock.Setup(...).Returns/Throws(...)
            if (TryExtractMoqSetup(invocation, tf, ref seq))
                return;

            // Regular method call
            ExtractCallFact(invocation, assignedTo, tf, ref seq);
        }
        else if (expr is AssignmentExpressionSyntax assignment)
        {
            var target = assignment.Left.ToString();
            var right = assignment.Right;

            if (right is InvocationExpressionSyntax rhsInvoke)
                ExtractFromExpression(rhsInvoke, target, tf, ref seq);
            else if (right is ObjectCreationExpressionSyntax rhsCreate)
            {
                var args = ExtractArgStrings(rhsCreate.ArgumentList);
                tf.Facts.Add(new CreatedFact(seq++, target, rhsCreate.Type.ToString(), args));
            }
        }
    }

    static void ExtractCallFact(InvocationExpressionSyntax invocation, string? resultVar, TestFacts tf, ref int seq)
    {
        string? receiver = null;
        string? receiverType = null;
        string method;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            method = memberAccess.Name.ToString();

            // Handle inline cast-as-receiver: ((IType)var).Method(...)
            if (memberAccess.Expression is ParenthesizedExpressionSyntax paren &&
                paren.Expression is CastExpressionSyntax castExpr)
            {
                receiver = castExpr.Expression.ToString();
                receiverType = castExpr.Type.ToString();
            }
            else if (memberAccess.Expression is CastExpressionSyntax directCast)
            {
                receiver = directCast.Expression.ToString();
                receiverType = directCast.Type.ToString();
            }
            else
            {
                receiver = memberAccess.Expression.ToString();
                receiverType = ResolveType(receiver, tf);
            }
        }
        else if (invocation.Expression is IdentifierNameSyntax idName)
        {
            method = idName.ToString();
        }
        else
        {
            return; // can't parse this call form
        }

        var args = ExtractArgStrings(invocation.ArgumentList);
        var callbackTarget = ExtractCallbackAssignTarget(invocation);
        var callbackThrowType = ExtractCallbackThrowType(invocation);

        tf.Facts.Add(new CalledFact(seq++, receiver, receiverType, method, args, resultVar, callbackTarget, callbackThrowType));
    }

    // ── Assertion extraction ─────────────────────────────

    static bool TryExtractAssertion(InvocationExpressionSyntax invocation, TestFacts tf, ref int seq)
    {
        string? methodName = null;
        string? receiver = null;

        if (invocation.Expression is MemberAccessExpressionSyntax ma)
        {
            methodName = ma.Name is GenericNameSyntax gn ? gn.Identifier.ToString() : ma.Name.ToString();
            receiver = ma.Expression.ToString();
        }
        else if (invocation.Expression is IdentifierNameSyntax id)
        {
            methodName = id.ToString();
        }

        if (methodName == null) return false;

        // Detect Assert.Throws<T>(), Assert.ThrowsException<T>(), Assert.ThrowsAsync<T>(), Assert.ThrowsAny<T>()
        if (methodName is "Throws" or "ThrowsException" or "ThrowsAsync" or "ThrowsAny")
        {
            var args = ExtractArgStrings(invocation.ArgumentList);
            // Get generic type if present: Assert.Throws<T>(...)
            if (invocation.Expression is MemberAccessExpressionSyntax ma2 &&
                ma2.Name is GenericNameSyntax generic)
            {
                var exType = generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "any";
                args.Insert(0, exType);
            }
            tf.Facts.Add(new AssertedFact(seq++, "Throws", args));
            return true;
        }

        // Detect NUnit Assert.That(() => expr, Throws.TypeOf<T>()) or Throws.InstanceOf<T>()
        if (methodName == "That" && receiver != null && receiver.EndsWith("Assert"))
        {
            if (TryExtractNUnitThrowsConstraint(invocation, tf, ref seq))
                return true;
        }

        // Detect standard assertion patterns
        var semantic = DetermineSemanticMeaning(methodName, invocation.ArgumentList);
        if (semantic != null)
        {
            var args = ExtractArgStrings(invocation.ArgumentList);
            tf.Facts.Add(new AssertedFact(seq++, semantic, args));
            return true;
        }

        // Check fluent assertion chains: x.Should().BeNull(), etc.
        if (IsFluentAssertionMethod(methodName))
        {
            var semantic2 = FluentToSemantic(methodName);
            if (semantic2 != null)
            {
                var args = new List<string>();
                // The subject is the receiver chain before .Should()
                if (invocation.Expression is MemberAccessExpressionSyntax chain)
                {
                    var subject = ExtractFluentSubject(chain);
                    if (subject != null) args.Add(subject);
                }
                args.AddRange(ExtractArgStrings(invocation.ArgumentList));
                tf.Facts.Add(new AssertedFact(seq++, semantic2, args));
                return true;
            }
        }

        return false;
    }

    static string? DetermineSemanticMeaning(string methodName, ArgumentListSyntax? argList)
    {
        var lower = methodName.ToLowerInvariant();
        var args = ExtractArgStrings(argList);
        var hasNull = args.Any(a => a == "null" || a == "null!");

        // One-arg null checks
        if (lower.Contains("null") && !lower.Contains("notnull"))
            return "IsNull";
        if (lower.Contains("notnull"))
            return "IsNotNull";

        // Equal with null → IsNull
        if (lower.Contains("equal") && !lower.Contains("notequal") && hasNull)
            return "IsNull";

        // Boolean checks
        if (lower.Contains("true") && !lower.Contains("false"))
            return "IsTrue";
        if (lower.Contains("false"))
            return "IsFalse";

        // NotEqual before Equal (since "notequal" contains "equal")
        if (lower.Contains("notequal") || lower.Contains("not_equal"))
            return "AreNotEqual";

        // Equality
        if (lower.Contains("equal") || lower.Contains("same"))
            return "AreEqual";

        // Known assertion receiver patterns
        if (lower == "ok" || lower == "pass" || lower == "passes")
            return "IsTrue";
        if (lower == "fail" || lower == "fails")
            return "IsFalse";

        // Contains
        if (lower.Contains("contain") || lower.Contains("match"))
            return "Contains";

        // Check if it's called on "Assert" — anything on Assert is an assertion
        return null;
    }

    // ── Moq extraction ───────────────────────────────────

    static bool TryExtractMoqSetup(InvocationExpressionSyntax invocation, TestFacts tf, ref int seq)
    {
        // Pattern: mock.Setup(m => m.Method(...)).Returns(value)
        //      or: mock.Setup(m => m.Method(...)).Throws<T>()
        if (invocation.Expression is not MemberAccessExpressionSyntax chainAccess)
            return false;

        var chainMethod = chainAccess.Name.ToString();
        if (chainMethod != "Returns" && chainMethod != "ReturnsAsync" &&
            chainMethod != "Throws" && chainMethod != "ThrowsAsync")
            return false;

        // The inner call should be .Setup(...)
        if (chainAccess.Expression is not InvocationExpressionSyntax setupCall)
            return false;
        if (setupCall.Expression is not MemberAccessExpressionSyntax setupAccess)
            return false;
        if (setupAccess.Name.ToString() != "Setup" && setupAccess.Name.ToString() != "SetupGet")
            return false;

        var mockVar = setupAccess.Expression.ToString();

        // Extract the method name from the lambda: m => m.MethodName(...)
        string? mockedMethod = ExtractMoqLambdaMethod(setupCall.ArgumentList);
        if (mockedMethod == null) return false;

        string? returnValue = null;
        string? throwsType = null;

        if (chainMethod == "Returns" || chainMethod == "ReturnsAsync")
        {
            var retArgs = ExtractArgStrings(invocation.ArgumentList);
            returnValue = retArgs.FirstOrDefault() ?? "null";
        }
        else // Throws
        {
            if (chainAccess.Name is GenericNameSyntax generic)
                throwsType = generic.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
            else
                throwsType = ExtractArgStrings(invocation.ArgumentList).FirstOrDefault();
        }

        tf.Facts.Add(new MockSetupFact(seq++, mockVar, mockedMethod, returnValue, throwsType));
        return true;
    }

    static string? ExtractMoqLambdaMethod(ArgumentListSyntax? argList)
    {
        if (argList == null) return null;
        var firstArg = argList.Arguments.FirstOrDefault()?.Expression;
        if (firstArg is SimpleLambdaExpressionSyntax lambda)
        {
            // m => m.MethodName(...) or m => m.Property
            if (lambda.Body is InvocationExpressionSyntax lambdaInvoke &&
                lambdaInvoke.Expression is MemberAccessExpressionSyntax lambdaMember)
                return lambdaMember.Name.ToString();

            if (lambda.Body is MemberAccessExpressionSyntax memberExpr)
                return memberExpr.Name.ToString();
        }
        return null;
    }

    // ── Helpers ──────────────────────────────────────────

    static string? ResolveType(string varName, TestFacts tf)
    {
        if (tf.VariableTypes.TryGetValue(varName, out var t))
        {
            if (!t.Equals("var", StringComparison.OrdinalIgnoreCase))
                return t;
        }
        // Check if there's an AssignedFact with a cast type for this var
        for (int i = tf.Facts.Count - 1; i >= 0; i--)
        {
            if (tf.Facts[i] is AssignedFact af && af.Target == varName && af.CastType != null)
                return af.CastType;
        }
        // Check if there's a CreatedFact for this var
        for (int i = tf.Facts.Count - 1; i >= 0; i--)
        {
            if (tf.Facts[i] is CreatedFact cf && cf.Variable == varName)
                return cf.Type;
        }
        return null;
    }

    static string? ExtractCallbackAssignTarget(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList?.Arguments ?? Enumerable.Empty<ArgumentSyntax>())
        {
            SyntaxNode? body = arg.Expression switch
            {
                SimpleLambdaExpressionSyntax s => s.Body,
                ParenthesizedLambdaExpressionSyntax p => p.Body,
                _ => null
            };
            if (body == null) continue;

            // Expression body: c => x = c
            if (body is AssignmentExpressionSyntax assign)
                return assign.Left.ToString();

            // Block body: c => { x = true; y = c; }
            if (body is BlockSyntax block)
            {
                foreach (var s in block.Statements)
                {
                    if (s is ExpressionStatementSyntax es &&
                        es.Expression is AssignmentExpressionSyntax blockAssign)
                        return blockAssign.Left.ToString();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Extract the exception type from a throw expression inside a lambda argument.
    /// e.g. OnFirstSet(_ => throw new InvalidOperationException("msg")) → "InvalidOperationException"
    /// Also handles: Setup(m => m.Method()).Throws&lt;T&gt;() patterns via block bodies.
    /// </summary>
    static string? ExtractCallbackThrowType(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList?.Arguments ?? Enumerable.Empty<ArgumentSyntax>())
        {
            SyntaxNode? body = arg.Expression switch
            {
                SimpleLambdaExpressionSyntax s => s.Body,
                ParenthesizedLambdaExpressionSyntax p => p.Body,
                _ => null
            };
            if (body == null) continue;

            // Expression body: _ => throw new ExceptionType(...)
            if (body is ThrowExpressionSyntax throwExpr &&
                throwExpr.Expression is ObjectCreationExpressionSyntax throwCreate)
                return throwCreate.Type.ToString();

            // Block body: _ => { throw new ExceptionType(...); }
            if (body is BlockSyntax block)
            {
                foreach (var s in block.Statements)
                {
                    if (s is ThrowStatementSyntax throwStmt &&
                        throwStmt.Expression is ObjectCreationExpressionSyntax blockThrowCreate)
                        return blockThrowCreate.Type.ToString();
                }
            }
        }
        return null;
    }

    static List<string> ExtractArgStrings(ArgumentListSyntax? argList)
    {
        if (argList == null) return new();
        return argList.Arguments.Select(a => a.Expression.ToString()).ToList();
    }

    static bool IsTestMethod(MethodDeclarationSyntax method)
    {
        return method.AttributeLists.SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name is "Fact" or "Test" or "TestMethod" or "Theory"
                    or "Xunit.Fact" or "NUnit.Framework.Test" or "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod";
            });
    }

    /// <summary>
    /// Handles NUnit Assert.That(() => expr, Throws.TypeOf&lt;T&gt;()) pattern.
    /// </summary>
    static bool TryExtractNUnitThrowsConstraint(InvocationExpressionSyntax invocation, TestFacts tf, ref int seq)
    {
        var argList = invocation.ArgumentList;
        if (argList == null || argList.Arguments.Count < 2) return false;

        var constraintArg = argList.Arguments[1].Expression;
        var constraintText = constraintArg.ToString();

        // Match patterns: Throws.TypeOf<T>(), Throws.InstanceOf<T>(), Throws.Exception.TypeOf<T>()
        if (!constraintText.Contains("Throws")) return false;

        // Extract exception type from generic invocation in the constraint
        string? exType = ExtractGenericTypeFromConstraint(constraintArg);

        var args = new List<string>();
        if (exType != null) args.Add(exType);

        // Include the lambda/delegate arg as well
        var lambdaArg = argList.Arguments[0].Expression.ToString();
        args.Add(lambdaArg);

        tf.Facts.Add(new AssertedFact(seq++, "Throws", args));
        return true;
    }

    /// <summary>
    /// Walks an expression tree to find a generic invocation like TypeOf&lt;T&gt;() or InstanceOf&lt;T&gt;().
    /// </summary>
    static string? ExtractGenericTypeFromConstraint(ExpressionSyntax expr)
    {
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is GenericNameSyntax gn &&
                gn.Identifier.Text is "TypeOf" or "InstanceOf")
            {
                return gn.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
            }
        }
        return null;
    }

    static bool IsFluentAssertionMethod(string name)
    {
        return name is "BeNull" or "NotBeNull" or "Be" or "NotBe" or "BeTrue" or "BeFalse"
            or "Contain" or "NotContain" or "BeEquivalentTo" or "BeGreaterThan" or "BeLessThan";
    }

    static string? FluentToSemantic(string name) => name switch
    {
        "BeNull" => "IsNull",
        "NotBeNull" => "IsNotNull",
        "Be" or "BeEquivalentTo" => "AreEqual",
        "NotBe" => "AreNotEqual",
        "BeTrue" => "IsTrue",
        "BeFalse" => "IsFalse",
        "Contain" => "Contains",
        _ => null
    };

    static string? ExtractFluentSubject(MemberAccessExpressionSyntax chain)
    {
        // Walk backward through .Should().Be() → find subject before .Should()
        if (chain.Expression is InvocationExpressionSyntax shouldCall &&
            shouldCall.Expression is MemberAccessExpressionSyntax shouldAccess &&
            shouldAccess.Name.ToString() == "Should")
        {
            return shouldAccess.Expression.ToString();
        }
        return chain.Expression.ToString();
    }
}
