// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;

namespace Pamba;

/// <summary>
/// A complete MVU programme definition.
/// All four functions are pure. The runtime executes them.
/// See <see cref="MvuRuntime{TState, TMsg, TCmd, TSub}"/> for execution.
/// </summary>
/// <typeparam name="TState">Immutable application state. Must implement value equality.</typeparam>
/// <typeparam name="TMsg">Message type - sealed record hierarchy.</typeparam>
/// <typeparam name="TCmd">Command type - sealed record hierarchy describing effects.</typeparam>
/// <typeparam name="TSub">Subscription type - sealed record hierarchy describing ongoing effects.</typeparam>
public sealed record MvuProgramme<TState, TMsg, TCmd, TSub>
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  /// <summary>Returns the initial state and startup commands.</summary>
  public required Func<(TState State, IReadOnlyList<TCmd> Commands)> Init { get; init; }

  /// <summary>Pure state transition function. Total over all (TMsg, TState) inputs.</summary>
  public required Func<TMsg, TState, (TState State, IReadOnlyList<TCmd> Commands)> Update { get; init; }

  /// <summary>Returns the set of active subscriptions for the current state.</summary>
  public required Func<TState, IReadOnlyList<TSub>> Subscriptions { get; init; }

  /// <summary>
  /// Maps a failed command and its exception to a message for the Update loop.
  /// Ensures command execution failures are routed as typed values rather than silently lost.
  /// </summary>
  public required Func<TCmd, Exception, TMsg> OnCommandError { get; init; }

  /// <summary>
  /// Optional state validator invoked after every transition.
  /// Returns the validated state, or throws on invariant violation.
  /// When present, runs in all build configurations - not just tests.
  /// </summary>
  public Func<TState, TState>? Validate { get; init; }
}
