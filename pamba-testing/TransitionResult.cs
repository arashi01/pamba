// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Immutable;

namespace Pamba.Testing;

/// <summary>
/// The result of a single state transition, including state, commands, and subscriptions.
/// Returned by <see cref="MvuTestRunner"/> for assertion.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TSub">Subscription type.</typeparam>
/// <param name="State">The state after the transition (or the pre-rejection state if <see cref="CorrectionMessage"/> is non-null).</param>
/// <param name="Message">The message that triggered this transition. <c>null</c> for Init results.</param>
/// <param name="CorrectionMessage">Non-null when the validator rejected the transition. The corrective message the validator produced.</param>
/// <param name="Commands">Commands returned by Update. Empty when the transition was rejected by the validator.</param>
/// <param name="Subscriptions">Active subscriptions for the current state.</param>
public sealed record TransitionResult<TState, TMsg, TCmd, TSub>(
    TState State,
    TMsg? Message,
    TMsg? CorrectionMessage,
    ImmutableArray<TCmd> Commands,
    ImmutableArray<TSub> Subscriptions)
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>;
