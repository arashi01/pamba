// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

namespace Pamba;

/// <summary>
/// Strongly-typed key identifying a subscription instance.
/// Two subscriptions with the same <see cref="SubscriptionKey"/> are considered the same logical subscription.
/// </summary>
public readonly record struct SubscriptionKey(string Value);
