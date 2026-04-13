package com.testruleverifier.models;

import java.util.List;

/** Assertion call with resolved semantic meaning. */
public final class AssertedFact extends Fact {
    private final String semanticMeaning;
    private List<String> arguments;

    public AssertedFact(int sequence, String semanticMeaning, List<String> arguments) {
        super(sequence);
        this.semanticMeaning = semanticMeaning;
        this.arguments = arguments;
    }

    public String getSemanticMeaning() { return semanticMeaning; }
    public List<String> getArguments() { return arguments; }
    public void setArguments(List<String> arguments) { this.arguments = arguments; }
}
