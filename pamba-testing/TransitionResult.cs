// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System.Collections.Generic;

namespace Pamba.Testing;

/// <summary>
/// The result of a single state transition, including state, commands, and subscriptions.
/// Returned by <see cref="MvuTestRunner"/> for assertion.
/// </summary>
/// <typeparam name="TState">State type.</typeparam>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TSub">Subscription type.</typeparam>
public sealed record TransitionResult<TState, TCmd, TSub>(
    TState State,
    IReadOnlyList<TCmd> Commands,
    IReadOnlyList<TSub> Subscriptions);
