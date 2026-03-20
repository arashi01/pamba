// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Pamba.Testing;

/// <summary>
/// Entry point for scenario-based testing of MVU programs.
/// Dispatches message sequences and provides assertion hooks at each step.
/// </summary>
public static class MvuScenario
{
  /// <summary>
  /// Create a scenario runner for the given program.
  /// Calls Init to establish the starting state.
  /// </summary>
  public static ScenarioRunner<TState, TMsg, TCmd, TSub>
      For<TState, TMsg, TCmd, TSub>(
          MvuProgram<TState, TMsg, TCmd, TSub> program)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    var init = MvuTestRunner.InitAndValidate(program);
    return new ScenarioRunner<TState, TMsg, TCmd, TSub>(program, init);
  }
}

/// <summary>
/// Fluent scenario runner for multi-step MVU testing.
/// Each <see cref="Dispatch(TMsg)"/> call advances the state machine by one message.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TSub">Subscription type.</typeparam>
public sealed class ScenarioRunner<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  private readonly MvuProgram<TState, TMsg, TCmd, TSub> _program;
  private readonly List<TransitionResult<TState, TMsg, TCmd, TSub>> _history;
  private TState _currentState;

  internal ScenarioRunner(
      MvuProgram<TState, TMsg, TCmd, TSub> program,
      TransitionResult<TState, TMsg, TCmd, TSub> initResult)
  {
    _program = program;
    _currentState = initResult.State;
    _history = [initResult];
  }

  /// <summary>Current state after all dispatched messages.</summary>
  public TState State => _currentState;

  /// <summary>Full transition history including Init.</summary>
  public ImmutableArray<TransitionResult<TState, TMsg, TCmd, TSub>> History =>
      [.. _history];

  /// <summary>Dispatch a message and optionally assert on the result.</summary>
  public ScenarioRunner<TState, TMsg, TCmd, TSub> Dispatch(
      TMsg message,
      Action<TransitionResult<TState, TMsg, TCmd, TSub>>? assert)
  {
    TransitionResult<TState, TMsg, TCmd, TSub> result =
        MvuTestRunner.UpdateAndValidate(_program, _currentState, message);
    _currentState = result.State;
    _history.Add(result);
    assert?.Invoke(result);
    return this;
  }

  /// <summary>Dispatch a message without intermediate assertion.</summary>
  public ScenarioRunner<TState, TMsg, TCmd, TSub> Dispatch(TMsg message)
  {
    return Dispatch(message, null);
  }

  /// <summary>Assert on the current state.</summary>
  public ScenarioRunner<TState, TMsg, TCmd, TSub> AssertState(Action<TState> assert)
  {
    ArgumentNullException.ThrowIfNull(assert);
    assert(_currentState);
    return this;
  }
}
