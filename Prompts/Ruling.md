# Ruling

You are a testing professional, you will be provided with a code file, a list of required tests to cover, and a HowTo file explaining how rules should be implemented. With the provided information, write a rules file implementing all the required tests for the code.

## The code file

The code file provides the code to be tested against. The file should provide information about how a rule can be logically implemented.

## The HowTo file

The HowTo file contains information about how rules should be implemented. Carefully read the document and follow the steps to write a rule file covering all the required tests.

## The required tests list

The required tests list is a list of test ids grouped by the method being tested. 
The test id implements the structure of "method name to be tested_condition or operation_assertion".
The test ids can be used as rule ids in the output rules file.

## Requirements

- Implement the required tests one by one as rules, no required tests should be missed out.
- Every rule should have enough details to make sure the condition/action is actually performed, and assertion is not vague, to avoid false positive. Try to avoid the use of "any" as much as possible.
- Consider different implementations of unit tests, cover all possible implementations for a rule in a or group, to avoid false negative.
- Output the rules into a yaml file under the same folder where code file is located.


## Example

rules:
  - id: no_value_is_null
    description: "Accessor created without value - Value should be null"
    given:
      - create $acc: Accessor()
    assert:
      - IsNull: $acc.Value

  - id: with_value_stores
    description: "Accessor created with value - Value should equal constructor arg"
    given:
      - create $acc: Accessor($val)
    assert:
      - AreEqual: [$acc.Value, $val]

  - id: with_value_stores_null
    parent: with_value_stores
    description: "Accessor created with value - Value should equal constructor arg"
    given:
      - create $acc: Accessor(null)
    assert:
      - AreEqual: [$acc.Value, null]

  - id: with_value_stores_empty
    parent: with_value_stores
    description: "Accessor created with value - Value should equal constructor arg"
    given:
      - create $acc: Accessor("")
    assert:
      - AreEqual: [$acc.Value, ""]

  - id: setvalue_updates
    description: "After SetValue via IAccessorSetter, Value should reflect the new value"
    given:
      - create $acc: Accessor
      - call: SetValue on IAccessorSetter<any>
    assert:
      - AreEqual: [$acc.Value, any]

  - id: setvalue_null_clears
    parent: setvalue_updates
    description: "SetValue(null) via IAccessorSetter clears value to null"
    given:
      - create $acc: Accessor($val)
      - call: SetValue on IAccessorSetter<any>
    assert:
      - IsNull: [$acc.Value]

  - id: onfirstset_immediate_1
    or_group: onfirstset_immediate
    description: "OnFirstSet when value already exists - callback fires immediately with value"
    given:
      - create $acc: Accessor($val)
      - call: OnFirstSet
    assert:
      - AreEqual: [any, $val]

  - id: onfirstset_immediate_2
    or_group: onfirstset_immediate
    description: "OnFirstSet when value already exists - callback fires immediately with value"
    given:
      - create $acc: Accessor
      - call: SetValue($val) on IAccessorSetter<any>
      - call: OnFirstSet
    assert:
      - AreEqual: [any, $val]

  - id: no_value_no_set_remains_null
    description: "Accessor created without value and SetValue NOT called - Value stays null"
    given:
      - create $acc: Accessor()
      - not call: SetValue on IAccessorSetter<any>
    assert:
      - IsNull: $acc.Value

  - id: onfirstset_setvalue_order
    description: "Order of OnFirstSet and SetValue calls - callback should fire on first SetValue regardless of order"
    given:
      - create $acc: Accessor
      - call: SetValue($val) on IAccessorSetter<any>
      - call: OnFirstSet

  - id: onfirstset_only_first_1
    description: "OnFirstSet should only fire on first SetValue, not subsequent ones"
    or_group: onfirstset_only_first
    given:
      - create $acc: Accessor()
      - call: OnFirstSet
      - call: SetValue on IAccessorSetter<any>
      - call: SetValue on IAccessorSetter<any>

  - id: onfirstset_only_first_2
    description: "OnFirstSet should only fire on first SetValue, not subsequent ones"
    or_group: onfirstset_only_first
    given:
      - create $acc: Accessor()
      - call: SetValue on IAccessorSetter<any>
      - call: SetValue on IAccessorSetter<any>
      - call: OnFirstSet

  - id: onfirstset_only_first_3
    description: "OnFirstSet should only fire on first SetValue, not subsequent ones"
    or_group: onfirstset_only_first
    given:
      - create $acc: Accessor()
      - call: SetValue on IAccessorSetter<any>
      - call: OnFirstSet
      - call: SetValue on IAccessorSetter<any>

  - id: onfirstset_only_first_4
    description: "OnFirstSet should only fire on first SetValue, not subsequent ones"
    or_group: onfirstset_only_first
    given:
      - create $acc: Accessor($val)
      - call: SetValue on IAccessorSetter<any>
      - call: OnFirstSet

  - id: onfirstset_only_first_5
    description: "OnFirstSet should only fire on first SetValue, not subsequent ones"
    or_group: onfirstset_only_first
    given:
      - create $acc: Accessor($val)
      - call: OnFirstSet
      - call: SetValue on IAccessorSetter<any>

  - id: getvalueorthrow_returns
    description: "GetValueOrThrow returns the stored value when value is set"
    given:
      - create $acc: Accessor($val)
      - call: $acc.GetValueOrThrow
    assert:
      - AreEqual: [any, $val]

