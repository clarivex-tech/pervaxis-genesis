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
/// Constants for Genesis Messaging multi-tenant isolation using message attributes.
/// SECURITY: These values are reserved and controlled by the platform - never trust caller-supplied values.
/// </summary>
public static class MessagingConstants
{
    /// <summary>
    /// Reserved message attribute name for tenant ID.
    /// Platform automatically injects this from ITenantContext - caller-supplied values are rejected.
    /// ISOLATION: App-enforced in Phase 1. Future: IAM-enforced via per-tenant resources.
    /// </summary>
    public const string TenantIdAttribute = "pervaxis_tenant_id";

    /// <summary>
    /// Reserved attribute prefix. Any message attributes starting with this prefix are platform-controlled
    /// and will be rejected if supplied by callers.
    /// </summary>
    public const string ReservedAttributePrefix = "pervaxis_";

    /// <summary>
    /// Special tenant ID value for platform-scoped messages.
    /// This value is already rejected by TenantIdValidator for real tenants (reserved word),
    /// making it safe to use as a marker for platform messages.
    /// </summary>
    public const string PlatformTenantId = "platform";

    /// <summary>
    /// Message attribute name for DLQ failure reasons.
    /// Added when a message is dead-lettered due to validation failures.
    /// </summary>
    public const string FailureReasonAttribute = "pervaxis_failure_reason";

    /// <summary>
    /// Standard failure reason codes for dead-lettered messages.
    /// </summary>
#pragma warning disable CA1034 // Nested type is intentional for grouping related constants
    public static class FailureReasons
#pragma warning restore CA1034
    {
        /// <summary>
        /// Message is missing the required pervaxis_tenant_id attribute.
        /// </summary>
        public const string MissingTenantId = "missing_tenant_id";

        /// <summary>
        /// The pervaxis_tenant_id attribute contains an invalid tenant ID
        /// (uppercase letters, reserved words, invalid characters, etc.).
        /// </summary>
        public const string InvalidTenantId = "invalid_tenant_id";

        /// <summary>
        /// A tenant-scoped consumer received a platform message (pervaxis_tenant_id = "platform").
        /// Platform messages must be consumed by platform-specific consumers.
        /// </summary>
        public const string PlatformNotAllowedInTenantConsumer = "platform_message_not_allowed_in_tenant_consumer";

        /// <summary>
        /// A platform-scoped consumer received a tenant message.
        /// Platform consumers should only process messages with pervaxis_tenant_id = "platform".
        /// </summary>
        public const string TenantNotAllowedInPlatformConsumer = "tenant_message_not_allowed_in_platform_consumer";

        /// <summary>
        /// Caller attempted to publish a platform-scoped message without required authorization.
        /// Platform scope requires IPlatformMessagingProvider and explicit permission.
        /// </summary>
        public const string UnauthorizedPlatformPublish = "unauthorized_platform_publish";

        /// <summary>
        /// Caller attempted to supply a reserved message attribute (pervasis_* prefix).
        /// These attributes are platform-controlled and cannot be set by domain code.
        /// </summary>
        public const string ReservedAttributeSupplied = "reserved_attribute_supplied_by_caller";
    }
}
