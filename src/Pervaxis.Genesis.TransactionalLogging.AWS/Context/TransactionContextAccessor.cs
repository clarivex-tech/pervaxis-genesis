/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Context;

/// <summary>
/// AsyncLocal-based accessor for the current <see cref="TransactionContext"/>.
/// Allows middleware and interceptors to share the same transaction scope
/// across async call chains within a single request.
/// </summary>
public sealed class TransactionContextAccessor
{
    private static readonly AsyncLocal<TransactionContext?> _current = new();

    /// <summary>
    /// Gets or sets the current transaction context for the executing async flow.
    /// Returns null when no transaction scope is active (e.g., background jobs without explicit scope).
    /// </summary>
    public TransactionContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// Gets or sets whether implicit capture is suppressed in the current scope.
    /// Used by the SuppressCapture mechanism.
    /// </summary>
    public bool IsSuppressed { get; set; }
}
