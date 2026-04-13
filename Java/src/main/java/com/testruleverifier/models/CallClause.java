package com.testruleverifier.models;

import java.util.ArrayList;
import java.util.List;

/** call: Method or call: $recv.Method($arg) or $result = call: Method on Type */
public class CallClause extends GivenClause {
    private String resultBindVar;
    private String receiverBindVar;
    private String onTypePattern;
    private String methodName = "";
    private List<String> argBindings = new ArrayList<>();
    private String callbackBindVar;
    private String throwsBindVar;

    public String getResultBindVar() { return resultBindVar; }
    public void setResultBindVar(String resultBindVar) { this.resultBindVar = resultBindVar; }
    public String getReceiverBindVar() { return receiverBindVar; }
    public void setReceiverBindVar(String receiverBindVar) { this.receiverBindVar = receiverBindVar; }
    public String getOnTypePattern() { return onTypePattern; }
    public void setOnTypePattern(String onTypePattern) { this.onTypePattern = onTypePattern; }
    public String getMethodName() { return methodName; }
    public void setMethodName(String methodName) { this.methodName = methodName; }
    public List<String> getArgBindings() { return argBindings; }
    public void setArgBindings(List<String> argBindings) { this.argBindings = argBindings; }
    public String getCallbackBindVar() { return callbackBindVar; }
    public void setCallbackBindVar(String callbackBindVar) { this.callbackBindVar = callbackBindVar; }
    public String getThrowsBindVar() { return throwsBindVar; }
    public void setThrowsBindVar(String throwsBindVar) { this.throwsBindVar = throwsBindVar; }
}
