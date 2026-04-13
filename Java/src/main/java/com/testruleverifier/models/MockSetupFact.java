package com.testruleverifier.models;

/** Mock setup: when(mock.method()).thenReturn(value) or Mockito.when/doThrow patterns. */
public final class MockSetupFact extends Fact {
    private final String mockVariable;
    private final String method;
    private final String returnValue;
    private final String throwsType;

    public MockSetupFact(int sequence, String mockVariable, String method, String returnValue, String throwsType) {
        super(sequence);
        this.mockVariable = mockVariable;
        this.method = method;
        this.returnValue = returnValue;
        this.throwsType = throwsType;
    }

    public String getMockVariable() { return mockVariable; }
    public String getMethod() { return method; }
    public String getReturnValue() { return returnValue; }
    public String getThrowsType() { return throwsType; }
}
