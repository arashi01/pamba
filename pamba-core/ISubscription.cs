// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

namespace Pamba;

/// <summary>
/// A subscription description. Pure data - the runtime manages lifecycle.
/// Implementations are sealed records with value equality.
/// </summary>
/// <remarks>
/// The runtime diffs subscriptions by <see cref="Key"/>:
/// same key = keep running; new key = start; removed key = cancel.
/// </remarks>
/// <typeparam name="TMsg">Message type produced by this subscription.</typeparam>
public interface ISubscription<out TMsg>
{
  /// <summary>
  /// Unique key identifying this subscription instance.
  /// Two subscriptions with the same key are considered the same logical subscription.
  /// </summary>
  public SubscriptionKey Key { get; }
}
