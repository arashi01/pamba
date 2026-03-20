// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.ComponentModel;
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
    return new DispatcherTimerHandle(timer);
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
    return new DispatcherTimerHandle(timer);
  }
}

/// <summary>
/// Creates a subscription that bridges <see cref="INotifyPropertyChanged"/> events
/// into the MVU loop. Dispatches a message when the specified property changes on the source.
/// Use inside a <see cref="SubscriptionStarter{TSub, TMsg}"/> to subscribe to external
/// observable state (e.g. Lugha <c>LocaleHost</c>, system theme providers).
/// </summary>
public static class PropertyChangedSubscription
{
  /// <summary>Start listening for property changes on an <see cref="INotifyPropertyChanged"/> source.</summary>
  /// <typeparam name="TMsg">Message type.</typeparam>
  /// <param name="source">The observable source.</param>
  /// <param name="propertyName">
  /// Property name to filter on. When <c>null</c>, all property changes dispatch a message.
  /// </param>
  /// <param name="createMessage">Factory for the message to dispatch on property change.</param>
  /// <param name="dispatch">Dispatch function.</param>
  /// <param name="dispatcherQueue">
  /// WinUI dispatcher queue. Ensures <paramref name="createMessage"/> runs on the UI thread
  /// regardless of which thread raised the event.
  /// </param>
  /// <returns>A disposable that detaches the event handler when disposed.</returns>
  public static IDisposable Start<TMsg>(
      INotifyPropertyChanged source,
      string? propertyName,
      Func<TMsg> createMessage,
      Dispatch<TMsg> dispatch,
      DispatcherQueue dispatcherQueue)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(createMessage);
    ArgumentNullException.ThrowIfNull(dispatch);
    ArgumentNullException.ThrowIfNull(dispatcherQueue);

    source.PropertyChanged += Handler;
    return new EventSubscriptionHandle(() => source.PropertyChanged -= Handler);

    void Handler(object? sender, PropertyChangedEventArgs e)
    {
      if (propertyName is null || e.PropertyName == propertyName)
      {
        dispatcherQueue.TryEnqueue(() => dispatch(createMessage()));
      }
    }
  }
}
