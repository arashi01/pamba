// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;

namespace Pamba.Testing;

/// <summary>
/// Entry point for scenario-based testing of MVU programmes.
/// Dispatches message sequences and provides assertion hooks at each step.
/// </summary>
public static class MvuScenario
{
  /// <summary>
  /// Create a scenario runner for the given programme.
  /// Calls Init to establish the starting state.
  /// </summary>
  public static ScenarioRunner<TState, TMsg, TCmd, TSub>
      For<TState, TMsg, TCmd, TSub>(
          MvuProgramme<TState, TMsg, TCmd, TSub> programme)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    var init = MvuTestRunner.InitAndValidate(programme);
    return new ScenarioRunner<TState, TMsg, TCmd, TSub>(programme, init);
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
  private readonly MvuProgramme<TState, TMsg, TCmd, TSub> _programme;
  private readonly List<TransitionResult<TState, TCmd, TSub>> _history;
  private TState _currentState;

  internal ScenarioRunner(
      MvuProgramme<TState, TMsg, TCmd, TSub> programme,
      TransitionResult<TState, TCmd, TSub> initResult)
  {
    _programme = programme;
    _currentState = initResult.State;
    _history = [initResult];
  }

  /// <summary>Current state after all dispatched messages.</summary>
  public TState State => _currentState;

  /// <summary>Full transition history including Init.</summary>
  public IReadOnlyList<TransitionResult<TState, TCmd, TSub>> History => _history;

  /// <summary>Dispatch a message and optionally assert on the result.</summary>
  public ScenarioRunner<TState, TMsg, TCmd, TSub> Dispatch(
      TMsg message,
      Action<TransitionResult<TState, TCmd, TSub>>? assert)
  {
    var result = MvuTestRunner.UpdateAndValidate(_programme, _currentState, message);
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
