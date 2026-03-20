// Copyright (c) 2026 Ali Rashid. Licensed under the Apache License, Version 2.0.
// See LICENSE in the project root for licence information.

using System.Threading;
using System.Threading.Tasks;

namespace Pamba;

/// <summary>
/// Executes a single command and dispatches resulting messages.
/// Implemented by the Shell. Returns <see cref="ValueTask"/> - zero-allocation for synchronous completions.
/// </summary>
/// <typeparam name="TCmd">Command type.</typeparam>
/// <typeparam name="TMsg">Message type.</typeparam>
/// <param name="command">The command to execute.</param>
/// <param name="dispatch">Dispatch function for resulting messages.</param>
/// <param name="cancellationToken">Cancellation token for the operation.</param>
public delegate ValueTask CommandExecutor<in TCmd, TMsg>(
    TCmd command,
    Dispatch<TMsg> dispatch,
    CancellationToken cancellationToken);
