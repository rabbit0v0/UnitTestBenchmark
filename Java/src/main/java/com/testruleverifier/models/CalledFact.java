package com.testruleverifier.models;

import java.util.List;

/** Method call: receiver.Method(args), optionally assigned to a result variable. */
public final class CalledFact extends Fact {
    private final String receiver;
    private final String receiverType;
    private final String method;
    private List<String> arguments;
    private final String resultVariable;
    private final String callbackAssignTarget;
    private final String callbackThrowType;

    public CalledFact(int sequence, String receiver, String receiverType, String method,
                      List<String> arguments, String resultVariable,
                      String callbackAssignTarget, String callbackThrowType) {
        super(sequence);
        this.receiver = receiver;
        this.receiverType = receiverType;
        this.method = method;
        this.arguments = arguments;
        this.resultVariable = resultVariable;
        this.callbackAssignTarget = callbackAssignTarget;
        this.callbackThrowType = callbackThrowType;
    }

    public String getReceiver() { return receiver; }
    public String getReceiverType() { return receiverType; }
    public String getMethod() { return method; }
    public List<String> getArguments() { return arguments; }
    public void setArguments(List<String> arguments) { this.arguments = arguments; }
    public String getResultVariable() { return resultVariable; }
    public String getCallbackAssignTarget() { return callbackAssignTarget; }
    public String getCallbackThrowType() { return callbackThrowType; }
}
