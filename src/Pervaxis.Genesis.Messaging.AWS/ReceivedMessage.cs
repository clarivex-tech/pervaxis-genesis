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
/// Represents a received message with its payload and metadata.
/// Phase 1B: Includes tenant ID extracted from message attributes for proper tenant scoping.
/// </summary>
/// <typeparam name="T">The type of the message payload.</typeparam>
/// <param name="Message">The deserialized message payload.</param>
/// <param name="ReceiptHandle">Receipt handle for acknowledging/deleting the message.</param>
/// <param name="MessageId">Unique identifier assigned by the messaging service.</param>
/// <param name="TenantId">The tenant ID extracted from message attributes (null if tenant isolation disabled).</param>
public sealed record ReceivedMessage<T>(
    T Message,
    string ReceiptHandle,
    string MessageId,
    string? TenantId = null)
    where T : notnull;
