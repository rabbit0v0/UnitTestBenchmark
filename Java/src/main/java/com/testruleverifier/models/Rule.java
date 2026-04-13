package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.List;

public class Rule {
    private String id = "";
    private String parent;
    private String orGroup;
    private String description = "";
    private List<GivenClause> given = new ArrayList<>();
    private List<AssertClause> assertClauses = new ArrayList<>();

    public String getId() { return id; }
    public void setId(String id) { this.id = id; }
    public String getParent() { return parent; }
    public void setParent(String parent) { this.parent = parent; }
    public String getOrGroup() { return orGroup; }
    public void setOrGroup(String orGroup) { this.orGroup = orGroup; }
    public String getDescription() { return description; }
    public void setDescription(String description) { this.description = description; }
    public List<GivenClause> getGiven() { return given; }
    public void setGiven(List<GivenClause> given) { this.given = given; }
    public List<AssertClause> getAssertClauses() { return assertClauses; }
    public void setAssertClauses(List<AssertClause> assertClauses) { this.assertClauses = assertClauses; }
}
