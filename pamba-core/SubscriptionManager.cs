// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Pamba;

/// <summary>
/// Manages subscription lifecycle via key-based diffing.
/// Internal to the runtime - not part of the public API.
/// </summary>
internal sealed class SubscriptionManager<TSub, TMsg> : IDisposable
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  private readonly SubscriptionStarter<TSub, TMsg> _starter;
  private readonly Dictionary<SubscriptionKey, (TSub Subscription, IDisposable Handle)> _active;
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
  /// Same key + equal data = keep running. Same key + changed data = stop old, start new.
  /// New key = start. Removed key = cancel.
  /// </summary>
  internal void Diff(ImmutableArray<TSub> current, Dispatch<TMsg> dispatch)
  {
    _currentKeys.Clear();

    foreach (TSub sub in current)
    {
      if (!_currentKeys.Add(sub.Key))
      {
        Trace.TraceError($"Duplicate subscription key: '{sub.Key.Value}'. Each subscription must have a unique key.");
      }

      if (_active.TryGetValue(sub.Key, out var existing))
      {
        if (!existing.Subscription.Equals(sub))
        {
          DisposeHandle(existing.Handle);
          _active[sub.Key] = (sub, _starter(sub, dispatch));
        }
      }
      else
      {
        _active[sub.Key] = (sub, _starter(sub, dispatch));
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
      DisposeHandle(_active[key].Handle);
      _active.Remove(key);
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    foreach (var (_, handle) in _active.Values)
    {
      DisposeHandle(handle);
    }

    _active.Clear();
  }

#pragma warning disable CA1031 // Subscription dispose must not block cleanup of remaining subscriptions
  private static void DisposeHandle(IDisposable handle)
  {
    try
    {
      handle.Dispose();
    }
    catch (Exception ex)
    {
      Trace.TraceError($"Subscription Dispose threw an exception: {ex}");
    }
  }
#pragma warning restore CA1031
}
