/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 ************************************************************************
 */

using Pervaxis.Genesis.TransactionalLogging.AWS.Context;
using Pervaxis.Genesis.TransactionalLogging.AWS.Models;

namespace Pervaxis.Genesis.TransactionalLogging.AWS.Storage;

/// <summary>
/// Internal store abstraction for persisting and querying transaction logs.
/// </summary>
internal interface ITransactionLogStore
{
    Task PersistAsync(TransactionContext context, CancellationToken cancellationToken = default);
    Task<TransactionLogQueryResult> QueryAsync(TransactionLogQuery query, CancellationToken cancellationToken = default);
}
