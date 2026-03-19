// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba.Testing;

/// <summary>
/// Test utilities for MVU programmes.
/// Calls Update + Validate + Subscriptions in sequence, returning all results for assertion.
/// No test framework dependency - works with xUnit, NUnit, MSTest.
/// </summary>
public static class MvuTestRunner
{
  /// <summary>
  /// Call Update, then Validate (if present), then Subscriptions.
  /// Returns all three results for assertion.
  /// Throws if validation fails - test fails with invariant violation message.
  /// </summary>
  public static TransitionResult<TState, TCmd, TSub>
      UpdateAndValidate<TState, TMsg, TCmd, TSub>(
          MvuProgramme<TState, TMsg, TCmd, TSub> programme,
          TState currentState,
          TMsg message)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    ArgumentNullException.ThrowIfNull(programme);
    var (newState, cmds) = programme.Update(message, currentState);

    if (programme.Validate is not null)
    {
      newState = programme.Validate(newState);
    }

    var subs = programme.Subscriptions(newState);

    return new TransitionResult<TState, TCmd, TSub>(newState, cmds, subs);
  }

  /// <summary>
  /// Call Init, then Validate (if present), then Subscriptions on the initial state.
  /// Returns all three results for assertion.
  /// </summary>
  public static TransitionResult<TState, TCmd, TSub>
      InitAndValidate<TState, TMsg, TCmd, TSub>(
          MvuProgramme<TState, TMsg, TCmd, TSub> programme)
      where TState : IEquatable<TState>
      where TMsg : notnull
      where TCmd : notnull
      where TSub : IEquatable<TSub>, ISubscription<TMsg>
  {
    ArgumentNullException.ThrowIfNull(programme);
    var (initialState, cmds) = programme.Init();

    if (programme.Validate is not null)
    {
      initialState = programme.Validate(initialState);
    }

    var subs = programme.Subscriptions(initialState);

    return new TransitionResult<TState, TCmd, TSub>(initialState, cmds, subs);
  }
}
