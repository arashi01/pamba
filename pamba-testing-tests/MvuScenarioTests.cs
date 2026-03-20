// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using Xunit;

namespace Pamba.Testing.Tests;

public sealed class MvuScenarioTests
{
  private sealed record TestState(int Count, bool IsRunning);

  private abstract record TestMsg
  {
    internal sealed record Increment : TestMsg;
    internal sealed record Start : TestMsg;
    internal sealed record Stop : TestMsg;
  }

  private abstract record TestCmd;

  private sealed record TestSub(SubscriptionKey Key) : ISubscription<TestMsg>;

  private static readonly MvuProgram<TestState, TestMsg, TestCmd, TestSub> _program = new()
  {
    Init = () => (new TestState(0, false), []),
    Update = (msg, state) => msg switch
    {
      TestMsg.Increment => (state with { Count = state.Count + 1 }, []),
      TestMsg.Start => (state with { IsRunning = true }, []),
      TestMsg.Stop => (state with { IsRunning = false }, []),
      _ => (state, [])
    },
    Subscriptions = state => state.IsRunning
        ? [new TestSub(new SubscriptionKey("tick"))]
        : [],
    OnCommandError = (_, ex) => throw new InvalidOperationException("Unexpected command error", ex),
    OnRuntimeError = err => throw new InvalidOperationException($"Unexpected runtime error: {err}")
  };

  [Fact]
  public void Scenario_dispatches_sequence_and_preserves_state()
  {
    MvuScenario.For(_program)
        .Dispatch(new TestMsg.Increment(), r => Assert.Equal(1, r.State.Count))
        .Dispatch(new TestMsg.Increment(), r => Assert.Equal(2, r.State.Count))
        .Dispatch(new TestMsg.Increment())
        .AssertState(s => Assert.Equal(3, s.Count));
  }

  [Fact]
  public void Scenario_tracks_subscription_changes()
  {
    MvuScenario.For(_program)
        .Dispatch(new TestMsg.Start(), r =>
        {
          Assert.True(r.State.IsRunning);
          Assert.Single(r.Subscriptions);
          Assert.Equal("tick", r.Subscriptions[0].Key.Value);
        })
        .Dispatch(new TestMsg.Stop(), r =>
        {
          Assert.False(r.State.IsRunning);
          Assert.Empty(r.Subscriptions);
        });
  }

  [Fact]
  public void Scenario_history_includes_init_and_all_transitions()
  {
    ScenarioRunner<TestState, TestMsg, TestCmd, TestSub> runner = MvuScenario.For(_program)
        .Dispatch(new TestMsg.Increment())
        .Dispatch(new TestMsg.Increment());

    // Init + 2 dispatches = 3 entries
    Assert.Equal(3, runner.History.Length);
  }
}
