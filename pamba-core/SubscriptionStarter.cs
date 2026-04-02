// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba;

/// <summary>
/// Creates and manages the lifecycle of a single subscription.
/// Implemented by the Shell per subscription type.
/// Returns an <see cref="IAsyncDisposable"/> that cancels the subscription when disposed.
/// Subscriptions holding only synchronous resources implement <see cref="IAsyncDisposable"/>
/// trivially via <c>ValueTask.CompletedTask</c>.
/// </summary>
/// <typeparam name="TSub">Subscription type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <param name="subscription">The subscription to start.</param>
/// <param name="dispatch">Dispatch function for messages produced by the subscription.</param>
/// <returns>An async disposable handle that cancels the subscription when disposed.</returns>
public delegate IAsyncDisposable SubscriptionStarter<in TSub, TMsg>(
    TSub subscription,
    Dispatch<TMsg> dispatch)
    where TSub : ISubscription<TMsg>;
