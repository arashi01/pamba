// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Pamba;

/// <summary>
/// Manages subscription lifecycle via key-based diffing.
/// Internal to the runtime - not part of the public API.
/// </summary>
internal sealed class SubscriptionManager<TSub, TMsg> : IDisposable
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  private readonly SubscriptionStarter<TSub, TMsg> _starter;
  private readonly Dictionary<SubscriptionKey, IDisposable> _active;
  private readonly HashSet<SubscriptionKey> _currentKeys;
  private readonly List<SubscriptionKey> _removalBuffer;

  internal SubscriptionManager(SubscriptionStarter<TSub, TMsg> starter)
  {
    _starter = starter;
    _active = [];
    _currentKeys = [];
    _removalBuffer = [];
  }

  /// <summary>
  /// Diff previous and current subscription sets.
  /// Start new subscriptions, cancel removed ones, keep unchanged ones.
  /// </summary>
  internal void Diff(ImmutableArray<TSub> current, Dispatch<TMsg> dispatch)
  {
    _currentKeys.Clear();

    foreach (TSub sub in current)
    {
      _currentKeys.Add(sub.Key);

      if (!_active.ContainsKey(sub.Key))
      {
        _active[sub.Key] = _starter(sub, dispatch);
      }
    }

    _removalBuffer.Clear();

    foreach (SubscriptionKey key in _active.Keys)
    {
      if (!_currentKeys.Contains(key))
      {
        _removalBuffer.Add(key);
      }
    }

    foreach (SubscriptionKey key in _removalBuffer)
    {
#pragma warning disable CA1031 // Subscription dispose must not block cleanup of remaining subscriptions
      try
      {
        _active[key].Dispose();
      }
      catch { }
#pragma warning restore CA1031
      _active.Remove(key);
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    foreach (IDisposable handle in _active.Values)
    {
#pragma warning disable CA1031 // Subscription dispose must not block cleanup of remaining subscriptions
      try
      {
        handle.Dispose();
      }
      catch { }
#pragma warning restore CA1031
    }

    _active.Clear();
  }
}
