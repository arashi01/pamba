// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;

namespace Pamba.Tests;

public sealed class SubscriptionManagerTests
{
  private abstract record TestMsg;

  private sealed record TestSub(SubscriptionKey Key) : ISubscription<TestMsg>;

  private sealed class TrackingDisposable : IDisposable
  {
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
  }

  [Fact]
  public void Diff_starts_new_subscriptions()
  {
    List<string> started = [];
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      started.Add(sub.Key.Value);
      return new TrackingDisposable();
    }

    using SubscriptionManager<TestSub, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff(
        [new TestSub(new SubscriptionKey { Value = "timer-a" }), new TestSub(new SubscriptionKey { Value = "timer-b" })],
        dispatch);

    Assert.Equal(2, started.Count);
    Assert.Contains("timer-a", started);
    Assert.Contains("timer-b", started);
  }

  [Fact]
  public void Diff_cancels_removed_subscriptions()
  {
    Dictionary<string, TrackingDisposable> handles = [];
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      TrackingDisposable handle = new();
      handles[sub.Key.Value] = handle;
      return handle;
    }

    using SubscriptionManager<TestSub, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff(
        [new TestSub(new SubscriptionKey { Value = "timer-a" }), new TestSub(new SubscriptionKey { Value = "timer-b" })],
        dispatch);
    Assert.False(handles["timer-a"].IsDisposed);
    Assert.False(handles["timer-b"].IsDisposed);

    // Remove timer-b
    manager.Diff([new TestSub(new SubscriptionKey { Value = "timer-a" })], dispatch);

    Assert.False(handles["timer-a"].IsDisposed);
    Assert.True(handles["timer-b"].IsDisposed);
  }

  [Fact]
  public void Diff_keeps_unchanged_subscriptions()
  {
    int startCount = 0;
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      startCount++;
      return new TrackingDisposable();
    }

    using SubscriptionManager<TestSub, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub(new SubscriptionKey { Value = "timer-a" })], dispatch);
    Assert.Equal(1, startCount);

    // Same subscription, should not restart
    manager.Diff([new TestSub(new SubscriptionKey { Value = "timer-a" })], dispatch);
    Assert.Equal(1, startCount);
  }

  [Fact]
  public void Diff_to_empty_cancels_all_subscriptions()
  {
    Dictionary<string, TrackingDisposable> handles = [];
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      TrackingDisposable handle = new();
      handles[sub.Key.Value] = handle;
      return handle;
    }

    using SubscriptionManager<TestSub, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff(
        [new TestSub(new SubscriptionKey { Value = "a" }), new TestSub(new SubscriptionKey { Value = "b" })],
        dispatch);
    Assert.False(handles["a"].IsDisposed);
    Assert.False(handles["b"].IsDisposed);

    manager.Diff(ImmutableArray<TestSub>.Empty, dispatch);
    Assert.True(handles["a"].IsDisposed);
    Assert.True(handles["b"].IsDisposed);
  }

  [Fact]
  public void Dispose_cancels_all_active_subscriptions()
  {
    Dictionary<string, TrackingDisposable> handles = [];
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      TrackingDisposable handle = new();
      handles[sub.Key.Value] = handle;
      return handle;
    }

    SubscriptionManager<TestSub, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff(
        [new TestSub(new SubscriptionKey { Value = "a" }), new TestSub(new SubscriptionKey { Value = "b" }), new TestSub(new SubscriptionKey { Value = "c" })],
        dispatch);

    manager.Dispose();

    Assert.All(handles.Values, h => Assert.True(h.IsDisposed));
  }

  [Fact]
  public void Diff_restarts_subscription_when_data_changes()
  {
    // C4 regression: same key, different data must stop old and start new
    List<int> startedIntervals = [];
    TrackingDisposable? latestHandle = null;

    IDisposable Starter(TestSubWithData sub, Dispatch<TestMsg> dispatch)
    {
      startedIntervals.Add(sub.Interval);
      latestHandle = new TrackingDisposable();
      return latestHandle;
    }

    using SubscriptionManager<TestSubWithData, TestMsg> manager = new(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    // Start with Interval=5
    manager.Diff([new TestSubWithData(new SubscriptionKey { Value = "timer" }, 5)], dispatch);
    Assert.Single(startedIntervals);
    Assert.Equal(5, startedIntervals[0]);
    TrackingDisposable firstHandle = latestHandle!;

    // Same key, different Interval=10 -> should restart
    manager.Diff([new TestSubWithData(new SubscriptionKey { Value = "timer" }, 10)], dispatch);
    Assert.Equal(2, startedIntervals.Count);
    Assert.Equal(10, startedIntervals[1]);
    Assert.True(firstHandle.IsDisposed);
    Assert.False(latestHandle!.IsDisposed);

    // Same key, same Interval=10 -> should NOT restart
    manager.Diff([new TestSubWithData(new SubscriptionKey { Value = "timer" }, 10)], dispatch);
    Assert.Equal(2, startedIntervals.Count); // Still 2, not 3
  }

  private sealed record TestSubWithData(SubscriptionKey Key, int Interval) : ISubscription<TestMsg>;
}
