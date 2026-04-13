package com.testruleverifier.services;

import com.testruleverifier.models.*;
import org.yaml.snakeyaml.Yaml;

import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;

/**
 * Parses YAML rule files into Rule objects.
 *
 * YAML format:
 *   rules:
 *     - id: my_rule
 *       description: "..."
 *       given:
 *         - create $acc: Accessor($val)
 *         - call: $recv.SetValue($newVal) on IAccessorSetter<any>
 *         - assign $setter = $acc: IAccessorSetter<any>
 *         - mock: OtherFunc returns false
 *       assert:
 *         - IsNull: $acc.Value
 *         - AreEqual: [$a, $b]
 *         - Throws: any
 */
public class RuleParser {

    @SuppressWarnings("unchecked")
    public static List<Rule> parse(String yamlContent) {
        Yaml yaml = new Yaml();
        Map<String, Object> doc = yaml.load(yamlContent);
        List<Map<String, Object>> rulesData = (List<Map<String, Object>>) doc.get("rules");

        return rulesData.stream()
                .map(RuleParser::parseRule)
                .collect(Collectors.toList());
    }

    @SuppressWarnings("unchecked")
    private static Rule parseRule(Map<String, Object> yr) {
        Rule rule = new Rule();
        rule.setId((String) yr.getOrDefault("id", ""));
        rule.setParent((String) yr.get("parent"));
        rule.setOrGroup((String) yr.get("or_group"));
        rule.setDescription((String) yr.getOrDefault("description", ""));

        List<Map<String, Object>> givenData = (List<Map<String, Object>>) yr.get("given");
        if (givenData != null) {
            for (Map<String, Object> givenMap : givenData) {
                rule.getGiven().add(parseGivenClause(givenMap));
            }
        }

        List<Map<String, Object>> assertData = (List<Map<String, Object>>) yr.get("assert");
        if (assertData != null) {
            for (Map<String, Object> assertMap : assertData) {
                rule.getAssertClauses().add(parseAssertClause(assertMap));
            }
        }

        return rule;
    }

    // ── Given clause parsing ─────────────────────────────

    private static GivenClause parseGivenClause(Map<String, Object> map) {
        Map.Entry<String, Object> entry = map.entrySet().iterator().next();
        String key = entry.getKey();
        String val = entry.getValue() != null ? entry.getValue().toString() : "";

        boolean negated = false;
        if (key.toLowerCase().startsWith("not ")) {
            negated = true;
            key = key.substring(4);
        }

        GivenClause clause = parseGivenClauseCore(key, val);
        clause.setNegated(negated);
        return clause;
    }

    private static GivenClause parseGivenClauseCore(String key, String val) {
        // create $var: TypePattern($arg1, $arg2)
        Matcher createMatch = Pattern.compile("^create\\s+(\\$\\w+)$").matcher(key);
        if (createMatch.matches()) {
            return parseCreateClause(createMatch.group(1), val);
        }

        // $result = call: ...
        Matcher resultCallMatch = Pattern.compile("^(\\$\\w+)\\s*=\\s*call$").matcher(key);
        if (resultCallMatch.matches()) {
            return parseCallClause(val, resultCallMatch.group(1));
        }

        // call: ...
        if (key.equals("call")) {
            return parseCallClause(val, null);
        }

        // assign $target = $source: CastType
        Matcher assignMatch = Pattern.compile("^assign\\s+(\\$\\w+)\\s*=\\s*(\\$\\w+)$").matcher(key);
        if (assignMatch.matches()) {
            AssignClause clause = new AssignClause();
            clause.setTargetBindVar(assignMatch.group(1));
            clause.setSourceBindVar(assignMatch.group(2));
            clause.setCastTypePattern(val);
            return clause;
        }

        // mock: ...
        if (key.equals("mock")) {
            return parseMockClause(val);
        }

        throw new IllegalArgumentException("Unknown given clause key: '" + key + "'");
    }

    private static CreateClause parseCreateClause(String bindVar, String value) {
        Matcher match = Pattern.compile("^([\\w<>,\\s?]+?)(?:\\(([^)]*)\\))?$").matcher(value);
        if (!match.matches()) {
            throw new IllegalArgumentException("Cannot parse create value: '" + value + "'");
        }

        CreateClause clause = new CreateClause();
        clause.setBindVar(bindVar);
        clause.setTypePattern(match.group(1).trim());

        if (match.group(2) != null) {
            clause.setExactArgCount(true);
            String argStr = match.group(2).trim();
            if (!argStr.isEmpty()) {
                clause.setArgBindings(splitArgs(argStr));
            }
        }

        return clause;
    }

    private static CallClause parseCallClause(String value, String resultBindVar) {
        Matcher match = Pattern.compile(
                "^(?:(\\$\\w+)\\.)?(\\w+)(?:\\(([^)]*)\\))?\\s*(?:on\\s+(.+?))?\\s*(?:->\\s*(\\$\\w+))?\\s*(?:throws\\s+(\\$\\w+|\\w+))?$"
        ).matcher(value);

        if (!match.matches()) {
            throw new IllegalArgumentException("Cannot parse call value: '" + value + "'");
        }

        CallClause clause = new CallClause();
        clause.setResultBindVar(resultBindVar);
        clause.setMethodName(match.group(2));

        if (match.group(1) != null && !match.group(1).isEmpty()) {
            clause.setReceiverBindVar(match.group(1));
        }
        if (match.group(3) != null && !match.group(3).trim().isEmpty()) {
            clause.setArgBindings(splitArgs(match.group(3)));
        }
        if (match.group(4) != null && !match.group(4).isEmpty()) {
            clause.setOnTypePattern(match.group(4).trim());
        }
        if (match.group(5) != null && !match.group(5).isEmpty()) {
            clause.setCallbackBindVar(match.group(5));
        }
        if (match.group(6) != null && !match.group(6).isEmpty()) {
            clause.setThrowsBindVar(match.group(6));
        }

        return clause;
    }

    private static MockClause parseMockClause(String value) {
        Matcher match = Pattern.compile("^(?:(\\$\\w+)\\.)?(\\w+)\\s+returns\\s+(.+)$").matcher(value);
        if (!match.matches()) {
            throw new IllegalArgumentException("Cannot parse mock value: '" + value + "'");
        }

        MockClause clause = new MockClause();
        if (match.group(1) != null) {
            clause.setMockBindVar(match.group(1));
        }
        clause.setMethodName(match.group(2));
        clause.setReturnValue(match.group(3).trim());
        return clause;
    }

    // ── Assert clause parsing ────────────────────────────

    @SuppressWarnings("unchecked")
    private static AssertClause parseAssertClause(Map<String, Object> map) {
        Map.Entry<String, Object> entry = map.entrySet().iterator().next();
        String key = entry.getKey();
        Object value = entry.getValue();

        AssertClause clause = new AssertClause();
        clause.setSemanticMeaning(key);

        if (value instanceof List) {
            List<Object> list = (List<Object>) value;
            clause.setArgs(list.stream().map(o -> {
                if (o == null) return "null";
                String s = o.toString();
                if (s.isEmpty()) return "\"\"";
                return s;
            }).collect(Collectors.toList()));
        } else if (value != null) {
            String s = value.toString();
            if (s.isEmpty()) s = "\"\"";
            if (!s.equals("any")) {
                clause.setArgs(new ArrayList<>(List.of(s)));
            }
        }

        return clause;
    }

    // ── Utility ──────────────────────────────────────────

    private static List<String> splitArgs(String argStr) {
        return Arrays.stream(argStr.split(","))
                .map(String::trim)
                .filter(a -> !a.isEmpty())
                .collect(Collectors.toList());
    }
}
