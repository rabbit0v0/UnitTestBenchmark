package com.testruleverifier.models;

import java.util.List;

/** Object creation: new TypeName(args) assigned to a variable. */
public final class CreatedFact extends Fact {
    private final String variable;
    private final String type;
    private List<String> arguments;

    public CreatedFact(int sequence, String variable, String type, List<String> arguments) {
        super(sequence);
        this.variable = variable;
        this.type = type;
        this.arguments = arguments;
    }

    public String getVariable() { return variable; }
    public String getType() { return type; }
    public List<String> getArguments() { return arguments; }
    public void setArguments(List<String> arguments) { this.arguments = arguments; }
}
