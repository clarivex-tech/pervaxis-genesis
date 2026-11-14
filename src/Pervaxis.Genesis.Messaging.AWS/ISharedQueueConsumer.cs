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

namespace Pervaxis.Genesis.Messaging.AWS;

/// <summary>
/// Trusted shared-queue worker. Register explicitly and invoke outside any tenant
/// scope. Queue policies must restrict writers to trusted tenant-stamping producers.
/// </summary>
public interface ISharedQueueConsumer
{
    /// <summary>Processes messages in individual tenant and DI scopes; acknowledges only successful handlers.</summary>
    /// <typeparam name="T">Message payload type.</typeparam>
    /// <param name="queue">Logical shared queue name.</param>
    /// <param name="handler">Handler; resolve tenant services from the supplied scoped provider.</param>
    /// <param name="maxMessages">Maximum messages to receive (1-10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing after this batch is processed.</returns>
    Task ProcessAsync<T>(string queue, Func<T, IServiceProvider, CancellationToken, Task> handler,
        int maxMessages = 10, CancellationToken cancellationToken = default) where T : notnull;
}
