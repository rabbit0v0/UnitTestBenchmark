package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/** All facts extracted from a single test method. */
public class TestFacts {
    private String testName = "";
    private final List<Fact> facts = new ArrayList<>();
    private final Map<String, String> variableTypes = new HashMap<>();

    public String getTestName() { return testName; }
    public void setTestName(String testName) { this.testName = testName; }
    public List<Fact> getFacts() { return facts; }
    public Map<String, String> getVariableTypes() { return variableTypes; }
}
