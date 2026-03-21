// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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
  private const int _defaultMaxHistorySize = 1000;
  private readonly int _maxHistorySize;
  private readonly Queue<TransitionRecord<TState, TMsg, TCmd, TSub>> _messageHistory;
#endif

  private TState _state;
  private volatile bool _disposed;

  internal MvuRuntime(
      MvuProgram<TState, TMsg, TCmd, TSub> program,
      CommandExecutor<TCmd, TMsg> commandExecutor,
      SubscriptionStarter<TSub, TMsg> subscriptionStarter,
      Func<Action, bool> enqueue,
      Action<TState>? onInit,
      Action<TState, TState>? onStateChanged,
      int maxHistorySize)
  {
    _program = program;
    _commandExecutor = commandExecutor;
    _enqueue = enqueue;
    _onStateChanged = onStateChanged;
    _cts = new CancellationTokenSource();

    // Wrap the starter so that exceptions are routed via OnRuntimeError rather than propagating
    _subscriptionManager = new SubscriptionManager<TSub, TMsg>(SafeStarter);

#if DEBUG
    // 0 means not configured (builder default); positive values are pre-validated by builder
    _maxHistorySize = maxHistorySize > 0 ? maxHistorySize : _defaultMaxHistorySize;
    _messageHistory = new Queue<TransitionRecord<TState, TMsg, TCmd, TSub>>(_maxHistorySize);
#endif

    try
    {
      (TState initialState, ImmutableArray<TCmd> startupCmds) = program.Init();

      bool hasCorrectiveMessage = false;
      TMsg correctiveMessage = default!;
      if (program.Validate is not null)
      {
        switch (program.Validate(initialState))
        {
          case ValidationResult<TState, TMsg>.Valid v:
            initialState = v.State;
            break;
          case ValidationResult<TState, TMsg>.Invalid i:
            startupCmds = ImmutableArray<TCmd>.Empty;
            hasCorrectiveMessage = true;
            correctiveMessage = i.Error;
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

      if (hasCorrectiveMessage)
      {
        Dispatch(correctiveMessage);
      }
    }
    catch
    {
      _subscriptionManager.Dispose();
      _cts.Dispose();
      throw;
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
        SafeDispatchRuntimeError(new PambaError.SubscriptionStartFailed(sub.Key, ex));
        return NoopDisposable._instance;
      }
#pragma warning restore CA1031
    }
  }

  /// <summary>Current application state. Read-only snapshot.</summary>
  public TState State => _state;

  /// <summary>
  /// Message history (debug builds only). Null in release.
  /// Bounded to a configurable maximum size (default 1000).
  /// </summary>
  public IReadOnlyCollection<TransitionRecord<TState, TMsg, TCmd, TSub>>? MessageHistory =>
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
      // Queue has shut down. Cannot safely mutate state - no thread context for ProcessMessage.
      // Consumer's OnRuntimeError can observe/log but the resulting message is not dispatched.
      NotifyRuntimeError(new PambaError.DispatchRejected());
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

    bool hasCorrective = false;
    TMsg corrective = default!;
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
          hasCorrective = true;
          corrective = i.Error;
          break;
      }
    }

    _state = newState;

    ImmutableArray<TSub> newSubs = ImmutableArray<TSub>.Empty;
    if (!oldState.Equals(newState))
    {
      newSubs = _program.Subscriptions(newState);
      _subscriptionManager.Diff(newSubs, Dispatch);
      SafeInvokeProjection(oldState, newState);
    }

#if DEBUG
    if (_messageHistory.Count >= _maxHistorySize)
    {
      _messageHistory.Dequeue();
    }

    _messageHistory.Enqueue(new TransitionRecord<TState, TMsg, TCmd, TSub>(
        message, oldState, newState, cmds, newSubs));
#endif

    foreach (TCmd cmd in cmds)
    {
      ExecuteCommand(cmd);
    }

    if (hasCorrective)
    {
      Dispatch(corrective);
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
        SafeDispatchCommandError(cmd, ex);
      }
    }
  }
#pragma warning restore CA1031

#pragma warning disable CA1031 // Last-resort safety net: error handler failures must not crash the runtime
  private void SafeDispatchCommandError(TCmd cmd, Exception ex)
  {
    try
    {
      Dispatch(_program.OnCommandError(cmd, ex));
    }
    catch (Exception handlerEx)
    {
      // OnCommandError threw — escalate to OnRuntimeError with ErrorHandlerFailed
      SafeDispatchRuntimeError(
          new PambaError.ErrorHandlerFailed($"OnCommandError for {cmd}", handlerEx));
    }
  }

  private void SafeInvokeProjection(TState oldState, TState newState)
  {
    if (_onStateChanged is null)
    {
      return;
    }

    try
    {
      _onStateChanged(oldState, newState);
    }
    catch (Exception ex)
    {
      SafeDispatchRuntimeError(new PambaError.ProjectionFailed(ex));
    }
  }

  private void SafeDispatchRuntimeError(PambaError error)
  {
    TMsg? msg = NotifyRuntimeError(error);
    if (msg is not null)
    {
      Dispatch(msg);
    }
  }

  /// <summary>
  /// Calls OnRuntimeError safely, returning the message if successful, null if the handler threw.
  /// Used directly (without dispatch) for DispatchRejected where the queue is unavailable.
  /// </summary>
  private TMsg? NotifyRuntimeError(PambaError error)
  {
    try
    {
      return _program.OnRuntimeError(error);
    }
    catch (Exception handlerEx)
    {
      // OnRuntimeError threw — no typed channel remains. Trace for production visibility.
      Trace.TraceError(
          $"OnRuntimeError threw an exception. Original error: {error}\nHandler exception: {handlerEx}");
      return default;
    }
  }
#pragma warning restore CA1031

  private sealed class NoopDisposable : IDisposable
  {
    internal static readonly NoopDisposable _instance = new();

    public void Dispose() { }
  }
}
