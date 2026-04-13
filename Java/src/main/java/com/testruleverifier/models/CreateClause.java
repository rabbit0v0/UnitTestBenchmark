package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.List;

/** create $var: TypePattern or create $var: TypePattern($arg1, $arg2) */
public class CreateClause extends GivenClause {
    private String bindVar = "";
    private String typePattern = "";
    private List<String> argBindings = new ArrayList<>();
    private boolean exactArgCount;

    public String getBindVar() { return bindVar; }
    public void setBindVar(String bindVar) { this.bindVar = bindVar; }
    public String getTypePattern() { return typePattern; }
    public void setTypePattern(String typePattern) { this.typePattern = typePattern; }
    public List<String> getArgBindings() { return argBindings; }
    public void setArgBindings(List<String> argBindings) { this.argBindings = argBindings; }
    public boolean isExactArgCount() { return exactArgCount; }
    public void setExactArgCount(boolean exactArgCount) { this.exactArgCount = exactArgCount; }
}
