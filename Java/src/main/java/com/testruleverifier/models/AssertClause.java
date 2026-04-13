package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.List;

public class AssertClause {
    private String semanticMeaning = "";
    private List<String> args = new ArrayList<>();

    public String getSemanticMeaning() { return semanticMeaning; }
    public void setSemanticMeaning(String semanticMeaning) { this.semanticMeaning = semanticMeaning; }
    public List<String> getArgs() { return args; }
    public void setArgs(List<String> args) { this.args = args; }
}
