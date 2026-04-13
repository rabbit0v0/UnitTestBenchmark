package com.testruleverifier.models;

/** mock: Method returns value or mock: $mock.Method returns value */
public class MockClause extends GivenClause {
    private String mockBindVar;
    private String methodName = "";
    private String returnValue = "";

    public String getMockBindVar() { return mockBindVar; }
    public void setMockBindVar(String mockBindVar) { this.mockBindVar = mockBindVar; }
    public String getMethodName() { return methodName; }
    public void setMethodName(String methodName) { this.methodName = methodName; }
    public String getReturnValue() { return returnValue; }
    public void setReturnValue(String returnValue) { this.returnValue = returnValue; }
}
