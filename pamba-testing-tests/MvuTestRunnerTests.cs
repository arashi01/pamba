// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using Xunit;

namespace Pamba.Testing.Tests;

public sealed class MvuTestRunnerTests
{
  private sealed record TestState(int Count);

  private abstract record TestMsg
  {
    internal sealed record SetCount(int Value) : TestMsg;
    internal sealed record CountWasNegative : TestMsg;
  }

  private abstract record TestCmd
  {
    internal sealed record Persist(int Value) : TestCmd;
  }

  private sealed record TestSub(SubscriptionKey Key) : ISubscription<TestMsg>;

  private static readonly MvuProgram<TestState, TestMsg, TestCmd, TestSub> _program = new()
  {
    Init = () => (new TestState(0), []),
    Update = (msg, state) => msg switch
    {
      TestMsg.SetCount s => (new TestState(s.Value), [new TestCmd.Persist(s.Value)]),
      _ => (state, [])
    },
    Subscriptions = state => state.Count > 0
        ? [new TestSub(new SubscriptionKey("tick-timer"))]
        : [],
    OnCommandError = (_, ex) => throw new InvalidOperationException("Unexpected command error", ex),
    OnRuntimeError = err => throw new InvalidOperationException($"Unexpected runtime error: {err}"),
    Validate = state => state.Count >= 0
        ? new ValidationResult<TestState, TestMsg>.Valid(state)
        : new ValidationResult<TestState, TestMsg>.Invalid(new TestMsg.CountWasNegative())
  };

  [Fact]
  public void InitAndValidate_returns_initial_state_and_subscriptions()
  {
    TransitionResult<TestState, TestMsg, TestCmd, TestSub> result = MvuTestRunner.InitAndValidate(_program);

    Assert.Equal(0, result.State.Count);
    Assert.Empty(result.Commands);
    Assert.Empty(result.Subscriptions);
    Assert.Null(result.Message);
    Assert.Null(result.CorrectionMessage);
  }

  [Fact]
  public void UpdateAndValidate_returns_state_commands_and_subscriptions()
  {
    TestState initial = new(0);
    TransitionResult<TestState, TestMsg, TestCmd, TestSub> result =
        MvuTestRunner.UpdateAndValidate(_program, initial, new TestMsg.SetCount(5));

    Assert.Equal(5, result.State.Count);
    Assert.Single(result.Commands);
    Assert.IsType<TestCmd.Persist>(result.Commands[0]);
    Assert.Single(result.Subscriptions);
    Assert.Equal("tick-timer", result.Subscriptions[0].Key.Value);
    Assert.Null(result.CorrectionMessage);
  }

  [Fact]
  public void UpdateAndValidate_returns_correction_message_when_validation_rejects()
  {
    TestState initial = new(0);
    TransitionResult<TestState, TestMsg, TestCmd, TestSub> result =
        MvuTestRunner.UpdateAndValidate(_program, initial, new TestMsg.SetCount(-1));

    // Transition rejected: state reverts to initial, commands dropped
    Assert.Equal(0, result.State.Count);
    Assert.Empty(result.Commands);
    Assert.NotNull(result.CorrectionMessage);
    Assert.IsType<TestMsg.CountWasNegative>(result.CorrectionMessage);
  }
}
