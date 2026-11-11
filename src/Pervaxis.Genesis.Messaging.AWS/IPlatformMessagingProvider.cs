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

using Pervaxis.Genesis.Base.Exceptions;

namespace Pervaxis.Genesis.Messaging.AWS;

/// <summary>
/// Platform-scoped messaging operations interface.
/// SECURITY: Accessing platform-scoped resources requires BOTH:
/// 1. Explicit dependency injection of this interface (not IMessaging)
/// 2. Authorized caller with platform scope permission
/// 
/// This separation prevents accidental platform access from tenant-scoped code.
/// Fail closed: denied by default unless explicitly configured.
/// </summary>
/// <remarks>
/// Platform scope does NOT bypass tenant isolation - it uses different resource names
/// (e.g., "orders-platform" instead of "orders-tenant-acme"). Platform resources are shared
/// across all tenants and require explicit permission to access.
/// </remarks>
public interface IPlatformMessagingProvider
{
    /// <summary>
    /// Publishes a message to a platform-scoped destination (queue or topic).
    /// SECURITY: Requires platform scope permission. Throws <see cref="GenesisAuthorizationException"/> if denied.
    /// </summary>
    /// <typeparam name="T">Type of the message payload.</typeparam>
    /// <param name="destination">The platform destination name (e.g., "system-events").</param>
    /// <param name="message">The message payload to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Message ID assigned by the messaging service.</returns>
    /// <exception cref="GenesisAuthorizationException">Caller lacks platform scope permission.</exception>
    /// <exception cref="GenesisException">Message delivery failed or destination not provisioned.</exception>
    Task<string> PublishPlatformMessageAsync<T>(
        string destination,
        T message,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    /// Publishes multiple messages to a platform-scoped destination in a single batch.
    /// SECURITY: Requires platform scope permission. Throws <see cref="GenesisAuthorizationException"/> if denied.
    /// </summary>
    /// <typeparam name="T">Type of the message payloads.</typeparam>
    /// <param name="destination">The platform destination name.</param>
    /// <param name="messages">Collection of messages to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of message IDs in the same order as the input messages.</returns>
    /// <exception cref="GenesisAuthorizationException">Caller lacks platform scope permission.</exception>
    /// <exception cref="GenesisException">Batch delivery failed or destination not provisioned.</exception>
    Task<IEnumerable<string>> PublishPlatformBatchAsync<T>(
        string destination,
        IEnumerable<T> messages,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    /// Receives messages from a platform-scoped queue.
    /// SECURITY: Requires platform scope permission. Throws <see cref="GenesisAuthorizationException"/> if denied.
    /// </summary>
    /// <typeparam name="T">Type to deserialize the messages into.</typeparam>
    /// <param name="queue">The platform queue name.</param>
    /// <param name="maxMessages">Maximum number of messages to receive (default 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of received messages with receipt handles.</returns>
    /// <exception cref="GenesisAuthorizationException">Caller lacks platform scope permission.</exception>
    /// <exception cref="GenesisException">Receive operation failed or queue not provisioned.</exception>
    Task<IEnumerable<ReceivedMessage<T>>> ReceivePlatformMessagesAsync<T>(
        string queue,
        int maxMessages = 10,
        CancellationToken cancellationToken = default)
        where T : notnull;

    /// <summary>
    /// Deletes a message from a platform-scoped queue after processing.
    /// SECURITY: Requires platform scope permission. Throws <see cref="GenesisAuthorizationException"/> if denied.
    /// </summary>
    /// <param name="queue">The platform queue name.</param>
    /// <param name="receiptHandle">The receipt handle from the received message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was deleted; false if it was already deleted or expired.</returns>
    /// <exception cref="GenesisAuthorizationException">Caller lacks platform scope permission.</exception>
    /// <exception cref="GenesisException">Delete operation failed or queue not provisioned.</exception>
    Task<bool> DeletePlatformMessageAsync(
        string queue,
        string receiptHandle,
        CancellationToken cancellationToken = default);
}
