// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Omex.Extensions.Abstractions.Accessors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Omex.Extensions.Abstractions.UnitTests.Accessors
{
	[TestClass]
	public class AccessorTests
	{
		private const string TestValue = "TestValue";
		private const string AnotherTestValue = "AnotherValue";

		#region Constructor Tests

		[TestMethod]
		public void Constructor_WithoutValue_InitializesWithNull()
		{
			// Arrange & Act
			var accessor = new Accessor<string>();

			// Assert
			Assert.IsNull(accessor.Value);
		}

		[TestMethod]
		public void Constructor_WithValue_StoresValue()
		{
			// Arrange & Act
			var accessor = new Accessor<string>(TestValue);

			// Assert
			Assert.AreEqual(TestValue, accessor.Value);
		}

		[TestMethod]
		[DataRow(null)]
		[DataRow("")]
		[DataRow("TestValue")]
		public void Constructor_WithVariousValues_StoresCorrectly(string? value)
		{
			// Arrange & Act
			var accessor = new Accessor<string?>(value);

			// Assert
			Assert.AreEqual(value, accessor.Value);
		}

		#endregion

		#region Value Property Tests

		[TestMethod]
		public void Value_WhenNotSet_ReturnsNull()
		{
			// Arrange
			var accessor = new Accessor<string>();

			// Act
			var value = accessor.Value;

			// Assert
			Assert.IsNull(value);
		}

		[TestMethod]
		public void Value_AfterInitialization_ReturnsInitialValue()
		{
			// Arrange & Act
			var accessor = new Accessor<string>(TestValue);

			// Assert
			Assert.AreEqual(TestValue, accessor.Value);
		}

		[TestMethod]
		public void Value_AfterSetValue_ReturnsUpdatedValue()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var setter = (IAccessorSetter<string>)accessor;

			// Act
			setter.SetValue(AnotherTestValue);

			// Assert
			Assert.AreEqual(AnotherTestValue, accessor.Value);
		}

		#endregion

		#region GetValueOrThrow Tests

		[TestMethod]
		public void GetValueOrThrow_WhenValueIsSet_ReturnsValue()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);

			// Act
			var value = accessor.GetValueOrThrow();

			// Assert
			Assert.AreEqual(TestValue, value);
		}

		[TestMethod]
		public void GetValueOrThrow_WhenValueIsNull_ThrowsInvalidOperationException()
		{
			// Arrange
			var accessor = new Accessor<string>();

			// Act & Assert
			var exception = Assert.ThrowsException<InvalidOperationException>(() => accessor.GetValueOrThrow());
			Assert.IsTrue(exception.Message.Contains(typeof(string).Name));
		}

		[TestMethod]
		public void GetValueOrThrow_AfterSetValue_ReturnsUpdatedValue()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var setter = (IAccessorSetter<string>)accessor;

			// Act
			setter.SetValue(AnotherTestValue);
			var value = accessor.GetValueOrThrow();

			// Assert
			Assert.AreEqual(AnotherTestValue, value);
		}

		#endregion

		#region OnFirstSet Callback Tests

		[TestMethod]
		public void OnFirstSet_WhenValueIsAlreadySet_ExecutesCallbackImmediately()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var callbackExecuted = false;
			string? receivedValue = null;

			// Act
			accessor.OnFirstSet(value =>
			{
				callbackExecuted = true;
				receivedValue = value;
			});

			// Assert
			Assert.IsTrue(callbackExecuted);
			Assert.AreEqual(TestValue, receivedValue);
		}

		[TestMethod]
		public void OnFirstSet_WhenValueIsNotSet_StoresCallbackForLaterExecution()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var callbackExecuted = false;

			// Act
			accessor.OnFirstSet(_ =>
			{
				callbackExecuted = true;
			});

			// Assert
			Assert.IsFalse(callbackExecuted);
		}

		[TestMethod]
		public void OnFirstSet_CallbackStoredThenSetValue_ExecutesCallback()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var callbackExecuted = false;
			string? receivedValue = null;
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(value =>
			{
				callbackExecuted = true;
				receivedValue = value;
			});

			// Act
			setter.SetValue(TestValue);

			// Assert
			Assert.IsTrue(callbackExecuted);
			Assert.AreEqual(TestValue, receivedValue);
		}

		[TestMethod]
		public void OnFirstSet_MultipleCallbacks_AllExecuteWhenValueIsSet()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var executionOrder = new List<int>();
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => executionOrder.Add(1));
			accessor.OnFirstSet(_ => executionOrder.Add(2));
			accessor.OnFirstSet(_ => executionOrder.Add(3));

			// Act
			setter.SetValue(TestValue);

			// Assert
			Assert.AreEqual(3, executionOrder.Count);
			CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, executionOrder);
		}

		[TestMethod]
		public void OnFirstSet_MultipleCallbacks_ReceiveCorrectValue()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var receivedValues = new List<string?>();
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(value => receivedValues.Add(value));
			accessor.OnFirstSet(value => receivedValues.Add(value));

			// Act
			setter.SetValue(TestValue);

			// Assert
			Assert.AreEqual(2, receivedValues.Count);
			Assert.AreEqual(TestValue, receivedValues[0]);
			Assert.AreEqual(TestValue, receivedValues[1]);
		}

		[TestMethod]
		public void OnFirstSet_AfterSetValue_NewCallbacksExecuteImmediately()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var firstCallbackExecuted = false;
			var secondCallbackExecuted = false;
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => firstCallbackExecuted = true);
			setter.SetValue(AnotherTestValue);

			// Act
			accessor.OnFirstSet(_ => secondCallbackExecuted = true);

			// Assert
			Assert.IsTrue(firstCallbackExecuted);
			Assert.IsTrue(secondCallbackExecuted);
		}

		#endregion

		#region SetValue Tests

		[TestMethod]
		public void SetValue_WithNullValue_SetsValueToNull()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var setter = (IAccessorSetter<string>)accessor;

			// Act
			setter.SetValue(null!);

			// Assert
			Assert.IsNull(accessor.Value);
		}

		[TestMethod]
		public void SetValue_WithNewValue_UpdatesValue()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var setter = (IAccessorSetter<string>)accessor;

			// Act
			setter.SetValue(AnotherTestValue);

			// Assert
			Assert.AreEqual(AnotherTestValue, accessor.Value);
		}

		[TestMethod]
		public void SetValue_ClearsCallbackListAfterExecution()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var callbackExecutionCount = 0;
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => callbackExecutionCount++);

			// Act
			setter.SetValue(TestValue);
			setter.SetValue(AnotherTestValue);

			// Assert - callback should only execute once
			Assert.AreEqual(1, callbackExecutionCount);
		}

		[TestMethod]
		public void SetValue_MultipleTimesBeforeOnFirstSet_LastValueIsUsed()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var setter = (IAccessorSetter<string>)accessor;
			var receivedValue = string.Empty;

			// Act
			setter.SetValue(TestValue);
			setter.SetValue(AnotherTestValue);

			accessor.OnFirstSet(value => receivedValue = value);

			// Assert
			Assert.AreEqual(AnotherTestValue, receivedValue);
		}

		#endregion

		#region Integration Scenario Tests

		[TestMethod]
		public void Scenario_MultipleOnFirstSetThenSetValue_AllCallbacksExecute()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var executionCount = 0;
			var setter = (IAccessorSetter<string>)accessor;

			// Act
			for (int i = 0; i < 5; i++)
			{
				accessor.OnFirstSet(_ => executionCount++);
			}

			setter.SetValue(TestValue);

			// Assert
			Assert.AreEqual(5, executionCount);
		}

		[TestMethod]
		public void Scenario_SetValueBeforeAnyOnFirstSet_SubsequentCallbacksExecuteImmediately()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var setter = (IAccessorSetter<string>)accessor;
			var callbackExecuted = false;

			// Act
			setter.SetValue(TestValue);
			accessor.OnFirstSet(_ => callbackExecuted = true);

			// Assert
			Assert.IsTrue(callbackExecuted);
			Assert.AreEqual(TestValue, accessor.Value);
		}

		[TestMethod]
		public void Scenario_SetValueAfterPartialOnFirstSetRegistration_RemainingCallbacksDoNotGetStored()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var firstCallbackExecuted = false;
			var secondCallbackExecuted = false;
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => firstCallbackExecuted = true);

			// Act
			setter.SetValue(TestValue);
			accessor.OnFirstSet(_ => secondCallbackExecuted = true);

			// Assert
			Assert.IsTrue(firstCallbackExecuted);
			Assert.IsTrue(secondCallbackExecuted);
		}

		[TestMethod]
		public void Scenario_ValueChangedMultipleTimes_OnlyFirstSetCallbacksExecuteOnce()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var executionCount = 0;
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => executionCount++);

			// Act
			setter.SetValue(TestValue);
			setter.SetValue(AnotherTestValue);
			setter.SetValue("ThirdValue");

			// Assert - callback executes only once
			Assert.AreEqual(1, executionCount);
		}

		[TestMethod]
		public void Scenario_MixedCallbacksWithInitialValue_ImmediateAndStoredCallbacksWork()
		{
			// Arrange
			var accessor = new Accessor<string>(TestValue);
			var executionOrder = new List<string>();
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => executionOrder.Add("Immediate1"));

			// Act
			setter.SetValue(AnotherTestValue);
			accessor.OnFirstSet(_ => executionOrder.Add("Immediate2"));

			// Assert
			Assert.AreEqual(2, executionOrder.Count);
			CollectionAssert.AreEqual(new List<string> { "Immediate1", "Immediate2" }, executionOrder);
		}

		#endregion

		#region Weak Reference Tests

		[TestMethod]
		public void OnFirstSet_WeakReferencesAllowGarbageCollection()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var setter = (IAccessorSetter<string>)accessor;
			var executedCallbacks = 0;

			CreateCallbackAndLetItBeCollected(accessor, ref executedCallbacks);
			GC.Collect();
			GC.WaitForPendingFinalizers();

			// Act
			setter.SetValue(TestValue);

			// Assert
			// If weak references work correctly, the collected callback should not execute
			Assert.AreEqual(0, executedCallbacks);
		}

		[TestMethod]
		public void OnFirstSet_SkipsCollectedCallbacksAndExecutesAliveOnes()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var setter = (IAccessorSetter<string>)accessor;
			var aliveCallbackExecuted = false;

			CreateCallbackAndLetItBeCollected(accessor, ref var _);

			accessor.OnFirstSet(_ => aliveCallbackExecuted = true);
			GC.Collect();
			GC.WaitForPendingFinalizers();

			// Act
			setter.SetValue(TestValue);

			// Assert
			Assert.IsTrue(aliveCallbackExecuted);
		}

		#endregion

		#region Helper Methods

		private static void CreateCallbackAndLetItBeCollected(Accessor<string> accessor, ref int executionCount)
		{
			Action<string> action = _ => executionCount++;
			accessor.OnFirstSet(action);
			// action goes out of scope here
		}

		#endregion

		#region Edge Case Tests

		[TestMethod]
		[ExpectedException(typeof(InvalidOperationException))]
		public void GetValueOrThrow_WithDifferentGenericType_ThrowsWithCorrectTypeName()
		{
			// Arrange
			var accessor = new Accessor<MyTestClass>();

			// Act
			accessor.GetValueOrThrow();
		}

		[TestMethod]
		public void OnFirstSet_WithCallbackThrowingException_ExceptionPropagates()
		{
			// Arrange
			var accessor = new Accessor<string>();
			var setter = (IAccessorSetter<string>)accessor;

			accessor.OnFirstSet(_ => throw new InvalidOperationException("Test exception"));

			// Act & Assert
			var exception = Assert.ThrowsException<InvalidOperationException>(() => setter.SetValue(TestValue));
			Assert.AreEqual("Test exception", exception.Message);
		}

		[TestMethod]
		public void Constructor_MultipleAccessorsOfDifferentTypes_WorkIndependently()
		{
			// Arrange & Act
			var stringAccessor = new Accessor<string>(TestValue);
			var intAccessor = new Accessor<MyTestClass>(new MyTestClass { Value = 42 });

			// Assert
			Assert.AreEqual(TestValue, stringAccessor.Value);
			Assert.IsNotNull(intAccessor.Value);
			Assert.AreEqual(42, intAccessor.Value.Value);
		}

		#endregion

		private class MyTestClass
		{
			public int Value { get; set; }
		}
	}
}