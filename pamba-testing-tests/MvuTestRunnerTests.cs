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
  }

  private abstract record TestCmd
  {
    internal sealed record Persist(int Value) : TestCmd;
  }

  private sealed record TestSub(string Key) : ISubscription<TestMsg>;

  private static readonly MvuProgramme<TestState, TestMsg, TestCmd, TestSub> _programme = new()
  {
    Init = () => (new TestState(0), []),
    Update = (msg, state) => msg switch
    {
      TestMsg.SetCount s => (new TestState(s.Value), [new TestCmd.Persist(s.Value)]),
      _ => (state, [])
    },
    Subscriptions = state => state.Count > 0
        ? [new TestSub("tick-timer")]
        : [],
    OnCommandError = (_, ex) => throw new InvalidOperationException("Unexpected command error", ex),
    Validate = state => state.Count >= 0
        ? state
        : throw new InvalidOperationException($"Count must be non-negative: {state.Count}")
  };

  [Fact]
  public void InitAndValidate_returns_initial_state_and_subscriptions()
  {
    TransitionResult<TestState, TestCmd, TestSub> result = MvuTestRunner.InitAndValidate(_programme);

    Assert.Equal(0, result.State.Count);
    Assert.Empty(result.Commands);
    Assert.Empty(result.Subscriptions);
  }

  [Fact]
  public void UpdateAndValidate_returns_state_commands_and_subscriptions()
  {
    var initial = new TestState(0);
    TransitionResult<TestState, TestCmd, TestSub> result = MvuTestRunner.UpdateAndValidate(_programme, initial, new TestMsg.SetCount(5));

    Assert.Equal(5, result.State.Count);
    Assert.Single(result.Commands);
    Assert.IsType<TestCmd.Persist>(result.Commands[0]);
    Assert.Single(result.Subscriptions);
    Assert.Equal("tick-timer", result.Subscriptions[0].Key);
  }

  [Fact]
  public void UpdateAndValidate_throws_on_invariant_violation()
  {
    var initial = new TestState(0);

    Assert.Throws<InvalidOperationException>(() =>
        MvuTestRunner.UpdateAndValidate(_programme, initial, new TestMsg.SetCount(-1)));
  }
}
