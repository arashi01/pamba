// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pamba;

/// <summary>
/// The MVU dispatch loop. Framework-agnostic.
/// Requires a thread dispatcher for FIFO message ordering.
/// See <see cref="MvuRuntimeBuilder"/> for construction.
/// </summary>
/// <typeparam name="TState">Immutable application state.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TSub">Subscription type.</typeparam>
public sealed class MvuRuntime<TState, TMsg, TCmd, TSub> : IDisposable
    where TState : IEquatable<TState>
    where TMsg : notnull
    where TCmd : notnull
    where TSub : IEquatable<TSub>, ISubscription<TMsg>
{
  private readonly MvuProgramme<TState, TMsg, TCmd, TSub> _programme;
  private readonly CommandExecutor<TCmd, TMsg> _commandExecutor;
  private readonly Action<Action> _enqueue;
  private readonly Action<TState, TState>? _onStateChanged;
  private readonly CancellationTokenSource _cts;
  private readonly SubscriptionManager<TSub, TMsg> _subscriptionManager;

#if DEBUG
  private readonly List<TransitionRecord<TState, TMsg, TCmd>> _messageHistory;
#endif

  private TState _state;
  private volatile bool _disposed;

  internal MvuRuntime(
      MvuProgramme<TState, TMsg, TCmd, TSub> programme,
      CommandExecutor<TCmd, TMsg> commandExecutor,
      SubscriptionStarter<TSub, TMsg> subscriptionStarter,
      Action<Action> enqueue,
      Action<TState>? onInit,
      Action<TState, TState>? onStateChanged)
  {
    _programme = programme;
    _commandExecutor = commandExecutor;
    _enqueue = enqueue;
    _onStateChanged = onStateChanged;
    _cts = new CancellationTokenSource();
    _subscriptionManager = new SubscriptionManager<TSub, TMsg>(subscriptionStarter);

#if DEBUG
    _messageHistory = [];
#endif

    (TState initialState, IReadOnlyList<TCmd> startupCmds) = programme.Init();

    if (programme.Validate is not null)
    {
      initialState = programme.Validate(initialState);
    }

    _state = initialState;

    IReadOnlyList<TSub> initialSubs = programme.Subscriptions(initialState);
    _subscriptionManager.Diff(initialSubs, Dispatch);

    onInit?.Invoke(initialState);

    foreach (TCmd cmd in startupCmds)
    {
      ExecuteCommand(cmd);
    }
  }

  /// <summary>Current application state. Read-only snapshot.</summary>
  public TState State => _state;

  /// <summary>
  /// Message history (debug builds only). Null in release.
  /// </summary>
  public IReadOnlyList<TransitionRecord<TState, TMsg, TCmd>>? MessageHistory =>
#if DEBUG
      _messageHistory;
#else
        null;
#endif

  /// <summary>Dispatch a message into the loop. Thread-safe.</summary>
  public void Dispatch(TMsg message)
  {
    if (_disposed)
    {
      return;
    }

    _enqueue(() => ProcessMessage(message));
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _cts.Cancel();
    _subscriptionManager.Dispose();
    _cts.Dispose();
  }

  private void ProcessMessage(TMsg message)
  {
    if (_disposed)
    {
      return;
    }

    TState oldState = _state;
    (TState newState, IReadOnlyList<TCmd> cmds) = _programme.Update(message, oldState);

    if (_programme.Validate is not null)
    {
      newState = _programme.Validate(newState);
    }

#if DEBUG
    _messageHistory.Add(new TransitionRecord<TState, TMsg, TCmd>(
        message, oldState, newState, cmds));
#endif

    _state = newState;

    if (!oldState.Equals(newState))
    {
      IReadOnlyList<TSub> newSubs = _programme.Subscriptions(newState);
      _subscriptionManager.Diff(newSubs, Dispatch);
      _onStateChanged?.Invoke(oldState, newState);
    }

    foreach (TCmd cmd in cmds)
    {
      ExecuteCommand(cmd);
    }
  }

  private void ExecuteCommand(TCmd cmd) =>
      _ = ExecuteCommandCore(cmd);

#pragma warning disable CA1031 // Runtime boundary: must catch all consumer command executor exceptions to route as typed messages via OnCommandError
  private async Task ExecuteCommandCore(TCmd cmd)
  {
    CancellationToken token = _cts.Token;
    try
    {
      await _commandExecutor(cmd, Dispatch, token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
      // Expected during disposal - command was cancelled
    }
    catch (Exception ex)
    {
      if (!_disposed)
      {
        Dispatch(_programme.OnCommandError(cmd, ex));
      }
    }
  }
#pragma warning restore CA1031
}
