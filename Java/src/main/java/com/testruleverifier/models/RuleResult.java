package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

public class RuleResult {
    private Rule rule;
    private boolean isImplemented;
    private String matchedTest;
    private Map<String, String> bindings;
    private List<String> givenMatched = new ArrayList<>();
    private List<String> givenMissing = new ArrayList<>();
    private List<String> assertMatched = new ArrayList<>();
    private List<String> assertMissing = new ArrayList<>();
    private List<String> allMatchedTests = new ArrayList<>();

    public Rule getRule() { return rule; }
    public void setRule(Rule rule) { this.rule = rule; }
    public boolean isImplemented() { return isImplemented; }
    public void setImplemented(boolean implemented) { isImplemented = implemented; }
    public String getMatchedTest() { return matchedTest; }
    public void setMatchedTest(String matchedTest) { this.matchedTest = matchedTest; }
    public Map<String, String> getBindings() { return bindings; }
    public void setBindings(Map<String, String> bindings) { this.bindings = bindings; }
    public List<String> getGivenMatched() { return givenMatched; }
    public void setGivenMatched(List<String> givenMatched) { this.givenMatched = givenMatched; }
    public List<String> getGivenMissing() { return givenMissing; }
    public void setGivenMissing(List<String> givenMissing) { this.givenMissing = givenMissing; }
    public List<String> getAssertMatched() { return assertMatched; }
    public void setAssertMatched(List<String> assertMatched) { this.assertMatched = assertMatched; }
    public List<String> getAssertMissing() { return assertMissing; }
    public void setAssertMissing(List<String> assertMissing) { this.assertMissing = assertMissing; }
    public List<String> getAllMatchedTests() { return allMatchedTests; }
    public void setAllMatchedTests(List<String> allMatchedTests) { this.allMatchedTests = allMatchedTests; }
}
