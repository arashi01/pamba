// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System;

namespace Pamba;

/// <summary>
/// Sealed hierarchy of library-originated errors.
/// All errors produced by the runtime are subtypes of <see cref="PambaError"/>.
/// Routed into the MVU loop via <see cref="MvuProgram{TState,TMsg,TCmd,TSub}.OnRuntimeError"/>.
/// </summary>
public abstract record PambaError
{
  private protected PambaError() { }

#pragma warning disable CA1034 // Sealed ADT hierarchy: nested public types form a closed discriminated union. private protected constructor prevents external derivation.
  /// <summary>
  /// The dispatcher queue rejected a message because it has shut down.
  /// Produced when the underlying <c>TryEnqueue</c> returns <c>false</c>.
  /// </summary>
  public sealed record DispatchRejected : PambaError;

  /// <summary>
  /// A subscription starter threw an exception when starting the subscription identified by <see cref="Key"/>.
  /// </summary>
  /// <param name="Key">The subscription key whose starter threw.</param>
  /// <param name="Cause">The exception thrown by the starter.</param>
  public sealed record SubscriptionStartFailed(SubscriptionKey Key, Exception Cause) : PambaError;

  /// <summary>
  /// An error handler (<see cref="MvuProgram{TState,TMsg,TCmd,TSub}.OnCommandError"/> or
  /// <see cref="MvuProgram{TState,TMsg,TCmd,TSub}.OnRuntimeError"/>) itself threw an exception.
  /// Contains the original error that triggered the handler and the handler's exception.
  /// </summary>
  /// <param name="OriginalError">Description of the error the handler was processing.</param>
  /// <param name="HandlerException">The exception thrown by the error handler.</param>
  public sealed record ErrorHandlerFailed(string OriginalError, Exception HandlerException) : PambaError;

  /// <summary>
  /// The state projection callback threw an exception during a state transition.
  /// The state transition itself completed successfully; only the UI projection failed.
  /// </summary>
  /// <param name="Cause">The exception thrown by the projection callback.</param>
  public sealed record ProjectionFailed(Exception Cause) : PambaError;
#pragma warning restore CA1034
}
