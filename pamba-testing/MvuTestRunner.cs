// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Immutable;

namespace Pamba.Testing;

/// <summary>
/// Test utilities for MVU programs.
/// Calls Update + Validate + Subscriptions in sequence, returning all results for assertion.
/// No test framework dependency - works with xUnit, NUnit, MSTest.
/// </summary>
public static class MvuTestRunner
{
  /// <summary>
  /// Call Update, then Validate, then Subscriptions.
  /// Returns all results for assertion including any corrective message produced by a rejecting validator.
  /// </summary>
  public static TransitionResult<TState, TMsg, TCmd, TSub>
      UpdateAndValidate<TState, TMsg, TCmd, TSub>(
          MvuProgram<TState, TMsg, TCmd, TSub> program,
          TState currentState,
          TMsg message)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    ArgumentNullException.ThrowIfNull(program);
    (TState newState, ImmutableArray<TCmd> cmds) = program.Update(message, currentState);
    TMsg? correctionMessage = default;

    switch (program.Validate(newState))
    {
      case ValidationResult<TState, TMsg>.Valid v:
        newState = v.State;
        break;
      case ValidationResult<TState, TMsg>.Invalid i:
        newState = currentState;
        cmds = ImmutableArray<TCmd>.Empty;
        correctionMessage = i.Error;
        break;
    }

    ImmutableArray<TSub> subs = program.Subscriptions(newState);
    return new TransitionResult<TState, TMsg, TCmd, TSub>(newState, message, correctionMessage, cmds, subs);
  }

  /// <summary>
  /// Call Init, then Validate, then Subscriptions on the initial state.
  /// Returns all results for assertion.
  /// </summary>
  public static TransitionResult<TState, TMsg, TCmd, TSub>
      InitAndValidate<TState, TMsg, TCmd, TSub>(
          MvuProgram<TState, TMsg, TCmd, TSub> program)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    ArgumentNullException.ThrowIfNull(program);
    (TState initialState, ImmutableArray<TCmd> cmds) = program.Init();
    TMsg? correctionMessage = default;

    switch (program.Validate(initialState))
    {
      case ValidationResult<TState, TMsg>.Valid v:
        initialState = v.State;
        break;
      case ValidationResult<TState, TMsg>.Invalid i:
        cmds = ImmutableArray<TCmd>.Empty;
        correctionMessage = i.Error;
        break;
    }

    ImmutableArray<TSub> subs = program.Subscriptions(initialState);
    return new TransitionResult<TState, TMsg, TCmd, TSub>(initialState, default, correctionMessage, cmds, subs);
  }
}
