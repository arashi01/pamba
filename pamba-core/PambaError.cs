// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

namespace Pamba;

/// <summary>
/// Sealed hierarchy of library-originated errors.
/// All errors produced by the runtime are subtypes of <see cref="PambaError"/>.
/// Routed into the MVU loop via <see cref="MvuProgram{TState,TMsg,TCmd,TSub}.OnRuntimeError"/>.
/// All variants carry immutable diagnostic strings captured at the catch site — not live
/// <c>Exception</c> objects — preserving record value equality and serializability.
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
  /// <param name="ExceptionType">The exception type name, captured at the catch site.</param>
  /// <param name="ExceptionMessage">The exception message, captured at the catch site.</param>
  public sealed record SubscriptionStartFailed(
      SubscriptionKey Key,
      string ExceptionType,
      string ExceptionMessage) : PambaError;

  /// <summary>
  /// The state projection callback threw an exception during a state transition.
  /// The state transition itself completed successfully; only the UI projection failed.
  /// </summary>
  /// <param name="ExceptionType">The exception type name, captured at the catch site.</param>
  /// <param name="ExceptionMessage">The exception message, captured at the catch site.</param>
  public sealed record ProjectionFailed(
      string ExceptionType,
      string ExceptionMessage) : PambaError;

  /// <summary>
  /// The <see cref="MvuProgram{TState,TMsg,TCmd,TSub}.Subscriptions"/> function returned
  /// multiple subscriptions with the same <see cref="SubscriptionKey"/>. First occurrence wins;
  /// duplicates are skipped.
  /// </summary>
  /// <param name="Key">The duplicated subscription key.</param>
  public sealed record DuplicateSubscriptionKey(SubscriptionKey Key) : PambaError;

  /// <summary>
  /// A command executor threw an unexpected exception (programming bug in the executor).
  /// Well-implemented executors signal expected failures via <see cref="CommandResultExtensions"/>
  /// rather than throwing. This variant covers only unhandled throws.
  /// </summary>
  /// <param name="CommandType">The command type name.</param>
  /// <param name="ExceptionType">The exception type name, captured at the catch site.</param>
  /// <param name="ExceptionMessage">The exception message, captured at the catch site.</param>
  public sealed record CommandExecutorFailed(
      string CommandType,
      string ExceptionType,
      string ExceptionMessage) : PambaError;
#pragma warning restore CA1034
}
