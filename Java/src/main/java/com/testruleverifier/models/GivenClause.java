package com.testruleverifier.models;

/** Base type for given clauses. */
public abstract class GivenClause {
    private boolean negated;

    public boolean isNegated() { return negated; }
    public void setNegated(boolean negated) { this.negated = negated; }
}
