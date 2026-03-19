// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using Xunit;

namespace Pamba.Tests;

public sealed class SubscriptionManagerTests
{
  private abstract record TestMsg;

  private sealed record TestSub(string Key) : ISubscription<TestMsg>;

  private sealed class TrackingDisposable : IDisposable
  {
    public bool IsDisposed { get; private set; }
    public void Dispose() => IsDisposed = true;
  }

  [Fact]
  public void Diff_starts_new_subscriptions()
  {
    var started = new List<string>();
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      started.Add(sub.Key);
      return new TrackingDisposable();
    }

    using var manager = new SubscriptionManager<TestSub, TestMsg>(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub("timer-a"), new TestSub("timer-b")], dispatch);

    Assert.Equal(2, started.Count);
    Assert.Contains("timer-a", started);
    Assert.Contains("timer-b", started);
  }

  [Fact]
  public void Diff_cancels_removed_subscriptions()
  {
    var handles = new Dictionary<string, TrackingDisposable>();
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      var handle = new TrackingDisposable();
      handles[sub.Key] = handle;
      return handle;
    }

    using var manager = new SubscriptionManager<TestSub, TestMsg>(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub("timer-a"), new TestSub("timer-b")], dispatch);
    Assert.False(handles["timer-a"].IsDisposed);
    Assert.False(handles["timer-b"].IsDisposed);

    // Remove timer-b
    manager.Diff([new TestSub("timer-a")], dispatch);

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

    using var manager = new SubscriptionManager<TestSub, TestMsg>(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub("timer-a")], dispatch);
    Assert.Equal(1, startCount);

    // Same subscription, should not restart
    manager.Diff([new TestSub("timer-a")], dispatch);
    Assert.Equal(1, startCount);
  }

  [Fact]
  public void Diff_to_empty_cancels_all_subscriptions()
  {
    var handles = new Dictionary<string, TrackingDisposable>();
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      var handle = new TrackingDisposable();
      handles[sub.Key] = handle;
      return handle;
    }

    using var manager = new SubscriptionManager<TestSub, TestMsg>(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub("a"), new TestSub("b")], dispatch);
    Assert.False(handles["a"].IsDisposed);
    Assert.False(handles["b"].IsDisposed);

    manager.Diff([], dispatch);
    Assert.True(handles["a"].IsDisposed);
    Assert.True(handles["b"].IsDisposed);
  }

  [Fact]
  public void Dispose_cancels_all_active_subscriptions()
  {
    var handles = new Dictionary<string, TrackingDisposable>();
    IDisposable Starter(TestSub sub, Dispatch<TestMsg> dispatch)
    {
      var handle = new TrackingDisposable();
      handles[sub.Key] = handle;
      return handle;
    }

    var manager = new SubscriptionManager<TestSub, TestMsg>(Starter);
    Dispatch<TestMsg> dispatch = _ => { };

    manager.Diff([new TestSub("a"), new TestSub("b"), new TestSub("c")], dispatch);

    manager.Dispose();

    Assert.All(handles.Values, h => Assert.True(h.IsDisposed));
  }
}
