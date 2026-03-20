// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
  private readonly MvuProgram<TState, TMsg, TCmd, TSub> _program;
  private readonly CommandExecutor<TCmd, TMsg> _commandExecutor;
  private readonly Func<Action, bool> _enqueue;
  private readonly Action<TState, TState>? _onStateChanged;
  private readonly CancellationTokenSource _cts;
  private readonly SubscriptionManager<TSub, TMsg> _subscriptionManager;

#if DEBUG
  private readonly List<TransitionRecord<TState, TMsg, TCmd>> _messageHistory;
#endif

  private TState _state;
  private volatile bool _disposed;

  internal MvuRuntime(
      MvuProgram<TState, TMsg, TCmd, TSub> program,
      CommandExecutor<TCmd, TMsg> commandExecutor,
      SubscriptionStarter<TSub, TMsg> subscriptionStarter,
      Func<Action, bool> enqueue,
      Action<TState>? onInit,
      Action<TState, TState>? onStateChanged)
  {
    _program = program;
    _commandExecutor = commandExecutor;
    _enqueue = enqueue;
    _onStateChanged = onStateChanged;
    _cts = new CancellationTokenSource();

    // Wrap the starter so that exceptions are routed via OnRuntimeError rather than propagating
    _subscriptionManager = new SubscriptionManager<TSub, TMsg>(SafeStarter);

#if DEBUG
    _messageHistory = [];
#endif

    (TState initialState, ImmutableArray<TCmd> startupCmds) = program.Init();

    if (program.Validate is not null)
    {
      switch (program.Validate(initialState))
      {
        case ValidationResult<TState, TMsg>.Valid v:
          initialState = v.State;
          break;
        case ValidationResult<TState, TMsg>.Invalid i:
          // Init produced an invalid state - keep it and queue the corrective message
          startupCmds = ImmutableArray<TCmd>.Empty;
          _state = initialState;
          Dispatch(i.Error);
          break;
      }
    }

    _state = initialState;

    ImmutableArray<TSub> initialSubs = program.Subscriptions(initialState);
    _subscriptionManager.Diff(initialSubs, Dispatch);

    onInit?.Invoke(initialState);

    foreach (TCmd cmd in startupCmds)
    {
      ExecuteCommand(cmd);
    }

    return;

    IDisposable SafeStarter(TSub sub, Dispatch<TMsg> dispatch)
    {
#pragma warning disable CA1031 // Runtime boundary: subscription starter exceptions routed via OnRuntimeError
      try
      {
        return subscriptionStarter(sub, dispatch);
      }
      catch (Exception ex)
      {
        Dispatch(_program.OnRuntimeError(new PambaError.SubscriptionStartFailed(sub.Key, ex)));
        return new NoopDisposable();
      }
#pragma warning restore CA1031
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

    if (!_enqueue(() => ProcessMessage(message)))
    {
      // Queue rejected the enqueue - dispatcher has shut down.
      // Route synchronously since the queue is unavailable.
      ProcessMessage(_program.OnRuntimeError(new PambaError.DispatchRejected()));
    }
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
    GC.SuppressFinalize(this);
  }

  private void ProcessMessage(TMsg message)
  {
    if (_disposed)
    {
      return;
    }

    TState oldState = _state;
    (TState newState, ImmutableArray<TCmd> cmds) = _program.Update(message, oldState);

    if (_program.Validate is not null)
    {
      switch (_program.Validate(newState))
      {
        case ValidationResult<TState, TMsg>.Valid v:
          newState = v.State;
          break;
        case ValidationResult<TState, TMsg>.Invalid i:
          newState = oldState;
          cmds = ImmutableArray<TCmd>.Empty;
          Dispatch(i.Error);
          break;
      }
    }

#if DEBUG
    _messageHistory.Add(new TransitionRecord<TState, TMsg, TCmd>(
        message, oldState, newState, cmds));
#endif

    _state = newState;

    if (!oldState.Equals(newState))
    {
      ImmutableArray<TSub> newSubs = _program.Subscriptions(newState);
      _subscriptionManager.Diff(newSubs, Dispatch);
      _onStateChanged?.Invoke(oldState, newState);
    }

    foreach (TCmd cmd in cmds)
    {
      ExecuteCommand(cmd);
    }
  }

#pragma warning disable CA2012 // ValueTask fire-and-forget: command execution is intentionally not awaited; all results dispatch back as messages via OnCommandError
  private void ExecuteCommand(TCmd cmd) =>
      _ = ExecuteCommandCore(cmd);
#pragma warning restore CA2012

#pragma warning disable CA1031 // Runtime boundary: must catch all consumer command executor exceptions to route as typed messages via OnCommandError
  private async ValueTask ExecuteCommandCore(TCmd cmd)
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
        Dispatch(_program.OnCommandError(cmd, ex));
      }
    }
  }
#pragma warning restore CA1031

  private sealed class NoopDisposable : IDisposable
  {
    public void Dispose() { }
  }
}
