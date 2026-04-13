package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.List;

/**
 * Base type for all extracted facts. Sequence preserves source order.
 */
public abstract sealed class Fact permits CreatedFact, CalledFact, AssignedFact, MockSetupFact, AssertedFact {
    private final int sequence;

    protected Fact(int sequence) {
        this.sequence = sequence;
    }

    public int getSequence() {
        return sequence;
    }
}
