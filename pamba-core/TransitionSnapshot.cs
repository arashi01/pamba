// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System.Collections.Immutable;

namespace Pamba;

/// <summary>
/// Point-in-time snapshot of a single state transition.
/// Recorded by the runtime in debug builds for time-travel diagnostics.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TSub">Subscription type.</typeparam>
public sealed record TransitionSnapshot<TState, TMsg, TCmd, TSub>(
    TMsg Message,
    TState StateBefore,
    TState StateAfter,
    ImmutableArray<TCmd> Commands,
    ImmutableArray<TSub> Subscriptions);
