package com.testruleverifier.models;

/** assign $target = $source: CastType */
public class AssignClause extends GivenClause {
    private String targetBindVar = "";
    private String sourceBindVar = "";
    private String castTypePattern = "";

    public String getTargetBindVar() { return targetBindVar; }
    public void setTargetBindVar(String targetBindVar) { this.targetBindVar = targetBindVar; }
    public String getSourceBindVar() { return sourceBindVar; }
    public void setSourceBindVar(String sourceBindVar) { this.sourceBindVar = sourceBindVar; }
    public String getCastTypePattern() { return castTypePattern; }
    public void setCastTypePattern(String castTypePattern) { this.castTypePattern = castTypePattern; }
}
