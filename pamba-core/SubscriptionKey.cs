// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba;

/// <summary>
/// Strongly-typed key identifying a subscription instance.
/// Two subscriptions with the same <see cref="SubscriptionKey"/> are considered the same logical subscription.
/// Rejects null or empty values at construction.
/// </summary>
public readonly record struct SubscriptionKey
{
  /// <summary>The string value of this subscription key.</summary>
  public required string Value
  {
    get;
    init => field = string.IsNullOrEmpty(value)
        ? throw new ArgumentException("Subscription key must not be null or empty.", nameof(value))
        : value;
  }
}
