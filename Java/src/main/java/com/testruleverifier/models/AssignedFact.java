package com.testruleverifier.models;

/** Assignment: target = source, optionally with a cast or interface type. */
public final class AssignedFact extends Fact {
    private final String target;
    private final String source;
    private final String castType;

    public AssignedFact(int sequence, String target, String source, String castType) {
        super(sequence);
        this.target = target;
        this.source = source;
        this.castType = castType;
    }

    public String getTarget() { return target; }
    public String getSource() { return source; }
    public String getCastType() { return castType; }
}
