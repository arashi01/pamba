// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using Microsoft.UI.Dispatching;

namespace Pamba.WinUI;

/// <summary>
/// Creates a <see cref="DispatcherQueueTimer"/>-based subscription that dispatches
/// a message at a fixed interval. Disposed when the subscription is removed.
/// </summary>
public static class TimerSubscription
{
  /// <summary>Start a repeating timer subscription.</summary>
  /// <typeparam name="TMsg">Message type.</typeparam>
  /// <param name="interval">Time between dispatches.</param>
  /// <param name="createMessage">Factory for the message to dispatch on each tick.</param>
  /// <param name="dispatch">Dispatch function.</param>
  /// <param name="dispatcherQueue">WinUI dispatcher queue.</param>
  /// <returns>A disposable that stops the timer when disposed.</returns>
  public static IDisposable Start<TMsg>(
      TimeSpan interval,
      Func<TMsg> createMessage,
      Dispatch<TMsg> dispatch,
      DispatcherQueue dispatcherQueue)
  {
    ArgumentNullException.ThrowIfNull(dispatcherQueue);
    var timer = dispatcherQueue.CreateTimer();
    timer.Interval = interval;
    timer.IsRepeating = true;
    timer.Tick += (_, _) => dispatch(createMessage());
    timer.Start();
    return new TimerHandle(timer);
  }

  private sealed class TimerHandle : IDisposable
  {
    private readonly DispatcherQueueTimer _timer;

    internal TimerHandle(DispatcherQueueTimer timer)
    {
      _timer = timer;
    }

    public void Dispose()
    {
      _timer.Stop();
    }
  }
}

/// <summary>
/// Creates a one-shot delayed subscription that dispatches a message
/// after a specified delay. Disposed if cancelled before firing.
/// </summary>
public static class DelayedSubscription
{
  /// <summary>Start a one-shot delayed subscription.</summary>
  /// <typeparam name="TMsg">Message type.</typeparam>
  /// <param name="delay">Delay before dispatching.</param>
  /// <param name="createMessage">Factory for the message to dispatch.</param>
  /// <param name="dispatch">Dispatch function.</param>
  /// <param name="dispatcherQueue">WinUI dispatcher queue.</param>
  /// <returns>A disposable that cancels the delay when disposed.</returns>
  public static IDisposable Start<TMsg>(
      TimeSpan delay,
      Func<TMsg> createMessage,
      Dispatch<TMsg> dispatch,
      DispatcherQueue dispatcherQueue)
  {
    ArgumentNullException.ThrowIfNull(dispatcherQueue);
    var timer = dispatcherQueue.CreateTimer();
    timer.Interval = delay;
    timer.IsRepeating = false;
    timer.Tick += (_, _) => dispatch(createMessage());
    timer.Start();
    return new TimerHandle(timer);
  }

  private sealed class TimerHandle : IDisposable
  {
    private readonly DispatcherQueueTimer _timer;

    internal TimerHandle(DispatcherQueueTimer timer)
    {
      _timer = timer;
    }

    public void Dispose()
    {
      _timer.Stop();
    }
  }
}
